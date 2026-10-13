// Plain MsgPack for request bodies (application/msgpack): objects as maps keyed by their property names, as the formatters read them with their Plain options.

import { MsgPackError } from './errors.js';
import { MsgPackExtension } from './reader.js';

const textEncoder = new TextEncoder();

export interface EncodeOptions {
  /** Objects and arrays nested deeper are refused (cycles), 256 by default. */
  maxDepth?: number;
}

/**
 * Encodes a value: null and undefined as nil (undefined properties are left out), numbers as the smallest integer or a float64, bigint as a 64 bit integer,
 * Date as a timestamp, Uint8Array (and other typed arrays, ArrayBuffer) as bin, Map and plain objects as maps, MsgPackExtension as an extension.
 */
export function encode(value: unknown, options: EncodeOptions = {}): Uint8Array {
  const writer = new Writer(options.maxDepth ?? 256);
  writer.write(value, 0);
  return writer.result();
}

class Writer {
  private buf = new Uint8Array(256);
  private view = new DataView(this.buf.buffer);
  private pos = 0;

  constructor(private readonly maxDepth: number) {}

  result(): Uint8Array {
    return this.buf.slice(0, this.pos);
  }

  private ensure(count: number): void {
    if (this.pos + count <= this.buf.length) return;
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
    if (typeof value === 'object') {
      const o = value as Record<string, unknown>;
      const keys = Object.keys(o).filter((k) => o[k] !== undefined && typeof o[k] !== 'function');
      this.mapHeader(keys.length);
      for (const k of keys) {
        this.string(k);
        this.write(o[k], depth + 1);
      }
      return;
    }
    throw new MsgPackError(`A ${typeof value} cannot be written as MsgPack.`);
  }

  private number(n: number): void {
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

  private bigint(n: bigint): void {
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

  private string(s: string): void {
    const bytes = textEncoder.encode(s);
    const n = bytes.length;
    if (n <= 31) this.u8(0xa0 | n);
    else if (n <= 0xff) this.header(0xd9, n, 1);
    else if (n <= 0xffff) this.header(0xda, n, 2);
    else this.header(0xdb, n, 4);
    this.raw(bytes);
  }

  private raw(bytes: Uint8Array): void {
    this.ensure(bytes.length);
    this.buf.set(bytes, this.pos);
    this.pos += bytes.length;
  }

  private bin(bytes: Uint8Array): void {
    const n = bytes.length;
    if (n <= 0xff) this.header(0xc4, n, 1);
    else if (n <= 0xffff) this.header(0xc5, n, 2);
    else this.header(0xc6, n, 4);
    this.raw(bytes);
  }

  private arrayHeader(n: number): void {
    if (n <= 15) this.u8(0x90 | n);
    else if (n <= 0xffff) this.header(0xdc, n, 2);
    else this.header(0xdd, n, 4);
  }

  private mapHeader(n: number): void {
    if (n <= 15) this.u8(0x80 | n);
    else if (n <= 0xffff) this.header(0xde, n, 2);
    else this.header(0xdf, n, 4);
  }

  private ext(type: number, data: Uint8Array): void {
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
  private date(date: Date): void {
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
