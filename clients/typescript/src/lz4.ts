// LZ4 compressed bodies (MsgPackOptions.Compression of LsMsgPack, the package LsMsgPack.Lz4; MessagePack-CSharp's Lz4Block and Lz4BlockArray), read only: an optional
// subpath export, so the main entry point carries none of it. Pass `lz4` as DecodeOptions.compression (also of the client and the stream reader).
// The checks are those of LsMsgPack.Core's CompressedBody: every claimed length is checked before anything is allocated, a block must decompress to exactly its length.
import type { BodyDecompression } from './decoder.js';
import { EndOfDataError, MsgPackError } from './errors.js';

/** The extension type of a body compressed as one block: an integer with the length of the body, then the LZ4 block. */
export const BlockExtensionType = 99;
/** The extension type at the start of a body compressed in blocks: an array of this extension (the length of each block) and the blocks as binary values. */
export const BlockArrayExtensionType = 98;

/** LZ4 makes at most about 255 bytes of each byte of a block (a match length grows by 255 per byte); a claim beyond is not LZ4. */
const MaxExpansion = 255;
const ExpansionSlack = 64;

/**
 * Decompresses the LZ4 compressed bodies LsMsgPack (`Compression = Lz4Compression.Block` or `BlockArray`) and MessagePack-CSharp (`WithCompression`) write:
 * `new Decoder({ compression: lz4 })`, `new LsMsgPackClient({ compression: lz4 })`. Uncompressed bodies are read as before. Reading only: the encoder does not compress.
 */
export const lz4: BodyDecompression = {
  startsAt(bytes: Uint8Array, pos: number, end: number): boolean {
    if (end - pos < 2) return false;
    const type = extTypeAt(bytes, pos, end);
    if (type !== undefined) return type === BlockExtensionType;
    const header = arrayHeaderLength(bytes[pos]);
    return header > 0 && pos + header < end && extTypeAt(bytes, pos + header, end) === BlockArrayExtensionType;
  },

  decompress(bytes: Uint8Array, pos: number, end: number, maxLength: number): { body: Uint8Array; end: number } {
    const c = new Cursor(bytes, pos, end);
    const count = c.arrayHeader();
    const ext = c.extHeader();
    if (ext.type !== (count < 0 ? BlockExtensionType : BlockArrayExtensionType)) throw invalid(`extension type ${ext.type} where the compressed data starts`, pos);
    const contentEnd = c.take(ext.length);
    if (count < 0) {
      // ext 99: the length of the body, then one block
      const length = new Cursor(bytes, c.pos - ext.length, contentEnd);
      const claimed = length.integer();
      checkClaim(claimed, contentEnd - length.pos, maxLength, pos);
      const body = new Uint8Array(claimed);
      decompressInto(bytes, length.pos, contentEnd, body, 0, claimed, pos);
      return { body, end: c.pos };
    }

    // [ext 98 with the length of each block, the blocks as bin]: every block checked before the body is allocated
    const blocks = count - 1;
    if (blocks < 1) throw invalid('an array of blocks without a block', pos);
    const lengths = new Cursor(bytes, c.pos - ext.length, contentEnd);
    const sizes: number[] = [];
    let total = 0;
    for (let b = 0; b < blocks; b++) {
      const size = lengths.integer();
      if (size < 0) throw invalid('a negative length', pos);
      total += size;
      if (total > maxLength) throw tooLong(total, maxLength, pos);
      sizes.push(size);
    }
    if (lengths.pos !== contentEnd) throw invalid('more lengths than blocks', pos);
    const starts: number[] = [];
    const ends: number[] = [];
    for (let b = 0; b < blocks; b++) {
      const length = c.binHeader();
      starts.push(c.pos);
      ends.push(c.take(length));
      checkExpansion(sizes[b], length, pos);
    }
    const body = new Uint8Array(total);
    let at = 0;
    for (let b = 0; b < blocks; b++) {
      decompressInto(bytes, starts[b], ends[b], body, at, sizes[b], pos);
      at += sizes[b];
    }
    return { body, end: c.pos };
  },
};

function checkClaim(claimed: number, blockLength: number, maxLength: number, at: number): void {
  if (claimed < 0) throw invalid('a negative length', at);
  if (claimed > maxLength) throw tooLong(claimed, maxLength, at);
  checkExpansion(claimed, blockLength, at);
}

function checkExpansion(claimed: number, blockLength: number, at: number): void {
  if (claimed > blockLength * MaxExpansion + ExpansionSlack)
    throw invalid(`a block of ${blockLength} bytes claiming ${claimed} bytes, more than LZ4 can produce from it`, at);
}

function decompressInto(src: Uint8Array, start: number, end: number, dst: Uint8Array, at: number, length: number, offset: number): void {
  checkExpansion(length, end - start, offset);
  const written = decodeBlock(src, start, end, dst, at, at + length);
  if (written !== length) throw invalid(written < 0 ? 'a block that is not valid LZ4' : `a block of ${written} bytes where ${length} were claimed`, offset);
}

/**
 * Decodes one raw LZ4 block (the block format of lz4, without the frame) from src[start, end) into dst[at, dstEnd).
 * @returns the bytes written, -1 when the block is not valid LZ4 or does not fit (nothing is written outside dst[at, dstEnd))
 */
export function decodeBlock(src: Uint8Array, start: number, end: number, dst: Uint8Array, at: number, dstEnd: number): number {
  let ip = start;
  let op = at;
  while (ip < end) {
    const token = src[ip++];
    let literals = token >>> 4;
    if (literals === 15) {
      let b: number;
      do {
        if (ip >= end) return -1;
        b = src[ip++];
        literals += b;
      } while (b === 255);
    }
    if (literals > end - ip || literals > dstEnd - op) return -1;
    if (literals < 16) {
      for (let t = 0; t < literals; t++) dst[op + t] = src[ip + t];
    } else {
      dst.set(src.subarray(ip, ip + literals), op);
    }
    ip += literals;
    op += literals;
    if (ip >= end) break; // the last sequence has literals only

    if (end - ip < 2) return -1;
    const distance = src[ip] | (src[ip + 1] << 8);
    ip += 2;
    if (distance === 0 || distance > op - at) return -1;
    let match = token & 15;
    if (match === 15) {
      let b: number;
      do {
        if (ip >= end) return -1;
        b = src[ip++];
        match += b;
      } while (b === 255);
    }
    match += 4;
    if (match > dstEnd - op) return -1;
    let from = op - distance;
    if (distance >= match && match >= 32) {
      dst.copyWithin(op, from, from + match); // not overlapping
    } else {
      for (let t = 0; t < match; t++) dst[op + t] = dst[from + t]; // overlapping: the bytes just written repeat
    }
    op += match;
  }
  return op - at;
}

function extTypeAt(bytes: Uint8Array, pos: number, end: number): number | undefined {
  let at: number;
  switch (bytes[pos]) {
    case 0xd4: case 0xd5: case 0xd6: case 0xd7: case 0xd8: at = 1; break;
    case 0xc7: at = 2; break;
    case 0xc8: at = 3; break;
    case 0xc9: at = 5; break;
    default: return undefined;
  }
  return pos + at < end ? (bytes[pos + at] << 24) >> 24 : undefined;
}

function arrayHeaderLength(first: number): number {
  if ((first & 0xf0) === 0x90) return 1;
  if (first === 0xdc) return 3;
  if (first === 0xdd) return 5;
  return 0;
}

function invalid(what: string, at: number): MsgPackError {
  return new MsgPackError(`The compressed body is not valid: ${what}.`, at);
}

function tooLong(length: number, max: number, at: number): MsgPackError {
  return new MsgPackError(`The data holds a compressed body that decompresses to ${length} bytes, more than maxDecompressedLength (${max}) allows.`, at);
}

/** Reads the headers of a compressed body (big-endian, as the specification), every length checked against the data. */
class Cursor {
  constructor(private readonly bytes: Uint8Array, public pos: number, private readonly end: number) {}

  private byte(): number {
    if (this.pos >= this.end) throw new EndOfDataError(this.pos);
    return this.bytes[this.pos++];
  }

  private uint(size: number): number {
    if (this.end - this.pos < size) throw new EndOfDataError(this.pos);
    let v = 0;
    for (let t = 0; t < size; t++) v = v * 256 + this.bytes[this.pos++];
    return v;
  }

  /** Passes `length` bytes, returns where they end. */
  take(length: number): number {
    if (length > this.end - this.pos) throw new EndOfDataError(this.end);
    this.pos += length;
    return this.pos;
  }

  /** The count of an array header, -1 when the value is no array (the cursor stays). */
  arrayHeader(): number {
    if (this.pos >= this.end) throw new EndOfDataError(this.pos);
    const first = this.bytes[this.pos];
    if ((first & 0xf0) === 0x90) {
      this.pos++;
      return first & 0x0f;
    }
    if (first === 0xdc || first === 0xdd) {
      this.pos++;
      return this.uint(first === 0xdc ? 2 : 4);
    }
    return -1;
  }

  extHeader(): { length: number; type: number } {
    const at = this.pos;
    let length: number;
    switch (this.byte()) {
      case 0xd4: length = 1; break;
      case 0xd5: length = 2; break;
      case 0xd6: length = 4; break;
      case 0xd7: length = 8; break;
      case 0xd8: length = 16; break;
      case 0xc7: length = this.uint(1); break;
      case 0xc8: length = this.uint(2); break;
      case 0xc9: length = this.uint(4); break;
      default: throw invalid('no extension where the compressed data starts', at);
    }
    return { length, type: (this.byte() << 24) >> 24 };
  }

  binHeader(): number {
    const at = this.pos;
    switch (this.byte()) {
      case 0xc4: return this.uint(1);
      case 0xc5: return this.uint(2);
      case 0xc6: return this.uint(4);
      default: throw invalid('a block that is no binary value', at);
    }
  }

  /** An integer in any format (MessagePack-CSharp writes the lengths compact, the length of one block as int 32). */
  integer(): number {
    const at = this.pos;
    const first = this.byte();
    if (first <= 0x7f) return first;
    if (first >= 0xe0) return first - 256;
    switch (first) {
      case 0xcc: return this.uint(1);
      case 0xcd: return this.uint(2);
      case 0xce: return this.uint(4);
      case 0xcf: return this.uint(8);
      case 0xd0: return (this.uint(1) << 24) >> 24;
      case 0xd1: return (this.uint(2) << 16) >> 16;
      case 0xd2: return this.uint(4) | 0;
      case 0xd3: { const high = this.uint(4) | 0; const low = this.uint(4); return high * 4294967296 + low; }
      default: throw invalid('a length that is no integer', at);
    }
  }
}
