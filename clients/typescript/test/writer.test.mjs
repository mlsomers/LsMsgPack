// Request bodies: what the encoder writes reads back as @msgpack/msgpack and the reader of this package read it.
import assert from 'node:assert';
import { test } from 'node:test';
import { decode, encode as reference } from '@msgpack/msgpack';
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

test('strings: the bytes of TextEncoder in every header size, lone surrogates as U+FFFD', () => {
  const strings = ['', 'a', 'x'.repeat(31), 'x'.repeat(32), 'x'.repeat(31) + 'é', 'é'.repeat(32), '😀'.repeat(16), 'a\uD800', '\uD800a', '\uDC00', 'a\uD83D', '\uDE00\uD83D',
    'x'.repeat(33), 'é'.repeat(200), 'x'.repeat(300), '😀'.repeat(20000), 'x'.repeat(70000)];
  for (const s of strings) {
    const utf8 = new TextEncoder().encode(s);
    const n = utf8.length;
    const header = n <= 31 ? [0xa0 | n] : n <= 0xff ? [0xd9, n] : n <= 0xffff ? [0xda, n >> 8, n & 0xff] : [0xdb, n >>> 24, (n >> 16) & 0xff, (n >> 8) & 0xff, n & 0xff];
    assert.deepStrictEqual(Buffer.from(encode(s)), Buffer.concat([Buffer.from(header), Buffer.from(utf8)]), JSON.stringify(s).slice(0, 20));
  }
});

test('$type is the type id: the key "", first', () => {
  assert.deepStrictEqual([...encode({ Name: 'Rex', $type: 'Dog', Barks: 3 })], [...encode(new Map([['', 'Dog'], ['Name', 'Rex'], ['Barks', 3]]))]);
  assert.deepStrictEqual([...encode({ $type: undefined, Name: 'Rex' })], [...encode({ Name: 'Rex' })], 'undefined: left out');
  assert.throws(() => encode({ $type: 1 }), /\$type is a \.NET type name/);
  assert.deepStrictEqual(decode(encode(new Map([['$type', 'x']]))), { $type: 'x' }, 'a Map is a dictionary: written as it is');
});

test('left out properties: the map header counts the entries written, also when it gets smaller', () => {
  for (const [keys, left] of [[20, 6], [17, 1], [16, 1], [20, 0], [70000, 1]]) {
    const o = {};
    for (let t = 0; t < keys; t++) o['k' + t] = t < left ? undefined : t;
    const written = Object.fromEntries(Object.entries(o).filter(([, v]) => v !== undefined));
    assert.deepStrictEqual(Buffer.from(encode(o)), Buffer.from(reference(written)), `${keys} keys, ${left} undefined`);
  }
  const late = { a: 1, b: undefined };
  for (let t = 0; t < 18; t++) late['k' + t] = t;
  late.$type = 'Late';
  assert.deepStrictEqual(decode(encode(late)), { '': 'Late', a: 1, ...Object.fromEntries(Array.from({ length: 18 }, (_, t) => ['k' + t, t])) }, '$type after other properties: first');
  assert.strictEqual(encode(late)[0], 0xde, 'map16 for 20 entries');
});

test('only own properties are written', () => {
  const o = Object.create({ inherited: 1 });
  o.own = 2;
  assert.deepStrictEqual(decode(encode(o)), { own: 2 });
  assert.deepStrictEqual(decode(encode(Object.assign(Object.create(null), { a: 1 }))), { a: 1 });
});
