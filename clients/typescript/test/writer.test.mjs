// Request bodies: what the encoder writes reads back as @msgpack/msgpack and the reader of this package read it.
import assert from 'node:assert';
import { test } from 'node:test';
import { decode } from '@msgpack/msgpack';
import { encode, MsgPackExtension, MsgPackReader } from '../dist/index.js';

test('values read back as they were', () => {
  const values = [
    null, true, false, 0, 127, 128, 255, 256, 65535, 65536, 4294967295, 4294967296, -1, -32, -33, -128, -129, -32768, -32769, -2147483648, -2147483649,
    0.5, -0.0, 1e300, '', 'héllo', 'x'.repeat(40), 'x'.repeat(300), 'x'.repeat(70000), [1, [2]], new Array(20).fill(0), { a: 1, b: [true, 'x'] },
    new Uint8Array([1, 2, 3]), Object.fromEntries(Array.from({ length: 20 }, (_, t) => ['k' + t, t])),
  ];
  for (const value of values) {
    const bytes = encode(value);
    assert.deepStrictEqual(decode(bytes), value, JSON.stringify(value)?.slice(0, 40));
    assert.deepStrictEqual(new MsgPackReader(bytes).value(), value);
  }
});

test('the smallest encodings', () => {
  assert.deepStrictEqual(encode(5), Uint8Array.of(5));
  assert.deepStrictEqual(encode(-5), Uint8Array.of(0xfb));
  assert.deepStrictEqual(encode(200), Uint8Array.of(0xcc, 200));
  assert.deepStrictEqual(encode(1.5), Uint8Array.of(0xcb, 0x3f, 0xf8, 0, 0, 0, 0, 0, 0));
});

test('dates as timestamps, in the three sizes', () => {
  for (const date of [new Date(Date.UTC(2026, 9, 13)), new Date(Date.UTC(2026, 9, 13, 1, 2, 3, 456)), new Date(-62135596800000), new Date(-1500), new Date(Date.UTC(2600, 0, 1))]) {
    assert.deepStrictEqual(decode(encode(date)), date, date.toISOString());
    assert.deepStrictEqual(new MsgPackReader(encode(date)).value(), date);
  }
  assert.strictEqual(encode(new Date(Date.UTC(2026, 9, 13))).length, 6, 'timestamp 32');
});

test('bigint, typed arrays, Map, extensions, undefined properties', () => {
  assert.strictEqual(decode(encode(2n ** 63n), { useBigInt64: true }), 2n ** 63n);
  assert.strictEqual(decode(encode(-(2n ** 63n)), { useBigInt64: true }), -(2n ** 63n));
  assert.throws(() => encode(2n ** 64n));
  assert.deepStrictEqual(decode(encode(new Uint16Array([1]))), new Uint8Array([1, 0]));
  assert.deepStrictEqual(decode(encode(new Map([[1, 'a']]))), { 1: 'a' });
  assert.deepStrictEqual(decode(encode({ a: 1, b: undefined, c: () => 1 })), { a: 1 });
  const ext = new MsgPackReader(encode(new MsgPackExtension(5, new Uint8Array([9, 8])))).value();
  assert.deepStrictEqual([ext.type, [...ext.data]], [5, [9, 8]]);
});

test('cycles are refused', () => {
  const a = {};
  a.self = a;
  assert.throws(() => encode(a), /nested deeper/);
});
