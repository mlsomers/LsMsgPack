// Conversions of the .NET value formats (see docs/Compatibility.md and docs/TypeScriptClient.md).

/** How decimals are returned: 'number' (default, as JSON.parse gives them), 'string' (the text of decimal.ToString(), keeps the scale and all digits), 'raw' (the extension), or a function of that text (e.g. a decimal library). */
export type DecimalMode = 'number' | 'string' | 'raw' | ((text: string) => unknown);

const twoPow32 = 4294967296;

/** Extension type 1: System.Decimal as it is in memory (flags, hi, lo, mid: little-endian 32 bit integers, see LsMsgPack's MpDecimal). */
export function decodeDecimal(bytes: Uint8Array, start: number, mode: DecimalMode): unknown {
  const flags = int32le(bytes, start); // read in place: a DataView per value costs more than the rest
  const hi = int32le(bytes, start + 4) >>> 0;
  const lo = int32le(bytes, start + 8) >>> 0;
  const mid = int32le(bytes, start + 12) >>> 0;
  const scale = (flags >>> 16) & 0xff;
  const negative = flags < 0;
  if (mode === 'number' && hi === 0 && mid < 0x200000 && scale <= 22) { // the mantissa and the power of ten are exact doubles: one correctly rounded division
    const mantissa = mid * twoPow32 + lo;
    if (mantissa === 0) return 0;
    const value = scale === 0 ? mantissa : mantissa / 10 ** scale;
    return negative ? -value : value;
  }
  const text = decimalText(hi, mid, lo, scale, negative);
  if (mode === 'string' || mode === 'raw') return text;
  if (mode === 'number') return Number(text);
  return mode(text);
}

function int32le(b: Uint8Array, p: number): number {
  return b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);
}

function decimalText(hi: number, mid: number, lo: number, scale: number, negative: boolean): string {
  let digits = hi === 0 && mid < 0x200000 ? String(mid * twoPow32 + lo) : ((BigInt(hi) << 64n) | (BigInt(mid) << 32n) | BigInt(lo)).toString();
  const zero = digits === '0';
  if (scale > 0) {
    if (digits.length <= scale) digits = '0'.repeat(scale - digits.length + 1) + digits;
    digits = digits.slice(0, digits.length - scale) + '.' + digits.slice(digits.length - scale);
  }
  return negative && !zero ? '-' + digits : digits;
}

/** A decimal written as text or a number (DecimalFormat.String, or a default value) in the requested mode. */
export function convertDecimal(value: unknown, mode: DecimalMode): unknown {
  if (typeof value === 'number') return mode === 'string' || mode === 'raw' ? String(value) : typeof mode === 'function' ? mode(String(value)) : value;
  if (typeof value === 'string') return mode === 'number' ? Number(value) : typeof mode === 'function' ? mode(value) : value;
  return value;
}

const hex: string[] = [];
for (let t = 0; t < 256; t++) hex.push((t < 16 ? '0' : '') + t.toString(16));

/** A Guid as bin 16 (Guid.ToByteArray(): the first three groups little-endian) as its text, e.g. "0f8fad5b-d9cb-469f-a165-70867728950e". */
export function guidString(b: Uint8Array, p = 0): string {
  return hex[b[p + 3]] + hex[b[p + 2]] + hex[b[p + 1]] + hex[b[p]] + '-' + hex[b[p + 5]] + hex[b[p + 4]] + '-' + hex[b[p + 7]] + hex[b[p + 6]] + '-' +
    hex[b[p + 8]] + hex[b[p + 9]] + '-' + hex[b[p + 10]] + hex[b[p + 11]] + hex[b[p + 12]] + hex[b[p + 13]] + hex[b[p + 14]] + hex[b[p + 15]];
}

/** The bytes of a Guid as .NET writes them (the reverse of guidString). */
export function guidBytes(guid: string): Uint8Array {
  const h = guid.replace(/[-{}]/g, '');
  if (!/^[0-9a-fA-F]{32}$/.test(h)) throw new Error(`"${guid}" is not a Guid.`);
  const b = new Uint8Array(16);
  for (let t = 0; t < 16; t++) b[t] = parseInt(h.substr(t * 2, 2), 16);
  const swap = (i: number, j: number) => { const x = b[i]; b[i] = b[j]; b[j] = x; };
  swap(0, 3); swap(1, 2); swap(4, 5); swap(6, 7);
  return b;
}

export const EmptyGuid = '00000000-0000-0000-0000-000000000000';

/** DateTime.MinValue (0001-01-01T00:00:00Z) in milliseconds since 1970. */
export const MinDateTimeMs = -62135596800000;


const TicksPerSecond = 10000000;

function pad(value: number, length: number): string {
  let s = String(value);
  while (s.length < length) s = '0' + s;
  return s;
}

/** TimeSpan ticks (100 ns) as TimeSpan.ToString("c") and System.Text.Json write it: [-][d.]hh:mm:ss[.fffffff]. */
export function timeSpanText(ticks: number | bigint): string {
  let t = typeof ticks === 'bigint' ? ticks : BigInt(Math.trunc(ticks));
  const negative = t < 0n;
  if (negative) t = -t;
  const fraction = Number(t % 10000000n);
  let seconds = t / 10000000n;
  const s = Number(seconds % 60n); seconds /= 60n;
  const m = Number(seconds % 60n); seconds /= 60n;
  const h = Number(seconds % 24n);
  const days = seconds / 24n;
  return (negative ? '-' : '') + (days > 0n ? days.toString() + '.' : '') + pad(h, 2) + ':' + pad(m, 2) + ':' + pad(s, 2) + (fraction > 0 ? '.' + pad(fraction, 7) : '');
}

/** DateOnly's day number (days since 0001-01-01) as yyyy-MM-dd. */
export function dateOnlyText(dayNumber: number): string {
  const date = new Date(MinDateTimeMs + dayNumber * 86400000);
  return pad(date.getUTCFullYear(), 4) + '-' + pad(date.getUTCMonth() + 1, 2) + '-' + pad(date.getUTCDate(), 2);
}

/** TimeOnly's ticks as HH:mm:ss[.fffffff] (as System.Text.Json writes it: seven digits when there is a fraction). */
export function timeOnlyText(ticks: number | bigint): string {
  const t = Number(ticks);
  const fraction = t % TicksPerSecond;
  const seconds = Math.floor(t / TicksPerSecond);
  const text = pad(Math.floor(seconds / 3600), 2) + ':' + pad(Math.floor(seconds / 60) % 60, 2) + ':' + pad(seconds % 60, 2);
  return fraction === 0 ? text : text + '.' + pad(fraction, 7);
}

/** A moment with its offset as ISO 8601 text with that offset, e.g. 2026-01-01T12:00:00.000+02:00. */
export function offsetText(moment: Date, offsetMinutes: number): string {
  const clock = new Date(moment.getTime() + offsetMinutes * 60000).toISOString(); // ends with Z
  const sign = offsetMinutes < 0 ? '-' : '+';
  const abs = Math.abs(offsetMinutes);
  return clock.slice(0, clock.length - 1) + sign + pad(Math.floor(abs / 60), 2) + ':' + pad(abs % 60, 2);
}


