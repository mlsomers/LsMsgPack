// Reading and writing a table of invoices as a stream (docs/Streaming.md): a head with the described schema and an array of unknown length,
// against decoding and encoding the same invoices as one payload. Build the test server first: dotnet build clients/typescript/test-server
import { encode, LsMsgPackClient, MsgPackStreamReader, MsgPackStreamWriter } from '../dist/index.js';
import { serverAssembly, startServer } from '../test/helpers.mjs';

if (!serverAssembly) {
  console.log('Build the test server first: dotnet build clients/typescript/test-server');
  process.exit(1);
}
const count = +(process.argv[2] ?? 10000);
const server = await startServer();
try {
  const bytes = async (path, accept = '*/*') => {
    const response = await fetch(server.url + path, { headers: { Accept: accept } });
    return { body: new Uint8Array(await response.arrayBuffer()), schema: response.headers.get('MsgPack-Schema') };
  };
  const client = new LsMsgPackClient({ baseUrl: server.url, negotiateSchemas: false });
  const payload = (await bytes(`/invoices?count=${count}`, 'application/x-lsmsgpack; schema=described, application/x-lsmsgpack; q=0.9')).body;
  const stream = (await bytes(`/stream/table?count=${count}`)).body;
  const blocks = [];
  for (let at = 0; at < stream.length; at += 65536) blocks.push(stream.subarray(at, at + 65536));
  console.log(`${count} invoices: payload ${payload.length} bytes, stream ${stream.length} bytes (with its index)`);

  const time = async (label, fn, runs = 20) => {
    for (let t = 0; t < 3; t++) await fn();
    const start = process.hrtime.bigint();
    for (let t = 0; t < runs; t++) await fn();
    console.log(label.padEnd(60), (Number(process.hrtime.bigint() - start) / runs / 1e6).toFixed(2).padStart(8), 'ms');
  };
  const table = async (source) => {
    const reader = new MsgPackStreamReader(source, { decoder: client.decoder });
    await reader.readHead();
    let n = 0;
    for await (const invoice of reader.elements()) n += invoice === null ? 0 : 1;
    if (n !== count) throw new Error(`${n} invoices`);
  };

  console.log('\nread');
  await time('decode of the List<Invoice> payload (schema inline)', () => client.decode(payload));
  await time('stream: elements() over the bytes in one chunk', () => table([stream]));
  await time('stream: elements() over 64 KiB chunks', () => table(blocks));
  await time('stream: elements() over a ReadableStream of 64 KiB chunks', () => table(new ReadableStream({
    start(controller) { for (const b of blocks) controller.enqueue(b); controller.close(); },
  })));

  console.log('\nwrite');
  const invoices = client.decode(payload).value;
  await time('encode of the list', () => encode(invoices));
  await time('stream writer: an array of unknown length, 64 KiB chunks', async () => {
    const writer = new MsgPackStreamWriter(() => {});
    writer.writeArrayStart();
    for (const invoice of invoices) writer.write(invoice);
    writer.writeEnd();
    await writer.close();
  });
  await time('the same with an index (a point every 100)', async () => {
    const writer = new MsgPackStreamWriter(() => {}, { index: { pointInterval: 100 } });
    writer.writeArrayStart();
    for (const invoice of invoices) writer.write(invoice);
    writer.writeEnd();
    await writer.close();
  });
} finally {
  server.stop();
}
