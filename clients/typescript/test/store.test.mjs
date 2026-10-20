// The schema store: learning only verified schemas, advertising per origin (most recent first), limits, export and import.
import assert from 'node:assert';
import { test } from 'node:test';
import { Schema, SchemaStore } from '../dist/index.js';
import { schemaBytes } from './helpers.mjs';

const schemaOf = (name) => new Schema(schemaBytes([[name, ['A']]]));

test('a schema is learned only when its hash is the id of the header', () => {
  const store = new SchemaStore();
  const schema = schemaOf('A');
  assert.strictEqual(store.learn('https://a', '00112233445566778899aabbccddeeff', schema), false);
  assert.strictEqual(store.learn('https://a', null, schema), false);
  assert.strictEqual(store.size, 0);
  assert.strictEqual(store.learn('https://a', schema.id.toUpperCase(), schema), true);
  assert.strictEqual(store.get(schema.id), schema);
  assert.strictEqual(store.advertise('https://a'), schema.id);
  assert.strictEqual(store.advertise('https://b'), undefined, 'per origin');
});

test('the most recent first, at most maxAdvertisedSchemas', () => {
  const store = new SchemaStore({ maxAdvertisedSchemas: 2 });
  const [a, b, c] = ['A', 'B', 'C'].map(schemaOf);
  for (const s of [a, b, c]) store.learn('o', s.id, s);
  assert.strictEqual(store.advertise('o'), `${c.id},${b.id}`);
  store.touch('o', b.id);
  assert.strictEqual(store.advertise('o'), `${b.id},${c.id}`);
  store.touch('o', 'ffffffffffffffffffffffffffffffff');
  assert.strictEqual(store.advertise('o'), `${b.id},${c.id}`, 'an id the store does not hold is not advertised');
});

test('a full store keeps working, it stops learning', () => {
  const store = new SchemaStore({ maxSchemas: 1 });
  const [a, b] = ['A', 'B'].map(schemaOf);
  assert.strictEqual(store.learn('o', a.id, a), true);
  assert.strictEqual(store.learn('o', b.id, b), false);
  assert.strictEqual(store.advertise('o'), a.id);
});

test('export and import (JSON), the ids are computed again', () => {
  const store = new SchemaStore();
  const [a, b] = ['A', 'B'].map(schemaOf);
  store.learn('o', a.id, a);
  store.learn('o', b.id, b);
  const json = JSON.parse(JSON.stringify(store.export()));
  json.schemas.push('bm90IGEgc2NoZW1h'); // not a schema: skipped
  const copy = new SchemaStore();
  copy.import(json);
  assert.strictEqual(copy.size, 2);
  assert.strictEqual(copy.advertise('o'), store.advertise('o'));
  store.clear();
  assert.strictEqual(store.size, 0);
  assert.strictEqual(store.advertise('o'), undefined);
});
