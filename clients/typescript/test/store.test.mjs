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

test('per route: a request lists the schemas of its route, a route not called yet the most recent ones', () => {
  for (const advertiseByRoute of [true, false]) {
    const store = new SchemaStore({ advertiseByRoute });
    const [order, node, point] = ['Order', 'Node', 'Point'].map(schemaOf);
    store.learn('o', order.id, order, '/api/orders/17');
    store.learn('o', node.id, node, '/api/nodes');
    store.learn('o', point.id, point, '/api/points/3f2504e0-4f89-11d3-9a0c-0305e82c3301');
    const recent = `${point.id},${node.id},${order.id}`;
    const routed = (id) => (advertiseByRoute ? id : recent);
    assert.strictEqual(store.advertise('o', '/api/orders/18'), routed(order.id), 'a segment of digits is an id');
    assert.strictEqual(store.advertise('o', '/api/nodes'), routed(node.id));
    assert.strictEqual(store.advertise('o', '/api/points/00000000-0000-0000-0000-00000000000A'), routed(point.id), 'a Guid is an id');
    assert.strictEqual(store.advertise('o', '/api/orders/18/lines'), recent, 'a route not called yet');
    assert.strictEqual(store.advertise('o', '/v2/api/nodes'), recent);
    assert.strictEqual(store.advertise('o'), recent, 'without a path');
    assert.strictEqual(store.advertise('other', '/api/nodes'), undefined, 'per origin');

    // a route with several types lists them all; the recent ones change only for ids new to a route
    store.touch('o', order.id, '/api/nodes');
    assert.strictEqual(store.advertise('o', '/api/nodes'), advertiseByRoute ? `${order.id},${node.id}` : `${order.id},${point.id},${node.id}`);
    store.touch('o', point.id, '/api/points/17');
    assert.strictEqual(store.advertise('o', '/unknown'), advertiseByRoute ? `${order.id},${point.id},${node.id}` : `${point.id},${order.id},${node.id}`);

    // export and import keep the routes
    const copy = new SchemaStore({ advertiseByRoute });
    copy.import(JSON.parse(JSON.stringify(store.export())));
    for (const path of ['/api/orders/1', '/api/nodes', '/api/points/2', '/unknown']) assert.strictEqual(copy.advertise('o', path), store.advertise('o', path), path);
    store.clear();
    assert.strictEqual(store.advertise('o', '/api/nodes'), undefined);
  }
});
