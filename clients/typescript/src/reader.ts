// A MsgPack reader for all formats of the specification, with LsMsgPack's extensions (timestamps -1, decimal 1, big integers -2).
// The schema-aware reader (decoder.ts) derives from it: array() and map() are the places it hooks into.

import { MsgPackError } from './errors.js';
import { DecimalMode, decodeDecimal } from './values.js';

/** An extension type the reader does not know (as LtMsgPack's MsgPackExtension). */
export class MsgPackExtension {
  constructor(readonly type: number, readonly data: Uint8Array) {}
}

export interface ReaderOptions {
  /** Decimals (extension type 1): 'number' (default), 'string' (the text of decimal.ToString()), 'raw' (a MsgPackExtension) or a function of the text. */
  decimal?: DecimalMode;
  /** Arrays and maps nested deeper are refused (MsgPackOptions.MaxDepth), 256 by default. */
  maxDepth?: number;
}

const textDecoder = new TextDecoder('utf-8', { fatal: false });
const twoPow32 = 4294967296;

export class MsgPackReader {
  readonly bytes: Uint8Array;
  readonly view: DataView;
  pos: number;
  protected depth = 0;
  protected readonly maxDepth: number;
  protected readonly decimal: DecimalMode;

  constructor(bytes: Uint8Array, options?: ReaderOptions, pos = 0) {
    this.bytes = bytes;
    this.view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    this.pos = pos;
    this.maxDepth = options?.maxDepth ?? 256;
    this.decimal = options?.decimal ?? 'number';
  }

  get remaining(): number {
    return this.bytes.length - this.pos;
  }

  /** The next byte, without reading it. */
  peek(): number {
    if (this.pos >= this.bytes.length) throw this.endOfData();
    return this.bytes[this.pos];
  }

  protected endOfData(): MsgPackError {
    return new MsgPackError('Unexpected end of data.', this.pos);
  }

  /** Throws when fewer than count bytes are left (checked before anything is allocated for a length read from the data). */
  protected need(count: number): void {
    if (count > this.bytes.length - this.pos) throw this.endOfData();
  }

  protected u8(): number {
    if (this.pos >= this.bytes.length) throw this.endOfData();
    return this.bytes[this.pos++];
  }

  protected u16(): number {
    this.need(2);
    const v = this.view.getUint16(this.pos);
    this.pos += 2;
    return v;
  }

  protected u32(): number {
    this.need(4);
    const v = this.view.getUint32(this.pos);
    this.pos += 4;
    return v;
  }

  /** A 64 bit integer: a number when it is safe, otherwise a bigint (no precision is lost). */
  protected i64(signed: boolean): number | bigint {
    this.need(8);
    const hi = signed ? this.view.getInt32(this.pos) : this.view.getUint32(this.pos);
    const lo = this.view.getUint32(this.pos + 4);
    const pos = this.pos;
    this.pos += 8;
    const n = hi * twoPow32 + lo;
    if (Number.isSafeInteger(n)) return n;
    return signed ? this.view.getBigInt64(pos) : this.view.getBigUint64(pos);
  }

  /** A string of the given number of UTF-8 bytes. */
  str(length: number): string {
    this.need(length);
    const bytes = this.bytes;
    const start = this.pos;
    this.pos += length;
    if (length <= 32) { // short ASCII strings (property values, keys) without the decoder call: the codes in an array, one fromCharCode (+= per character was 25% slower, TextDecoder wins beyond 32)
      const codes = new Array<number>(length);
      for (let t = 0; t < length; t++) {
        const c = bytes[start + t];
        if (c > 127) return textDecoder.decode(bytes.subarray(start, start + length));
        codes[t] = c;
      }
      return String.fromCharCode.apply(null, codes);
    }
    return textDecoder.decode(bytes.subarray(start, start + length));
  }

  /** A copy, so the result does not keep the whole payload alive. */
  bin(length: number): Uint8Array {
    this.need(length);
    const result = this.bytes.slice(this.pos, this.pos + length);
    this.pos += length;
    return result;
  }

  /** Any value. */
  value(): unknown {
    const b = this.u8();
    if (b <= 0x7f) return b;
    if (b >= 0xe0) return b - 256;
    if (b >= 0xa0 && b <= 0xbf) return this.str(b & 0x1f);
    if (b >= 0x90 && b <= 0x9f) return this.array(b & 0x0f);
    if (b >= 0x80 && b <= 0x8f) return this.map(b & 0x0f);
    switch (b) {
      case 0xc0: return null;
      case 0xc2: return false;
      case 0xc3: return true;
      case 0xc4: return this.bin(this.u8());
      case 0xc5: return this.bin(this.u16());
      case 0xc6: return this.bin(this.u32());
      case 0xc7: { const n = this.u8(); return this.ext(this.i8(), n); }
      case 0xc8: { const n = this.u16(); return this.ext(this.i8(), n); }
      case 0xc9: { const n = this.u32(); return this.ext(this.i8(), n); }
      case 0xca: { this.need(4); const v = this.view.getFloat32(this.pos); this.pos += 4; return v; }
      case 0xcb: { this.need(8); const v = this.view.getFloat64(this.pos); this.pos += 8; return v; }
      case 0xcc: return this.u8();
      case 0xcd: return this.u16();
      case 0xce: return this.u32();
      case 0xcf: return this.i64(false);
      case 0xd0: return this.i8();
      case 0xd1: { this.need(2); const v = this.view.getInt16(this.pos); this.pos += 2; return v; }
      case 0xd2: { this.need(4); const v = this.view.getInt32(this.pos); this.pos += 4; return v; }
      case 0xd3: return this.i64(true);
      case 0xd4: return this.ext(this.i8(), 1);
      case 0xd5: return this.ext(this.i8(), 2);
      case 0xd6: return this.ext(this.i8(), 4);
      case 0xd7: return this.ext(this.i8(), 8);
      case 0xd8: return this.ext(this.i8(), 16);
      case 0xd9: return this.str(this.u8());
      case 0xda: return this.str(this.u16());
      case 0xdb: return this.str(this.u32());
      case 0xdc: return this.array(this.u16());
      case 0xdd: return this.array(this.u32());
      case 0xde: return this.map(this.u16());
      case 0xdf: return this.map(this.u32());
    }
    throw new MsgPackError(`0x${b.toString(16)} is not a MsgPack type.`, this.pos - 1);
  }

  protected i8(): number {
    const v = this.u8();
    return v > 127 ? v - 256 : v;
  }

  /** The length of an array (its header is read), or -1 when the next value is not an array (nothing is read). */
  arrayLength(): number {
    const b = this.peek();
    if (b >= 0x90 && b <= 0x9f) { this.pos++; return this.count(b & 0x0f); }
    if (b === 0xdc) { this.pos++; return this.count(this.u16()); }
    if (b === 0xdd) { this.pos++; return this.count(this.u32()); }
    return -1;
  }

  /** The number of entries of a map (its header is read), or -1 when the next value is not a map (nothing is read). */
  mapLength(): number {
    const b = this.peek();
    if (b >= 0x80 && b <= 0x8f) { this.pos++; return this.count(b & 0x0f); }
    if (b === 0xde) { this.pos++; return this.count(this.u16()); }
    if (b === 0xdf) { this.pos++; return this.count(this.u32()); }
    return -1;
  }

  /** Every item takes at least a byte: a count beyond the rest of the data is refused before anything is allocated. */
  protected count(n: number): number {
    if (n > this.bytes.length - this.pos) throw this.endOfData();
    return n;
  }

  /** The next value is a map whose first key is "" (a type id: { "": typeId, ... }). Reads nothing. */
  startsWithTypeId(): boolean {
    const b = this.bytes[this.pos];
    let key: number;
    if (b >= 0x81 && b <= 0x8f) key = this.pos + 1;
    else if (b === 0xde) key = this.pos + 3;
    else if (b === 0xdf) key = this.pos + 5;
    else return false;
    const k = this.bytes[key];
    return k === 0xa0 || (k === 0xd9 && this.bytes[key + 1] === 0);
  }

  protected enter(): void {
    if (++this.depth > this.maxDepth) throw new MsgPackError(`The data is nested deeper than ${this.maxDepth} levels.`, this.pos);
  }

  protected array(n: number): unknown[] {
    this.count(n);
    this.enter();
    const result = new Array(n);
    for (let t = 0; t < n; t++) result[t] = this.value();
    this.depth--;
    return result;
  }

  /** A map with string keys becomes an object, other keys are converted to strings (as JSON does). */
  protected map(n: number): unknown {
    this.count(n);
    this.enter();
    const result: Record<string, unknown> = {};
    for (let t = 0; t < n; t++) {
      const key = this.key();
      const name = typeof key === 'string' ? key : String(key);
      if (name === '__proto__') Object.defineProperty(result, name, { value: this.value(), writable: true, enumerable: true, configurable: true }); // an own property, as JSON.parse makes it
      else result[name] = this.value();
    }
    this.depth--;
    return result;
  }

  /** A map key: a fixstr through the key cache, anything else as a value. */
  protected key(): unknown {
    const bytes = this.bytes, p = this.pos, b = bytes[p];
    if (b > 0xa0 && b <= 0xbf) {
      const length = b & 0x1f;
      if (p + 1 + length <= bytes.length) {
        this.pos = p + 1;
        return cachedKey(this, bytes, p + 1, length);
      }
    }
    return this.value();
  }

  protected ext(type: number, length: number): unknown {
    this.need(length);
    const start = this.pos;
    if (type === -1) return this.timestamp(length);
    this.pos += length;
    if (type === 1 && length === 16 && this.decimal !== 'raw') return decodeDecimal(this.bytes, start, this.decimal);
    if (type === -2) return bigIntOf(this.bytes, start, length);
    return new MsgPackExtension(type, this.bytes.slice(start, start + length));
  }

  /** The timestamp extension (type -1) as a Date (milliseconds: the nanoseconds below them are dropped). */
  protected timestamp(length: number): Date {
    const view = this.view;
    const p = this.pos;
    this.pos += length;
    switch (length) {
      case 4: return new Date(view.getUint32(p) * 1000);
      case 8: {
        const hi = view.getUint32(p);
        const lo = view.getUint32(p + 4);
        return new Date(((hi & 3) * twoPow32 + lo) * 1000 + Math.floor((hi >>> 2) / 1e6));
      }
      case 12: return new Date(Number(view.getBigInt64(p + 4)) * 1000 + Math.floor(view.getUint32(p) / 1e6));
    }
    throw new MsgPackError(`A timestamp of ${length} bytes is not valid.`, p);
  }

  /** Skips a value. */
  skip(): void {
    this.value();
  }
}

// The keys of maps read by name repeat (the property names of every object): the same bytes give the same string instance, which V8 has hashed and internalized
// when it was first used as a property name, so setting the property does not hash new text again (decodePlain of 100 invoices with names: 3.4 -> 1.6 ms).
const KeySlots = 1024; // a power of two
const keyBytes: (Uint8Array | undefined)[] = new Array(KeySlots);
const keyStrings: string[] = new Array(KeySlots);

function cachedKey(reader: MsgPackReader, bytes: Uint8Array, start: number, length: number): string {
  let h = 0x811c9dc5; // FNV-1a
  for (let t = start; t < start + length; t++) h = Math.imul(h ^ bytes[t], 0x01000193);
  const slot = (h ^ (h >>> 16)) & (KeySlots - 1);
  const known = keyBytes[slot];
  if (known !== undefined && known.length === length) {
    let t = 0;
    while (t < length && known[t] === bytes[start + t]) t++;
    if (t === length) {
      reader.pos = start + length;
      return keyStrings[slot];
    }
  }
  const key = reader.str(length);
  keyBytes[slot] = bytes.slice(start, start + length);
  keyStrings[slot] = key;
  return key;
}

/** Extension type -2: a big-endian two's complement integer. */
export function bigIntOf(bytes: Uint8Array, start: number, length: number): bigint {
  let value = 0n;
  for (let t = start; t < start + length; t++) value = (value << 8n) | BigInt(bytes[t]);
  if (length > 0 && bytes[start] & 0x80) value -= 1n << BigInt(length * 8);
  return value;
}
