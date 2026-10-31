// The escape sequences of arrays, maps, str and bin of unknown length: the byte 0xC1 followed by one code byte, as proposed in msgpack/msgpack#347
// (not in the specification yet, so the codes may still change: only this file has them, as Escape.cs on the .NET side).
// Each start code is the first byte of an empty value of the same family, the end code is nil. Every other code is reserved and an error.

import { MsgPackError } from './errors.js';

export const Escape = {
  Byte: 0xc1,
  ArrayStart: 0x90,
  MapStart: 0x80,
  StringStart: 0xa0,
  BinaryStart: 0xc4,
  End: 0xc0,
} as const;

export const Proposal = 'msgpack/msgpack#347';

export function reservedEscape(code: number, offset: number): MsgPackError {
  return new MsgPackError(`The escape sequence 0xC1 0x${hex(code)} is reserved (${Proposal}), the data is not valid.`, offset);
}

export function escapeAtEnd(offset: number): MsgPackError {
  return new MsgPackError('The data ends with the escape byte 0xC1, an escape sequence has two bytes.', offset);
}

export function misplacedEnd(where: string, offset: number): MsgPackError {
  return new MsgPackError(`The end of a value of unknown length (0xC1 0xC0) ${where}.`, offset);
}

export function hex(b: number): string {
  return b.toString(16).toUpperCase().padStart(2, '0');
}

/**
 * The size of the header of the value at `at` (a complete value without escape byte) and the length of the data after it
 * (str, bin, extension, the bytes of a number; 0 for arrays and maps: their elements follow).
 */
function measure(b: Uint8Array, view: DataView, at: number): [header: number, length: number] {
  const f = b[at];
  if (f <= 0x7f || f >= 0xe0 || (f & 0xf0) === 0x90 || (f & 0xf0) === 0x80) return [1, 0];
  if ((f & 0xe0) === 0xa0) return [1, f & 0x1f];
  switch (f) {
    case 0xc0: case 0xc2: case 0xc3: return [1, 0];
    case 0xcc: case 0xd0: return [1, 1];
    case 0xcd: case 0xd1: return [1, 2];
    case 0xce: case 0xd2: case 0xca: return [1, 4];
    case 0xcf: case 0xd3: case 0xcb: return [1, 8];
    case 0xd9: case 0xc4: return [2, b[at + 1]];
    case 0xda: case 0xc5: return [3, view.getUint16(at + 1)];
    case 0xdb: case 0xc6: return [5, view.getUint32(at + 1)];
    case 0xdc: case 0xde: return [3, 0];
    case 0xdd: case 0xdf: return [5, 0];
    case 0xd4: return [2, 1];
    case 0xd5: return [2, 2];
    case 0xd6: return [2, 4];
    case 0xd7: return [2, 8];
    case 0xd8: return [2, 16];
    case 0xc7: return [3, b[at + 1]];
    case 0xc8: return [4, view.getUint16(at + 1)];
    case 0xc9: return [6, view.getUint32(at + 1)];
  }
  throw new MsgPackError(`0x${hex(f)} is not a MsgPack type.`, at);
}

/**
 * The value at bytes[start, end) with its arrays, maps, str and bin of unknown length replaced by ones of known length, so the decoder can read it.
 * The value was checked token by token before (the stream reader), which kept the number of elements, entries or bytes of each value of unknown length
 * in the order of their starts (`counts`).
 */
export function plain(bytes: Uint8Array, start: number, end: number, counts: readonly number[]): Uint8Array {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const parts: Uint8Array[] = [];
  let size = 0;
  const add = (part: Uint8Array) => {
    if (part.length === 0) return;
    parts.push(part);
    size += part.length;
  };
  let next = 0; // the next count
  let chunks = false;
  let run = start; // values without escapes are copied in runs
  let at = start;
  while (at < end) {
    if (bytes[at] === Escape.Byte) {
      add(bytes.subarray(run, at));
      const code = bytes[at + 1];
      at += 2;
      switch (code) {
        case Escape.ArrayStart: add(header(0x90, 16, 0xdc, 0xdd, counts[next++])); break;
        case Escape.MapStart: add(header(0x80, 16, 0xde, 0xdf, counts[next++])); break;
        case Escape.StringStart: add(stringHeader(counts[next++])); chunks = true; break;
        case Escape.BinaryStart: add(binHeader(counts[next++])); chunks = true; break;
        default: chunks = false; break; // the end: nothing for arrays and maps (their count is in the header), the chunks of a str or bin end
      }
      run = at;
      continue;
    }
    const [h, length] = measure(bytes, view, at);
    if (chunks) {
      add(bytes.subarray(at + h, at + h + length)); // the data of the chunk, behind the one header
      run = at + h + length;
    }
    at += h + length;
  }
  add(bytes.subarray(run, end));
  const result = new Uint8Array(size);
  let p = 0;
  for (const part of parts) {
    result.set(part, p);
    p += part.length;
  }
  return result;
}

/** An array or map header (fixarray/fixmap below `fixLimit`, then 16 and 32 bits). */
function header(fix: number, fixLimit: number, b16: number, b32: number, n: number): Uint8Array {
  if (n < fixLimit) return Uint8Array.of(fix | n);
  if (n < 0x10000) return Uint8Array.of(b16, n >>> 8, n & 0xff);
  return Uint8Array.of(b32, n >>> 24, (n >>> 16) & 0xff, (n >>> 8) & 0xff, n & 0xff);
}

function stringHeader(n: number): Uint8Array {
  if (n < 32) return Uint8Array.of(0xa0 | n);
  if (n < 0x100) return Uint8Array.of(0xd9, n);
  return header(0, 0, 0xda, 0xdb, n);
}

function binHeader(n: number): Uint8Array {
  if (n < 0x100) return Uint8Array.of(0xc4, n);
  return header(0, 0, 0xc5, 0xc6, n);
}
