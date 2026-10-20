// The .NET value formats: decimal (extension 1), Guid, TimeSpan, DateOnly, TimeOnly, DateTimeOffset text.
import assert from 'node:assert';
import { test } from 'node:test';
import { guidBytes, guidString } from '../dist/index.js';
import { dateOnlyText, decodeDecimal, offsetText, timeOnlyText, timeSpanText } from '../dist/values.js';

/** The 16 bytes of a System.Decimal (MpDecimal.DecimalStruct: flags, hi, lo, mid, little-endian). */
function decimalBytes(mantissa, scale, negative = false) {
  const bytes = new Uint8Array(16);
  const view = new DataView(bytes.buffer);
  view.setInt32(0, (scale << 16) | (negative ? 0x80000000 : 0), true);
  view.setUint32(4, Number((mantissa >> 64n) & 0xffffffffn), true);
  view.setUint32(8, Number(mantissa & 0xffffffffn), true);
  view.setUint32(12, Number((mantissa >> 32n) & 0xffffffffn), true);
  return bytes;
}

test('decimals as numbers and as the text of decimal.ToString()', () => {
  const cases = [
    [1250n, 2, false, 12.5, '12.50'],
    [0n, 2, false, 0, '0.00'],
    [0n, 0, true, 0, '0'],
    [15n, 1, true, -1.5, '-1.5'],
    [5n, 4, false, 0.0005, '0.0005'],
    [123456789012345678n, 0, false, 123456789012345678, '123456789012345678'],
    [79228162514264337593543950335n, 0, false, 79228162514264337593543950335, '79228162514264337593543950335'],
    [1n, 28, false, 1e-28, '0.0000000000000000000000000001'],
    [123456789n, 3, true, -123456.789, '-123456.789'],
  ];
  for (const [mantissa, scale, negative, number, text] of cases) {
    const bytes = decimalBytes(mantissa, scale, negative);
    assert.strictEqual(decodeDecimal(bytes, 0, 'number'), number, text);
    assert.strictEqual(decodeDecimal(bytes, 0, 'string'), text);
    assert.strictEqual(decodeDecimal(bytes, 0, (t) => `[${t}]`), `[${text}]`);
  }
});

test('Guid in the byte order of Guid.ToByteArray()', () => {
  const bytes = new Uint8Array([0x5b, 0xad, 0x8f, 0x0f, 0xcb, 0xd9, 0x9f, 0x46, 0xa1, 0x65, 0x70, 0x86, 0x77, 0x28, 0x95, 0x0e]); // .NET: Guid.Parse("0f8fad5b-...").ToByteArray()
  assert.strictEqual(guidString(bytes), '0f8fad5b-d9cb-469f-a165-70867728950e');
  assert.deepStrictEqual(guidBytes('0F8FAD5B-D9CB-469F-A165-70867728950E'), bytes);
  assert.throws(() => guidBytes('not a guid'));
});

test('TimeSpan as TimeSpan.ToString("c")', () => {
  assert.strictEqual(timeSpanText(0), '00:00:00');
  assert.strictEqual(timeSpanText(937845000000), '1.02:03:04.5000000');
  assert.strictEqual(timeSpanText(-600000000), '-00:01:00');
  assert.strictEqual(timeSpanText(9223372036854775807n), '10675199.02:48:05.4775807');
});

test('DateOnly and TimeOnly', () => {
  assert.strictEqual(dateOnlyText(0), '0001-01-01');
  assert.strictEqual(dateOnlyText(739536), '2025-10-13');
  assert.strictEqual(timeOnlyText(0), '00:00:00');
  assert.strictEqual(timeOnlyText(452962500000), '12:34:56.2500000');
});

test('DateTimeOffset text with its offset', () => {
  assert.strictEqual(offsetText(new Date(Date.UTC(2026, 9, 13, 12, 0, 0)), 120), '2026-10-13T14:00:00.000+02:00');
  assert.strictEqual(offsetText(new Date(Date.UTC(2026, 9, 13, 12, 0, 0)), -330), '2026-10-13T06:30:00.000-05:30');
});
