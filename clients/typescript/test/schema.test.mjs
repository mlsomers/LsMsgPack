// Schema ids (SHA-256 as SchemaId.Compute), plain and described schemas.
import assert from 'node:assert';
import { createHash, randomBytes } from 'node:crypto';
import { test } from 'node:test';
import { encode, Schema } from '../dist/index.js';
import { sha256, toHex } from '../dist/sha256.js';
import { schemaBytes } from './helpers.mjs';

test('SHA-256 equals node:crypto for every length around the block size', () => {
  for (let length = 0; length < 300; length++) {
    const data = randomBytes(length);
    assert.strictEqual(toHex(sha256(data)), createHash('sha256').update(data).digest('hex'), `${length} bytes`);
  }
  assert.strictEqual(toHex(sha256(new TextEncoder().encode('abc'))), 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
});

test('the id hashes the format version before the bytes, as SchemaId.Compute', () => {
  const plain = new Schema(schemaBytes([['Dog', ['Barks', 'Name']]]));
  assert.strictEqual(plain.described, false);
  assert.strictEqual(plain.id, createHash('sha256').update(Buffer.from([1])).update(plain.bytes).digest('hex').slice(0, 32));

  const described = new Schema(schemaBytes([['Dog', [['Barks', 'int'], ['Name', 'string']]], ['', [['@', 0]]]]));
  assert.strictEqual(described.described, true);
  assert.strictEqual(described.id, createHash('sha256').update(Buffer.from([2])).update(described.bytes).digest('hex').slice(0, 32));
});

test('entries are kept in order, also with the same name', () => {
  const schema = new Schema(schemaBytes([['Item', ['A']], ['Item', ['B', 'C']]]));
  assert.deepStrictEqual(schema.entries.map((e) => e.properties.map((p) => p.name)), [['A'], ['B', 'C']]);
});

test('described: kinds, entries, nullables, collections, defaults, content and root', () => {
  const schema = new Schema(schemaBytes([
    ['Person', [['Name', 'string'], ['Age', ['?', 'int']], ['Home', 1], ['Tags', ['list', 'string']], ['Scores', ['dict', 'string', 'decimal']], ['Nick', 'string', 'Bob'], ['Pos', ['struct', [['X', 'int']]]], ['New', 'some-future-kind']]],
    ['Place', [['City', 'string']]],
    ['List<Place>', [['@', ['list', 1]]]],
    ['', [['@', ['list', 0]]]],
  ]));
  assert.strictEqual(schema.entries.length, 3, 'the root entry is not an entry type ids refer to');
  const [person] = schema.entries;
  assert.deepStrictEqual(person.properties[0], { name: 'Name', type: { k: 'kind', kind: 'string' }, hasDefault: false });
  assert.deepStrictEqual(person.properties[1].type, { k: 'nullable', of: { k: 'kind', kind: 'int' } });
  assert.deepStrictEqual(person.properties[2].type, { k: 'object', entry: 1, struct: false });
  assert.deepStrictEqual(person.properties[3].type, { k: 'list', of: { k: 'kind', kind: 'string' } });
  assert.deepStrictEqual(person.properties[4].type, { k: 'dict', key: { k: 'kind', kind: 'string' }, value: { k: 'kind', kind: 'decimal' } });
  assert.strictEqual(person.properties[5].default, 'Bob');
  assert.strictEqual(person.properties[6].type.struct, true);
  assert.deepStrictEqual(person.properties[7].type, { k: 'kind', kind: 'any' }, 'a kind of a newer server is read as any');
  assert.deepStrictEqual(schema.entries[2].content, { k: 'list', of: { k: 'object', entry: 1, struct: false } });
  assert.deepStrictEqual(schema.root, { k: 'list', of: { k: 'object', entry: 0, struct: false } });
});

test('not a schema', () => {
  assert.throws(() => new Schema(encode([1, 2])), /does not start with a schema/);
  assert.throws(() => new Schema(Uint8Array.from([...schemaBytes([['A', ['B']]]), 0xc0])), /Data after the schema/);
});
