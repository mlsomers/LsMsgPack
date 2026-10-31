// Writes MsgPack values one after another to a sink (a WritableStream or a function), as LtMsgPack's MsgPackStreamWriter: values as encode writes them,
// arrays and maps of known length, arrays, maps, str and bin of unknown length (the escape sequences of msgpack/msgpack#347), and optionally an index at the end.
// Objects are written as plain MsgPack (maps of their property names, $type as the type id ""), as the request bodies of the client: no heads, no indexed schema.

import { crc32 } from './crc32.js';
import { Escape } from './stream-escape.js';
import { SidecarExtensionType, IndexExtensionType, StreamEntry, StreamIndexStatus, writeEntry, writeIdentifier, writeIndex, writeMetadata, writePlain, writeTail } from './stream-index.js';
import { EncodeOptions, Writer } from './writer.js';

/** Hashes the bytes of every entry of the index (StreamEntry.hash), e.g. an adapter of a SHA-256 or BLAKE3 library with an incremental API. Called on the writer's thread. */
export interface StreamHasher {
  /** A name of the multicodec table: "sha2-256", "blake3"... (StreamIndex.hashAlgorithm). */
  readonly algorithm: string;
  append(bytes: Uint8Array): void;
  /** The hash of the bytes since the last call, then starts again. */
  digest(): Uint8Array;
}

/** Gets the bytes of every entry, and the entry at its end: to add to it (metadata, a content type), and to return bytes to write after it (a sidecar, which readers skip). */
export interface StreamObserver {
  append?(bytes: Uint8Array): void;
  entryEnded?(entry: StreamEntry): Uint8Array | undefined | void;
}

export interface StreamIndexOptions {
  /** A point every so many elements of an entry that is an array (0: none, the default). */
  pointInterval?: number;
  /** An index after every so many entries (0: only at the end, the default): written and passed to the sink, so a cut-off stream keeps the entries before it. */
  checkpointInterval?: number;
  /** What wrote the stream (StreamIndex.writer), default "@lsmsgpack/client". */
  writer?: string;
  /** Plain values for the whole stream, in every index. */
  metadata?: Record<string, unknown>;
  hasher?: StreamHasher;
  observer?: StreamObserver;
}

export interface StreamWriterOptions extends EncodeOptions {
  /** The buffer goes to the sink once it holds this many bytes (64 KiB; 0: after every value). */
  flushThreshold?: number;
  /** close() does not close a WritableStream sink (it releases its lock). */
  leaveOpen?: boolean;
  /** Keep an index of the root values and write it at the end (docs/Streaming.md "The index"). */
  index?: StreamIndexOptions;
}

/** Where the writer puts its bytes: a WritableStream, or a function (awaited in order when it returns a promise). */
export type StreamSink = WritableStream<Uint8Array> | ((chunk: Uint8Array) => void | Promise<void>);

const MaxUnknownCount = 0xffffffff;
const Kind = { Array: 0, Map: 1, String: 2, Binary: 3 } as const;
type Kind = (typeof Kind)[keyof typeof Kind];
const KindNames = ['array', 'map', 'str', 'bin'];

interface Frame {
  kind: Kind;
  unknown: boolean;
  /** Known length: the values still to come (keys and values for a map). Unknown: the values (or bytes) written. */
  count: number;
}

/** What the writer keeps for the index: the entries since the last index (encoded), the open entry. */
class IndexState {
  readonly hasher?: StreamHasher;
  readonly observer?: StreamObserver;
  readonly checkpointInterval: number;
  readonly pointInterval: number;
  readonly writer: string;
  readonly metadata?: Uint8Array;
  previousTail?: number;
  readonly entries = new Writer(64, undefined, false);
  entryCount = 0;
  next?: StreamEntry; // of beginEntry, for the next root value
  entry?: StreamEntry; // open: its root value is being written
  feedFrom = 0; // the entry's bytes in the buffer from here are not passed to the hasher and observer yet
  rootArray = false;
  elements = 0;
  elementStart = 0;

  constructor(options: StreamIndexOptions) {
    const checkpoint = options.checkpointInterval ?? 0, points = options.pointInterval ?? 0;
    if (!Number.isSafeInteger(checkpoint) || checkpoint < 0) throw new RangeError('checkpointInterval is a number of entries, 0 or more.');
    if (!Number.isSafeInteger(points) || points < 0) throw new RangeError('pointInterval is a number of elements, 0 or more.');
    this.hasher = options.hasher;
    this.observer = options.observer;
    this.checkpointInterval = checkpoint;
    this.pointInterval = points;
    this.writer = options.writer ?? '@lsmsgpack/client';
    if (options.metadata !== undefined) {
      const w = new Writer(64, undefined, false);
      writeMetadata(w, options.metadata); // throws now for values that are not plain
      this.metadata = w.take();
    }
  }

  feed(bytes: Uint8Array): void {
    if (bytes.length === 0) return;
    this.hasher?.append(bytes);
    this.observer?.append?.(bytes);
  }

  /** A point at `element`, which starts at `position`; a second one at the same element replaces it (keeping a key). */
  addPoint(element: number, position: number, key: unknown): void {
    const entry = this.entry!;
    const points = entry.points ?? (entry.points = []);
    const offset = position - entry.offset;
    const last = points[points.length - 1];
    if (last !== undefined && last.element === element) points[points.length - 1] = withKey({ element, offset }, key ?? last.key);
    else points.push(withKey({ element, offset }, key));
  }
}

function withKey(point: { element: number; offset: number; key?: unknown }, key: unknown) {
  if (key !== undefined && key !== null) point.key = key;
  return point;
}

export class MsgPackStreamWriter {
  private readonly w: Writer;
  private readonly maxDepth: number;
  private readonly flushThreshold: number;
  private readonly leaveOpen: boolean;
  private readonly send: (chunk: Uint8Array) => void;
  private readonly finish: () => Promise<void>;
  private readonly ix?: IndexState;
  private frames: Frame[] = [];
  private flushed = 0;
  private pendingHigh = ''; // a high surrogate at the end of a chunk, written with the next one (a chunk holds whole UTF-8 sequences)
  private pending: Promise<void> = Promise.resolve();
  private error: unknown;
  private faulted = false; // the sink failed: close writes no index
  private closed = false;

  constructor(sink: StreamSink, options: StreamWriterOptions = {}) {
    this.maxDepth = options.maxDepth ?? 256;
    this.w = new Writer(this.maxDepth, options.models, options.generateCode ?? true);
    this.flushThreshold = options.flushThreshold ?? 64 * 1024;
    this.leaveOpen = options.leaveOpen ?? false;
    if (options.index !== undefined) this.ix = new IndexState(options.index);

    if (typeof sink === 'function') {
      this.send = (chunk) => {
        this.pending = this.pending.then(() => (this.error === undefined ? sink(chunk) : undefined)).catch((e) => this.fail(e));
      };
      this.finish = async () => {};
    } else if (typeof WritableStream !== 'undefined' && sink instanceof WritableStream) {
      const writer = sink.getWriter();
      this.send = (chunk) => {
        this.pending = this.pending.then(() => (this.error === undefined ? writer.write(chunk) : undefined)).catch((e) => this.fail(e));
      };
      this.finish = async () => {
        if (this.leaveOpen) writer.releaseLock();
        else if (this.error === undefined) await writer.close();
        else await writer.abort(this.error).catch(() => undefined);
      };
    } else throw new TypeError('The sink of a stream writer is a WritableStream or a function that takes the chunks.');
  }

  private fail(e: unknown): void {
    if (this.error === undefined) this.error = e;
    this.faulted = true;
  }

  /** 0 between root values. */
  get depth(): number {
    return this.frames.length;
  }

  /** The number of bytes written since the writer was created, including the ones still in the buffer. */
  get position(): number {
    return this.flushed + this.w.length;
  }

  /** Resolves when the sink took the chunks passed to it so far: await it now and then (between rows) so the buffered chunks do not pile up. Rejects when the sink failed. */
  get ready(): Promise<void> {
    return this.pending.then(() => {
      if (this.error !== undefined) throw this.error;
    });
  }

  // ---- Values ----

  /**
   * Any value as encode writes it (models included): null and undefined as nil, numbers, bigints, strings, Date, Uint8Array, MsgPackExtension, arrays, Maps and objects.
   * In a str of unknown length a string is its next chunk, in a bin of unknown length a Uint8Array.
   */
  write(value: unknown): void {
    if (typeof value === 'string' && this.inFrame(Kind.String)) return this.writeStringChunk(value);
    if (value instanceof Uint8Array && this.inFrame(Kind.Binary)) return this.writeBinary(value);
    this.value();
    const start = this.w.length;
    try {
      this.w.write(value, this.frames.length);
    } catch (e) {
      this.w.truncate(start); // nothing of a value that cannot be written
      throw e;
    }
    this.wrote();
  }

  writeNil(): void {
    this.write(null);
  }

  /** A bin. In a bin of unknown length (writeBinaryStart) the next chunk. */
  writeBinary(bytes: Uint8Array): void {
    if (this.inFrame(Kind.Binary)) {
      this.addBytes(bytes.length);
      this.w.bin(bytes);
      this.flushIfFull();
      return;
    }
    this.value();
    this.w.bin(bytes);
    this.wrote();
  }

  /** An extension value: its type code (-128 to 127) and data. */
  writeExtension(type: number, data: Uint8Array): void {
    if (!Number.isInteger(type) || type < -128 || type > 127) throw new RangeError('An extension type is an integer from -128 to 127.');
    this.value();
    this.w.ext(type, data);
    this.wrote();
  }

  /** One complete MsgPack value that is already encoded, counted as one value. The bytes are not checked. */
  writeRaw(bytes: Uint8Array): void {
    this.value();
    this.w.raw(bytes);
    this.wrote();
  }

  // ---- Arrays and maps ----

  /** An array of `count` elements: the next `count` values (it ends by itself after them, without writeEnd). */
  writeArrayHeader(count: number): void {
    checkCount(count);
    this.value();
    this.checkDepth();
    this.w.arrayHeader(count);
    this.count();
    this.push(Kind.Array, false, count);
    this.done();
  }

  /** A map of `count` entries: the next 2 × `count` values are its keys and values (it ends by itself after them). */
  writeMapHeader(count: number): void {
    checkCount(count);
    this.value();
    this.checkDepth();
    this.w.mapHeader(count);
    this.count();
    this.push(Kind.Map, false, 2 * count);
    this.done();
  }

  /** Starts an array of unknown length: the values that follow are its elements, until writeEnd. */
  writeArrayStart(): void {
    this.start(Kind.Array, Escape.ArrayStart);
  }

  /** Starts a map of unknown length: the values that follow are its keys and values, until writeEnd (where the next key would come). */
  writeMapStart(): void {
    this.start(Kind.Map, Escape.MapStart);
  }

  /** Starts a str of unknown length: the strings that follow (write, writeStringChunk) are its chunks, until writeEnd. */
  writeStringStart(): void {
    this.start(Kind.String, Escape.StringStart);
  }

  /** Starts a bin of unknown length: the Uint8Arrays that follow (write, writeBinary) are its chunks, until writeEnd. */
  writeBinaryStart(): void {
    this.start(Kind.Binary, Escape.BinaryStart);
  }

  /** The next chunk of a str of unknown length. A high surrogate at the end waits for the next chunk, so a chunk never splits a character (as the format requires). */
  writeStringChunk(text: string): void {
    if (!this.inFrame(Kind.String)) throw new Error('writeStringChunk writes the chunks of a str of unknown length, start it with writeStringStart.');
    if (text.length === 0) return;
    if (this.pendingHigh !== '') {
      text = this.pendingHigh + text;
      this.pendingHigh = '';
    }
    const last = text.charCodeAt(text.length - 1);
    if (last >= 0xd800 && last <= 0xdbff) {
      this.pendingHigh = text[text.length - 1];
      text = text.slice(0, -1);
      if (text.length === 0) return;
    }
    this.chunk(text);
    this.flushIfFull();
  }

  private chunk(text: string): void {
    const start = this.w.length;
    this.w.string(text);
    const b = this.w.bytesView(start, start + 1)[0];
    const header = b >= 0xa0 && b <= 0xbf ? 1 : b === 0xd9 ? 2 : b === 0xda ? 3 : 5;
    try {
      this.addBytes(this.w.length - start - header);
    } catch (e) {
      this.w.truncate(start);
      throw e;
    }
  }

  /** Ends the innermost array, map, str or bin of unknown length. */
  writeEnd(): void {
    this.throwIfClosed();
    const frame = this.frames[this.frames.length - 1];
    if (frame === undefined) throw new Error('There is nothing to end: no array, map, str or bin of unknown length is open.');
    if (!frame.unknown)
      throw new Error(`The open ${KindNames[frame.kind]} has a known length, it ends by itself after ${frame.count} more ${frame.kind === Kind.Map ? 'keys and values' : 'elements'}. writeEnd ends the ones of unknown length (writeArrayStart, writeMapStart, writeStringStart, writeBinaryStart).`);
    if (frame.kind === Kind.Map && (frame.count & 1) !== 0) throw new Error('The map of unknown length ends after a key: write its value first.');
    if (frame.kind === Kind.String && this.pendingHigh !== '') { // a high surrogate without its low one: written as the encoder does (U+FFFD)
      const high = this.pendingHigh;
      this.pendingHigh = '';
      this.chunk(high);
    }
    this.w.u8(Escape.Byte);
    this.w.u8(Escape.End);
    this.frames.pop();
    this.done();
  }

  private start(kind: Kind, code: number): void {
    this.value();
    this.checkDepth();
    this.w.u8(Escape.Byte);
    this.w.u8(code);
    this.count();
    this.push(kind, true, 0);
    this.done();
  }

  // ---- The index ----

  /**
   * The index entry of the next root value, to name it and add to it (modified, metadata...) before or while it is written; the writer fills in where it is.
   * A root value without it gets an entry without a name.
   * @param identifier a string or an integer
   * @param contentType what the entry holds (a MIME type, the name of the root type)
   */
  beginEntry(identifier?: string | number | bigint, contentType?: string): StreamEntry {
    this.throwIfClosed();
    if (this.ix === undefined) throw new Error('The writer keeps no index: create it with the index option.');
    if (this.ix.entry !== undefined) throw new Error('An entry begins before a root value (depth 0), not in one.');
    writeIdentifier(new Writer(8, undefined, false), identifier); // checks it
    const entry: StreamEntry = { offset: 0, length: 0 };
    if (identifier !== undefined) entry.identifier = identifier;
    if (contentType !== undefined) entry.contentType = contentType;
    this.ix.next = entry;
    return entry;
  }

  /** Adds a point to the sparse index of the current entry: where the next element of its array starts, e.g. where the key the elements are ordered by changes. */
  addPoint(key?: unknown): void {
    this.throwIfClosed();
    if (this.ix === undefined) throw new Error('The writer keeps no index: create it with the index option.');
    if (this.frames.length !== 1 || !this.ix.rootArray || this.ix.entry === undefined)
      throw new Error('A point is added between the elements of an array that is a root value (depth 1), before the element it points to.');
    if (key !== undefined && key !== null) writePlain(new Writer(8, undefined, false), key, 1); // checks it
    this.ix.addPoint(this.ix.elements, this.position, key);
  }

  private startEntry(): void {
    const ix = this.ix!;
    const entry = ix.next ?? { offset: 0, length: 0 };
    ix.next = undefined;
    entry.offset = this.position;
    ix.entry = entry;
    ix.feedFrom = this.w.length;
    ix.elements = 0;
    ix.rootArray = false;
  }

  private countElement(): void {
    const ix = this.ix!;
    const element = ix.elements++;
    if (ix.pointInterval > 0 && element % ix.pointInterval === 0) ix.addPoint(element, ix.elementStart, undefined);
  }

  /** The root value of the entry is complete (or the writer is closed in the middle of it): its length, hash and points, the observer's sidecar, encoded for the next index. */
  private endEntry(incomplete: boolean): void {
    const ix = this.ix!;
    const entry = ix.entry!;
    ix.entry = undefined; // the sidecar and an index written below are no part of the entry
    ix.feed(this.w.bytesView(ix.feedFrom));
    entry.length = this.position - entry.offset;
    if (ix.hasher !== undefined) entry.hash = ix.hasher.digest();
    if (ix.rootArray) {
      entry.elementCount = ix.elements;
      const points = entry.points;
      while (points !== undefined && points.length > 0 && points[points.length - 1].element >= ix.elements) points.pop(); // added before an element that did not come
    }
    if (incomplete) entry.incomplete = true;

    if (!incomplete && ix.observer?.entryEnded !== undefined) {
      const sidecar = ix.observer.entryEnded(entry);
      if (sidecar instanceof Uint8Array && sidecar.length > 0) {
        this.w.ext(SidecarExtensionType, sidecar);
        entry.sidecarOffset = this.position - sidecar.length;
        entry.sidecarLength = sidecar.length;
      }
    }

    try {
      writeEntry(ix.entries, entry);
    } catch (e) {
      if (!incomplete) throw e;
      delete entry.metadata; // close still writes the index: the entry without its metadata
      writeEntry(ix.entries, entry);
    }
    ix.entryCount++;

    if (!incomplete && ix.checkpointInterval > 0 && ix.entryCount >= ix.checkpointInterval) {
      this.writeIndex(StreamIndexStatus.Checkpoint);
      this.flushBuffer(); // a checkpoint is only worth something in the sink
    }
  }

  /** The index of the entries since the last one, then its tail, between root values. */
  private writeIndex(status: StreamIndexStatus): void {
    const ix = this.ix!;
    const content = new Writer(64, undefined, false);
    writeIndex(content, status, ix.writer, ix.previousTail, ix.hasher?.algorithm, ix.entryCount, ix.entries.bytesView(), ix.metadata);
    const offset = this.position;
    const start = this.w.length;
    this.w.ext(IndexExtensionType, content.bytesView());
    const length = this.w.length - start;
    const crc = crc32(this.w.bytesView(start));
    ix.previousTail = this.position;
    writeTail(this.w, offset, length, crc);
    ix.entries.take();
    ix.entryCount = 0;
  }

  // ---- Bookkeeping ----

  private inFrame(kind: Kind): boolean {
    const n = this.frames.length;
    return n > 0 && this.frames[n - 1].kind === kind;
  }

  /** A value may come here: not in a str or bin of unknown length (only their chunks), not more than an array of unknown length holds. */
  private value(): void {
    this.throwIfClosed();
    const n = this.frames.length;
    if (n === 0) {
      if (this.ix !== undefined && this.ix.entry === undefined) this.startEntry();
      return;
    }
    const frame = this.frames[n - 1];
    if (frame.kind === Kind.String) throw new Error('A str of unknown length holds only its chunks: strings (write, writeStringChunk), then writeEnd.');
    if (frame.kind === Kind.Binary) throw new Error('A bin of unknown length holds only its chunks: Uint8Arrays (write, writeBinary), then writeEnd.');
    if (frame.unknown && frame.count >= (frame.kind === Kind.Map ? 2 * MaxUnknownCount : MaxUnknownCount))
      throw new Error(`${frame.kind === Kind.Map ? 'A map' : 'An array'} of unknown length holds at most ${MaxUnknownCount} ${frame.kind === Kind.Map ? 'entries' : 'elements'}.`);
    if (n === 1 && this.ix !== undefined) this.ix.elementStart = this.position;
  }

  /** The value was written: it is an element of the open array or map. */
  private count(): void {
    const n = this.frames.length;
    if (n === 0) return;
    if (n === 1 && this.ix !== undefined && this.ix.rootArray) this.countElement();
    const frame = this.frames[n - 1];
    if (frame.unknown) frame.count++;
    else frame.count--;
  }

  private wrote(): void {
    this.count();
    this.done();
  }

  /** Closes the arrays and maps of known length that have all their elements, and passes the buffer to the sink when it is long enough. */
  private done(): void {
    const frames = this.frames;
    while (frames.length > 0 && !frames[frames.length - 1].unknown && frames[frames.length - 1].count === 0) frames.pop();
    if (frames.length === 0 && this.ix !== undefined && this.ix.entry !== undefined) this.endEntry(false);
    this.flushIfFull();
  }

  private flushIfFull(): void {
    if (this.w.length >= this.flushThreshold) this.flushBuffer();
  }

  private addBytes(count: number): void {
    this.throwIfClosed();
    const frame = this.frames[this.frames.length - 1];
    if (frame.count + count > MaxUnknownCount) throw new Error(`A ${KindNames[frame.kind]} of unknown length holds at most ${MaxUnknownCount} bytes.`);
    frame.count += count;
  }

  private checkDepth(): void {
    if (this.frames.length >= this.maxDepth) throw new Error(`The values are nested deeper than maxDepth (${this.maxDepth}), readers refuse them.`);
  }

  private push(kind: Kind, unknown: boolean, count: number): void {
    if (this.frames.length === 0 && this.ix !== undefined) this.ix.rootArray = kind === Kind.Array;
    this.frames.push({ kind, unknown, count });
  }

  private throwIfClosed(): void {
    if (this.closed) throw new Error('The stream writer is closed.');
    if (this.error !== undefined) throw this.error;
  }

  // ---- Output ----

  private flushBuffer(): void {
    if (this.w.length === 0) return;
    const ix = this.ix;
    if (ix !== undefined && ix.entry !== undefined) {
      ix.feed(this.w.bytesView(ix.feedFrom));
      ix.feedFrom = 0;
    }
    const chunk = this.w.take();
    this.flushed += chunk.length;
    this.send(chunk);
  }

  /** Passes the buffer to the sink and waits until it took everything. */
  async flush(): Promise<void> {
    this.throwIfClosed();
    this.flushBuffer();
    await this.ready;
  }

  /**
   * Writes the index (when the writer keeps one), passes the buffer to the sink, waits for it and closes a WritableStream (unless leaveOpen).
   * Arrays and maps that are still open are not ended: a reader finds the data cut off. With an index: Complete between root values,
   * Interrupted (that entry incomplete) in the middle of one. No index after the sink failed.
   */
  async close(): Promise<void> {
    if (this.closed) return;
    this.closed = true;
    try {
      const ix = this.ix;
      if (ix !== undefined && !this.faulted) {
        let status: StreamIndexStatus = StreamIndexStatus.Complete;
        if (ix.entry !== undefined && this.frames.length === 0 && this.position === ix.entry.offset) ix.entry = undefined; // a root value that was refused (nothing written)
        if (ix.entry !== undefined) {
          this.endEntry(true);
          status = StreamIndexStatus.Interrupted;
        }
        this.writeIndex(status);
      }
      this.flushBuffer();
      await this.pending;
      if (this.error !== undefined) throw this.error;
    } finally {
      await this.finish();
    }
  }
}

function checkCount(count: number): void {
  if (!Number.isSafeInteger(count) || count < 0 || count > MaxUnknownCount) throw new RangeError('A count is an integer from 0 to 2^32-1.');
}
