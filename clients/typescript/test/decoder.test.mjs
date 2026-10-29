// Decoding payloads made by hand: complete objects from the described schema (array and map layout, type ids), the plain schema, references and options.
import assert from 'node:assert';
import { test } from 'node:test';
import { Decoder, encode, MissingSchemaError, MsgPackError, MsgPackExtension, Schema } from '../dist/index.js';
import { schemaBytes } from './helpers.mjs';

const MinDate = new Date(-62135596800000);
const concat = (...parts) => Uint8Array.from(Buffer.concat(parts.map((p) => Buffer.from(p))));
const nil = null;
/** The schemas and the expected objects of these tests have the .NET names (the default is camelCase, tested below). */
const dotnet = { propertyNames: 'dotnet' };

/** System.Decimal 12.50 (extension type 1). */
const decimal1250 = new MsgPackExtension(1, Uint8Array.from([0, 0, 2, 0, 0, 0, 0, 0, 0xe2, 0x04, 0, 0, 0, 0, 0, 0]));
const guid = new Uint8Array([0x5b, 0xad, 0x8f, 0x0f, 0xcb, 0xd9, 0x9f, 0x46, 0xa1, 0x65, 0x70, 0x86, 0x77, 0x28, 0x95, 0x0e]);

const people = [
  ['Person', [['Name', 'string'], ['Age', 'int'], ['Born', 'datetime'], ['Tags', ['list', 'string']], ['Home', 1], ['Score', 'decimal'], ['Id', 'guid'], ['Big', 'long'], ['Nick', 'string', 'Bob'], ['Pos', ['struct', [['X', 'int'], ['Y', 'int']]]]]],
  ['Place', [['City', 'string'], ['Zip', ['?', 'int']]]],
  ['', [['@', ['list', 0]]]],
];
const described = schemaBytes(people);
const plain = schemaBytes([['Person', people[0][1].map((p) => p[0])], ['Place', ['City', 'Zip']]]);

const defaults = { Name: null, Age: 0, Born: MinDate, Tags: null, Home: null, Score: 0, Id: '00000000-0000-0000-0000-000000000000', Big: 0, Nick: 'Bob', Pos: { X: 0, Y: 0 } };

test('array layout: nil and missing values get their defaults', () => {
  const body = encode([['Ann', nil, nil, nil, ['Utrecht']], ['Ben', 30, new Date(1000), ['a'], nil, decimal1250, guid, 5000000000, 'Benny', [1, 2]], []]);
  const value = new Decoder(dotnet).decode(concat(described, body)).value;
  assert.deepStrictEqual(value, [
    { ...defaults, Name: 'Ann', Home: { City: 'Utrecht', Zip: null } },
    { Name: 'Ben', Age: 30, Born: new Date(1000), Tags: ['a'], Home: null, Score: 12.5, Id: '0f8fad5b-d9cb-469f-a165-70867728950e', Big: 5000000000, Nick: 'Benny', Pos: { X: 1, Y: 2 } },
    defaults,
  ]);
  assert.deepStrictEqual(Object.keys(value[0]), Object.keys(defaults), 'the properties in the order of the schema');
  assert.notStrictEqual(value[0].Born, value[2].Born, 'a default Date is not shared');
});

test('map layout: keys are property indexes', () => {
  const body = encode([new Map([[0, 'Ann'], [4, new Map([[0, 'Paris']])], [99, 'skipped']])]);
  assert.deepStrictEqual(new Decoder(dotnet).decode(concat(described, body)).value, [{ ...defaults, Name: 'Ann', Home: { City: 'Paris', Zip: null } }]);
});

test('type ids: { "": index, "@": [values] } and { "": index, 0: value }, the type name in $type', () => {
  const body = encode([new Map([['', 1], ['@', ['Rome', 123]]]), new Map([['', 1], [0, 'Oslo']])]);
  for (const generateCode of [true, false]) {
    const value = new Decoder({ ...dotnet, generateCode }).decode(concat(described, body)).value;
    assert.deepStrictEqual(value, [{ $type: 'Place', City: 'Rome', Zip: 123 }, { $type: 'Place', City: 'Oslo', Zip: null }]);
    assert.deepStrictEqual(Object.keys(value[0]), ['$type', 'City', 'Zip'], '$type first, as System.Text.Json writes it');
  }
  class Place {}
  const [instance] = new Decoder({ ...dotnet, classes: { Place } }).decode(concat(described, body)).value;
  assert.ok(instance instanceof Place);
  assert.strictEqual(instance.$type, 'Place', 'also on instances of your classes');
  assert.throws(() => new Decoder(dotnet).decode(concat(described, encode([new Map([['', 7], ['@', []]])]))), /type id 7 is not an entry/);
});

test('the plain schema: the root as it is, objects where type ids are (defaults null)', () => {
  const body = encode([['Ann', nil], new Map([['', 1], ['@', ['Rome']]])]);
  const decoded = new Decoder(dotnet).decode(concat(plain, body));
  assert.strictEqual(decoded.schema.described, false);
  assert.deepStrictEqual(decoded.value, [['Ann', null], { $type: 'Place', City: 'Rome', Zip: null }]);
});

test('$type round trips: the encoder writes it as the type id, first', () => {
  const body = encode([new Map([['', 1], ['@', ['Rome', 123]]])]);
  const [place] = new Decoder(dotnet).decode(concat(described, body)).value;
  place.City = 'Milan';
  assert.deepStrictEqual([...encode(place)], [...encode(new Map([['', 'Place'], ['City', 'Milan'], ['Zip', 123]]))]);
});

test('a reference: the schema from the store', () => {
  const schema = new Schema(described);
  const id = schema.id;
  const reference = concat([0xd8, 0x02], Buffer.from(id, 'hex'), encode([['Ann']]));
  assert.throws(() => new Decoder(dotnet).decode(reference), (e) => e instanceof MissingSchemaError && e.schemaId === id);
  const decoded = new Decoder(dotnet).decode(reference, (i) => (i === id ? schema : undefined));
  assert.strictEqual(decoded.reference, true);
  assert.strictEqual(decoded.value[0].Name, 'Ann');
});

test('null is written without a schema, data after the value is refused', () => {
  assert.strictEqual(new Decoder(dotnet).decode(Uint8Array.of(0xc0)).value, null);
  assert.strictEqual(new Decoder(dotnet).decode(new Uint8Array(0)).value, undefined);
  assert.throws(() => new Decoder(dotnet).decode(concat(described, encode([]), [0xc0])), /Data after the value/);
  assert.throws(() => new Decoder(dotnet).decode(encode([1])), (e) => e instanceof MsgPackError && /does not start with a schema/.test(e.message));
  assert.deepStrictEqual(new Decoder({ ...dotnet, indexedSchema: false }).decode(encode([1])).value, [1]);
});

test('options: bigint, iso dates, decimals as text', () => {
  const body = encode([['Ann', nil, new Date(Date.UTC(2026, 0, 2)), nil, nil, decimal1250, nil, 5]]);
  const [person] = new Decoder({ ...dotnet, int64: 'bigint', dates: 'iso', decimal: 'string' }).decode(concat(described, body)).value;
  assert.strictEqual(person.Big, 5n);
  assert.strictEqual(person.Born, '2026-01-02T00:00:00.000Z');
  assert.strictEqual(person.Score, '12.50');
  const [empty] = new Decoder({ ...dotnet, int64: 'bigint', dates: 'iso', decimal: 'string' }).decode(concat(described, encode([[]]))).value;
  assert.deepStrictEqual([empty.Big, empty.Born, empty.Score], [0n, '0001-01-01T00:00:00.000Z', '0']);
});

test('classes: instances of your own classes, their initialized properties kept', () => {
  class Person {
    Nick = 'from the class';
    get greeting() { return 'Hi ' + this.Name; }
  }
  const [person] = new Decoder({ ...dotnet, classes: { Person } }).decode(concat(described, encode([['Ann']]))).value;
  assert.ok(person instanceof Person);
  assert.strictEqual(person.greeting, 'Hi Ann');
  assert.strictEqual(person.Nick, 'from the class');
  assert.strictEqual(person.Age, 0);
});

test('without generated code (a Content-Security-Policy without unsafe-eval): the same objects', () => {
  const body = encode([['Ann', nil, nil, nil, ['Utrecht']], ['Ben', 30, new Date(1000), ['a'], nil, decimal1250, guid, 5000000000, 'Benny', [1, 2], 'extra'], [], new Map([[0, 'Map']])]);
  const generated = new Decoder(dotnet).decode(concat(described, body)).value;
  const generic = new Decoder({ ...dotnet, generateCode: false }).decode(concat(described, body)).value;
  assert.deepStrictEqual(generic, generated);
  assert.deepStrictEqual(Object.keys(generic[0]), Object.keys(generated[0]));
});

test('a property or key named __proto__ is an own property, never the prototype', () => {
  const schema = schemaBytes([['Odd', [['__proto__', 'any'], ['Name', 'string']]], ['', [['@', ['list', 0]]]]]);
  const body = encode([[{ polluted: true }, 'x']]);
  for (const generateCode of [true, false]) {
    const [odd] = new Decoder({ ...dotnet, generateCode }).decode(concat(schema, body)).value;
    assert.strictEqual(Object.getPrototypeOf(odd), Object.prototype);
    assert.deepStrictEqual(Object.getOwnPropertyDescriptor(odd, '__proto__').value, { polluted: true });
    assert.strictEqual(odd.polluted, undefined);
  }
  const map = new Decoder({ ...dotnet, indexedSchema: false }).decode(Uint8Array.from([0x81, 0xa9, ...Buffer.from('__proto__'), 0x81, 0xa1, 0x61, 1])).value;
  assert.strictEqual(Object.getPrototypeOf(map), Object.prototype);
  assert.strictEqual(map.a, undefined);
});

test('dictionaries, pairs, tuples, timespans, characters, DateTimeOffset', () => {
  const schema = schemaBytes([
    ['All', [['D', ['dict', 'int', 'string']], ['P', ['pair', 'string', 'int']], ['T', ['tuple', 'int', 'string']], ['S', 'timespan'], ['C', 'char'], ['O', 'dto'], ['K', 'dto-clock'], ['E', ['pair', 'string', 'int']]]],
    ['', [['@', 0]]],
  ]);
  const moment = new Date(Date.UTC(2026, 9, 13, 12));
  const body = encode([new Map([[1, 'one']]), ['a', 1], [2, 'b'], 937845000000, 233, [moment, 120], [new Date(moment.getTime() + 7200000), 120]]);
  const value = new Decoder(dotnet).decode(concat(schema, body)).value;
  assert.deepStrictEqual(value, { D: { 1: 'one' }, P: { Key: 'a', Value: 1 }, T: [2, 'b'], S: '1.02:03:04.5000000', C: 'é', O: moment, K: moment, E: { Key: null, Value: 0 } });
  assert.strictEqual(new Decoder({ ...dotnet, dates: 'iso' }).decode(concat(schema, body)).value.O, '2026-10-13T14:00:00.000+02:00');
});

test("propertyNames: camelCase by default (as ASP.NET Core's JSON): the properties of objects and pairs, not dictionary keys or $type", () => {
  const schema = schemaBytes([
    ['Item', [['ID', 'int'], ['URLValue', 'string'], ['Created', 'datetime'], ['Labels', ['dict', 'string', 'int']], ['Entry', ['pair', 'string', 'int']], ['Owner', 1]]],
    ['Person', [['Name', 'string']]],
    ['', [['@', ['list', 0]]]],
  ]);
  const body = encode([[7, 'u', nil, new Map([['Big', 1]]), ['k', 2], new Map([['', 1], ['@', ['Ann']]])], [], new Map([[1, 'by index']])]);
  for (const generateCode of [true, false]) {
    const value = new Decoder({ generateCode }).decode(concat(schema, body)).value;
    assert.deepStrictEqual(value, [
      { id: 7, urlValue: 'u', created: MinDate, labels: { Big: 1 }, entry: { key: 'k', value: 2 }, owner: { $type: 'Person', name: 'Ann' } },
      { id: 0, urlValue: null, created: MinDate, labels: null, entry: { key: null, value: 0 }, owner: null },
      { id: 0, urlValue: 'by index', created: MinDate, labels: null, entry: { key: null, value: 0 }, owner: null },
    ]);
  }
  const [dotnetItem] = new Decoder(dotnet).decode(concat(schema, body)).value;
  assert.deepStrictEqual(dotnetItem.Entry, { Key: 'k', Value: 2 }, "'dotnet': the names of the .NET properties (the default of 2026.10.22)");
  assert.strictEqual(dotnetItem.URLValue, 'u');
  const named = new Decoder({ indexedSchema: false }).decode(encode(new Map([['ID', 1]]))).value;
  assert.deepStrictEqual(named, { ID: 1 }, 'without a schema maps are read as they are');
  class Item {}
  const [item] = new Decoder({ classes: { Item } }).decode(concat(schema, body)).value;
  assert.ok(item instanceof Item);
  assert.strictEqual(item.urlValue, 'u');
});

test('values in other formats than the usual ones read as they are converted', () => {
  const long = 'x'.repeat(300);
  const body = encode([[long, 300, new Date(-62135596800000), nil, nil, '12.50', nil, nil, 'é'.repeat(20)], ['Ann', -5, new MsgPackExtension(-1, new Uint8Array(12)), nil, nil, 7]]);
  for (const generateCode of [true, false]) {
    const [first, second] = new Decoder({ ...dotnet, generateCode }).decode(concat(described, body)).value;
    assert.deepStrictEqual([first.Name, first.Age, first.Born, first.Score, first.Nick], [long, 300, MinDate, 12.5, 'é'.repeat(20)], 'str16, uint16, timestamp 96, decimal text');
    assert.deepStrictEqual([second.Age, second.Born, second.Score], [-5, new Date(0), 7], 'negative fixint, timestamp 96, a number');
  }
});
