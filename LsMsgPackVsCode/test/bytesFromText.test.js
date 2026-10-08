const test = require('node:test');
const assert = require('node:assert');
const { bytesFromText, unquote } = require('../out/bytesFromText');

const bytes = (...values) => Uint8Array.from(values);

test('hex string, with or without 0x, odd length padded like the explorer', () => {
  assert.deepStrictEqual(bytesFromText('0x1a4F'), bytes(0x1a, 0x4f));
  assert.deepStrictEqual(bytesFromText('81A3'), bytes(0x81, 0xa3));
  assert.deepStrictEqual(bytesFromText('abc'), bytes(0xab, 0xc0));
});

test('delimited hex (also as BitConverter.ToString writes it)', () => {
  assert.deepStrictEqual(bytesFromText('81 A3 66'), bytes(0x81, 0xa3, 0x66));
  assert.deepStrictEqual(bytesFromText('81-A3-66'), bytes(0x81, 0xa3, 0x66));
  assert.deepStrictEqual(bytesFromText('0x81, 0xA3,0x66'), bytes(0x81, 0xa3, 0x66));
});

test('hex in groups of different lengths (the header separated from the content)', () => {
  assert.deepStrictEqual(bytesFromText('91 c4 02 0001'), bytes(0x91, 0xc4, 0x02, 0x00, 0x01));
  assert.deepStrictEqual(bytesFromText('1234 56'), bytes(0x12, 0x34, 0x56)); // 1234 is no decimal byte
  assert.deepStrictEqual(bytesFromText('100, 20'), bytes(100, 20)); // still decimal
});

test('values with 0x are hex also when they are not all written with two digits', () => {
  // "0x92, 1, 2" was read as the decimal values 92, 1 and 2
  assert.deepStrictEqual(bytesFromText('[0x92, 1, 2]'), bytes(0x92, 0x01, 0x02));
  assert.deepStrictEqual(bytesFromText('{ 0x92, 0x1, 0xA }'), bytes(0x92, 0x01, 0x0a));
  assert.throws(() => bytesFromText('0x92, 0x100'), /not a byte/);
});

test('delimited decimal values (an array copied from a debugger)', () => {
  assert.deepStrictEqual(bytesFromText('[129, 163, 102, 200]'), bytes(129, 163, 102, 200));
  assert.deepStrictEqual(bytesFromText('{ 1, 2, 255 }'), bytes(1, 2, 255));
  assert.throws(() => bytesFromText('1, 2, 256'), /not a byte/);
});

test('base64, also broken over lines', () => {
  assert.deepStrictEqual(bytesFromText('gaNmb28='), bytes(0x81, 0xa3, 0x66, 0x6f, 0x6f));
  assert.deepStrictEqual(bytesFromText('gaNm\nb28='), bytes(0x81, 0xa3, 0x66, 0x6f, 0x6f));
});

test('Python bytes literals', () => {
  assert.deepStrictEqual(bytesFromText("b'\\x81\\xa3foo'"), bytes(0x81, 0xa3, 0x66, 0x6f, 0x6f));
  assert.deepStrictEqual(bytesFromText('b"\\n\\\\\\x00"'), bytes(10, 92, 0));
});

test('text that is no bytes', () => {
  assert.throws(() => bytesFromText('hello world!'), /does not seem to represent a byte array/);
  assert.strictEqual(bytesFromText('  ').length, 0);
});

test('unquote: the strings of C#, JavaScript and Python debuggers', () => {
  assert.strictEqual(unquote('"gaNm"'), 'gaNm');
  assert.strictEqual(unquote("'gaNm'"), 'gaNm');
  assert.strictEqual(unquote('"a\\r\\nb\\"c\\u0041"'), 'a\r\nb"cA');
  assert.strictEqual(unquote('@"a""b"'), 'a"b');
  assert.strictEqual(unquote('400'), '400');
});
