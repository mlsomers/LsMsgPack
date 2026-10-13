// Reading 100 invoices of the test server (BenchmarkInvoices' model) in Node: JSON.parse, @msgpack/msgpack (plain MsgPack, names)
// and this client (application/x-lsmsgpack with the described schema, the body after a schema reference). Also the sizes (raw, gzip).
// Build the test server first: dotnet build clients/typescript/test-server
import { decode } from '@msgpack/msgpack';
import { gzipSync } from 'node:zlib';
import { LsMsgPackClient } from '../dist/index.js';
import { serverAssembly, startServer } from '../test/helpers.mjs';

if (!serverAssembly) {
  console.log('Build the test server first: dotnet build clients/typescript/test-server');
  process.exit(1);
}
const count = +(process.argv[2] ?? 100);
const server = await startServer();
try {
  const bytes = async (path, accept, headers = {}) => {
    const response = await fetch(server.url + path, { headers: { Accept: accept, ...headers } });
    return { body: new Uint8Array(await response.arrayBuffer()), schema: response.headers.get('MsgPack-Schema') };
  };
  const json = (await bytes(`/expected/invoices?count=${count}`, 'application/json')).body;
  const plain = (await bytes(`/invoices?count=${count}`, 'application/msgpack')).body;
  const client = new LsMsgPackClient({ baseUrl: server.url });
  const inline = await bytes(`/invoices?count=${count}`, 'application/x-lsmsgpack; schema=described, application/x-lsmsgpack; q=0.9');
  const origin = new URL(server.url).origin;
  client.decode(inline.body, origin, inline.schema); // learns the schema
  const reference = (await bytes(`/invoices?count=${count}`, 'application/x-lsmsgpack; schema=described, application/x-lsmsgpack; q=0.9', { 'MsgPack-Schemas': inline.schema })).body;
  const plainSchema = (await bytes(`/invoices?count=${count}`, 'application/x-lsmsgpack')).body;

  console.log(`${count} invoices`.padEnd(46), 'raw'.padStart(9), 'gzip'.padStart(9));
  for (const [name, b] of [['JSON (System.Text.Json, every property)', json], ['application/msgpack (names)', plain], ['x-lsmsgpack, plain schema inline', plainSchema],
    ['x-lsmsgpack, described schema inline', inline.body], ['x-lsmsgpack, schema reference', reference]])
    console.log(name.padEnd(46), String(b.length).padStart(9), String(gzipSync(b).length).padStart(9));

  const text = new TextDecoder().decode(json);
  const time = (label, fn) => {
    for (let t = 0; t < 200; t++) fn();
    const runs = 2000;
    const start = process.hrtime.bigint();
    for (let t = 0; t < runs; t++) fn();
    console.log(label.padEnd(56), (Number(process.hrtime.bigint() - start) / runs / 1000).toFixed(1).padStart(8), 'µs');
  };
  console.log('\nread (bytes to objects)');
  time('JSON.parse of the text', () => JSON.parse(text));
  time('JSON.parse with the UTF-8 decoding', () => JSON.parse(new TextDecoder().decode(json)));
  time('@msgpack/msgpack, application/msgpack (names)', () => decode(plain));
  time('this client, x-lsmsgpack described, schema reference', () => client.decode(reference, origin));
  time('this client, x-lsmsgpack described, schema inline', () => client.decode(inline.body, origin, inline.schema));
  const generic = new LsMsgPackClient({ baseUrl: server.url, store: client.store, generateCode: false });
  time('this client without generated code (CSP), reference', () => generic.decode(reference, origin));
} finally {
  server.stop();
}
