// The MsgPack reader against @msgpack/msgpack (every format), and data that ends early or nests too deep.
import assert from 'node:assert';
import { test } from 'node:test';
import { encode as reference, decode } from '@msgpack/msgpack';
import { MsgPackError, MsgPackExtension, MsgPackReader } from '../dist/index.js';

const read = (bytes) => new MsgPackReader(bytes).value();

test('every format reads as @msgpack/msgpack reads it', () => {
  const values = [
    0, 1, 127, 128, 255, 256, 65535, 65536, 4294967295, 4294967296, Number.MAX_SAFE_INTEGER,
    -1, -32, -33, -128, -129, -32768, -32769, -2147483648, -2147483649, Number.MIN_SAFE_INTEGER,
    0.5, -1.25, 1e300, true, false, null,
    '', 'a', 'x'.repeat(31), 'x'.repeat(32), 'x'.repeat(33), 'é'.repeat(16), 'x'.repeat(31) + 'é', 'x'.repeat(255), 'x'.repeat(256), 'x'.repeat(70000), 'héllo wörld €', '😀'.repeat(20),
    new Uint8Array([1, 2, 3]), new Uint8Array(300), new Uint8Array(70000),
    [], [1, [2, [3]]], new Array(20).fill(7), new Array(70000).fill(1),
    {}, { a: 1, b: { c: [true, null] } }, Object.fromEntries(Array.from({ length: 20 }, (_, t) => ['k' + t, t])),
  ];
  for (const value of values) {
    const bytes = reference(value);
    assert.deepStrictEqual(read(bytes), decode(bytes), `${JSON.stringify(value)?.slice(0, 40)}`);
  }
});

test('float32', () => {
  assert.strictEqual(read(new Uint8Array([0xca, 0x3f, 0xc0, 0x00, 0x00])), 1.5);
});

test('64 bit integers: a number when safe, otherwise a bigint', () => {
  assert.strictEqual(read(new Uint8Array([0xcf, 0, 0, 0, 0, 0, 0, 0, 5])), 5);
  assert.strictEqual(read(new Uint8Array([0xd3, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xfe])), -2);
  assert.strictEqual(read(new Uint8Array([0xcf, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff])), 18446744073709551615n);
  assert.strictEqual(read(new Uint8Array([0xd3, 0x80, 0, 0, 0, 0, 0, 0, 0])), -9223372036854775808n);
});

test('extensions: timestamps, big integers, unknown types', () => {
  assert.deepStrictEqual(read(reference(new Date(Date.UTC(2026, 9, 13, 12, 0, 0)))), new Date(Date.UTC(2026, 9, 13, 12, 0, 0)));
  assert.deepStrictEqual(read(reference(new Date(Date.UTC(2026, 9, 13, 12, 0, 0, 789)))), new Date(Date.UTC(2026, 9, 13, 12, 0, 0, 789)));
  assert.deepStrictEqual(read(reference(new Date(-62135596800000))), new Date(-62135596800000), 'DateTime.MinValue (timestamp 96)');
  assert.deepStrictEqual(read(reference(new Date(-1500))), new Date(-1500), 'before 1970: the seconds round down, the nanoseconds are added');
  assert.strictEqual(read(new Uint8Array([0xd4, 0xfe, 0xff])), -1n);
  assert.strictEqual(read(new Uint8Array([0xd5, 0xfe, 0x00, 0x80])), 128n);
  assert.strictEqual(read(new Uint8Array([0xd5, 0xfe, 0x80, 0x00])), -32768n);
  const unknown = read(new Uint8Array([0xc7, 3, 9, 1, 2, 3]));
  assert.ok(unknown instanceof MsgPackExtension);
  assert.strictEqual(unknown.type, 9);
  assert.deepStrictEqual(unknown.data, new Uint8Array([1, 2, 3]));
});

test('data that ends early throws a MsgPackError (every prefix)', () => {
  const bytes = reference({ name: 'x'.repeat(40), list: [1, 300, 70000, 5000000000, -5, 1.5], bin: new Uint8Array(20), when: new Date(1000), nested: { a: [null, true] } });
  for (let length = 0; length < bytes.length; length++)
    assert.throws(() => read(bytes.subarray(0, length)), MsgPackError, `${length} of ${bytes.length} bytes`);
});

test('a length beyond the data is refused before anything is allocated', () => {
  assert.throws(() => read(new Uint8Array([0xdd, 0x7f, 0xff, 0xff, 0xff])), /Unexpected end of data/);
  assert.throws(() => read(new Uint8Array([0xdf, 0x7f, 0xff, 0xff, 0xff])), /Unexpected end of data/);
  assert.throws(() => read(new Uint8Array([0xc6, 0x7f, 0xff, 0xff, 0xff])), /Unexpected end of data/);
});

test('nesting deeper than maxDepth is refused', () => {
  const deep = new Uint8Array(300).fill(0x91);
  assert.throws(() => read(deep), /nested deeper than 256/);
  assert.throws(() => new MsgPackReader(new Uint8Array([0x91, 0x91, 0x91, 0xc0]), { maxDepth: 2 }).value(), /nested deeper than 2/);
  assert.deepStrictEqual(new MsgPackReader(new Uint8Array([0x91, 0x91, 0xc0]), { maxDepth: 2 }).value(), [[null]]);
});

test('a byte that is not a MsgPack type', () => {
  assert.throws(() => read(new Uint8Array([0xc1])), /not a MsgPack type/);
});

test('map keys: the same text for the same bytes, also for keys that share a slot of the key cache', () => {
  const keys = [];
  for (let t = 0; t < 3000; t++) keys.push((t % 7 === 0 ? 'é' : '') + t.toString(36).repeat(1 + (t % 9)).slice(0, 31));
  keys.push('constructor', 'x'.repeat(31), 'y'.repeat(31));
  const maps = [];
  for (let t = 0; t < keys.length; t += 10) maps.push(Object.fromEntries(keys.slice(t, t + 10).map((k, i) => [k, i])));
  const bytes = reference(maps);
  for (let round = 0; round < 2; round++) assert.deepStrictEqual(read(bytes), decode(bytes), `round ${round}`);
});
