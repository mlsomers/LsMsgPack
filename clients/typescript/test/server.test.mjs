// Against the ASP.NET Core formatters (test-server/): complete objects, schema negotiation, request bodies. Skipped when the test server is not built
// (dotnet build clients/typescript/test-server).
import assert from 'node:assert';
import { after, before, test } from 'node:test';
import { HttpError, LsMsgPackClient, MissingSchemaError, Schema, SchemaHeader, SchemaStore } from '../dist/index.js';
import { assertLikeJson, serverAssembly, startServer } from './helpers.mjs';

if (!serverAssembly && process.env.LSMSGPACK_TS_REQUIRE_SERVER) throw new Error('The test server is not built (dotnet build clients/typescript/test-server).');
const skip = serverAssembly ? false : 'the test server is not built';
let server;

before(async () => { if (!skip) server = await startServer(); });
after(() => server?.stop());

async function expected(name, query = '') {
  const response = await fetch(`${server.url}/expected/${name}${query}`);
  assert.ok(response.ok);
  return response.json();
}

test('invoices are complete objects', { skip }, async () => {
  const client = new LsMsgPackClient({ baseUrl: server.url });
  const invoices = await client.get('/invoices?count=20');
  assertLikeJson(invoices, await expected('invoices', '?count=20'));
  assert.ok(invoices.some((i) => i.Lines.some((l) => l.DiscountPercentage === 0)), 'values the writer left out have their defaults');
});

test('every kind, as default values', { skip }, async () => {
  const client = new LsMsgPackClient({ baseUrl: server.url });
  const kinds = await client.get('/kinds/defaults');
  assertLikeJson(kinds, await expected('defaults'));
  assert.deepStrictEqual(kinds.Point, { X: 0, Y: 0 }, 'a struct never written has its default object');
  assert.strictEqual(kinds.Sound, 'Woof', '[DefaultValue]');
  assert.strictEqual(kinds.Seven, 7);
});

test('every kind, with values', { skip }, async () => {
  const client = new LsMsgPackClient({ baseUrl: server.url });
  assertLikeJson(await client.get('/kinds/values'), await expected('values'));
});

test('polymorphic values get the properties of their class', { skip }, async () => {
  const client = new LsMsgPackClient({ baseUrl: server.url });
  const zoo = await client.get('/zoo');
  assert.deepStrictEqual(zoo.Animals, [{ $type: 'Dog', Barks: 3, Name: 'Rex' }, { $type: 'Cat', Indoor: true, Name: 'Tom' }, { $type: 'Dog', Barks: 0, Name: 'Quiet' }]);
  assert.deepStrictEqual(zoo.Favorite, { $type: 'Cat', Indoor: false, Name: 'Felix' });
  assert.deepStrictEqual(zoo.Anything, { $type: 'Dog', Barks: 1, Name: 'Boxed' });
  assert.ok(!('$type' in zoo), 'only where the server wrote a type id');
  assert.strictEqual(zoo.Addresses[0].City, 'Rome');
  assert.strictEqual(zoo.Addresses[0].Street, null);
});

test('a list as the root, and null', { skip }, async () => {
  const client = new LsMsgPackClient({ baseUrl: server.url });
  assertLikeJson(await client.get('/addresses'), await expected('addresses'));
  const nothing = await client.send('/nothing');
  assert.strictEqual(nothing.response.status, 204);
  assert.strictEqual(nothing.value, undefined);
});

test('the schema is sent once, then referred to', { skip }, async () => {
  const client = new LsMsgPackClient({ baseUrl: server.url });
  const origin = new URL(server.url).origin;
  const first = await client.send('/invoice');
  assert.strictEqual(first.reference, false);
  assert.ok(first.schema.described);
  assert.strictEqual(first.response.headers.get(SchemaHeader), first.schema.id, 'the id .NET computed is the hash the client computes');
  assert.strictEqual(client.store.advertise(origin), first.schema.id);

  const second = await client.send('/invoice');
  assert.strictEqual(second.reference, true);
  assert.strictEqual(second.schema, first.schema);
  assert.deepStrictEqual(second.value, first.value);
});

test('the store can be exported and imported (e.g. into localStorage)', { skip }, async () => {
  const client = new LsMsgPackClient({ baseUrl: server.url });
  await client.get('/invoice');
  const store = new SchemaStore();
  store.import(JSON.parse(JSON.stringify(client.store.export())));
  const restored = new LsMsgPackClient({ baseUrl: server.url, store });
  assert.strictEqual((await restored.send('/invoice')).reference, true);
});

test('a reference to a schema the store no longer holds: a GET is repeated without advertising', { skip }, async () => {
  const store = new SchemaStore();
  let clear = false;
  const client = new LsMsgPackClient({
    baseUrl: server.url, store,
    fetch: async (url, init) => {
      const response = await fetch(url, init);
      if (clear) { store.clear(); clear = false; } // e.g. cleared by another part of the application while the request ran
      return response;
    },
  });
  await client.get('/invoice');
  clear = true;
  const again = await client.send('/invoice');
  assert.strictEqual(again.reference, false);

  const other = new LsMsgPackClient({ baseUrl: server.url });
  await assert.rejects(other.get('/invoice', { headers: { 'MsgPack-Schemas': again.schema.id } }), MissingSchemaError, 'not repeated when the caller set the header');
});

test('without the described schema: objects only where type ids are', { skip }, async () => {
  const client = new LsMsgPackClient({ baseUrl: server.url, describedSchema: false });
  const result = await client.send('/zoo');
  assert.strictEqual(result.schema.described, false);
  assert.ok(Array.isArray(result.value), 'the root is the array of values: its class is not known');
  const animals = result.value[1];
  assert.deepStrictEqual(animals[0], { $type: 'Dog', Barks: 3, Name: 'Rex' }, 'a value with a type id is an object of its entry');
  assert.deepStrictEqual(animals[2], { $type: 'Dog', Barks: null, Name: 'Quiet' }, 'without the kinds a value the writer left out is null');
  assert.strictEqual(result.response.headers.get(SchemaHeader), Schema.idOf(result.schema.bytes, 1));
});

test('request bodies: plain MsgPack the server reads', { skip }, async () => {
  const client = new LsMsgPackClient({ baseUrl: server.url });
  const invoice = await client.get('/invoice');
  const echoed = await client.post('/echo/invoice', invoice);
  assertLikeJson(echoed, await expected('invoice'));
});

test("propertyNames: 'camelCase': the objects of System.Text.Json with camelCase, request bodies read ignoring case", { skip }, async () => {
  const client = new LsMsgPackClient({ baseUrl: server.url, propertyNames: 'camelCase' });
  assertLikeJson(await client.get('/invoices?count=20'), await expected('invoices', '?count=20&camel=true'));
  assertLikeJson(await client.get('/kinds/defaults'), await expected('defaults', '?camel=true'));
  assertLikeJson(await client.get('/kinds/values'), await expected('values', '?camel=true'));
  assertLikeJson(await client.post('/echo/invoice', await client.get('/invoice')), await expected('invoice'), 'the echo is written with the .NET names');
});

test('polymorphic request bodies: $type is the class the server creates', { skip }, async () => {
  const client = new LsMsgPackClient({ baseUrl: server.url });
  const zoo = await client.get('/zoo'); // read, changed and sent back: the objects keep their classes
  zoo.Animals.push({ $type: 'Cat', Name: 'Kit', Indoor: true }); // written by hand
  zoo.Favorite = zoo.Animals[0];
  const echoed = await client.post('/echo/zoo', zoo);
  assert.deepStrictEqual(echoed, {
    Title: 'City zoo',
    Animals: [
      { Type: 'Dog', Value: { Barks: 3, Name: 'Rex' } },
      { Type: 'Cat', Value: { Indoor: true, Name: 'Tom' } },
      { Type: 'Dog', Value: { Barks: 0, Name: 'Quiet' } },
      { Type: 'Cat', Value: { Indoor: true, Name: 'Kit' } },
    ],
    Favorite: { Type: 'Dog', Value: { Barks: 3, Name: 'Rex' } },
    Anything: { Type: 'Dog', Value: { Barks: 1, Name: 'Boxed' } }, // declared as object
    Addresses: [{ Street: null, HouseNumber: null, PostalCode: null, City: 'Rome', Region: null, CountryCode: null }],
  });

  const literal = await client.post('/echo/zoo', { Title: 'New', Animals: [{ $type: 'Dog', Name: 'Max' }] }); // nothing read first
  assert.deepStrictEqual(literal.Animals, [{ Type: 'Dog', Value: { Barks: 0, Name: 'Max' } }]);

  await assert.rejects(client.post('/echo/zoo', { Animals: [{ $type: 'Address', City: 'Rome' }] }), (e) => e instanceof HttpError && e.status === 400, 'a class that is not an Animal is refused');
  await assert.rejects(client.post('/echo/zoo', { Animals: [{ Name: 'Rex' }] }), (e) => e instanceof HttpError && e.status === 400, 'without $type the server cannot create an Animal');
});
