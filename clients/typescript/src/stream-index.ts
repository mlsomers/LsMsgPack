// The index of a stream (as LsMsgPack's IndexFormat, the only place with the positions of the fields; the layout is documented in docs/Streaming.md "The index"
// and by MsgPackStreamIndex in LsMsgPack.Core). The index is an extension value of type 3 whose data is one MsgPack array, followed by its tail:
// 94 cf <offset of the index> ce <its length> ce <CRC-32 of the index> a4 "LSIX" (25 bytes, big-endian). Its content is always in the specification's byte order.

import { crc32 } from './crc32.js';
import { MsgPackError } from './errors.js';
import { MsgPackExtension, MsgPackReader } from './reader.js';
import { toHex } from './sha256.js';
import type { RandomAccessSource } from './stream-sources.js';
import { Writer } from './writer.js';

/** Whether the writer finished the stream after an index. */
export const StreamIndexStatus = {
  /** The writer finished the stream. */
  Complete: 0,
  /** Written while the stream was written (every CheckpointInterval entries): more may follow. */
  Checkpoint: 1,
  /** The writer was closed in the middle of a root value (an exception): that entry is incomplete. */
  Interrupted: 2,
} as const;
export type StreamIndexStatus = (typeof StreamIndexStatus)[keyof typeof StreamIndexStatus];

/** A point of the sparse index of an entry that is an array: where an element starts. */
export interface StreamIndexPoint {
  /** The number of the element (0 for the first). */
  element: number;
  /** Where it starts, from the start of the entry. */
  offset: number;
  /** A plain value the elements are ordered by (a timestamp, an id), if the writer gave one. */
  key?: unknown;
}

/** A schema the entries refer to, and where its inline copy is in the stream (none: the store must hold it). */
export interface StreamSchemaLocation {
  /** The schema id (32 hex digits). */
  id: string;
  offset?: number;
  length: number;
}

/** A root value of the stream: a value, an array or map, an object with its schema, or a head with its body (a table). */
export interface StreamEntry {
  /** Where it starts in the stream (its schema or head included). */
  offset: number;
  /** Its length in bytes. */
  length: number;
  /** A string or an integer (not unique: the last entry with an identifier is the current one). */
  identifier?: string | number | bigint;
  /** What it holds (a MIME type, the name of the root type). */
  contentType?: string;
  modified?: Date;
  /** The hash of its bytes (the algorithm in StreamIndex.hashAlgorithm). */
  hash?: Uint8Array;
  /** The number of elements when the root value is an array (also the body of a head). */
  elementCount?: number;
  points?: StreamIndexPoint[];
  sidecarOffset?: number;
  sidecarLength?: number;
  /** Plain values: null, booleans, numbers, bigints, strings, Uint8Array, Date, arrays and objects of them. */
  metadata?: Record<string, unknown>;
  /** The writer was closed in the middle of it. */
  incomplete?: boolean;
}

/** One index of a stream: the entries since the index before it (a checkpoint, or the part the stream was appended to). */
export interface StreamIndex {
  version: number;
  status: StreamIndexStatus;
  /** What wrote the stream, e.g. "LsMsgPack 2026.10.31.1". */
  writer?: string;
  /** Where the tail of the index before this one starts, if any. */
  previousTail?: number;
  /** The algorithm of the entries' hashes (a name of the multicodec table: "sha2-256"...). */
  hashAlgorithm?: string;
  schemas: StreamSchemaLocation[];
  entries: StreamEntry[];
  metadata?: Record<string, unknown>;
  /** Where the index (its extension value) starts in the stream. */
  offset: number;
  /** Its length in bytes. */
  length: number;
}

export const IndexVersion = 1;
export const IndexExtensionType = 3;
export const SidecarExtensionType = 4;
export const TailLength = 25;
const Magic = [0x4c, 0x53, 0x49, 0x58]; // "LSIX"
const MaxPlainDepth = 32; // metadata nested deeper is refused when written

// ---- Writing ----

/** The entry as an array, trailing nils left out. Throws for metadata that is not a plain value, before anything is written (the entries written before stay valid). */
export function writeEntry(w: Writer, e: StreamEntry): void {
  let count = 2;
  if (e.identifier !== undefined && e.identifier !== null) count = 3;
  if (e.contentType !== undefined && e.contentType !== null) count = 4;
  if (e.modified !== undefined) count = 5;
  if (e.hash !== undefined) count = 6;
  if (e.elementCount !== undefined) count = 7;
  if (e.points !== undefined && e.points.length > 0) count = 8;
  if (e.sidecarOffset !== undefined) count = 10;
  if (e.metadata !== undefined) count = 11;
  if (e.incomplete) count = 12;

  const start = w.length;
  try {
    w.arrayHeader(count);
    w.number(e.offset);
    w.number(e.length);
    if (count === 2) return;
    writeIdentifier(w, e.identifier);
    if (count === 3) return;
    w.write(e.contentType ?? null, 0);
    if (count === 4) return;
    if (e.modified !== undefined) w.date(e.modified); else w.u8(0xc0);
    if (count === 5) return;
    if (e.hash !== undefined) w.bin(e.hash); else w.u8(0xc0);
    if (count === 6) return;
    if (e.elementCount !== undefined) w.number(e.elementCount); else w.u8(0xc0);
    if (count === 7) return;
    writePoints(w, e.points);
    if (count === 8) return;
    if (e.sidecarOffset !== undefined) {
      w.number(e.sidecarOffset);
      w.number(e.sidecarLength ?? 0);
    } else {
      w.u8(0xc0);
      w.u8(0xc0);
    }
    if (count === 10) return;
    writeMetadata(w, e.metadata);
    if (count === 11) return;
    w.u8(0xc3);
  } catch (error) {
    w.truncate(start);
    throw error;
  }
}

function writePoints(w: Writer, points: StreamIndexPoint[] | undefined): void {
  if (points === undefined || points.length === 0) return w.u8(0xc0);
  w.arrayHeader(points.length);
  for (const point of points) {
    const key = point.key !== undefined && point.key !== null;
    w.arrayHeader(key ? 3 : 2);
    w.number(point.element);
    w.number(point.offset);
    if (key) writePlain(w, point.key, 1);
  }
}

export function writeIdentifier(w: Writer, identifier: unknown): void {
  if (identifier === undefined || identifier === null || typeof identifier === 'string' || typeof identifier === 'bigint' || Number.isSafeInteger(identifier))
    return writePlain(w, identifier ?? null, 0);
  throw new TypeError(`An identifier of an entry is a string or an integer, not ${typeof identifier === 'number' ? identifier : typeof identifier}.`);
}

export function writeMetadata(w: Writer, metadata: Record<string, unknown> | undefined): void {
  if (metadata === undefined || metadata === null) return w.u8(0xc0);
  if (typeof metadata !== 'object' || Array.isArray(metadata)) throw new TypeError('Metadata is an object of names and plain values.');
  const keys = Object.keys(metadata);
  w.mapHeader(keys.length);
  for (const key of keys) {
    w.string(key);
    writePlain(w, metadata[key], 1);
  }
}

/** A plain value: null, booleans, numbers, bigints, strings, Uint8Array, Date, and arrays, Maps and plain objects of them. */
export function writePlain(w: Writer, value: unknown, depth: number): void {
  if (depth > MaxPlainDepth) throw new TypeError(`Metadata nested deeper than ${MaxPlainDepth} levels (a cycle?).`);
  if (value === null || value === undefined) return w.u8(0xc0);
  switch (typeof value) {
    case 'boolean':
    case 'number':
    case 'bigint':
    case 'string':
      return w.write(value, 0);
  }
  if (value instanceof Uint8Array) return w.bin(value);
  if (value instanceof Date) return w.date(value);
  if (Array.isArray(value)) {
    w.arrayHeader(value.length);
    for (const item of value) writePlain(w, item, depth + 1);
    return;
  }
  if (value instanceof Map) {
    w.mapHeader(value.size);
    for (const [k, v] of value) {
      writePlain(w, k, depth + 1);
      writePlain(w, v, depth + 1);
    }
    return;
  }
  if (typeof value === 'object' && (Object.getPrototypeOf(value) === Object.prototype || Object.getPrototypeOf(value) === null)) {
    const keys = Object.keys(value);
    w.mapHeader(keys.length);
    for (const key of keys) {
      w.string(key);
      writePlain(w, (value as Record<string, unknown>)[key], depth + 1);
    }
    return;
  }
  throw new TypeError(`Metadata holds plain values (null, booleans, numbers, bigints, strings, Uint8Array, Date, and arrays and objects of them), not ${Object.prototype.toString.call(value)}.`);
}

/** The content of the index (the data of its extension value). The writer of this client writes no schemas (it writes plain MsgPack). */
export function writeIndex(w: Writer, status: StreamIndexStatus, writer: string | undefined, previousTail: number | undefined, hashAlgorithm: string | undefined,
  entryCount: number, entries: Uint8Array, metadata: Uint8Array | undefined): void {
  w.arrayHeader(metadata === undefined ? 7 : 8);
  w.number(IndexVersion);
  w.number(status);
  w.write(writer ?? null, 0);
  w.write(previousTail ?? null, 0);
  w.write(hashAlgorithm ?? null, 0);
  w.arrayHeader(0);
  w.arrayHeader(entryCount);
  w.raw(entries);
  if (metadata !== undefined) w.raw(metadata);
}

/** The tail, 25 bytes, big-endian. */
export function writeTail(w: Writer, indexOffset: number, indexLength: number, crc: number): void {
  const b = new Uint8Array(TailLength);
  const view = new DataView(b.buffer);
  b[0] = 0x94;
  b[1] = 0xcf;
  view.setBigUint64(2, BigInt(indexOffset));
  b[10] = 0xce;
  view.setUint32(11, indexLength);
  b[15] = 0xce;
  view.setUint32(16, crc);
  b[20] = 0xa4;
  b.set(Magic, 21);
  w.raw(b);
}

// ---- The tail ----

export interface Tail {
  /** As the writer counted. */
  indexOffset: number;
  indexLength: number;
  crc: number;
}

/** The tail at `at` in `b` (fixed types and the magic), undefined when the bytes are no tail. */
export function parseTail(b: Uint8Array, at: number): Tail | undefined {
  if (at < 0 || at + TailLength > b.length) return undefined;
  if (b[at] !== 0x94 || b[at + 1] !== 0xcf || b[at + 10] !== 0xce || b[at + 15] !== 0xce || b[at + 20] !== 0xa4) return undefined;
  for (let t = 0; t < 4; t++) if (b[at + 21 + t] !== Magic[t]) return undefined;
  const view = new DataView(b.buffer, b.byteOffset, b.byteLength);
  const offset = view.getBigUint64(at + 2);
  if (offset > BigInt(Number.MAX_SAFE_INTEGER)) return undefined;
  return { indexOffset: Number(offset), indexLength: view.getUint32(at + 11), crc: view.getUint32(at + 16) };
}

/** The length of the header of the extension value that starts with `format`, 0 when it is no extension (the length in it is in the writer's byte order). */
export function extensionHeaderLength(format: number): number {
  switch (format) {
    case 0xd4: case 0xd5: case 0xd6: case 0xd7: case 0xd8: return 2;
    case 0xc7: return 3;
    case 0xc8: return 4;
    case 0xc9: return 6;
    default: return 0;
  }
}

// ---- Reading ----

export interface ReadAllResult {
  indexes: StreamIndex[];
  /** Where the tail of the last index starts, -1 when there is none. */
  lastTail: number;
  /** The stream ends with the tail of the last index. */
  endsWithTail: boolean;
  /** How far the stream was moved since the last index was written. */
  delta: number;
}

/**
 * The indexes of a stream, the first one first: the last one from the end (or, with `search`, searched backwards when the stream does not end with a tail),
 * the others through previousTail. Empty when the stream has no index. The offsets in them are positions in the stream.
 */
export async function readAll(source: RandomAccessSource, search: boolean): Promise<ReadAllResult> {
  const length = await source.size();
  const result: ReadAllResult = { indexes: [], lastTail: -1, endsWithTail: false, delta: 0 };
  let at = length - TailLength;
  let found: { index: StreamIndex; delta: number } | undefined;
  if (at >= 0) found = await tryReadAt(source, at);
  if (found !== undefined) result.endsWithTail = true;
  else if (search) {
    const searched = await searchBackwards(source, length);
    if (searched !== undefined) {
      at = searched.at;
      found = searched;
    }
  }
  if (found === undefined) return result;

  result.lastTail = at;
  result.delta = found.delta;
  let index = found.index;
  const indexes = [index];
  while (index.previousTail !== undefined) {
    const previous = index.previousTail;
    if (previous < 0 || previous > index.offset - TailLength)
      throw new MsgPackError(`The index at ${index.offset} points to a tail at ${previous}, which is not before it.`, index.offset);
    const before = await tryReadAt(source, previous);
    if (before === undefined)
      throw new MsgPackError(`The index at ${index.offset} points to a tail at ${previous}, which is not a valid tail (or its index does not match its CRC-32).`, previous);
    indexes.push(before.index);
    index = before.index;
  }
  result.indexes = indexes.reverse();
  return result;
}

/** The index whose tail starts at `at`, undefined when there is no tail or its index does not match (structure, CRC-32). An index that matches its CRC-32 but is not valid throws. */
async function tryReadAt(source: RandomAccessSource, at: number): Promise<{ index: StreamIndex; delta: number } | undefined> {
  const tailBytes = await source.read(at, TailLength);
  const tail = tailBytes.length === TailLength ? parseTail(tailBytes, 0) : undefined;
  if (tail === undefined || tail.indexLength < 3 || tail.indexLength > at) return undefined;
  const start = at - tail.indexLength;
  const delta = start - tail.indexOffset;
  const bytes = await source.read(start, tail.indexLength);
  if (bytes.length !== tail.indexLength || crc32(bytes) !== tail.crc) return undefined;
  const header = extensionHeaderLength(bytes[0]);
  if (header === 0 || header > bytes.length || ((bytes[header - 1] << 24) >> 24) !== IndexExtensionType) return undefined;
  const index = parse(bytes, header, delta, start);
  index.offset = start;
  index.length = bytes.length;
  return { index, delta };
}

/** The last tail before `length`, searched backwards in blocks (for its fixed types and magic, then its CRC-32). */
async function searchBackwards(source: RandomAccessSource, length: number): Promise<{ index: StreamIndex; delta: number; at: number } | undefined> {
  const Block = 64 * 1024;
  let blockEnd = length; // the bytes before this are not searched yet
  while (blockEnd > 0) {
    const blockStart = Math.max(0, blockEnd - Block);
    const count = Math.min(length, blockEnd + TailLength - 1) - blockStart; // overlap with the block after it, so a tail across the border is found
    if (count < TailLength) return undefined;
    const buffer = await source.read(blockStart, count);
    if (buffer.length !== count) return undefined;
    for (let p = count - TailLength; p >= 0; p--) {
      if (buffer[p] !== 0x94 || buffer[p + TailLength - 1] !== 0x58 || parseTail(buffer, p) === undefined) continue;
      const found = await tryReadAt(source, blockStart + p);
      if (found !== undefined) return { ...found, at: blockStart + p };
    }
    blockEnd = blockStart;
  }
  return undefined;
}

/** The content of an index (after the extension header), checked: types, version, offsets inside the stream before the index, points inside their entry and in order. */
function parse(data: Uint8Array, start: number, delta: number, indexStart: number): StreamIndex {
  const r = new MsgPackReader(data, { decimal: 'raw', maxDepth: 64 }, start);
  let fields: unknown;
  try {
    fields = r.value();
  } catch (e) {
    throw invalid(`is not valid MsgPack (${(e as Error).message})`, indexStart);
  }
  if (!Array.isArray(fields) || r.pos !== data.length) throw invalid('is not one array', indexStart);

  const version = integer(field(fields, 0), 1, Number.MAX_SAFE_INTEGER, 'version', indexStart);
  if (version > IndexVersion)
    throw new MsgPackError(`The index at ${indexStart} has version ${version}, this library reads up to version ${IndexVersion}.`, indexStart);
  const index: StreamIndex = {
    version,
    status: integer(field(fields, 1) ?? 0, 0, 2, 'status', indexStart) as StreamIndexStatus,
    writer: text(field(fields, 2), 'writer', indexStart),
    hashAlgorithm: text(field(fields, 4), 'hash algorithm', indexStart),
    schemas: [],
    entries: [],
    offset: indexStart,
    length: 0,
  };
  const previous = field(fields, 3);
  if (previous !== null) index.previousTail = offset(previous, delta, indexStart, 'previous tail');

  const schemas = field(fields, 5);
  if (schemas !== null && !Array.isArray(schemas)) throw invalid('has no array of schemas', indexStart);
  if (schemas !== null) for (const s of schemas as unknown[]) index.schemas.push(parseSchema(s, delta, indexStart));

  const entries = field(fields, 6);
  if (entries !== null && !Array.isArray(entries)) throw invalid('has no array of entries', indexStart);
  if (entries !== null) for (const e of entries as unknown[]) index.entries.push(parseEntry(e, delta, indexStart));

  const metadata = metadataOf(field(fields, 7), indexStart);
  if (metadata !== undefined) index.metadata = metadata;
  if (index.writer === undefined) delete index.writer;
  if (index.hashAlgorithm === undefined) delete index.hashAlgorithm;
  return index;
}

function parseSchema(value: unknown, delta: number, indexStart: number): StreamSchemaLocation {
  const fields = Array.isArray(value) ? value : undefined;
  const id = fields !== undefined && fields.length >= 3 ? fields[0] : undefined;
  if (!(id instanceof Uint8Array) || id.length !== 16) throw invalid('has a schema without an id of 16 bytes', indexStart);
  const length = integer(fields![2], 0, 0x7fffffff, 'schema length', indexStart);
  const location: StreamSchemaLocation = { id: toHex(id, 0, 16), length };
  if (fields![1] !== null) {
    location.offset = offset(fields![1], delta, indexStart, 'schema');
    if (length === 0 || length > indexStart - location.offset)
      throw invalid(`has a schema at ${location.offset} of ${length} bytes that does not end before the index`, indexStart);
  }
  return location;
}

function parseEntry(value: unknown, delta: number, indexStart: number): StreamEntry {
  if (!Array.isArray(value) || value.length < 2) throw invalid('has an entry that is not an array of at least its offset and length', indexStart);
  const fields = value;
  const entry: StreamEntry = { offset: offset(fields[0], delta, indexStart, 'entry'), length: integer(fields[1], 0, Number.MAX_SAFE_INTEGER, 'entry length', indexStart) };
  if (entry.length > indexStart - entry.offset)
    throw invalid(`has an entry at ${entry.offset} of ${entry.length} bytes that does not end before the index`, indexStart);

  const identifier = field(fields, 2);
  if (identifier !== null) {
    if (typeof identifier === 'string' || typeof identifier === 'bigint' || Number.isSafeInteger(identifier)) entry.identifier = identifier as string | number | bigint;
    else throw invalid('has an entry whose identifier is not a string or an integer', indexStart);
  }
  const contentType = text(field(fields, 3), 'content type', indexStart);
  if (contentType !== undefined) entry.contentType = contentType;
  const modified = field(fields, 4);
  if (modified !== null) {
    if (!(modified instanceof Date)) throw invalid('has an entry whose modification time is not a timestamp', indexStart);
    entry.modified = modified;
  }
  const hash = field(fields, 5);
  if (hash !== null) {
    if (!(hash instanceof Uint8Array)) throw invalid('has an entry whose hash is not a bin', indexStart);
    entry.hash = hash;
  }
  const elements = field(fields, 6);
  if (elements !== null) entry.elementCount = integer(elements, 0, 0xffffffff, 'element count', indexStart);
  const points = pointsOf(field(fields, 7), entry, indexStart);
  if (points !== undefined) entry.points = points;

  const sidecar = field(fields, 8);
  if (sidecar !== null) {
    const at = offset(sidecar, delta, indexStart, 'sidecar');
    const length = integer(field(fields, 9), 0, 0x7fffffff, 'sidecar length', indexStart);
    if (at < entry.offset + entry.length || length > indexStart - at)
      throw invalid(`has a sidecar at ${at} that is not between its entry and the index`, indexStart);
    entry.sidecarOffset = at;
    entry.sidecarLength = length;
  }
  const metadata = metadataOf(field(fields, 10), indexStart);
  if (metadata !== undefined) entry.metadata = metadata;
  const incomplete = field(fields, 11);
  if (incomplete !== null && typeof incomplete !== 'boolean') throw invalid('has an entry whose incomplete flag is not a bool', indexStart);
  if (incomplete === true) entry.incomplete = true;
  return entry;
}

function pointsOf(value: unknown, entry: StreamEntry, indexStart: number): StreamIndexPoint[] | undefined {
  if (value === null) return undefined;
  if (!Array.isArray(value)) throw invalid('has an entry whose points are not an array', indexStart);
  const points: StreamIndexPoint[] = [];
  let lastElement = -1, lastOffset = -1;
  for (const item of value) {
    if (!Array.isArray(item) || item.length < 2) throw invalid('has a point that is not an array of its element and offset', indexStart);
    const element = integer(item[0], 0, Number.MAX_SAFE_INTEGER, 'point element', indexStart);
    const at = integer(item[1], 0, Number.MAX_SAFE_INTEGER, 'point offset', indexStart);
    if (element <= lastElement || at <= lastOffset) throw invalid(`has points of the entry at ${entry.offset} that are not in ascending order`, indexStart);
    if (at >= entry.length || (entry.elementCount !== undefined && element >= entry.elementCount))
      throw invalid(`has a point (element ${element}, offset ${at}) outside of the entry at ${entry.offset}`, indexStart);
    const point: StreamIndexPoint = { element, offset: at };
    const key = field(item, 2);
    if (key !== null) point.key = key;
    points.push(point);
    lastElement = element;
    lastOffset = at;
  }
  return points;
}

function metadataOf(value: unknown, indexStart: number): Record<string, unknown> | undefined {
  if (value === null) return undefined;
  if (typeof value !== 'object' || Array.isArray(value) || value instanceof Uint8Array || value instanceof Date || value instanceof MsgPackExtension)
    throw invalid('has metadata that is not a map', indexStart);
  return value as Record<string, unknown>;
}

function field(fields: unknown[], position: number): unknown {
  return position < fields.length ? (fields[position] ?? null) : null;
}

function text(value: unknown, what: string, indexStart: number): string | undefined {
  if (value === null) return undefined;
  if (typeof value === 'string') return value;
  throw invalid(`has a ${what} that is not a string`, indexStart);
}

/** An offset as the writer counted it, moved by `delta`: a position in the stream before the index. */
function offset(value: unknown, delta: number, indexStart: number, what: string): number {
  const result = integer(value, 0, Number.MAX_SAFE_INTEGER, what + ' offset', indexStart) + delta;
  if (result < 0) throw invalid(`has a ${what} offset (${result - delta}) outside of the stream`, indexStart);
  if (result >= indexStart) throw invalid(`has a ${what} offset (${result}) that is not before the index`, indexStart);
  return result;
}

function integer(value: unknown, min: number, max: number, what: string, indexStart: number): number {
  if (typeof value === 'bigint') throw invalid(`has a ${what} that is too large`, indexStart);
  if (typeof value !== 'number' || !Number.isInteger(value)) throw invalid(`has a ${what} that is not an integer`, indexStart);
  if (value < min || value > max) throw invalid(`has a ${what} out of range (${value})`, indexStart);
  return value;
}

function invalid(what: string, indexStart: number): MsgPackError {
  return new MsgPackError(`The index at ${indexStart} ${what}.`, indexStart);
}
