// References (ReferenceHandling.Preserve of the server, docs/References.md): an object or collection met again is an extension of type 5 with the path to its first
// occurrence. The decoder reads a copy (default), the same instance ('preserve') or the extension ('none'); the encoder writes references with 'preserve'.
// The server tests run against test-server/ (Preserve on its formatters), skipped when it is not built.
import assert from 'node:assert';
import { after, before, test } from 'node:test';
import { Decoder, encode, LsMsgPackClient, MsgPackError, MsgPackExtension, MsgPackStreamReader, MsgPackStreamWriter } from '../dist/index.js';
import { assertLikeJson, generateModels, generatorAssembly, schemaBytes, serverAssembly, startServer } from './helpers.mjs';

const concat = (...parts) => Uint8Array.from(Buffer.concat(parts.map((p) => Buffer.from(p))));
const hex = (bytes) => Buffer.from(bytes).toString('hex');
const nil = null;
const dotnet = { propertyNames: 'dotnet' };
/** A reference: an extension of type 5 whose content is the path (as the server writes it). */
const ref = (...path) => new MsgPackExtension(5, Uint8Array.from(path));

// Order = [Id, Customer], Customer = [Name], the root a List<Order>
const orders = schemaBytes([['Order', [['Id', 'int'], ['Customer', 1]]], ['Customer', [['Name', 'string']]], ['', [['@', ['list', 0]]]]]);

test('the encoder writes the second occurrence as the path to the first: the example of docs/References.md', () => {
  const customer = { name: 'Ann' };
  const bytes = encode([{ id: 1, customer }, { id: 2, customer }], { references: 'preserve' });
  assert.strictEqual(hex(bytes), hex(concat(encode({ id: 1, customer }).length === 0 ? [] : [0x92], encode({ id: 1, customer }), [0x82, 0xa2, 0x69, 0x64, 0x02, 0xa8], Buffer.from('customer'), [0xd5, 0x05, 0x00, 0x01])));
  assert.strictEqual(hex(encode([{ id: 1, customer }, { id: 2, customer }])).includes('d505'), false, 'without the option: written again');
});

test('a reference is read as a copy by default, as the same instance with preserve, as the extension with none', () => {
  const body = encode([[1, ['Ann']], [2, ref(0, 1)], [3, ref(1, 1)]]); // the third points to the second: a reference to a reference is the same value
  const copies = new Decoder(dotnet).decode(concat(orders, body)).value;
  assert.deepStrictEqual(copies, [{ Id: 1, Customer: { Name: 'Ann' } }, { Id: 2, Customer: { Name: 'Ann' } }, { Id: 3, Customer: { Name: 'Ann' } }]);
  assert.notStrictEqual(copies[0].Customer, copies[1].Customer);
  const shared = new Decoder({ ...dotnet, references: 'preserve' }).decode(concat(orders, body)).value;
  assert.deepStrictEqual(shared, copies);
  assert.strictEqual(shared[0].Customer, shared[1].Customer);
  assert.strictEqual(shared[0].Customer, shared[2].Customer);
  const none = new Decoder({ ...dotnet, references: 'none' }).decode(concat(orders, body)).value;
  assert.deepStrictEqual(none[1].Customer, ref(0, 1));
  for (const generateCode of [true, false]) {
    const value = new Decoder({ ...dotnet, references: 'preserve', generateCode }).decode(concat(orders, body)).value;
    assert.strictEqual(value[0].Customer, value[2].Customer, `generateCode ${generateCode}`);
  }
});

test('shared collections, and references in the map layout count the entries written', () => {
  // Pair = { Left: list of int, Right: list of int }, root Pair; the map layout with the property indexes as keys
  const schema = schemaBytes([['Pair', [['Left', ['list', 'int']], ['Right', ['list', 'int']]]], ['', [['@', 0]]]]);
  const body = encode(new Map([[0, [1, 2]], [1, ref(0)]])); // entry 0 is Left: the step is the number of the entry, not the property index
  const value = new Decoder({ ...dotnet, references: 'preserve' }).decode(concat(schema, body)).value;
  assert.deepStrictEqual(value, { Left: [1, 2], Right: [1, 2] });
  assert.strictEqual(value.Left, value.Right);
  const trimmed = encode(new Map([[1, [3]], [0, ref(0)]])); // the first entry written is Right
  const other = new Decoder({ ...dotnet, references: 'preserve' }).decode(concat(schema, trimmed)).value;
  assert.strictEqual(other.Left, other.Right);
  assert.deepStrictEqual(other.Left, [3]);
});

// Node = [Name, Parent, Children], the root a Node
const nodes = schemaBytes([['Node', [['Name', 'string'], ['Parent', 0], ['Children', ['list', 0]]]], ['', [['@', 0]]]]);

test('a cycle: the empty path is the root, preserve resolves it, the default fails', () => {
  const body = encode(['root', nil, [['kid', ref(), nil], ['other', ref(), [['grandkid', ref(2, 1), nil]]]]]);
  const root = new Decoder({ ...dotnet, references: 'preserve' }).decode(concat(nodes, body)).value;
  assert.strictEqual(root.Children[0].Parent, root);
  assert.strictEqual(root.Children[1].Children[0].Parent, root.Children[1]);
  assert.throws(() => new Decoder(dotnet).decode(concat(nodes, body)), (e) => e instanceof MsgPackError && /cycle/.test(e.message));
});

test('a reference where the declared type is not the type of the value has the type id: { "": id, "@": <reference> }', () => {
  // Shared = [Pets: list of Dog, Animal], Dog = [Name, Barks], Animal = [Name]
  const schema = schemaBytes([['Shared', [['Pets', ['list', 1]], ['Animal', 2]]], ['Dog', [['Name', 'string'], ['Barks', 'int']]], ['Animal', [['Name', 'string']]], ['', [['@', 0]]]]);
  const body = encode([[['Rex', 3]], new Map([['', 1], ['@', ref(0, 0)]])]);
  const shared = new Decoder({ ...dotnet, references: 'preserve' }).decode(concat(schema, body)).value;
  assert.strictEqual(shared.Animal, shared.Pets[0]);
  assert.strictEqual(shared.Animal.Barks, 3, 'read as a Dog');
  const copies = new Decoder(dotnet).decode(concat(schema, body)).value;
  assert.deepStrictEqual(copies.Animal, { $type: 'Dog', Name: 'Rex', Barks: 3 }, 'read again as the type id says');
  assert.notStrictEqual(copies.Animal, copies.Pets[0]);
});

test('a path to a value that does not come before the reference, or to nothing, is an error', () => {
  const forward = encode([[1, ref(1, 1)], [2, ['Ben']]]);
  assert.throws(() => new Decoder(dotnet).decode(concat(orders, forward)), (e) => e instanceof MsgPackError && /does not come before/.test(e.message));
  const beyond = encode([[1, ['Ann']], [2, ref(0, 5)]]);
  assert.throws(() => new Decoder(dotnet).decode(concat(orders, beyond)), (e) => e instanceof MsgPackError && /beyond the end/.test(e.message));
  const intoValue = encode([[1, ['Ann']], [2, ref(0, 0, 0)]]);
  assert.throws(() => new Decoder(dotnet).decode(concat(orders, intoValue)), (e) => e instanceof MsgPackError && /not an array or a map/.test(e.message));
  const text = encode([[1, ['Ann']], [2, new MsgPackExtension(5, Uint8Array.from([0xa1, 0x30]))]]);
  assert.throws(() => new Decoder(dotnet).decode(concat(orders, text)), (e) => e instanceof MsgPackError && /positive integers/.test(e.message));
});

test('plain MsgPack (decodePlain): references in untyped values', () => {
  const customer = { name: 'Ann', tags: ['a'] };
  const bytes = encode({ first: customer, second: customer, tags: customer.tags, self: new Map([['k', customer]]) }, { references: 'preserve' });
  const value = new Decoder({ references: 'preserve' }).decodePlain(bytes);
  assert.strictEqual(value.first, value.second);
  assert.strictEqual(value.first.tags, value.tags);
  assert.strictEqual(value.self.k, value.first);
  const copies = new Decoder().decodePlain(bytes);
  assert.deepStrictEqual(copies, { first: customer, second: customer, tags: ['a'], self: { k: customer } });
  assert.notStrictEqual(copies.first, copies.second);
});

test('the encoder: cycles, the keys of a Map in full, $type on a reference, and the stream writer with each value a root of its own', async () => {
  const node = { name: 'root', children: [] };
  node.children.push({ name: 'kid', parent: node });
  const bytes = encode(node, { references: 'preserve' });
  const read = new Decoder({ references: 'preserve' }).decodePlain(bytes);
  assert.strictEqual(read.children[0].parent, read);
  assert.throws(() => encode(node), (e) => e instanceof MsgPackError && /nested deeper/.test(e.message));

  const key = { id: 1 };
  const map = new Map([[key, key]]);
  const mapBytes = encode(map, { references: 'preserve' });
  assert.strictEqual(hex(mapBytes), hex(encode(map)), 'a key is never a reference or a target');

  const dog = { $type: 'Dog', name: 'Rex' };
  const typed = encode([dog, dog], { references: 'preserve' });
  assert.strictEqual(hex(typed), '92' + hex(encode(dog)) + hex(encode(new Map([['', 'Dog'], ['@', ref(0)]]))), 'the reference gets the type id, as the value would');

  const chunks = [];
  const writer = new MsgPackStreamWriter((c) => { chunks.push(c); }, { references: 'preserve' });
  writer.writeArrayStart();
  writer.write({ a: key, b: key });
  writer.write({ a: key, b: key }); // the key object again: in full, a new root
  writer.writeEnd();
  await writer.close();
  const stream = concat(...chunks);
  const one = encode({ a: key, b: key }, { references: 'preserve' });
  assert.strictEqual(hex(one), '82a16181a2696401a162d40500', 'b is the path to a: [0]');
  assert.strictEqual(hex(stream), hex(concat([0xc1, 0x90], one, one, [0xc1, 0xc0])), 'an array of unknown length: each element its own root, the same bytes');
});

test('the stream reader: the paths of a value written in an array of the stream start at the value (read with readObject, as the .NET reader does)', async () => {
  const customer = { name: 'Ann' };
  const stream = concat([0x92], encode({ id: 1, customer, shipTo: customer }, { references: 'preserve' }), encode({ id: 2, customer, shipTo: customer }, { references: 'preserve' }));
  const byElement = new MsgPackStreamReader([stream], { indexedSchema: false, references: 'preserve' });
  await byElement.read();
  const first = await byElement.readObject();
  assert.strictEqual(first.customer, first.shipTo);
  const second = await byElement.readObject();
  assert.strictEqual(second.customer, second.shipTo);
  assert.notStrictEqual(first.customer, second.customer, 'another root value');
});

// ---- Against the test server (ReferenceHandling.Preserve on its formatters) ----

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

test('the server writes references: copies by default, one instance with preserve, a cycle with preserve only', { skip }, async () => {
  const json = await expected('shared');
  delete json.Animal; // System.Text.Json writes the Animal as its declared type (Name only)
  const copies = await new LsMsgPackClient({ ...dotnet, baseUrl: server.url }).get('/shared');
  assert.deepStrictEqual(copies.Animal, { $type: 'Dog', Name: 'Rex', Barks: 3 });
  delete copies.Animal;
  assertLikeJson(copies, json);
  assert.notStrictEqual(copies.Orders[0].Customer, copies.Customer);

  const client = new LsMsgPackClient({ ...dotnet, baseUrl: server.url, references: 'preserve' });
  const shared = await client.get('/shared');
  const { Animal, ...rest } = shared;
  assertLikeJson(rest, json);
  assert.strictEqual(shared.Orders[0].Customer, shared.Customer);
  assert.strictEqual(shared.Orders[2].Customer, shared.Customer);
  assert.strictEqual(shared.Orders[0].ShipTo, shared.Customer);
  assert.strictEqual(shared.Same, shared.Orders);
  assert.strictEqual(shared.Animal, shared.Pets[0]);
  assert.strictEqual(shared.Self, null);

  const cycle = await client.get('/shared?cycle=true');
  assert.strictEqual(cycle.Self, cycle);
  await assert.rejects(new LsMsgPackClient({ ...dotnet, baseUrl: server.url }).get('/shared?cycle=true'), (e) => e instanceof MsgPackError && /cycle/.test(e.message));
});

test('the server reads references the client writes: one instance there', { skip }, async () => {
  const client = new LsMsgPackClient({ ...dotnet, baseUrl: server.url, references: 'preserve' });
  const shared = await client.get('/shared?cycle=true');
  const echo = await client.post('/echo/shared', shared);
  assert.deepStrictEqual(echo, { Orders: 3, SameCustomer: true, SameList: true, Self: true, AnimalIsPet: true, Name: 'Rex' });
  const plainClient = new LsMsgPackClient({ ...dotnet, baseUrl: server.url });
  const copies = await plainClient.post('/echo/shared', await plainClient.get('/shared')); // copies: the Animal has its $type (a shared plain object read with 'preserve' has the one of its first place)
  assert.deepStrictEqual(copies, { Orders: 3, SameCustomer: false, SameList: false, Self: false, AnimalIsPet: false, Name: 'Rex' });
});

test('with the models: read into the classes, written by their generated writers', { skip: skip || (generatorAssembly ? false : 'lsmsgpack-ts is not built') }, async () => {
  const generated = await generateModels(['--property-names', 'dotnet']);
  const client = new LsMsgPackClient({ baseUrl: server.url, models: generated.models, schemas: generated.schemas, references: 'preserve' });
  const shared = await client.get('/shared?cycle=true');
  assert.strictEqual(shared.constructor.name, 'Shared');
  assert.strictEqual(shared.Orders[1].Customer, shared.Customer);
  assert.strictEqual(shared.Self, shared);
  assert.strictEqual(shared.Animal, shared.Pets[0]);
  assert.strictEqual(shared.Animal.constructor.name, 'Dog');
  const echo = await client.post('/echo/shared', shared);
  assert.deepStrictEqual(echo, { Orders: 3, SameCustomer: true, SameList: true, Self: true, AnimalIsPet: true, Name: 'Rex' });
});

test('a stream of the server: a head and orders whose paths start at the body, read element by element and whole', { skip }, async () => {
  const client = new LsMsgPackClient({ ...dotnet, baseUrl: server.url, references: 'preserve' });
  const reader = await client.stream('/stream/shared');
  await reader.readHead();
  await reader.read(); // the arrayStart
  const orders = [];
  while (!(await reader.atEnd())) orders.push(await reader.readObject());
  assert.strictEqual(orders.length, 3);
  for (const order of orders) assert.strictEqual(order.ShipTo, order.Customer, `order ${order.Id}`);
  assert.notStrictEqual(orders[0].Customer, orders[1].Customer, 'each value its own references');
  assertLikeJson(orders.map((o) => o.Customer), (await expected('shared')).Orders.map((o) => o.Customer));

  const whole = await client.stream('/stream/shared');
  await whole.readHead();
  const all = await collect(whole.elements());
  for (const order of all) assert.strictEqual(order.ShipTo, order.Customer);

  const asValue = await client.stream('/stream/shared');
  await asValue.readHead();
  await asValue.read();
  const list = await asValue.getValue();
  for (const order of list) assert.strictEqual(order.ShipTo, order.Customer);
});

test('a stream of the TypeScript writer with references, read by the .NET reader', { skip }, async () => {
  const customer = { customerId: 7, companyName: 'Acme' };
  const chunks = [];
  const writer = new MsgPackStreamWriter((c) => { chunks.push(c); }, { references: 'preserve' });
  writer.writeArrayStart();
  writer.write({ id: 1, customer, shipTo: customer });
  writer.write({ id: 2, customer, shipTo: { ...customer } });
  writer.writeEnd();
  await writer.close();
  const response = await fetch(`${server.url}/stream/echo/shared`, { method: 'POST', headers: { 'Content-Type': 'application/octet-stream' }, body: concat(...chunks) });
  const text = await response.text();
  assert.ok(response.ok, text);
  assert.deepStrictEqual(JSON.parse(text), [true, false]);
});

async function collect(iterable) {
  const items = [];
  for await (const item of iterable) items.push(item);
  return items;
}
