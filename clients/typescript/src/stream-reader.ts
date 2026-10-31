// Reads MsgPack values one after another from a stream (a fetch body, a ReadableStream, a file, a Blob), as LtMsgPack's MsgPackStreamReader:
// tokens, values read whole, objects with their schema (inline or a reference, see docs/Streaming.md), the schema of a head for the root value that follows it,
// values of unknown length (the escape sequences of msgpack/msgpack#347), and the index at the end of the stream (with a random access source).
//
// Everything is decoded synchronously from one buffer that only grows as data arrives; the reader awaits only to refill it. A value read whole is first decoded
// from the bytes buffered so far: only when they end inside it (or it has values of unknown length) is it passed token by token, then decoded once.

import { Decoder, DecodeOptions } from './decoder.js';
import { EndOfDataError, EscapeError, MissingSchemaError, MsgPackError } from './errors.js';
import { MsgPackExtension } from './reader.js';
import { AnyType, Schema, SchemaEntry, TypeExpr } from './schema.js';
import { toHex } from './sha256.js';
import { Escape, escapeAtEnd, hex, misplacedEnd, plain, reservedEscape } from './stream-escape.js';
import { extensionHeaderLength, IndexExtensionType, parseTail, readAll, SidecarExtensionType, StreamEntry, StreamIndex, StreamIndexPoint, TailLength } from './stream-index.js';
import { pullOf, Pull, RandomAccessSource, randomAccessOf, StreamSource } from './stream-sources.js';
import { SchemaStore } from './store.js';

export type StreamTokenType = 'none' | 'nil' | 'boolean' | 'integer' | 'float' | 'string' | 'binary' | 'extension'
  | 'arrayStart' | 'mapStart' | 'arrayEnd' | 'mapEnd' | 'stringStart' | 'binaryStart' | 'stringEnd' | 'binaryEnd';

/** The declared type of a value: a class of the models, the type name of a schema entry, or a type expression of the described schema. */
export type StreamType = (abstract new (...args: never[]) => object) | string | TypeExpr;

export interface StreamReaderOptions extends DecodeOptions {
  /** The decoder (e.g. a client's): its options win over the decode options here. */
  decoder?: Decoder;
  /** Where schemas are found (references) and kept (inline schemas of the stream, the schemas the index locates). Default: a new store. */
  store?: SchemaStore;
}

const NeedMore = Symbol('need more data'); // thrown by the synchronous parts when the buffer ends: the caller refills and calls again
const MaxUnknownCount = 0xffffffff;
const Kind = { Array: 0, Map: 1, String: 2, Binary: 3 } as const;
type Kind = (typeof Kind)[keyof typeof Kind];
const Head = { None: 0, AwaitingBody: 1, InBody: 2 } as const;
type Head = (typeof Head)[keyof typeof Head];
const twoPow32 = 4294967296;

interface Frame {
  kind: Kind;
  unknown: boolean;
  /** Known length: the values (keys and values for a map) still to come. Unknown: the ones read (bytes for a str or bin). */
  count: number;
  /** Known length: all of them. */
  size: number;
  /** The index of its count in `counts` while a value is read whole, -1 otherwise. */
  countIndex: number;
  /** In the body of a head: the declared type of the array or map (the types of its elements follow from it). */
  type?: TypeExpr;
  /** A map of an object (the map layout): the last key, the property index of the value that follows. */
  key?: number;
}

export class MsgPackStreamReader {
  readonly decoder: Decoder;
  readonly store: SchemaStore;
  private readonly random?: RandomAccessSource;
  private readonly maxDepth: number;
  private pull: Pull;

  private buf = new Uint8Array(16 * 1024);
  private view = new DataView(this.buf.buffer);
  private pos = 0;
  private end = 0;
  private base = 0; // the position of buf[0] in the stream
  private eof = false;
  private closed = false;

  private mark = -1;
  private escaped = false; // escape sequences since the mark
  private counts: number[] = []; // per value of unknown length since the mark (in the order of their starts): its number of elements, entries or bytes
  private passStarted = false; // passValue read the first token of the value

  private token: StreamTokenType = 'none';
  private tokenStartAt = 0;
  private tokenLength = 0;
  private payloadPending = false; // the data of a string, binary or extension token (from pos, tokenLength bytes) has not been passed yet
  private tailPending = false; // an index of the stream was passed, its tail not yet
  private skippingStreamValue = false; // the pending data is of an index or sidecar being skipped, not of the current token
  private integer: number | bigint = 0;
  private float = 0;
  private bool = false;
  private extensionType = 0;
  private key = false;
  private frames: Frame[] = [];

  private head: Head = Head.None;
  private headSchema: Schema | null = null; // null without the indexed schema
  private headRoot?: TypeExpr;
  private readonly inline: Schema[] = []; // the inline schemas of the stream (one instance per schema: its plans are made once)
  private readonly types = new Map<unknown, TypeExpr>();

  /**
   * @param source a fetch body (Response), ReadableStream, (async) iterable of chunks, bytes, a Blob or File, or a RandomAccessSource (httpSource, fileSource):
   * bytes, Blobs and random access sources also read the index and seek.
   */
  constructor(source: StreamSource, options: StreamReaderOptions = {}) {
    this.decoder = options.decoder ?? new Decoder(options);
    this.store = options.store ?? new SchemaStore();
    this.maxDepth = options.maxDepth ?? 256;
    this.random = randomAccessOf(source);
    this.pull = pullOf(this.random !== undefined ? this.random : source);
  }

  // ---- State ----

  get tokenType(): StreamTokenType {
    return this.token;
  }

  /** The number of elements or entries (-1: unknown), or of bytes of a str, bin or extension. */
  get length(): number {
    return this.tokenLength;
  }

  get depth(): number {
    return this.frames.length;
  }

  /** The current token is a key of a map. */
  get isKey(): boolean {
    return this.key;
  }

  /** Where the current token starts in the stream. */
  get tokenStart(): number {
    return this.base + this.tokenStartAt;
  }

  /** Where the current token ends in the stream (after its data). */
  get position(): number {
    return this.base + this.pos + (this.payloadPending ? this.tokenLength : 0);
  }

  /**
   * There are no more values here: the innermost open array, map, str or bin ends with the next token (its end sequence, or it has all its elements),
   * or at depth 0 the stream ends. For loops over the elements of an array of unknown length, or the root values of a stream.
   */
  atEnd(): Promise<boolean> {
    return this.run(() => this.atEndSync());
  }

  private atEndSync(): boolean {
    this.throwIfClosed();
    if (this.skippingStreamValue) this.passPending();
    const frame = this.frames[this.frames.length - 1];
    if (frame !== undefined && !frame.unknown) return frame.count === 0;
    const next = this.payloadPending ? this.tokenLength : 0; // after the data of the current token
    if (frame === undefined) {
      if (!this.payloadPending && this.mark < 0) {
        this.skipStreamValues();
        return !this.tryEnsure(1);
      }
      return !this.tryEnsure(this.pastStreamValues(next) + 1);
    }
    return this.tryEnsure(next + 2) && this.buf[this.pos + next] === Escape.Byte && this.buf[this.pos + next + 1] === Escape.End;
  }

  // ---- Tokens ----

  /** Moves to the next token (the data of the current one is passed without being buffered). False at the end of the stream between root values. */
  read(): Promise<boolean> {
    return this.run(() => this.readWithHead());
  }

  private readWithHead(): boolean {
    if (this.head === Head.None) return this.readToken();
    const depth = this.frames.length;
    const read = this.readToken();
    if (this.head === Head.AwaitingBody) {
      if (read && depth === 0) this.head = this.frames.length > 0 ? Head.InBody : Head.None; // the body starts (a scalar is all of it)
    } else if (this.frames.length === 0) this.head = Head.None; // the end of the body
    if (this.head === Head.None) this.headSchema = null;
    return read;
  }

  private readToken(): boolean {
    this.throwIfClosed();
    this.passPending();
    this.tokenLength = 0;
    this.key = false;
    this.tokenStartAt = this.pos;

    const frame = this.frames[this.frames.length - 1];
    if (frame !== undefined && !frame.unknown && frame.count === 0) { // after the last element of an array or map of known length
      this.token = frame.kind === Kind.Map ? 'mapEnd' : 'arrayEnd';
      this.frames.pop();
      return true;
    }

    if (frame === undefined && this.mark < 0) {
      this.skipStreamValues();
      this.tokenStartAt = this.pos;
    }
    if (!this.tryEnsure(1)) {
      if (frame !== undefined) throw this.endOfData();
      this.token = 'none';
      return false;
    }
    const b = this.buf[this.pos];
    if (b === Escape.Byte) this.readEscape();
    else this.readFormat(b);
    return true;
  }

  private readEscape(): void {
    if (!this.tryEnsure(2)) throw escapeAtEnd(this.base + this.pos);
    const code = this.buf[this.pos + 1];
    if (code === Escape.End) {
      const frame = this.frames[this.frames.length - 1];
      if (frame === undefined) throw misplacedEnd('is outside of any value of unknown length', this.base + this.pos);
      if (!frame.unknown) throw misplacedEnd(`is in ${frame.kind === Kind.Map ? 'a map' : 'an array'} of known length`, this.base + this.pos);
      if (frame.kind === Kind.Map && (frame.count & 1) !== 0) throw misplacedEnd('is where the value of a key of the map comes', this.base + this.pos);
      if (this.mark >= 0) this.escaped = true;
      if (frame.countIndex >= 0) this.counts[frame.countIndex] = frame.kind === Kind.Map ? frame.count / 2 : frame.count;
      this.pos += 2;
      this.frames.pop();
      this.token = frame.kind === Kind.Array ? 'arrayEnd' : frame.kind === Kind.Map ? 'mapEnd' : frame.kind === Kind.String ? 'stringEnd' : 'binaryEnd';
      return;
    }

    let kind: Kind;
    let token: StreamTokenType;
    switch (code) {
      case Escape.ArrayStart: kind = Kind.Array; token = 'arrayStart'; break;
      case Escape.MapStart: kind = Kind.Map; token = 'mapStart'; break;
      case Escape.StringStart: kind = Kind.String; token = 'stringStart'; break;
      case Escape.BinaryStart: kind = Kind.Binary; token = 'binaryStart'; break;
      default: throw reservedEscape(code, this.base + this.pos);
    }
    this.throwIfInChunks(Escape.Byte);
    this.checkDepth();
    if (this.mark >= 0) this.escaped = true;
    const type = this.typed() ? this.typeOfNext() : undefined;
    this.element();
    this.token = token;
    this.tokenLength = -1;
    this.pos += 2;
    this.push(kind, true, 0, type);
  }

  private readFormat(b: number): void {
    this.need(formatHeader(b));
    const frame = this.frames[this.frames.length - 1];
    if (frame !== undefined && (frame.kind === Kind.String || frame.kind === Kind.Binary)) return this.chunk(b, frame);
    const p = this.pos, view = this.view;

    if (b <= 0x7f) return this.scalar('integer', b, 1);
    if (b >= 0xe0) return this.scalar('integer', b - 256, 1);
    if ((b & 0xe0) === 0xa0) return this.payload('string', 1, b & 0x1f);
    if ((b & 0xf0) === 0x90) return this.container(Kind.Array, 1, b & 0x0f);
    if ((b & 0xf0) === 0x80) return this.container(Kind.Map, 1, b & 0x0f);
    switch (b) {
      case 0xc0: return this.scalar('nil', 0, 1);
      case 0xc2: case 0xc3: this.bool = b === 0xc3; return this.scalar('boolean', 0, 1);
      case 0xcc: return this.scalar('integer', this.buf[p + 1], 2);
      case 0xcd: return this.scalar('integer', view.getUint16(p + 1), 3);
      case 0xce: return this.scalar('integer', view.getUint32(p + 1), 5);
      case 0xcf: return this.scalar('integer', int64(view, p + 1, false), 9);
      case 0xd0: return this.scalar('integer', view.getInt8(p + 1), 2);
      case 0xd1: return this.scalar('integer', view.getInt16(p + 1), 3);
      case 0xd2: return this.scalar('integer', view.getInt32(p + 1), 5);
      case 0xd3: return this.scalar('integer', int64(view, p + 1, true), 9);
      case 0xca: this.float = view.getFloat32(p + 1); return this.scalar('float', 0, 5);
      case 0xcb: this.float = view.getFloat64(p + 1); return this.scalar('float', 0, 9);
      case 0xd9: return this.payload('string', 2, this.buf[p + 1]);
      case 0xda: return this.payload('string', 3, view.getUint16(p + 1));
      case 0xdb: return this.payload('string', 5, view.getUint32(p + 1));
      case 0xc4: return this.payload('binary', 2, this.buf[p + 1]);
      case 0xc5: return this.payload('binary', 3, view.getUint16(p + 1));
      case 0xc6: return this.payload('binary', 5, view.getUint32(p + 1));
      case 0xdc: return this.container(Kind.Array, 3, view.getUint16(p + 1));
      case 0xdd: return this.container(Kind.Array, 5, view.getUint32(p + 1));
      case 0xde: return this.container(Kind.Map, 3, view.getUint16(p + 1));
      case 0xdf: return this.container(Kind.Map, 5, view.getUint32(p + 1));
      case 0xd4: return this.extension(2, 1);
      case 0xd5: return this.extension(2, 2);
      case 0xd6: return this.extension(2, 4);
      case 0xd7: return this.extension(2, 8);
      case 0xd8: return this.extension(2, 16);
      case 0xc7: return this.extension(3, this.buf[p + 1]);
      case 0xc8: return this.extension(4, view.getUint16(p + 1));
      case 0xc9: return this.extension(6, view.getUint32(p + 1));
    }
    throw new EscapeError(this.base + p); // unreachable: 0xC1 is the escape byte
  }

  /** A chunk of a str or bin of unknown length: a str or bin, nothing else. */
  private chunk(b: number, frame: Frame): void {
    const p = this.pos, view = this.view;
    let header: number, length: number;
    if (frame.kind === Kind.String) {
      if ((b & 0xe0) === 0xa0) { header = 1; length = b & 0x1f; }
      else if (b === 0xd9) { header = 2; length = this.buf[p + 1]; }
      else if (b === 0xda) { header = 3; length = view.getUint16(p + 1); }
      else if (b === 0xdb) { header = 5; length = view.getUint32(p + 1); }
      else throw new MsgPackError(`A str of unknown length holds only str chunks, found the format 0x${hex(b)}.`, this.base + p);
    } else {
      if (b === 0xc4) { header = 2; length = this.buf[p + 1]; }
      else if (b === 0xc5) { header = 3; length = view.getUint16(p + 1); }
      else if (b === 0xc6) { header = 5; length = view.getUint32(p + 1); }
      else throw new MsgPackError(`A bin of unknown length holds only bin chunks, found the format 0x${hex(b)}.`, this.base + p);
    }
    if (frame.count + length > MaxUnknownCount)
      throw new MsgPackError(`A ${frame.kind === Kind.String ? 'str' : 'bin'} of unknown length holds at most ${MaxUnknownCount} bytes.`, this.base + p);
    frame.count += length;
    this.token = frame.kind === Kind.String ? 'string' : 'binary';
    this.pos += header;
    this.tokenLength = length;
    this.payloadPending = length > 0;
  }

  private throwIfInChunks(b: number): void {
    const frame = this.frames[this.frames.length - 1];
    if (b === Escape.Byte && frame !== undefined && (frame.kind === Kind.String || frame.kind === Kind.Binary)) {
      const name = frame.kind === Kind.String ? 'str' : 'bin';
      throw new MsgPackError(`A ${name} of unknown length holds only ${name} chunks, not values of unknown length.`, this.base + this.pos);
    }
  }

  /** The token starts an element of the open array or map. */
  private element(): void {
    const frame = this.frames[this.frames.length - 1];
    if (frame === undefined) return;
    this.key = frame.kind === Kind.Map && (frame.count & 1) === 0; // known: the keys and values still to come, unknown: the ones read
    if (!frame.unknown) frame.count--;
    else {
      if (frame.count >= (frame.kind === Kind.Map ? 2 * MaxUnknownCount : MaxUnknownCount))
        throw new MsgPackError(`${frame.kind === Kind.Map ? 'A map' : 'An array'} of unknown length holds at most ${MaxUnknownCount} ${frame.kind === Kind.Map ? 'entries' : 'elements'}.`, this.base + this.pos);
      frame.count++;
    }
  }

  private scalar(token: StreamTokenType, value: number | bigint, size: number): void {
    const frame = this.frames[this.frames.length - 1];
    this.element();
    this.token = token;
    if (token === 'integer') {
      this.integer = value;
      if (this.key && frame?.type !== undefined && typeof value === 'number') frame.key = value; // a property index of an object in the map layout
    }
    this.pos += size;
  }

  private payload(token: StreamTokenType, header: number, length: number): void {
    this.element();
    this.token = token;
    this.pos += header;
    this.tokenLength = length;
    this.payloadPending = length > 0;
  }

  private extension(header: number, length: number): void {
    this.element();
    this.extensionType = this.view.getInt8(this.pos + header - 1);
    this.token = 'extension';
    this.pos += header;
    this.tokenLength = length;
    this.payloadPending = length > 0;
  }

  private container(kind: Kind, header: number, count: number): void {
    this.checkDepth();
    const type = this.typed() ? this.typeOfNext() : undefined;
    this.element();
    this.token = kind === Kind.Map ? 'mapStart' : 'arrayStart';
    this.tokenLength = count;
    this.pos += header;
    this.push(kind, false, kind === Kind.Map ? 2 * count : count, type);
  }

  private checkDepth(): void {
    if (this.frames.length >= this.maxDepth) throw new MsgPackError(`The data is nested deeper than maxDepth (${this.maxDepth}).`, this.base + this.pos);
  }

  private push(kind: Kind, unknown: boolean, count: number, type: TypeExpr | undefined): void {
    let countIndex = -1;
    if (unknown && this.mark >= 0) {
      countIndex = this.counts.length;
      this.counts.push(0);
    }
    const frame: Frame = { kind, unknown, count, size: count, countIndex };
    if (type !== undefined) frame.type = type;
    this.frames.push(frame);
  }

  /** Passes the current array, map, str or bin to its end without buffering it (any other value is passed by the next read anyway). */
  skip(): Promise<void> {
    if (!isStart(this.token)) return Promise.resolve();
    const depth = this.frames.length;
    return this.run(() => {
      while (this.frames.length >= depth) this.readWithHead();
    });
  }

  // ---- Values of the current token ----

  getBoolean(): boolean {
    this.expect('boolean');
    return this.bool;
  }

  /** An integer: a number, or a bigint beyond 2^53. */
  getInteger(): number | bigint {
    this.expect('integer');
    return this.integer;
  }

  /** An integer or a float as a number (precision is lost beyond 2^53). */
  getNumber(): number {
    if (this.token === 'float') return this.float;
    this.expect('integer');
    return Number(this.integer);
  }

  getBigInt(): bigint {
    this.expect('integer');
    return BigInt(this.integer);
  }

  /** A string; at a stringStart the whole str of unknown length (the reader is then at its end). */
  async getString(): Promise<string> {
    if (this.token !== 'string' && this.token !== 'stringStart') this.expect('string');
    return (await this.getValue()) as string;
  }

  /** The bytes of a bin; at a binaryStart the whole bin of unknown length. */
  async getBytes(): Promise<Uint8Array> {
    if (this.token !== 'binary' && this.token !== 'binaryStart') this.expect('binary');
    return (await this.getValue()) as Uint8Array;
  }

  /** A timestamp (extension type -1). */
  async getDate(): Promise<Date> {
    if (this.token !== 'extension' || this.extensionType !== -1) throw new Error(`The current token is ${this.token}, not a timestamp.`);
    return (await this.getValue()) as Date;
  }

  /** An extension value as it is: its type code and a copy of its data. */
  async getExtension(): Promise<MsgPackExtension> {
    this.expect('extension');
    await this.run(() => this.bufferPayload());
    return new MsgPackExtension(this.extensionType, this.buf.slice(this.pos, this.pos + this.tokenLength));
  }

  /**
   * The current value as decodePlain reads it: null, booleans, numbers (bigint beyond 2^53), strings, Uint8Array, Date, decimals as the decimal option says,
   * MsgPackExtension, arrays and objects. At an arrayStart, mapStart, stringStart or binaryStart it reads the whole value: the reader is then at its end.
   */
  async getValue(): Promise<unknown> {
    switch (this.token) {
      case 'none': case 'arrayEnd': case 'mapEnd': case 'stringEnd': case 'binaryEnd':
        throw new Error(`The current token (${this.token}) is not a value.`);
    }
    if (isStart(this.token)) return this.readWhole();
    await this.run(() => this.bufferPayload());
    const end = this.pos + this.tokenLength;
    return this.decode(() => this.decoder.readValue(this.buf, this.tokenStartAt, end, null).value, this.tokenStartAt);
  }

  /** The array, map, str or bin that starts at the current token, read whole. */
  private async readWhole(): Promise<unknown> {
    const depth = this.frames.length;
    const frame = this.frames[depth - 1];
    this.mark = this.tokenStartAt;
    this.escaped = frame.unknown;
    this.counts = [];
    if (frame.unknown) { // started before the mark
      frame.countIndex = 0;
      this.counts.push(0);
    }
    try {
      await this.run(() => {
        while (this.frames.length >= depth) this.readWithHead();
        this.passPending();
      });
      const data = this.escaped ? plain(this.buf, this.mark, this.pos, this.counts) : this.buf.subarray(this.mark, this.pos);
      const start = this.mark;
      return this.decode(() => this.decoder.readValue(data, 0, data.length, null).value, start);
    } finally {
      this.mark = -1;
    }
  }

  private expect(token: StreamTokenType): void {
    this.throwIfClosed();
    if (this.token !== token) throw new Error(`The current token is ${this.token}, not ${token}.`);
  }

  // ---- Objects ----

  /**
   * Reads the next value whole, as the decoder reads a payload: with the indexed schema an object is its schema (or a reference to it) followed by the object,
   * so it is read as a root value of the stream (depth 0), unless it is in the body of a head (readHead): then only the value, with the schema of the head, at any depth.
   * Without the indexed schema, anywhere. Values of unknown length in it are read too.
   * @param type the declared type of the value: a class of the models, the type name of a schema entry, or a TypeExpr. Default: in the body of a head, the type of the place
   * as the described schema of the head says (the element type of the list...), otherwise dynamic (values with a type id still become objects of their class); for a root object, the root of its schema.
   */
  async readObject<T = unknown>(type?: StreamType): Promise<T> {
    this.throwIfClosed();
    const declared = this.declaredType(type);
    await this.run(() => this.beforeObject());
    const body = this.frames.length === 0 ? this.head === Head.AwaitingBody : this.head === Head.InBody;
    const place = body && declared === undefined && this.frames.length > 0 ? (this.typeOfNext() ?? AnyType) : declared;

    const fast = this.tryReadWhole(body, place);
    if (fast !== NeedMore) return fast as T;
    return (await this.readWholeSlowly(body, place)) as T;
  }

  /** The checks before an object, and the values of the index before a root value. */
  private beforeObject(): void {
    this.passPending();
    const frame = this.frames[this.frames.length - 1];
    if (frame !== undefined) {
      if (frame.kind === Kind.String || frame.kind === Kind.Binary) throw new Error('A str or bin of unknown length holds only its chunks, not objects.');
      if (this.decoder.usesIndexedSchema && this.head !== Head.InBody)
        throw new Error('With the indexed schema an object is two values (its schema or a reference to it, then the object): readObject reads it as a root value of the stream (depth 0). Objects in an array or map are read with the schema before it (readHead), or with indexedSchema: false.');
      if (this.atEndSync()) throw new Error(`The ${frame.kind === Kind.Map ? 'map' : 'array'} has no more values.`);
    } else this.skipStreamValues();
    if (!this.tryEnsure(1)) throw this.endOfData();
  }

  /** The value decoded from the bytes buffered so far, NeedMore when they end inside it or it has values of unknown length. */
  private tryReadWhole(body: boolean, type: TypeExpr | undefined): unknown {
    const start = this.pos;
    let result: { value: unknown; end: number };
    try {
      result = this.decodeObject(this.buf, start, this.end, body ? this.headSchema : undefined, type);
    } catch (e) {
      if ((e instanceof EndOfDataError && !this.eof) || e instanceof EscapeError) return NeedMore;
      throw moved(e, this.base);
    }
    // the bookkeeping of the tokens of the value: an element of the open array or map, the end of a head's body
    this.element();
    this.pos = result.end;
    this.tokenStartAt = this.pos;
    this.token = 'none';
    this.tokenLength = 0;
    if (this.frames.length === 0 && this.head !== Head.None) {
      this.head = Head.None;
      this.headSchema = null;
    }
    return result.value;
  }

  /** The value passed token by token (it is buffered from the mark, the counts of the values of unknown length kept), then decoded. */
  private async readWholeSlowly(body: boolean, type: TypeExpr | undefined): Promise<unknown> {
    const depth = this.frames.length;
    const headSchema = body ? this.headSchema : undefined; // reading the last token of a head's body ends the head
    this.mark = this.pos;
    this.escaped = false;
    this.counts = [];
    try {
      const schema = this.decoder.usesIndexedSchema && !body && this.buf[this.pos] !== 0xc0; // null is written without a schema
      await this.run(() => this.passValue(depth));
      if (schema) {
        if (this.escaped) throw new MsgPackError('A schema cannot have values of unknown length.', this.base + this.mark);
        await this.run(() => this.passValue(depth));
      }
      const data = this.escaped ? plain(this.buf, this.mark, this.pos, this.counts) : this.buf.subarray(this.mark, this.pos);
      const start = this.mark;
      this.token = 'none';
      this.tokenLength = 0;
      return this.decode(() => {
        const result = this.decodeObject(data, 0, data.length, headSchema, type);
        if (result.end !== data.length) throw new MsgPackError('Data after the value.', result.end);
        return result.value;
      }, start);
    } finally {
      this.mark = -1;
    }
  }

  /** @param headSchema in the body of a head: its schema (null without the indexed schema), otherwise undefined */
  private decodeObject(bytes: Uint8Array, start: number, end: number, headSchema: Schema | null | undefined, type: TypeExpr | undefined): { value: unknown; end: number } {
    if (headSchema !== undefined) return this.decoder.readValue(bytes, start, end, headSchema, type);
    if (!this.decoder.usesIndexedSchema) return this.decoder.readValue(bytes, start, end, null);
    return this.decoder.decodeAt(bytes, start, end, (id) => this.store.get(id), (schemaBytes) => this.inlineSchema(schemaBytes), type);
  }

  /** Passes the next value (resumable: its data stays in the buffer from the mark). */
  private passValue(depth: number): void {
    if (!this.passStarted) {
      if (!this.readWithHead()) throw this.endOfData();
      if (this.token === 'arrayEnd' || this.token === 'mapEnd') throw new Error(`The ${this.token === 'mapEnd' ? 'map' : 'array'} has no more values.`);
      this.passStarted = true;
    }
    while (this.frames.length > depth) this.readWithHead();
    this.passPending();
    this.passStarted = false;
  }

  /** The schema of the inline bytes (a copy: the buffer is reused), one instance per schema, kept in the store so later references to it are read (PerStream). */
  private inlineSchema(bytes: Uint8Array): Schema {
    for (let t = this.inline.length - 1; t >= 0; t--) if (sameBytes(this.inline[t].bytes, bytes)) return this.inline[t];
    const schema = new Schema(bytes.slice());
    this.inline.push(schema);
    this.store.register(schema); // a full store: this object is read (its schema is inline), only later references to it are not
    return schema;
  }

  /**
   * Reads the schema that a head wrote (MsgPackStreamWriter.WriteHead, inline or a reference) for the root value that follows it (its body): the objects in the body
   * are then read without their schema (readObject, at any depth). Any payload of the serializers can be read so too. Without the indexed schema nothing is read.
   * With a described schema the declared types of the places in the body follow from it; with a plain one pass the types to readObject and elements.
   */
  async readHead(): Promise<void> {
    this.throwIfClosed();
    await this.run(() => this.passPending());
    if (this.frames.length > 0) throw new Error('A head is read before a root value of the stream (depth 0).');
    if (this.head === Head.AwaitingBody) throw new Error('The head read before still waits for its body (the next root value).');
    this.head = Head.None;
    this.headSchema = null;
    this.headRoot = undefined;
    if (this.decoder.usesIndexedSchema) {
      await this.run(() => {
        this.skipStreamValues();
        if (!this.tryEnsure(1)) throw this.endOfData();
      });
      this.mark = this.pos;
      this.escaped = false;
      this.counts = [];
      try {
        await this.run(() => this.passValue(0));
        if (this.escaped) throw new MsgPackError('A schema cannot have values of unknown length.', this.base + this.mark);
        this.headSchema = this.schemaAt(this.mark, this.pos);
        this.headRoot = this.headSchema.root;
        this.token = 'none';
        this.tokenLength = 0;
      } finally {
        this.mark = -1;
      }
    }
    this.head = Head.AwaitingBody;
  }

  private schemaAt(start: number, end: number): Schema {
    const b = this.buf[start];
    if (b === 0xd8 && end - start === 18 && this.buf[start + 1] === 2) {
      const id = toHex(this.buf, start + 2, start + 18);
      const schema = this.store.get(id);
      if (schema === undefined) throw new MissingSchemaError(id);
      return schema;
    }
    if ((b >= 0x80 && b <= 0x8f) || b === 0xde || b === 0xdf) return this.decode(() => this.inlineSchema(this.buf.subarray(start, end)), start);
    throw new MsgPackError(`The value is not a schema or a reference to one (0x${hex(b)}).`, this.base + start);
  }

  /**
   * The elements of an array (read with readObject): at its arrayStart, or before it (then it is read first). The reader is at its end afterwards.
   * @param type the declared type of the elements (default: as the described schema of a head says, see readObject)
   */
  async *elements<T = unknown>(type?: StreamType): AsyncGenerator<T, void, undefined> {
    if (this.token !== 'arrayStart') {
      if (!(await this.read())) throw this.endOfData();
      const token = this.tokenType as StreamTokenType; // read() changed it
      if (token !== 'arrayStart') throw new Error(`The value is not an array (${token}).`);
    }
    const declared = this.declaredType(type);
    const depth = this.frames.length;
    for (;;) {
      // the elements already buffered without an await each
      const value = this.tryElement(depth, declared);
      if (value === NeedMore) {
        if (await this.atEnd()) break;
        yield await this.readObject<T>(type);
      } else if (value === End) break;
      else yield value as T;
    }
    await this.read(); // the arrayEnd
  }

  /** The next element when it can be read without waiting, End at the end of the array, NeedMore otherwise. */
  private tryElement(depth: number, declared: TypeExpr | undefined): unknown {
    if (this.frames.length !== depth || this.closed) return NeedMore;
    try {
      if (this.payloadPending || this.atEndSync()) return this.payloadPending ? NeedMore : End;
      const frame = this.frames[depth - 1];
      if (frame.kind !== Kind.Array || (this.decoder.usesIndexedSchema && this.head !== Head.InBody)) return NeedMore; // readObject tells what is wrong
    } catch (e) {
      if (e === NeedMore) return NeedMore;
      throw e;
    }
    const place = declared ?? (this.head === Head.InBody ? (this.typeOfNext() ?? AnyType) : undefined);
    return this.tryReadWhole(this.head === Head.InBody, place);
  }

  /** The root values of the stream (read with readObject) until it ends. A head is read with readHead, its body is then the next value. */
  async *values<T = unknown>(type?: StreamType): AsyncGenerator<T, void, undefined> {
    if (this.frames.length > 0) throw new Error('values() reads the root values of the stream (depth 0).');
    while (!(await this.atEnd())) yield await this.readObject<T>(type);
  }

  // ---- Declared types ----

  private declaredType(type: StreamType | undefined): TypeExpr | undefined {
    if (type === undefined) return undefined;
    let known = this.types.get(type);
    if (known !== undefined) return known;
    if (typeof type === 'string') known = { k: 'model', name: type, struct: false };
    else if (typeof type === 'function') {
      const name = this.decoder.modelOf(type);
      if (name === undefined) throw new Error(`${type.name} is not a class of the models of the decoder: pass the type name of its schema entry instead.`);
      known = { k: 'model', name, struct: false };
    } else known = type;
    this.types.set(type, known);
    return known;
  }

  /** In the body of a head with a declared root type: the types of the places follow it. */
  private typed(): boolean {
    return this.headRoot !== undefined && this.head !== Head.None;
  }

  /** The declared type of the next value: the root type of the head, or what the type of the open array or map says for the position (undefined: not known). */
  private typeOfNext(): TypeExpr | undefined {
    const frame = this.frames[this.frames.length - 1];
    if (frame === undefined) return this.head === Head.AwaitingBody ? this.headRoot : undefined;
    let type = frame.type;
    if (type === undefined) return undefined;
    while (type.k === 'nullable') type = type.of;
    const at = frame.unknown ? frame.count : frame.size - frame.count; // the values read so far
    switch (type.k) {
      case 'list': return type.of;
      case 'dict': return (at & 1) === 0 ? type.key : type.value;
      case 'pair': return at === 0 ? type.key : at === 1 ? type.value : undefined;
      case 'tuple': return type.items[at];
      case 'object': return this.propertyType(typeof type.entry === 'number' ? this.headSchema?.entries[type.entry] : type.entry, frame, at);
      case 'model': {
        const index = this.headSchema?.indexOf(type.name) ?? -1;
        return index < 0 ? undefined : this.propertyType(this.headSchema!.entries[index], frame, at);
      }
    }
    return undefined;
  }

  /** The type of the property at the position (the array layout) or of the last key (the map layout). */
  private propertyType(entry: SchemaEntry | undefined, frame: Frame, at: number): TypeExpr | undefined {
    if (entry === undefined) return undefined;
    let index = at;
    if (frame.kind === Kind.Map) {
      if ((at & 1) === 0) return undefined; // a key
      index = frame.key ?? -1;
    }
    const property = entry.properties[index];
    if (property === undefined) return undefined;
    return property.type ?? (entry.name === null ? undefined : this.decoder.models?.byName(entry.name)?.byName.get(property.name));
  }

  // ---- The index ----

  /**
   * The indexes of the stream, the first one first: together they list all entries. The schemas they locate are registered in the store, so entries that refer to them
   * can be read after seek. Empty when the stream has no index. The reader stays where it was. Needs a random access source (bytes, a Blob, httpSource, fileSource).
   * @param searchBackwards when the stream does not end with a tail (a writer that stopped after a checkpoint): search backwards for the last one (reads a stream without an index completely).
   */
  async readIndexes(options: { searchBackwards?: boolean } = {}): Promise<StreamIndex[]> {
    this.throwIfClosed();
    const random = this.randomAccess();
    const { indexes } = await readAll(random, options.searchBackwards ?? false);
    for (const index of indexes)
      for (const location of index.schemas) {
        if (location.offset === undefined) continue; // External: the store has it, or not
        const bytes = await random.read(location.offset, location.length);
        if (bytes.length !== location.length) throw new MsgPackError(`The schema at ${location.offset} that the index locates is cut off.`, location.offset);
        const schema = new Schema(bytes.slice());
        if (!this.inline.some((s) => s.id === schema.id)) this.inline.push(schema);
        this.store.register(schema);
      }
    return indexes;
  }

  /** Goes to an entry of the index: the next token is the start of its root value (or its head: read it with readHead). */
  async seek(entry: StreamEntry, point?: StreamIndexPoint): Promise<void> {
    this.throwIfClosed();
    this.randomAccess();
    if (point !== undefined) return this.seekPoint(entry, point);
    await this.moveTo(entry.offset);
    this.frames = [];
    this.head = Head.None;
    this.headSchema = null;
    this.headRoot = undefined;
  }

  /**
   * Goes to a point of an entry's sparse index: the next value is the element it points to. The reader must be in the array of that entry (depth 1: after its arrayStart,
   * and its head when it has one), so it knows the array and the schema of its objects.
   */
  private async seekPoint(entry: StreamEntry, point: StreamIndexPoint): Promise<void> {
    const frame = this.frames[0];
    if (this.frames.length !== 1 || frame.kind !== Kind.Array)
      throw new Error('A point is in the array of an entry: read the entry\'s arrayStart first (after seek(entry), and its head when it has one).');
    if (entry.elementCount === undefined) throw new Error('The entry is no array, it has no points.');
    if (point.element < 0 || point.element >= entry.elementCount || point.offset < 0 || point.offset >= entry.length) throw new RangeError('The point is not in the entry.');
    await this.moveTo(entry.offset + point.offset);
    frame.count = frame.unknown ? point.element : entry.elementCount - point.element;
    frame.countIndex = -1;
  }

  /**
   * Goes to an element of the array of an entry: to the last point before it, or from where the reader is when that is closer, then passes the elements up to it.
   * The next value is the element. The reader must be in the array of that entry, as for seek(entry, point).
   */
  async seekElement(entry: StreamEntry, element: number): Promise<void> {
    this.throwIfClosed();
    const frame = this.frames[0];
    if (this.frames.length !== 1 || frame.kind !== Kind.Array)
      throw new Error('An element is in the array of an entry: read the entry\'s arrayStart first (after seek(entry), and its head when it has one).');
    if (entry.elementCount === undefined) throw new Error('The entry is no array.');
    if (!Number.isInteger(element) || element < 0 || element >= entry.elementCount) throw new RangeError(`There is no element ${element} in the entry (${entry.elementCount}).`);
    await this.run(() => this.passPending());

    let current = frame.unknown ? frame.count : entry.elementCount - frame.count; // the next element
    const points = entry.points;
    let best = -1;
    if (points !== undefined) {
      let low = 0, high = points.length - 1;
      while (low <= high) { // the last point at or before the element
        const middle = (low + high) >> 1;
        if (points[middle].element <= element) { best = middle; low = middle + 1; }
        else high = middle - 1;
      }
    }
    if (best >= 0 && (current > element || points![best].element > current)) {
      this.randomAccess();
      await this.seekPoint(entry, points![best]);
      current = points![best].element;
    } else if (current > element)
      throw new Error(`The element ${element} is before the reader (at ${current}) and before the first point of the entry: seek to the entry again to read it.`);

    for (; current < element; current++) {
      if (!(await this.read())) throw this.endOfData();
      await this.skip();
    }
    await this.run(() => this.passPending()); // the data of the last element passed
    this.token = 'none';
    this.tokenLength = 0;
  }

  private randomAccess(): RandomAccessSource {
    if (this.random === undefined)
      throw new Error('The index is read from a random access source: bytes, a Blob or File, httpSource(url) (range requests) or fileSource(handle).');
    return this.random;
  }

  /** Continues at `position`: drops the buffer and the current token, keeps the open arrays and maps. */
  private async moveTo(position: number): Promise<void> {
    await this.pull.close();
    this.pull = pullOf(this.random!.stream(position));
    this.base = position;
    this.pos = 0;
    this.end = 0;
    this.tokenStartAt = 0;
    this.eof = false;
    this.payloadPending = false;
    this.tailPending = false;
    this.skippingStreamValue = false;
    this.passStarted = false;
    this.mark = -1;
    this.token = 'none';
    this.tokenLength = 0;
    this.key = false;
  }

  /** Stops reading (cancels a ReadableStream). */
  async close(): Promise<void> {
    if (this.closed) return;
    this.closed = true;
    await this.pull.close();
  }

  // ---- The values of the index between root values ----

  /** Passes indexes (extension type 3) with their tails, and sidecars (type 4): only at depth 0 and without a mark. */
  private skipStreamValues(): void {
    while (this.tryEnsure(1)) {
      const header = extensionHeaderLength(this.buf[this.pos]);
      if (header === 0 || !this.tryEnsure(header)) return;
      const type = this.view.getInt8(this.pos + header - 1);
      if (type !== IndexExtensionType && type !== SidecarExtensionType) return;
      this.tokenStartAt = this.pos;
      this.tokenLength = this.extensionLength(this.pos);
      this.pos += header;
      this.payloadPending = this.tokenLength > 0;
      this.tailPending = type === IndexExtensionType;
      this.skippingStreamValue = true;
      this.passPending();
      this.tokenLength = 0;
      this.tokenStartAt = this.pos;
    }
  }

  /** skipStreamValues without moving: the offset (from pos) after the values of the index at `at`, buffering them. */
  private pastStreamValues(at: number): number {
    while (this.tryEnsure(at + 1)) {
      const header = extensionHeaderLength(this.buf[this.pos + at]);
      if (header === 0 || !this.tryEnsure(at + header)) break;
      const type = this.view.getInt8(this.pos + at + header - 1);
      if (type !== IndexExtensionType && type !== SidecarExtensionType) break;
      at += header + this.extensionLength(this.pos + at) + (type === IndexExtensionType ? TailLength : 0);
    }
    return at;
  }

  private extensionLength(at: number): number {
    switch (this.buf[at]) {
      case 0xd4: return 1;
      case 0xd5: return 2;
      case 0xd6: return 4;
      case 0xd7: return 8;
      case 0xd8: return 16;
      case 0xc7: return this.buf[at + 1];
      case 0xc8: return this.view.getUint16(at + 1);
      default: return this.view.getUint32(at + 1);
    }
  }

  // ---- The buffer ----

  /** Passes the data of the current string, binary or extension token (dropped when it is not buffered and no value is read whole), and the tail of an index. */
  private passPending(): void {
    if (this.payloadPending) {
      const length = this.tokenLength;
      if (this.end - this.pos >= length) {
        this.pos += length;
        this.payloadPending = false;
      } else if (this.mark >= 0) { // read whole: buffered
        this.need(length);
      } else { // dropped as it arrives (tokenLength counts down: it is what is left)
        this.tokenLength -= this.end - this.pos;
        this.pos = this.end;
        this.tokenStartAt = this.pos;
        if (this.eof) throw this.endOfData();
        throw NeedMore;
      }
    }
    if (this.tailPending) {
      if (!this.tryEnsure(TailLength) || parseTail(this.buf, this.pos) === undefined)
        throw new MsgPackError('An index (extension type 3) of the stream is not followed by its tail.', this.base + this.pos);
      this.pos += TailLength;
      this.tailPending = false;
    }
    this.skippingStreamValue = false;
  }

  /** The data of the current string, binary or extension token is in the buffer (from pos). */
  private bufferPayload(): void {
    if (this.payloadPending) this.need(this.tokenLength);
  }

  private need(count: number): void {
    if (!this.tryEnsure(count)) throw this.endOfData();
  }

  /** True when the buffer holds `count` bytes from pos, false when the stream ends before; otherwise NeedMore (the caller refills and tries again). */
  private tryEnsure(count: number): boolean {
    if (this.end - this.pos >= count) return true;
    if (this.eof) return false;
    throw NeedMore;
  }

  /** Runs the synchronous step until it completes, refilling the buffer each time it needs more data. */
  private async run<T>(step: () => T): Promise<T> {
    for (;;) {
      try {
        return step();
      } catch (e) {
        if (e !== NeedMore) throw e;
      }
      await this.fill();
    }
  }

  /** Reads the next chunk into the buffer: drops what is before the current token (or the mark) when it needs room, otherwise grows it. */
  private async fill(): Promise<void> {
    if (this.eof) return;
    const chunk = await this.pull.next();
    if (chunk === null) {
      this.eof = true;
      return;
    }
    if (this.end + chunk.length > this.buf.length) {
      const keep = this.mark >= 0 && this.mark < this.tokenStartAt ? this.mark : this.tokenStartAt;
      if (keep > 0) {
        this.buf.copyWithin(0, keep, this.end);
        this.base += keep;
        this.pos -= keep;
        this.end -= keep;
        this.tokenStartAt -= keep;
        if (this.mark >= 0) this.mark -= keep;
      }
      if (this.end + chunk.length > this.buf.length) {
        let size = this.buf.length * 2;
        while (size < this.end + chunk.length) size *= 2;
        const grown = new Uint8Array(size);
        grown.set(this.buf.subarray(0, this.end));
        this.buf = grown;
        this.view = new DataView(grown.buffer);
      }
    }
    this.buf.set(chunk, this.end);
    this.end += chunk.length;
  }

  private decode<T>(f: () => T, start: number): T {
    try {
      return f();
    } catch (e) {
      throw moved(e, this.base + start);
    }
  }

  private endOfData(): MsgPackError {
    return new EndOfDataError(this.base + this.end);
  }

  private throwIfClosed(): void {
    if (this.closed) throw new Error('The stream reader is closed.');
  }
}

const End = Symbol('end of the array');

function isStart(token: StreamTokenType): boolean {
  return token === 'arrayStart' || token === 'mapStart' || token === 'stringStart' || token === 'binaryStart';
}

/** The bytes of a token without its data: what must be buffered before it is parsed. */
function formatHeader(b: number): number {
  if (b <= 0x7f || b >= 0xe0 || (b >= 0x80 && b <= 0xbf)) return 1;
  switch (b) {
    case 0xc0: case 0xc2: case 0xc3: return 1;
    case 0xcc: case 0xd0: case 0xd9: case 0xc4: case 0xd4: case 0xd5: case 0xd6: case 0xd7: case 0xd8: return 2;
    case 0xcd: case 0xd1: case 0xda: case 0xc5: case 0xdc: case 0xde: case 0xc7: return 3;
    case 0xc8: return 4;
    case 0xce: case 0xd2: case 0xca: case 0xdb: case 0xc6: case 0xdd: case 0xdf: return 5;
    case 0xc9: return 6;
    case 0xcf: case 0xd3: case 0xcb: return 9;
  }
  return 1;
}

function int64(view: DataView, p: number, signed: boolean): number | bigint {
  const hi = signed ? view.getInt32(p) : view.getUint32(p);
  const n = hi * twoPow32 + view.getUint32(p + 4);
  if (Number.isSafeInteger(n)) return n;
  return signed ? view.getBigInt64(p) : view.getBigUint64(p);
}

function sameBytes(a: Uint8Array, b: Uint8Array): boolean {
  if (a.length !== b.length) return false;
  for (let t = 0; t < a.length; t++) if (a[t] !== b[t]) return false;
  return true;
}

/** A decoding error with its position in the stream instead of the buffer. */
function moved(e: unknown, base: number): unknown {
  if (!(e instanceof MsgPackError) || e.offset === undefined || base === 0) return e;
  const message = e.message.replace(/ \(offset \d+\)$/, '');
  if (e instanceof EndOfDataError) return new EndOfDataError(base + e.offset);
  if (e instanceof EscapeError) return new EscapeError(base + e.offset);
  return new MsgPackError(message, base + e.offset);
}
