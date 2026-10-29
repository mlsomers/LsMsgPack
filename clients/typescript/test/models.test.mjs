// Generated models (lsmsgpack-ts) and the hot-loaded schema set: unit tests with hand-written models, and the models of the test server against the formatters.
import assert from 'node:assert';
import { after, before, test } from 'node:test';
import { Decoder, defineModels, encode, LsMsgPackClient, MsgPackReader, SchemaStore } from '../dist/index.js';
import { assertLikeJson, generateModels, generatorAssembly, schemaBytes, serverAssembly, startServer } from './helpers.mjs';

// Models as lsmsgpack-ts 2026.10.22 wrote them with its default (compiled to JavaScript): the .NET names, without propertyNames in defineModels
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

test('an entry of a model becomes an instance of its class: a nil is the default the writer left out, a missing value keeps what the class set', () => {
  const decoder = new Decoder({ models });
  // [Id, nil, nil, [nil, "Rome"], {"": 2, "@": [5, "Rex"]}, nil, 300000000 (30 s), nil]
  const body = [0x98, ...guidBin, 0xc0, 0xc0, 0x92, 0xc0, 0xa4, ...Buffer.from('Rome'), 0x82, 0xa0, 2, 0xa1, 0x40, 0x92, 5, 0xa3, ...Buffer.from('Rex'), 0xc0, 0xce, 0x11, 0xe1, 0xa3, 0x00, 0xc0];
  const order = decoder.decode(orderPayload(body)).value;
  assert.ok(order instanceof Order);
  assert.strictEqual(order.constructor, Order);
  assert.deepStrictEqual(Object.keys(order), Object.keys(new Order()), 'the fields in the order of the class');
  assert.strictEqual(order.Id, '0f8fad5b-d9cb-469f-a165-70867728950e');
  assert.strictEqual(order.Retries, 0, 'nil: the value the writer left out, as a .NET reader reads it (not the constructor value)');
  assert.strictEqual(order.Tags, null, 'nil: null');
  assert.ok(order.Home instanceof Address);
  assert.strictEqual(order.Home.Street, null, 'nil keeps the value of a new Address (as the .NET reader makes one), not the one the Order constructor put in its own');
  assert.strictEqual(order.Home.City, 'Rome');
  assert.ok(order.Pet instanceof Dog);
  assert.strictEqual(order.Pet.$type, 'Dog');
  assert.deepStrictEqual(Object.keys(order.Pet), ['Name', 'Barks'], 'no own $type on a model');
  assert.strictEqual(order.Timeout, '00:00:30');
  assert.strictEqual(order.Due.getTime(), -62135596800000);

  const again = decoder.decode(orderPayload(body)).value;
  assert.notStrictEqual(again.Due, order.Due, 'made for each object');

  for (const generateCode of [true, false]) {
    const shorter = new Decoder({ models, generateCode }).decode(orderPayload([0x92, ...guidBin, 5])).value; // [Id, 5]: the rest is missing (an older writer, or trimmed)
    assert.strictEqual(shorter.Retries, 5);
    assert.deepStrictEqual(shorter.Tags, ['new'], 'missing: the class value');
    assert.strictEqual(shorter.Home.Street, 'Main');
    assert.strictEqual(shorter.Timeout, '00:00:30');
  }
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
  assert.strictEqual(order.Home, null, 'nil: null, as a .NET reader reads it');
  assert.deepStrictEqual(Object.keys(order), [...Object.keys(new Order()), 'Priority'], 'the class\'s fields, then the ones it does not have');
  assert.notStrictEqual(decoder.decode(orderPayload([0x94, 9, 1, ...guidBin, 0xc0], items)).value.Tags, order.Tags, 'not shared either when the server does not write them');
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

// The same models generated with camelCase field names (the default of later versions, which always write propertyNames)
class CamelAddress {
  street = null;
  city = 'Utrecht';
}
class CamelOrder {
  id = '00000000-0000-0000-0000-000000000000';
  retries = 3;
  home = null;
  entry = { key: null, value: 0 };
}
const camelModels = defineModels({
  int64: 'number', dates: 'date', decimal: 'number', propertyNames: 'camelCase',
  classes: [
    [CamelAddress, 'Address', [['Street', 'string'], ['City', 'string']]],
    [CamelOrder, 'Order', [['Id', 'guid'], ['Retries', 'int'], ['Home', ['class', 'Address']], ['Entry', ['pair', 'string', 'int']]]],
  ],
});

test("models generated with propertyNames: 'camelCase': read into their fields, written with the .NET names", () => {
  const items = [['Id', 'guid'], ['Retries', 'int'], ['Home', 1], ['Entry', ['pair', 'string', 'int']], ['Priority', 'int']];
  const body = [0x95, ...guidBin, 0xc0, 0x92, 0xc0, 0xa4, ...Buffer.from('Rome'), 0x92, 0xa1, 0x6b, 2, 9];
  for (const generateCode of [true, false]) {
    const order = new Decoder({ models: camelModels, generateCode }).decode(orderPayload(body, items)).value;
    assert.ok(order instanceof CamelOrder);
    assert.deepStrictEqual({ ...order, home: { ...order.home } }, { id: '0f8fad5b-d9cb-469f-a165-70867728950e', retries: 0, home: { street: null, city: 'Rome' }, entry: { key: 'k', value: 2 }, priority: 9 });

    order.retries = 5;
    const written = entries(encode(order, { models: camelModels, generateCode }));
    assert.deepStrictEqual(written.map(([k]) => k), ['Id', 'Retries', 'Home', 'Entry', 'priority'], 'the .NET names; a property the models do not have as it is (the server reads names ignoring case)');
    assert.deepStrictEqual(written[2][1], { Street: null, City: 'Rome' });
    assert.deepStrictEqual(written[3][1], ['k', 2], 'the pair as [key, value]');
  }
  assert.throws(() => new Decoder({ models: camelModels, propertyNames: 'dotnet' }), /propertyNames/);
  assert.throws(() => new Decoder({ models, propertyNames: 'camelCase' }), /propertyNames/, 'models with the .NET names');
  assert.strictEqual(new LsMsgPackClient({ models: camelModels }).decoder.decode(orderPayload(body, items)).value.retries, 0, 'taken from the models when not given');
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

test('writing a model without generated code: the same bytes', () => {
  const order = new Order();
  order.Pet = Object.assign(new Dog(), { Name: 'Rex' });
  order.Retries = undefined;
  order.Extra = 'kept';
  const values = [order, new Order(), [new Dog(), Object.assign(new Address(), { Street: undefined, City: undefined })], Object.assign(new Address(), { Zip: '1234' })];
  for (const value of values) assert.deepStrictEqual(Buffer.from(encode(value, { models, generateCode: false })), Buffer.from(encode(value, { models })));
  assert.deepStrictEqual(entries(encode(order, { models })).map(([k]) => k), ['Id', 'Tags', 'Home', 'Pet', 'Pets', 'Timeout', 'Due', 'Extra'], 'undefined: left out, the header counts the rest');
  assert.deepStrictEqual(entries(encode(Object.assign(new Address(), { Street: undefined, City: undefined }), { models })), []);
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
  generated = await generateModels(['--property-names', 'dotnet']); // compared with the test server's JSON, which has the .NET names
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

test('with models the client reads what a .NET client reads (left out values, constructor values)', { skip }, async () => {
  const client = new LsMsgPackClient({ baseUrl: server.url, models: generated.models, schemas: generated.schemas });
  const preferences = await client.get('/preferences');
  const dotnet = await json('/dotnet/preferences');
  assert.strictEqual(preferences.Retries, 0, 'sent as 0, left out (nil): the default of the type, not the constructor value');
  assert.strictEqual(dotnet.Retries, 0);
  // DateTime.UtcNow in the constructor: each instance of the server has another moment (the generated class has DateTime.MinValue, the data replaces it)
  const { Created: created, ...rest } = dotnet;
  const { Created: read, ...others } = preferences;
  assertLikeJson(others, rest);
  assert.ok(Math.abs(read.getTime() - Date.parse(created)) < 60000);
  assert.strictEqual(new generated.Preferences().Created.getTime(), -62135596800000);

  const without = await new LsMsgPackClient({ baseUrl: server.url }).get('/preferences');
  assert.strictEqual(without.retries, 0, 'without models: the default of the type, as JSON clients');
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

test('the generated models by default: camelCase field names (--property-names camelCase)', { skip }, async () => {
  const camel = await generateModels();
  assert.strictEqual(camel.models.propertyNames, 'camelCase');
  assert.strictEqual(generated.models.propertyNames, 'dotnet', 'written in defineModels');
  const client = new LsMsgPackClient({ baseUrl: server.url, models: camel.models, schemas: camel.schemas });
  const first = await client.send('/kinds/values');
  assert.strictEqual(first.reference, true, 'the same schemas');
  const kinds = first.value;
  assert.ok(kinds instanceof camel.Kinds);
  assertLikeJson(kinds, await json('/dotnet/values?camel=true'));
  assertLikeJson(await client.get('/kinds/defaults'), await json('/dotnet/defaults?camel=true'));
  assertLikeJson(await client.get('/invoices'), await json('/dotnet/invoices?camel=true'));
  assertLikeJson(await client.post('/echo/kinds', kinds), await json('/expected/values'), 'written with the .NET names');

  const zoo = await client.get('/zoo');
  assert.ok(zoo.animals[0] instanceof camel.Dog);
  zoo.animals.push(Object.assign(new camel.Cat(), { name: 'Kit', indoor: true }));
  const echoed = await client.post('/echo/zoo', zoo);
  assert.deepStrictEqual(echoed.Animals.at(-1), { Type: 'Cat', Value: { Indoor: true, Name: 'Kit' } });
  assert.throws(() => new LsMsgPackClient({ models: camel.models, propertyNames: 'dotnet' }), /propertyNames/);
});

test('the generated models with other read options', { skip }, async () => {
  const iso = await generateModels(['--dates', 'iso', '--int64', 'bigint', '--decimal', 'string', '--property-names', 'dotnet']);
  const client = new LsMsgPackClient({ baseUrl: server.url, models: iso.models, schemas: iso.schemas });
  const kinds = await client.get('/kinds/values');
  assert.strictEqual(kinds.Long, -9000000000n);
  assert.strictEqual(kinds.Decimal, '1234.5600');
  assert.strictEqual(kinds.DateTime, '2026-10-13T12:34:56.789Z');
  assert.strictEqual(new iso.Kinds().DateTime, '0001-01-01T00:00:00.000Z');
  assertLikeJson(await client.post('/echo/kinds', kinds), await json('/expected/values'));
});
