// Streams of the .NET MsgPackStreamWriter (test-server/StreamController.cs) read by the TypeScript client, sequentially and by range requests,
// and a stream of the TypeScript writer read by the .NET MsgPackStreamReader. Skipped when the test server is not built (dotnet build clients/typescript/test-server).
import assert from 'node:assert';
import { after, before, test } from 'node:test';
import { LsMsgPackClient, MsgPackStreamWriter } from '../dist/index.js';
import { assertLikeJson, generateModels, generatorAssembly, serverAssembly, startServer } from './helpers.mjs';

if (!serverAssembly && process.env.LSMSGPACK_TS_REQUIRE_SERVER) throw new Error('The test server is not built (dotnet build clients/typescript/test-server).');
const skip = serverAssembly ? false : 'the test server is not built';
let server;
const dotnet = { propertyNames: 'dotnet' };

before(async () => { if (!skip) server = await startServer(); });
after(() => server?.stop());

async function json(path) {
  const response = await fetch(`${server.url}${path}`);
  assert.ok(response.ok, path);
  return response.json();
}

async function collect(iterable) {
  const items = [];
  for await (const item of iterable) items.push(item);
  return items;
}

test('a table: a head with the described schema and an array of unknown length', { skip }, async () => {
  const client = new LsMsgPackClient({ ...dotnet, baseUrl: server.url });
  const reader = await client.stream('/stream/table?count=60');
  await reader.readHead();
  const invoices = await collect(reader.elements());
  assertLikeJson(invoices, await json('/expected/invoices?count=60'));
  assert.ok(await reader.read());
  assert.strictEqual(reader.getInteger(), 60, 'the next entry');
  assert.strictEqual(await reader.read(), false, 'the index is skipped');
});

test('a table with the plain schema, read with the models', { skip: skip || (generatorAssembly ? false : 'lsmsgpack-ts is not built') }, async () => {
  const { models, Invoice } = await generateModels();
  const client = new LsMsgPackClient({ models, baseUrl: server.url });
  const reader = await client.stream('/stream/table?count=10&plain=true');
  await reader.readHead();
  const invoices = await collect(reader.elements(Invoice));
  assert.ok(invoices.every((i) => i instanceof Invoice));
  assertLikeJson(invoices, await json('/dotnet/invoices?camel=true'));
});

test('objects with their schema: inline the first time, then referred to (PerStream)', { skip }, async () => {
  const client = new LsMsgPackClient({ ...dotnet, baseUrl: server.url });
  const reader = await client.stream('/stream/objects?count=12');
  const invoices = [];
  for (let t = 0; t < 12; t++) invoices.push(await reader.readObject());
  assertLikeJson(invoices, await json('/expected/invoices?count=12'));
  await reader.read();
  assert.strictEqual(reader.getInteger(), 12);
});

test('maps, str and bin of unknown length', { skip }, async () => {
  const client = new LsMsgPackClient({ ...dotnet, baseUrl: server.url });
  const reader = await client.stream('/stream/escaped?count=5');
  await reader.read();
  const rows = await reader.getValue();
  const expected = await json('/expected/invoices?count=5');
  assert.deepStrictEqual(rows.map((r) => r.number), expected.map((i) => i.InvoiceNumber));
  assert.deepStrictEqual(rows.map((r) => r.customer), expected.map((i) => i.Customer.CompanyName));
  assert.deepStrictEqual([...rows[3].bytes], [3, 4, 5]);
});

test('the index over range requests: to an entry, to an element', { skip }, async () => {
  const client = new LsMsgPackClient({ ...dotnet, baseUrl: server.url });
  const expected = await json('/expected/invoices?count=200');
  for (const checkpoints of [false, true]) {
    const reader = client.openSeekable(`/stream/table?count=200&checkpoints=${checkpoints}`);
    const indexes = await reader.readIndexes();
    assert.strictEqual(indexes.length, checkpoints ? 3 : 1);
    const entries = indexes.flatMap((i) => i.entries);
    assert.deepStrictEqual(entries.map((e) => e.identifier), ['invoices', 'count']);
    const [table, count] = entries;
    assert.strictEqual(table.contentType, 'application/x-invoices');
    assert.strictEqual(table.elementCount, 200);
    assert.deepStrictEqual(table.points.map((p) => p.element), [0, 25, 50, 75, 100, 125, 150, 175]);

    await reader.seek(count);
    await reader.read();
    assert.strictEqual(reader.getInteger(), 200);

    await reader.seek(table);
    await reader.readHead();
    await reader.read(); // the arrayStart
    await reader.seekElement(table, 160);
    assertLikeJson(await reader.readObject(), expected[160]);
    assertLikeJson(await reader.readObject(), expected[161]);
    await reader.seekElement(table, 30);
    assertLikeJson(await reader.readObject(), expected[30]);
    await reader.close();
  }
});

test('a stream of the TypeScript writer, read by the .NET reader with its index', { skip }, async () => {
  const client = new LsMsgPackClient({ baseUrl: server.url });
  const invoices = await client.get('/invoices?count=30'); // camelCase: the .NET reader matches the names ignoring case
  const chunks = [];
  const writer = new MsgPackStreamWriter((c) => { chunks.push(c); }, { flushThreshold: 1000, index: { pointInterval: 10 } });
  writer.beginEntry('invoices', 'application/x-invoices').metadata = { source: 'test' };
  writer.writeArrayStart();
  for (const invoice of invoices) writer.write(invoice);
  writer.writeEnd();
  writer.beginEntry(2);
  writer.writeStringStart();
  writer.write('streamed ');
  writer.writeStringChunk('text 😀');
  writer.writeEnd();
  await writer.close();

  const response = await fetch(`${server.url}/stream/echo`, { method: 'POST', body: Buffer.concat(chunks), headers: { 'Content-Type': 'application/octet-stream' } });
  assert.ok(response.ok, await response.clone().text());
  const echoed = await response.json();
  assertLikeJson(echoed.Invoices, await json('/expected/invoices?count=30'));
  assert.strictEqual(echoed.Text, 'streamed text 😀');
  assert.strictEqual(echoed.Sought, invoices[29].invoiceNumber, 'SeekElement over the points of the TypeScript index');
  assert.strictEqual(echoed.Indexes.length, 1);
  const [index] = echoed.Indexes;
  assert.strictEqual(index.Status, 'Complete');
  assert.strictEqual(index.Writer, '@lsmsgpack/client');
  assert.deepStrictEqual(index.Entries.map((e) => [e.Identifier, e.ContentType, e.ElementCount]), [['invoices', 'application/x-invoices', 30], ['2', null, null]]);
  assert.deepStrictEqual(index.Entries[0].Points, [0, 10, 20]);
  assert.deepStrictEqual(index.Entries[0].Metadata, { source: 'test' });
  assert.strictEqual(index.Entries[1].Offset, index.Entries[0].Offset + index.Entries[0].Length);
});
