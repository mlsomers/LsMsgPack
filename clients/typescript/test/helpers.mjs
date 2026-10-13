// Shared by the tests: the test server (test-server/, built with dotnet), and comparing decoded values with System.Text.Json of the same .NET values.
import assert from 'node:assert';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';
import { encode } from '../dist/index.js';

const here = fileURLToPath(new URL('.', import.meta.url));

/** The built test server (the first that exists), or undefined: the live tests are skipped. */
export const serverAssembly = ['Debug', 'DEBUG', 'Release', 'RELEASE']
  .map((c) => `${here}../test-server/bin/${c}/net8.0/LsMsgPackTsTestServer.dll`)
  .find((p) => existsSync(p));

/** A schema map with the entries in order (encode of an object cannot repeat a name). */
export function schemaBytes(entries) {
  const parts = [new Uint8Array([0xde, entries.length >> 8, entries.length & 0xff])];
  for (const [name, items] of entries) parts.push(encode(name), encode(items));
  return Uint8Array.from(Buffer.concat(parts));
}

/** Starts the test server; stop() closes its standard input, which ends it. */
export async function startServer() {
  const server = spawn(process.env.DOTNET || 'dotnet', [serverAssembly], { stdio: ['pipe', 'pipe', 'inherit'], windowsHide: true });
  const url = await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.once('exit', (code) => reject(new Error(`The test server ended (${code}).`)));
    createInterface({ input: server.stdout }).once('line', resolve);
  });
  return { url, stop: () => server.stdin.end() };
}

const isoWithoutOffset = /^\d{4}-\d\d-\d\dT\d\d:\d\d(:\d\d(\.\d+)?)?$/;
const iso = /^\d{4}-\d\d-\d\dT\d\d:\d\d(:\d\d(\.\d+)?)?(Z|[+-]\d\d:\d\d)?$/;

/** The moment of System.Text.Json's text of a DateTime or DateTimeOffset (without an offset: UTC, as DateTime.MinValue). */
function moment(text) {
  return Date.parse(isoWithoutOffset.test(text) ? text + 'Z' : text);
}

/** Asserts that a decoded value is what System.Text.Json wrote for the same .NET value: the same properties (complete objects), Date for date text, Uint8Array for base64. */
export function assertLikeJson(actual, expected, path = '$') {
  if (expected === null) return assert.strictEqual(actual, null, `${path}: expected null`);
  if (typeof expected === 'string') {
    if (actual instanceof Date) return assert.strictEqual(actual.getTime(), moment(expected), `${path}: ${actual.toISOString()} is not ${expected}`);
    if (actual instanceof Uint8Array) return assert.strictEqual(Buffer.from(actual).toString('base64'), expected, `${path}: bytes`);
    if (typeof actual === 'string' && iso.test(actual) && iso.test(expected)) return assert.strictEqual(moment(actual), moment(expected), `${path}: ${actual} is not ${expected}`);
    return assert.strictEqual(actual, expected, `${path}`);
  }
  if (typeof expected === 'number') return assert.strictEqual(typeof actual === 'bigint' ? Number(actual) : actual, expected, `${path}`);
  if (typeof expected === 'boolean') return assert.strictEqual(actual, expected, `${path}`);
  if (Array.isArray(expected)) {
    assert.ok(Array.isArray(actual), `${path}: not an array`);
    assert.strictEqual(actual.length, expected.length, `${path}: length`);
    expected.forEach((e, t) => assertLikeJson(actual[t], e, `${path}[${t}]`));
    return;
  }
  assert.ok(actual !== null && typeof actual === 'object', `${path}: not an object`);
  assert.deepStrictEqual(Object.keys(actual).sort(), Object.keys(expected).sort(), `${path}: properties`);
  for (const key of Object.keys(expected)) assertLikeJson(actual[key], expected[key], `${path}.${key}`);
}
