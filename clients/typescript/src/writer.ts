// Plain MsgPack for request bodies (application/msgpack): objects as maps keyed by their property names, as the formatters read them with their Plain options.
// The property $type is written as the type id (the key "", first): the class of an object where the declared type is a base class, interface or object.

import { TypeProperty } from './decoder.js';
import { MsgPackError } from './errors.js';
import type { ModelClass, ModelSet } from './models.js';
import { MsgPackExtension } from './reader.js';
import type { Kind, TypeExpr } from './schema.js';
import { dateOnlyDayNumber, encodeDecimal, guidBytes, parseOffsetText, timeOnlyTicks, timeSpanTicks } from './values.js';

const textEncoder = new TextEncoder();

export interface EncodeOptions {
  /** Objects and arrays nested deeper are refused (cycles), 256 by default. */
  maxDepth?: number;
  /**
   * The generated models (lsmsgpack-ts): an instance of a model class is written as its properties are declared (a Guid string as its 16 bytes, TimeSpan, DateOnly and
   * TimeOnly text as the numbers .NET writes, decimal text as a decimal...), and with its type id only where the declared type is not its class.
   */
  models?: ModelSet;
  /** Generate the code that writes the properties of each model class (new Function, the property names as string literals), default true. Without it (or when a Content-Security-Policy does not allow it) a generic loop does the same. */
  generateCode?: boolean;
}

/**
 * Encodes a value: null and undefined as nil (undefined properties are left out), numbers as the smallest integer or a float64, bigint as a 64 bit integer,
 * Date as a timestamp, Uint8Array (and other typed arrays, ArrayBuffer) as bin, Map and plain objects as maps, MsgPackExtension as an extension.
 * The property $type of an object (a string: the .NET type name, as the decoder sets it) is written as the type id, the key "" (a Map is written as it is).
 */
export function encode(value: unknown, options: EncodeOptions = {}): Uint8Array {
  const writer = new Writer(options.maxDepth ?? 256, options.models, options.generateCode ?? true);
  writer.write(value, 0);
  return writer.result();
}

class Writer {
  private buf = new Uint8Array(256);
  private view = new DataView(this.buf.buffer);
  private pos = 0;

  constructor(private readonly maxDepth: number, private readonly models: ModelSet | undefined, private readonly generateCode: boolean) {}

  isModel(value: object): boolean {
    return this.models?.of(value) !== undefined;
  }

  result(): Uint8Array {
    return this.buf.slice(0, this.pos);
  }

  private ensure(count: number): void {
    if (this.pos + count > this.buf.length) this.grow(count); // small enough to be inlined
  }

  private grow(count: number): void {
    let size = this.buf.length * 2;
    while (size < this.pos + count) size *= 2;
    const grown = new Uint8Array(size);
    grown.set(this.buf.subarray(0, this.pos));
    this.buf = grown;
    this.view = new DataView(grown.buffer);
  }

  private u8(b: number): void {
    this.ensure(1);
    this.buf[this.pos++] = b;
  }

  private header(b: number, n: number, size: 1 | 2 | 4): void {
    this.ensure(1 + size);
    this.buf[this.pos++] = b;
    if (size === 1) this.buf[this.pos] = n;
    else if (size === 2) this.view.setUint16(this.pos, n);
    else this.view.setUint32(this.pos, n);
    this.pos += size;
  }

  write(value: unknown, depth: number): void {
    if (value === null || value === undefined) return this.u8(0xc0);
    switch (typeof value) {
      case 'boolean': return this.u8(value ? 0xc3 : 0xc2);
      case 'number': return this.number(value);
      case 'bigint': return this.bigint(value);
      case 'string': return this.string(value);
    }
    if (depth >= this.maxDepth) throw new MsgPackError(`The value is nested deeper than ${this.maxDepth} levels.`);
    if (value instanceof Date) return this.date(value);
    if (value instanceof Uint8Array) return this.bin(value);
    if (ArrayBuffer.isView(value)) return this.bin(new Uint8Array(value.buffer, value.byteOffset, value.byteLength));
    if (value instanceof ArrayBuffer) return this.bin(new Uint8Array(value));
    if (value instanceof MsgPackExtension) return this.ext(value.type, value.data);
    if (Array.isArray(value)) {
      this.arrayHeader(value.length);
      for (let t = 0; t < value.length; t++) this.write(value[t], depth + 1);
      return;
    }
    if (value instanceof Map) {
      this.mapHeader(value.size);
      for (const [k, v] of value) {
        this.write(k, depth + 1);
        this.write(v, depth + 1);
      }
      return;
    }
    if (typeof value === 'object') return this.object(value as Record<string, unknown>, depth, undefined);
    throw new MsgPackError(`A ${typeof value} cannot be written as MsgPack.`);
  }

  /** An array of values of one declared type (the models). */
  list(value: unknown[], element: ValueWriter, depth: number): void {
    if (depth >= this.maxDepth) throw new MsgPackError(`The value is nested deeper than ${this.maxDepth} levels.`);
    this.arrayHeader(value.length);
    for (let t = 0; t < value.length; t++) element(this, value[t], depth + 1);
  }

  /** An array of values of the declared types (a tuple; KeyValuePair as [key, value]). */
  items(value: unknown[], items: ValueWriter[], depth: number): void {
    if (depth >= this.maxDepth) throw new MsgPackError(`The value is nested deeper than ${this.maxDepth} levels.`);
    this.arrayHeader(value.length);
    for (let t = 0; t < value.length; t++) (t < items.length ? items[t] : writeAsIs)(this, value[t], depth + 1);
  }

  /** A dictionary (a plain object): the keys as the declared key type (text in JavaScript: an int key is written as a number). */
  dictionary(o: Record<string, unknown>, key: ValueWriter, value: ValueWriter, depth: number): void {
    if (depth >= this.maxDepth) throw new MsgPackError(`The value is nested deeper than ${this.maxDepth} levels.`);
    const keys = Object.keys(o).filter((k) => o[k] !== undefined && typeof o[k] !== 'function');
    this.mapHeader(keys.length);
    for (let t = 0; t < keys.length; t++) {
      key(this, keys[t], depth + 1);
      value(this, o[keys[t]], depth + 1);
    }
  }

  /** A Guid as .NET writes it (bin 16, Guid.ToByteArray()), read from its text without a copy; other text as it is. */
  guid(text: string): void {
    if (text.length === 36 && text.charCodeAt(8) === 45 && text.charCodeAt(13) === 45 && text.charCodeAt(18) === 45 && text.charCodeAt(23) === 45) {
      this.ensure(18);
      const b = this.buf, p = this.pos;
      let bad = 0;
      for (let t = 0; t < 16; t++) {
        const c = GuidSource[t];
        const hi = hexValue(text.charCodeAt(c)), lo = hexValue(text.charCodeAt(c + 1));
        bad |= hi | lo; // -1 for a character that is not a hexadecimal digit
        b[p + 2 + t] = (hi << 4) | lo;
      }
      if (bad >= 0) {
        b[p] = 0xc4;
        b[p + 1] = 16;
        this.pos = p + 18;
        return;
      }
    }
    if (/^[{]?[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}[}]?$/.test(text)) return this.bin(guidBytes(text)); // braces or no dashes
    this.string(text);
  }

  /** A string of a kind with a text form (TimeSpan, DateOnly...): what .NET writes for it, or the text when it is not one. */
  text(value: string, kind: Kind, depth: number): void {
    switch (kind) {
      case 'timespan': {
        const ticks = timeSpanTicks(value);
        if (ticks !== undefined) return typeof ticks === 'bigint' ? this.bigint(ticks) : this.number(ticks);
        break;
      }
      case 'timeonly': {
        const ticks = timeOnlyTicks(value);
        if (ticks !== undefined) return this.number(ticks);
        break;
      }
      case 'dateonly': {
        const day = dateOnlyDayNumber(value);
        if (day !== undefined) return this.number(day);
        break;
      }
      case 'char':
        if (value.length === 1) return this.number(value.charCodeAt(0));
        break;
      case 'datetime': {
        const ms = Date.parse(value);
        if (!Number.isNaN(ms)) return this.date(new Date(ms));
        break;
      }
      case 'dto':
      case 'dto-clock': {
        const parsed = parseOffsetText(value);
        if (parsed === undefined) break;
        this.arrayHeader(2); // DateTimeOffsetFormat TimestampAndOffset (dto) or ClockTimeAndOffset (dto-clock)
        this.date(kind === 'dto' ? parsed.moment : new Date(parsed.moment.getTime() + parsed.offset * 60000));
        return this.number(parsed.offset);
      }
      case 'decimal': {
        const bytes = encodeDecimal(value);
        if (bytes !== undefined) return this.ext(1, bytes); // the decimal extension: every digit
        break;
      }
      case 'int':
      case 'long':
      case 'ulong':
      case 'float':
        if (value.trim() !== '' && !Number.isNaN(Number(value))) return this.write(Number(value), depth); // a dictionary key
        break;
    }
    this.string(value);
  }

  /**
   * An object as a map of its own enumerable properties (not undefined or functions), $type as the type id first. An instance of a model class: see model.
   * @param declared the type name the place declares (a class of the models), AnyDeclared (object, an interface), undefined when not known (the root, untyped places)
   */
  object(o: Record<string, unknown>, depth: number, declared: string | undefined): void {
    const model = this.models?.of(o);
    if (model !== undefined) return this.model(o, model, depth, declared);
    let n = 0;
    for (const k in o) if (hasOwn.call(o, k)) n++;
    const start = this.pos;
    this.mapHeader(n); // one pass: the header for every key, made smaller when some are left out
    let count = 0;
    for (const k in o) {
      if (!hasOwn.call(o, k)) continue;
      const v = o[k];
      if (v === undefined || typeof v === 'function') continue;
      if (k === TypeProperty) {
        if (count !== 0) { // after other properties: written again with the type id first
          this.pos = start;
          return this.typedObject(o, Object.keys(o), depth);
        }
        if (typeof v !== 'string') throw typePropertyError(v);
        this.u8(0xa0); // the key "": the type id, first (LtMsgPack's fast path, System.Text.Json also wants it first)
        this.string(v);
      } else {
        this.key(k);
        this.write(v, depth + 1);
      }
      count++;
    }
    if (count !== n) this.patchMapHeader(start, n, count);
  }

  /** An object whose $type is not its first property: the type id first. */
  private typedObject(o: Record<string, unknown>, keys: string[], depth: number): void {
    let count = 0, typeName: string | undefined;
    for (let t = 0; t < keys.length; t++) {
      const v = o[keys[t]];
      if (v === undefined || typeof v === 'function') continue;
      if (keys[t] === TypeProperty) {
        if (typeof v !== 'string') throw typePropertyError(v);
        typeName = v;
      }
      count++;
    }
    this.mapHeader(count);
    this.u8(0xa0);
    this.string(typeName!);
    for (let t = 0; t < keys.length; t++) {
      const k = keys[t], v = o[k];
      if (v === undefined || typeof v === 'function' || k === TypeProperty) continue;
      this.key(k);
      this.write(v, depth + 1);
    }
  }

  /** Replaces the map header written at start for n entries by the one for count (smaller): the entries move when it is shorter. */
  private patchMapHeader(start: number, n: number, count: number): void {
    const reserved = n <= 15 ? 1 : n <= 0xffff ? 3 : 5;
    const size = count <= 15 ? 1 : count <= 0xffff ? 3 : 5;
    const end = this.pos;
    if (size !== reserved) this.buf.copyWithin(start + size, start + reserved, end);
    this.pos = start;
    this.mapHeader(count);
    this.pos = end - reserved + size;
  }

  /** A property name: the encoded names are kept (they repeat in every object of a type). */
  private key(k: string): void {
    let bytes = encodedKeys.get(k);
    if (bytes === undefined) {
      if (k.length > 32 || encodedKeys.size >= MaxEncodedKeys) return this.string(k);
      bytes = encode(k);
      encodedKeys.set(k, bytes);
    }
    this.small(bytes);
  }

  /**
   * An instance of a model class: its properties as declared, then other own properties as they are. The type id where the declared type is not its class
   * (where it is not known: when the class extends another one, e.g. the elements of an array of animals).
   */
  private model(o: Record<string, unknown>, model: ModelClass, depth: number, declared: string | undefined): void {
    if (depth >= this.maxDepth) throw new MsgPackError(`The value is nested deeper than ${this.maxDepth} levels.`);
    const typeId = declared === undefined ? model.derived : declared !== model.typeName;
    const plan = planOf(model);
    const keys = Object.keys(o);
    const extra = sameKeys(keys, plan.shape) ? undefined : this.extraKeys(o, keys, model, plan);
    const max = (typeId ? 1 : 0) + plan.names.length + (extra?.length ?? 0), start = this.pos;
    this.mapHeader(max); // one pass: the header for every property, made smaller for the undefined ones
    let count = 0;
    if (typeId) {
      this.u8(0xa0);
      this.string(model.typeName);
      count++;
    }
    if (this.generateCode && plan.generated === undefined) plan.generated = generateModelWriter(plan);
    if (this.generateCode && plan.generated !== null) count += plan.generated!(this, o, depth + 1);
    else {
      const names = plan.names, writers = plan.writers, encoded = plan.encoded;
      for (let t = 0; t < names.length; t++) {
        const v = o[names[t]];
        if (v === undefined) continue;
        this.small(encoded[t]);
        writers[t](this, v, depth + 1);
        count++;
      }
    }
    if (extra !== undefined)
      for (let t = 0; t < extra.length; t++) {
        this.string(extra[t]);
        this.write(o[extra[t]], depth + 1);
        count++;
      }
    if (count !== max) this.patchMapHeader(start, max, count);
  }

  /** The own properties the model does not have (e.g. one the server has and the models not yet). Keys without any are remembered: the next instance with them is not checked. */
  private extraKeys(o: Record<string, unknown>, keys: string[], model: ModelClass, plan: ModelPlan): string[] | undefined {
    let extra: string[] | undefined, other = false;
    for (let t = 0; t < keys.length; t++) {
      const k = keys[t], v = o[k];
      if (model.fields.has(k) || k === TypeProperty) continue;
      if (v !== undefined && typeof v !== 'function') (extra ??= []).push(k);
      else other = true; // left out now, but such a key may have a value in the next instance: not a shape to remember
    }
    if (extra === undefined && !other) plan.shape = keys;
    return extra;
  }

  /** Pre-encoded bytes (a property name), copied by a loop: set() costs more for a few bytes. */
  small(bytes: Uint8Array): void {
    const n = bytes.length;
    this.ensure(n);
    const b = this.buf;
    let p = this.pos;
    for (let t = 0; t < n; t++) b[p++] = bytes[t];
    this.pos = p;
  }

  number(n: number): void {
    if (!Number.isSafeInteger(n) || Object.is(n, -0)) { // beyond 2^53 a number is not an exact integer: as it is, a double
      this.ensure(9);
      this.buf[this.pos] = 0xcb;
      this.view.setFloat64(this.pos + 1, n);
      this.pos += 9;
      return;
    }
    if (n >= 0) {
      if (n <= 0x7f) return this.u8(n);
      if (n <= 0xff) return this.header(0xcc, n, 1);
      if (n <= 0xffff) return this.header(0xcd, n, 2);
      if (n <= 0xffffffff) return this.header(0xce, n, 4);
      return this.bigint(BigInt(n));
    }
    if (n >= -32) return this.u8(n & 0xff);
    if (n >= -128) return this.header(0xd0, n & 0xff, 1);
    if (n >= -32768) return this.header(0xd1, n & 0xffff, 2);
    if (n >= -2147483648) return this.header(0xd2, n >>> 0, 4);
    return this.bigint(BigInt(n));
  }

  bigint(n: bigint): void {
    if (n >= -(2n ** 63n) && n < 0n) {
      this.ensure(9);
      this.buf[this.pos] = 0xd3;
      this.view.setBigInt64(this.pos + 1, n);
    } else if (n >= 0n && n < 2n ** 64n) {
      this.ensure(9);
      this.buf[this.pos] = 0xcf;
      this.view.setBigUint64(this.pos + 1, n);
    } else throw new MsgPackError(`${n} does not fit in 64 bits.`);
    this.pos += 9;
  }

  /**
   * Strings up to 32 characters by a loop, longer ones with encodeInto after the largest header (moved back when a smaller one fits). TextEncoder.encode per string
   * (a call into the runtime and a new array) made writing the invoices 6x slower. Lone surrogates become U+FFFD, as TextEncoder writes them.
   */
  string(s: string): void {
    const length = s.length;
    if (length > 32) return this.longString(s);
    this.ensure(2 + length * 3); // at most 3 bytes per UTF-16 unit (a surrogate pair is 4 bytes for 2), and one more header byte for str8
    const b = this.buf, start = this.pos;
    let p = start + 1;
    for (let i = 0; i < length; i++) {
      let c = s.charCodeAt(i);
      if (c < 0x80) b[p++] = c;
      else if (c < 0x800) {
        b[p++] = 0xc0 | (c >> 6);
        b[p++] = 0x80 | (c & 0x3f);
      } else {
        if (c >= 0xd800 && c <= 0xdfff) {
          const d = c <= 0xdbff && i + 1 < length ? s.charCodeAt(i + 1) : 0;
          if (d >= 0xdc00 && d <= 0xdfff) {
            i++;
            c = 0x10000 + ((c - 0xd800) << 10) + (d - 0xdc00);
            b[p++] = 0xf0 | (c >> 18);
            b[p++] = 0x80 | ((c >> 12) & 0x3f);
            b[p++] = 0x80 | ((c >> 6) & 0x3f);
            b[p++] = 0x80 | (c & 0x3f);
            continue;
          }
          c = 0xfffd;
        }
        b[p++] = 0xe0 | (c >> 12);
        b[p++] = 0x80 | ((c >> 6) & 0x3f);
        b[p++] = 0x80 | (c & 0x3f);
      }
    }
    const n = p - start - 1;
    if (n <= 31) {
      b[start] = 0xa0 | n;
      this.pos = p;
      return;
    }
    b.copyWithin(start + 2, start + 1, p); // str8
    b[start] = 0xd9;
    b[start + 1] = n;
    this.pos = p + 1;
  }

  private longString(s: string): void {
    this.ensure(5 + s.length * 3);
    const start = this.pos;
    const n = textEncoder.encodeInto(s, this.buf.subarray(start + 5)).written; // at least 33 bytes: str8 or larger
    const size = n <= 0xff ? 2 : n <= 0xffff ? 3 : 5;
    if (size !== 5) this.buf.copyWithin(start + size, start + 5, start + 5 + n);
    if (size === 2) {
      this.buf[start] = 0xd9;
      this.buf[start + 1] = n;
    } else if (size === 3) {
      this.buf[start] = 0xda;
      this.view.setUint16(start + 1, n);
    } else {
      this.buf[start] = 0xdb;
      this.view.setUint32(start + 1, n);
    }
    this.pos = start + size + n;
  }

  private raw(bytes: Uint8Array): void {
    this.ensure(bytes.length);
    this.buf.set(bytes, this.pos);
    this.pos += bytes.length;
  }

  bin(bytes: Uint8Array): void {
    const n = bytes.length;
    if (n <= 0xff) this.header(0xc4, n, 1);
    else if (n <= 0xffff) this.header(0xc5, n, 2);
    else this.header(0xc6, n, 4);
    this.raw(bytes);
  }

  arrayHeader(n: number): void {
    if (n <= 15) this.u8(0x90 | n);
    else if (n <= 0xffff) this.header(0xdc, n, 2);
    else this.header(0xdd, n, 4);
  }

  mapHeader(n: number): void {
    if (n <= 15) this.u8(0x80 | n);
    else if (n <= 0xffff) this.header(0xde, n, 2);
    else this.header(0xdf, n, 4);
  }

  ext(type: number, data: Uint8Array): void {
    const n = data.length;
    const fixed = n === 1 ? 0xd4 : n === 2 ? 0xd5 : n === 4 ? 0xd6 : n === 8 ? 0xd7 : n === 16 ? 0xd8 : 0;
    if (fixed !== 0) this.u8(fixed);
    else if (n <= 0xff) this.header(0xc7, n, 1);
    else if (n <= 0xffff) this.header(0xc8, n, 2);
    else this.header(0xc9, n, 4);
    this.u8(type & 0xff);
    this.raw(data);
  }

  /** The timestamp extension (-1) in its smallest form, as the specification and LsMsgPack write it. */
  date(date: Date): void {
    const ms = date.getTime();
    if (Number.isNaN(ms)) throw new MsgPackError('An invalid Date cannot be written.');
    const seconds = Math.floor(ms / 1000);
    const nanoseconds = (ms - seconds * 1000) * 1000000;
    if (seconds >= 0 && seconds <= 0x3ffffffff) {
      if (nanoseconds === 0 && seconds <= 0xffffffff) {
        this.ensure(6);
        this.buf[this.pos++] = 0xd6;
        this.buf[this.pos++] = 0xff;
        this.view.setUint32(this.pos, seconds);
        this.pos += 4;
        return;
      }
      this.ensure(10);
      this.buf[this.pos++] = 0xd7;
      this.buf[this.pos++] = 0xff;
      this.view.setUint32(this.pos, nanoseconds * 4 + Math.floor(seconds / 4294967296));
      this.view.setUint32(this.pos + 4, seconds >>> 0);
      this.pos += 8;
      return;
    }
    this.ensure(15);
    this.buf[this.pos++] = 0xc7;
    this.buf[this.pos++] = 12;
    this.buf[this.pos++] = 0xff;
    this.view.setUint32(this.pos, nanoseconds);
    this.view.setBigInt64(this.pos + 4, BigInt(seconds));
    this.pos += 12;
  }
}

const hasOwn = Object.prototype.hasOwnProperty;
const encodedKeys = new Map<string, Uint8Array>();
const MaxEncodedKeys = 4096;

function typePropertyError(value: unknown): MsgPackError {
  return new MsgPackError(`${TypeProperty} is a .NET type name (a string), not a ${typeof value}.`);
}

/** The declared type object (or an interface, an abstract class the models do not have): an instance of a model always gets its type id. */
const AnyDeclared = '\u0000any';

const AnyKind: TypeExpr = { k: 'kind', kind: 'any' };

/** An object written as a map (not an array, Date, bytes, Map or extension). */
function isPlainObject(value: unknown): boolean {
  return typeof value === 'object' && value !== null && !Array.isArray(value) && !(value instanceof Date) && !(value instanceof Uint8Array) && !ArrayBuffer.isView(value)
    && !(value instanceof ArrayBuffer) && !(value instanceof MsgPackExtension) && !(value instanceof Map);
}

/** Writes a value of a declared type of the models (values in another form than the type expects are written as they are). Made once per type. */
type ValueWriter = (w: Writer, value: unknown, depth: number) => void;

const writeAsIs: ValueWriter = (w, v, d) => w.write(v, d);
const compiled = new WeakMap<TypeExpr, ValueWriter>();

function writerOf(type: TypeExpr): ValueWriter {
  let writer = compiled.get(type);
  if (writer === undefined) {
    writer = compile(type);
    compiled.set(type, writer);
  }
  return writer;
}

function compile(type: TypeExpr): ValueWriter {
  switch (type.k) {
    case 'nullable': return writerOf(type.of);
    case 'kind': return kindWriter(type.kind);
    case 'model': {
      const name = type.name;
      return (w, v, d) => (isPlainObject(v) ? w.object(v as Record<string, unknown>, d, name) : w.write(v, d));
    }
    case 'list': {
      const element = writerOf(type.of);
      return (w, v, d) => (Array.isArray(v) ? w.list(v, element, d) : w.write(v, d));
    }
    case 'tuple': {
      const items = type.items.map(writerOf);
      return (w, v, d) => (Array.isArray(v) ? w.items(v, items, d) : w.write(v, d));
    }
    case 'pair': {
      const items = [writerOf(type.key), writerOf(type.value)];
      return (w, v, d) => {
        if (!isPlainObject(v)) return w.write(v, d);
        const pair = v as { Key?: unknown; Value?: unknown; key?: unknown; value?: unknown };
        if ('Key' in pair) return w.items([pair.Key, pair.Value], items, d);
        if ('key' in pair) return w.items([pair.key, pair.value], items, d); // propertyNames: 'camelCase'
        w.write(v, d);
      };
    }
    case 'dict': {
      const key = writerOf(type.key), value = writerOf(type.value);
      return (w, v, d) => (isPlainObject(v) && !w.isModel(v as object) ? w.dictionary(v as Record<string, unknown>, key, value, d) : w.write(v, d));
    }
  }
  return writeAsIs; // an entry of a schema: not in models
}

/** The values in the form the kind usually has are written without the dispatch of write. */
function kindWriter(kind: Kind): ValueWriter {
  switch (kind) {
    case 'string': return (w, v, d) => (typeof v === 'string' ? w.string(v) : w.write(v, d));
    case 'guid': return (w, v, d) => (typeof v === 'string' ? w.guid(v) : w.write(v, d));
    case 'any': return (w, v, d) => (isPlainObject(v) ? w.object(v as Record<string, unknown>, d, AnyDeclared) : w.write(v, d));
    case 'datetime':
    case 'dto':
    case 'dto-clock':
      return (w, v, d) => (v instanceof Date ? w.date(v) : typeof v === 'string' ? w.text(v, kind, d) : w.write(v, d));
    case 'decimal':
    case 'int':
    case 'long':
    case 'ulong':
    case 'float':
      return (w, v, d) => (typeof v === 'number' ? w.number(v) : typeof v === 'string' ? w.text(v, kind, d) : w.write(v, d));
    case 'timespan':
    case 'timeonly':
    case 'dateonly':
    case 'char':
      return (w, v, d) => (typeof v === 'string' ? w.text(v, kind, d) : w.write(v, d));
  }
  return writeAsIs; // string, bool, bin, uri, bigint: as they are
}

interface ModelPlan {
  /** The field names (read from the instances). */
  names: string[];
  writers: ValueWriter[];
  /** The .NET names as MsgPack strings (the keys). */
  encoded: Uint8Array[];
  /** The own keys of an instance that had no other properties than the model's. */
  shape?: string[];
  /** The generated code writing the properties (see generateModelWriter), null when it cannot be generated, undefined until first needed. */
  generated?: WriteProperties | null;
}

/** Writes the properties of a model instance that are not undefined (their names and values), returns how many. */
type WriteProperties = (w: Writer, o: Record<string, unknown>, depth: number) => number;

let codeAllowed = true; // false once new Function failed (Content-Security-Policy without 'unsafe-eval')

/**
 * The properties of a model class as code: the names as literals (V8 then sees one property load per place, of instances of one class), the encoded names and the
 * value writers as constants (one function per call site). Undefined values are left out as in the generic loop.
 */
function generateModelWriter(plan: ModelPlan): WriteProperties | null {
  const names = plan.names;
  if (!codeAllowed || names.length === 0 || names.length > 512) return null;
  let code = `const ${names.map((_, t) => `e${t} = encoded[${t}], w${t} = writers[${t}]`).join(', ')};\nreturn function writeProperties(w, o, d) {\nlet c = 0, v;\n`;
  names.forEach((n, t) => {
    code += `v = o[${JSON.stringify(n)}];\nif (v !== undefined) { w.small(e${t}); w${t}(w, v, d); c++; }\n`;
  });
  code += 'return c;\n};';
  try {
    return (new Function('encoded', 'writers', code) as (encoded: Uint8Array[], writers: ValueWriter[]) => WriteProperties)(plan.encoded, plan.writers);
  } catch {
    codeAllowed = false;
    return null;
  }
}

const plans = new WeakMap<ModelClass, ModelPlan>();

function planOf(model: ModelClass): ModelPlan {
  let plan = plans.get(model);
  if (plan === undefined) {
    plan = { names: model.properties.map((p) => p.field), writers: model.properties.map((p) => writerOf(p.type)), encoded: model.properties.map((p) => encode(p.name)) };
    plans.set(model, plan);
  }
  return plan;
}

function sameKeys(keys: string[], shape: string[] | undefined): boolean {
  if (shape === undefined || keys.length !== shape.length) return false;
  for (let t = 0; t < keys.length; t++) if (keys[t] !== shape[t]) return false;
  return true;
}

/** The characters of the hexadecimal digits of byte t of Guid.ToByteArray() in the text (the first three groups are little-endian). */
const GuidSource = [6, 4, 2, 0, 11, 9, 16, 14, 19, 21, 24, 26, 28, 30, 32, 34];

function hexValue(c: number): number {
  if (c >= 48 && c <= 57) return c - 48;
  c |= 32; // lower case
  return c >= 97 && c <= 102 ? c - 87 : -1;
}
