// Generated models (lsmsgpack-ts) and the hot-loaded schema set: unit tests with hand-written models, and the models of the test server against the formatters.
import assert from 'node:assert';
import { after, before, test } from 'node:test';
import { Decoder, defineModels, encode, LsMsgPackClient, MsgPackReader, SchemaStore } from '../dist/index.js';
import { assertLikeJson, generateModels, generatorAssembly, schemaBytes, serverAssembly, startServer } from './helpers.mjs';

// Models as lsmsgpack-ts writes them (compiled to JavaScript)
class Address {
  Street = null;
  City = 'Utrecht';
}
class Animal {
  Name = null;
}
class Dog extends Animal {
  Barks = 0;
}
class Order {
  Id = '00000000-0000-0000-0000-000000000000';
  Retries = 3;
  Tags = ['new'];
  Home = Object.assign(new Address(), { Street: 'Main' });
  Pet = null;
  Pets = null;
  Timeout = '00:00:30';
  Due = new Date(-62135596800000);
}
const models = defineModels({
  int64: 'number', dates: 'date', decimal: 'number',
  classes: [
    [Address, 'Address', [['Street', 'string'], ['City', 'string']]],
    [Dog, 'Dog', [['Barks', 'int'], ['Name', 'string']]],
    [Order, 'Order', [['Id', 'guid'], ['Retries', 'int'], ['Tags', ['list', 'string']], ['Home', ['class', 'Address']], ['Pet', ['class', 'Animal']], ['Pets', ['list', ['class', 'Animal']]],
      ['Timeout', 'timespan'], ['Due', 'datetime']]],
  ],
});

const guidBin = [0xc4, 16, 0x5b, 0xad, 0x8f, 0x0f, 0xcb, 0xd9, 0x9f, 0x46, 0xa1, 0x65, 0x70, 0x86, 0x77, 0x28, 0x95, 0x0e];

test('defineModels puts the type name on the prototype', () => {
  const dog = new Dog();
  assert.strictEqual(dog.$type, 'Dog');
  assert.deepStrictEqual(Object.keys(dog), ['Name', 'Barks'], 'not an own property: JSON.stringify leaves it out');
  assert.strictEqual(models.byName('Order').ctor, Order);
  assert.strictEqual(models.of(new Order()).typeName, 'Order');
  assert.strictEqual(models.byName('Dog').derived, true, 'extends Animal (abstract: not in the list)');
  assert.strictEqual(models.byName('Order').derived, false);
});

/** A described schema with Order (entry 0) and Address (1), the root Order; the body as given. */
function orderPayload(body, orderItems) {
  const schema = schemaBytes([
    ['Order', orderItems ?? [['Id', 'guid'], ['Retries', 'int'], ['Tags', ['list', 'string']], ['Home', 1], ['Pet', 'any'], ['Pets', ['list', 'any']], ['Timeout', 'timespan'], ['Due', 'datetime']]],
    ['Address', [['Street', 'string'], ['City', 'string']]],
    ['Dog', [['Barks', 'int'], ['Name', 'string']]],
    ['', [['@', 0]]],
  ]);
  return Uint8Array.from([...schema, ...body]);
}

test('an entry of a model becomes an instance of its class, a left out value keeps what the class set', () => {
  const decoder = new Decoder({ models });
  // [Id, nil, nil, [nil, "Rome"], {"": 2, "@": [5, "Rex"]}, nil, 300000000 (30 s), nil]
  const body = [0x98, ...guidBin, 0xc0, 0xc0, 0x92, 0xc0, 0xa4, ...Buffer.from('Rome'), 0x82, 0xa0, 2, 0xa1, 0x40, 0x92, 5, 0xa3, ...Buffer.from('Rex'), 0xc0, 0xce, 0x11, 0xe1, 0xa3, 0x00, 0xc0];
  const order = decoder.decode(orderPayload(body)).value;
  assert.ok(order instanceof Order);
  assert.strictEqual(order.Id, '0f8fad5b-d9cb-469f-a165-70867728950e');
  assert.strictEqual(order.Retries, 3, 'nil: the constructor value, as a .NET reader keeps it');
  assert.deepStrictEqual(order.Tags, ['new']);
  assert.ok(order.Home instanceof Address);
  assert.strictEqual(order.Home.Street, null, 'nil keeps the value of a new Address (as the .NET reader makes one), not the one the Order constructor put in its own');
  assert.strictEqual(order.Home.City, 'Rome');
  assert.ok(order.Pet instanceof Dog);
  assert.strictEqual(order.Pet.$type, 'Dog');
  assert.deepStrictEqual(Object.keys(order.Pet), ['Name', 'Barks'], 'no own $type on a model');
  assert.strictEqual(order.Timeout, '00:00:30');
  assert.strictEqual(order.Due.getTime(), -62135596800000);

  const again = decoder.decode(orderPayload(body)).value;
  assert.notStrictEqual(again.Tags, order.Tags, 'initial values are not shared between objects');
  assert.notStrictEqual(again.Due, order.Due);
});

test('the server and the models differ: the schema decides which value is which', () => {
  const decoder = new Decoder({ models });
  // The server has a new property Priority and no longer writes Tags, Pet, Pets, Timeout or Due, and has another order
  const items = [['Priority', 'int'], ['Retries', 'int'], ['Id', 'guid'], ['Home', 1]];
  const order = decoder.decode(orderPayload([0x94, 9, 1, ...guidBin, 0xc0], items)).value;
  assert.ok(order instanceof Order);
  assert.strictEqual(order.Priority, 9, 'a property the models do not have yet is set too');
  assert.strictEqual(order.Retries, 1);
  assert.strictEqual(order.Id, '0f8fad5b-d9cb-469f-a165-70867728950e');
  assert.deepStrictEqual(order.Tags, ['new'], 'a property the server does not write keeps the class value');
  assert.strictEqual(order.Home.Street, 'Main');
});

test('a schema with names only: the models give the kinds', () => {
  const schema = schemaBytes([['Order', ['Id', 'Retries', 'Tags', 'Home', 'Pet', 'Pets', 'Timeout', 'Due']], ['Address', ['Street', 'City']]]);
  // { "": 0, "@": [...] }: a type id, so the root is known
  const body = [0x82, 0xa0, 0, 0xa1, 0x40, 0x98, ...guidBin, 0xc0, 0xc0, 0x92, 0xc0, 0xa1, 0x58, 0xc0, 0xc0, 0xce, 0x11, 0xe1, 0xa3, 0x00, 0xc0];
  const order = new Decoder({ models }).decode(Uint8Array.from([...schema, ...body])).value;
  assert.ok(order instanceof Order);
  assert.strictEqual(order.Id, '0f8fad5b-d9cb-469f-a165-70867728950e', 'bin 16 is a Guid: the model says so');
  assert.strictEqual(order.Timeout, '00:00:30', 'ticks are a TimeSpan');
  assert.ok(order.Home instanceof Address, 'the entry of the class by its name');
  assert.strictEqual(order.Home.City, 'X');
});

test('the options must be the ones the models were generated with', () => {
  assert.throws(() => new Decoder({ models, int64: 'bigint' }), /int64/);
  const payload = Uint8Array.from([...schemaBytes([['', [['@', 'datetime']]]]), 0xd6, 0xff, 0, 0, 0, 0]); // a DateTime root
  assert.strictEqual(new Decoder({ models: defineModels({ dates: 'iso', classes: [] }) }).decode(payload).value, '1970-01-01T00:00:00.000Z', 'taken from the models when not given');
});

/** The map of an encoded object as [key, value] pairs (in order). */
function entries(bytes) {
  const r = new MsgPackReader(bytes, { decimal: 'raw' });
  const n = r.mapLength();
  const result = [];
  for (let t = 0; t < n; t++) result.push([r.value(), r.value()]);
  return result;
}

test('writing a model: the declared kinds, and the type id only where the declared type is not the class', () => {
  const order = new Order();
  order.Id = '0f8fad5b-d9cb-469f-a165-70867728950e';
  order.Pet = Object.assign(new Dog(), { Name: 'Rex' });
  order.Pets = [new Dog()];
  order.Extra = 'kept';
  const written = entries(encode(order, { models }));
  assert.deepStrictEqual(written.map(([k]) => k), ['Id', 'Retries', 'Tags', 'Home', 'Pet', 'Pets', 'Timeout', 'Due', 'Extra'], 'no type id: an Order is written where an Order is declared');
  assert.deepStrictEqual([...written[0][1]], guidBin.slice(2), 'a Guid as its 16 bytes');
  assert.strictEqual(written[6][1], 300000000, 'TimeSpan text as ticks');
  assert.deepStrictEqual(written[4][1], { '': 'Dog', Name: 'Rex', Barks: 0 }, 'declared Animal: the type id, first');
  assert.deepStrictEqual(written[3][1], { Street: 'Main', City: 'Utrecht' }, 'declared Address: none');
  assert.deepStrictEqual(written[5][1], [{ '': 'Dog', Name: null, Barks: 0 }]);
  assert.deepStrictEqual(entries(encode([new Order()], { models }).subarray(1)).map(([k]) => k)[0], 'Id', 'the root: only a class deriving from another one gets one');
  assert.deepStrictEqual(entries(encode([new Dog()], { models }).subarray(1))[0], ['', 'Dog'], 'e.g. a List<Animal> body');
});

test('writing a model: other own properties are written after its own, also when the instance before had none', () => {
  const plain = new Address();
  assert.deepStrictEqual(entries(encode(plain, { models })).map(([k]) => k), ['Street', 'City']);
  const more = Object.assign(new Address(), { Skipped: undefined, Zip: '1234', Fn: () => 1, Country: 'NL' });
  assert.deepStrictEqual(entries(encode(more, { models })), [['Street', null], ['City', 'Utrecht'], ['Zip', '1234'], ['Country', 'NL']]);
  const noted = Object.assign(new Address(), { Skipped: undefined });
  assert.deepStrictEqual(entries(encode(noted, { models })).length, 2);
  noted.Skipped = 'now';
  assert.deepStrictEqual(entries(encode(noted, { models }))[2], ['Skipped', 'now'], 'the same keys as before, with a value now');
});

test('writing the other kinds', () => {
  const kinds = defineModels({
    classes: [[class K { }, 'K', [['C', 'char'], ['D', 'dateonly'], ['T', 'timeonly'], ['M', 'decimal'], ['O', 'dto'], ['N', ['dict', 'int', 'string']], ['P', ['pair', 'string', 'int']]]]],
  });
  const K = kinds.byName('K').ctor;
  const k = Object.assign(new K(), { C: 'é', D: '2026-10-13', T: '12:34:56.25', M: '1234.5600', O: '2026-10-13T14:34:56.789+02:00', N: { 1: 'one' }, P: { Key: 'a', Value: 1 } });
  const written = Object.fromEntries(entries(encode(k, { models: kinds })));
  assert.strictEqual(written.C, 233);
  assert.strictEqual(written.D, 739901, 'DateOnly.DayNumber');
  assert.strictEqual(written.T, 452962500000);
  assert.strictEqual(written.M.type, 1, 'the decimal extension: every digit');
  assert.deepStrictEqual([...written.M.data], [0, 0, 4, 0, 0, 0, 0, 0, 0x00, 0x61, 0xbc, 0x00, 0, 0, 0, 0], '12345600 (0xbc6100), scale 4');
  assert.strictEqual(written.O[0].getTime(), Date.parse('2026-10-13T12:34:56.789Z'));
  assert.strictEqual(written.O[1], 120);
  const bytes = [...encode(k, { models: kinds })];
  const n = bytes.findIndex((b, t) => b === 0xa1 && bytes[t + 1] === 0x4e); // the key "N"
  assert.deepStrictEqual(bytes.slice(n, n + 8), [0xa1, 0x4e, 0x81, 0x01, 0xa3, 0x6f, 0x6e, 0x65], 'the key of an int dictionary as a number, not "1"');
  assert.deepStrictEqual(written.P, ['a', 1]);
});

test('preloaded schemas are advertised to every server', () => {
  const store = new SchemaStore({ maxAdvertisedSchemas: 3 });
  const a = schemaBytes([['A', ['X']]]), b = schemaBytes([['B', ['Y']]]);
  assert.strictEqual(store.preload({ 'A[]': Buffer.from(a).toString('base64'), B: b }), 2);
  const ids = store.advertise('https://example.com').split(',');
  assert.strictEqual(ids.length, 2);
  assert.ok(store.has(ids[0]) && store.has(ids[1]));
  const c = schemaBytes([['C', ['Z']]]);
  store.preload([c], 'https://other.example');
  assert.strictEqual(store.advertise('https://other.example').split(',').length, 3, 'its own first, then the ones for every server');
  assert.strictEqual(store.advertise('https://example.com').split(',').length, 2);
  assert.throws(() => store.preload(['bm90IGEgc2NoZW1h']), 'not a schema');
});

// Against the formatters, with the models and schemas lsmsgpack-ts generates for the test server

if (!(serverAssembly && generatorAssembly) && process.env.LSMSGPACK_TS_REQUIRE_SERVER) throw new Error('Build the test server and the generator (dotnet build clients/typescript/test-server LsMsgPackTypeScript).');
const skip = serverAssembly && generatorAssembly ? false : 'the test server or lsmsgpack-ts is not built';
let server, generated;

before(async () => {
  if (skip) return;
  generated = await generateModels();
  server = await startServer();
});
after(() => server?.stop());

async function json(path) {
  const response = await fetch(`${server.url}${path}`);
  assert.ok(response.ok, path);
  return response.json();
}

test('hot-loaded schemas: the first response refers to its schema', { skip }, async () => {
  const client = new LsMsgPackClient({ baseUrl: server.url, models: generated.models, schemas: generated.schemas });
  const first = await client.send('/invoices?count=10');
  assert.strictEqual(first.reference, true, 'the server wrote the schema the generator made');
  assert.ok(first.value[0] instanceof generated.Invoice);
  assert.ok(first.value[0].Customer.BillingAddress instanceof generated.Address);
  assertLikeJson(first.value, await json('/expected/invoices?count=10'));
  assert.strictEqual((await client.send('/preferences')).reference, true);
  assert.strictEqual((await client.send('/kinds/values')).reference, true);
});

test('with models the client reads what a .NET client reads (constructor values)', { skip }, async () => {
  const client = new LsMsgPackClient({ baseUrl: server.url, models: generated.models, schemas: generated.schemas });
  const preferences = await client.get('/preferences');
  const dotnet = await json('/dotnet/preferences');
  assert.strictEqual(preferences.Retries, 3, 'sent as 0, left out: .NET keeps the constructor value');
  assert.strictEqual(dotnet.Retries, 3);
  // DateTime.UtcNow in the constructor: each instance of the server has another moment (the generated class has DateTime.MinValue, the data replaces it)
  const { Created: created, ...rest } = dotnet;
  const { Created: read, ...others } = preferences;
  assertLikeJson(others, rest);
  assert.ok(Math.abs(read.getTime() - Date.parse(created)) < 60000);
  assert.strictEqual(new generated.Preferences().Created.getTime(), -62135596800000);

  const without = await new LsMsgPackClient({ baseUrl: server.url }).get('/preferences');
  assert.strictEqual(without.Retries, 0, 'without models: the default of the type, as JSON clients');
  assertLikeJson(await client.get('/kinds/values'), await json('/dotnet/values'));
  assertLikeJson(await client.get('/kinds/defaults'), await json('/dotnet/defaults'));
});

test('polymorphism with models: instances of the classes, request bodies with type ids where needed', { skip }, async () => {
  const { models, schemas, Zoo, Dog, Cat, Address } = generated;
  const client = new LsMsgPackClient({ baseUrl: server.url, models, schemas });
  const zoo = await client.get('/zoo');
  assert.ok(zoo instanceof Zoo);
  assert.ok(zoo.Animals[0] instanceof Dog && zoo.Animals[1] instanceof Cat);
  assert.strictEqual(zoo.Animals[1].$type, 'Cat');
  assert.ok(zoo.Anything instanceof Dog, 'declared as object');
  assert.ok(zoo.Addresses[0] instanceof Address, 'a collection with a type id');

  const kit = Object.assign(new Cat(), { Name: 'Kit', Indoor: true });
  zoo.Animals.push(kit);
  zoo.Favorite = kit;
  const echoed = await client.post('/echo/zoo', zoo);
  assert.deepStrictEqual(echoed.Animals.map((a) => a.Type), ['Dog', 'Cat', 'Dog', 'Cat']);
  assert.deepStrictEqual(echoed.Favorite, { Type: 'Cat', Value: { Indoor: true, Name: 'Kit' } });
  assert.deepStrictEqual(echoed.Anything.Type, 'Dog');
});

test('request bodies with models: every kind as the server reads it', { skip }, async () => {
  const client = new LsMsgPackClient({ baseUrl: server.url, models: generated.models });
  const kinds = await client.get('/kinds/values');
  assertLikeJson(await client.post('/echo/kinds', kinds), await json('/expected/values'));
  const preferences = new generated.Preferences();
  preferences.Timeout = '1.02:03:04.5000000';
  const echoed = await client.post('/echo/preferences', preferences);
  assert.strictEqual(echoed.Timeout, '1.02:03:04.5000000');
  assert.strictEqual(echoed.Retries, 3);
  assert.deepStrictEqual(echoed.Home, { Street: null, HouseNumber: null, PostalCode: null, City: 'Utrecht', Region: null, CountryCode: null });
});

test('the generated models with other read options', { skip }, async () => {
  const iso = await generateModels(['--dates', 'iso', '--int64', 'bigint', '--decimal', 'string']);
  const client = new LsMsgPackClient({ baseUrl: server.url, models: iso.models, schemas: iso.schemas });
  const kinds = await client.get('/kinds/values');
  assert.strictEqual(kinds.Long, -9000000000n);
  assert.strictEqual(kinds.Decimal, '1234.5600');
  assert.strictEqual(kinds.DateTime, '2026-10-13T12:34:56.789Z');
  assert.strictEqual(new iso.Kinds().DateTime, '0001-01-01T00:00:00.000Z');
  assertLikeJson(await client.post('/echo/kinds', kinds), await json('/expected/values'));
});
