// MsgPackStreamReader and MsgPackStreamWriter without a server: values of unknown length (msgpack/msgpack#347), heads, schemas in a stream, the index.
// The sources deliver 1 to 3 bytes per chunk, so every token and value is cut somewhere.
import assert from 'node:assert';
import { test } from 'node:test';
import {
  blobSource, bytesSource, encode, httpSource, LsMsgPackClient, MsgPackError, MsgPackExtension, MsgPackStreamReader, MsgPackStreamWriter, Schema, SchemaStore, StreamIndexStatus,
} from '../dist/index.js';
import { schemaBytes } from './helpers.mjs';

const hex = (text) => Uint8Array.from(text.replace(/\s+/g, '').match(/../g).map((b) => parseInt(b, 16)));
const concat = (...parts) => Uint8Array.from(Buffer.concat(parts.map((p) => (p instanceof Uint8Array ? p : Uint8Array.from(p)))));

/** The bytes in chunks of 1, 2, 3, 1, 2, 3... bytes, each awaited. */
async function* trickle(bytes, sizes = [1, 2, 3]) {
  for (let at = 0, k = 0; at < bytes.length; k++) {
    const n = sizes[k % sizes.length];
    yield bytes.slice(at, at + n);
    at += n;
  }
}

const plainReader = (bytes, options = {}) => new MsgPackStreamReader(trickle(bytes), { indexedSchema: false, ...options });

/** Writes with a writer into one array of bytes. */
async function written(f, options = {}) {
  const chunks = [];
  const writer = new MsgPackStreamWriter((c) => { chunks.push(c); }, { flushThreshold: 7, ...options }); // small chunks: flushes in the middle of everything
  await f(writer);
  await writer.close();
  return concat(...chunks);
}

async function tokens(reader) {
  const result = [];
  while (await reader.read()) {
    const t = reader.tokenType;
    if (t === 'integer') result.push(reader.getInteger());
    else if (t === 'string') result.push(await reader.getString());
    else if (t === 'boolean') result.push(reader.getBoolean());
    else if (t === 'nil') result.push(null);
    else result.push(reader.length === -1 ? `${t}?` : t === 'arrayStart' || t === 'mapStart' ? `${t}${reader.length}` : t);
  }
  return result;
}

// ---- The format ----

test('the examples of the docs: tokens', async () => {
  assert.deepStrictEqual(await tokens(plainReader(hex('c1 90 01 a1 61 91 c3 c1 c0'))), ['arrayStart?', 1, 'a', 'arrayStart1', true, 'arrayEnd', 'arrayEnd']);
  assert.deepStrictEqual(await tokens(plainReader(hex('c1 a0 a2 61 62 a1 63 c1 c0'))), ['stringStart?', 'ab', 'c', 'stringEnd']);
});

test('the examples of the docs: values read whole', async () => {
  let r = plainReader(hex('c1 90 01 a1 61 91 c3 c1 c0'));
  await r.read();
  assert.deepStrictEqual(await r.getValue(), [1, 'a', [true]]);
  assert.strictEqual(r.depth, 0);
  assert.strictEqual(await r.read(), false);

  r = plainReader(hex('82 a4 72 6f 77 73 c1 90 01 02 c1 c0 a5 74 6f 74 61 6c 02'));
  assert.deepStrictEqual(await r.readObject(), { rows: [1, 2], total: 2 });

  r = plainReader(hex('c1 a0 a2 61 62 a1 63 c1 c0'));
  await r.read();
  assert.strictEqual(await r.getString(), 'abc');

  r = plainReader(hex('c1 c4 c4 02 01 02 c4 01 03 c1 c0'));
  assert.deepStrictEqual(await r.readObject(), Uint8Array.of(1, 2, 3));
});

test('arrays of unknown length nested in maps of unknown length, read as objects', async () => {
  const bytes = await written((w) => {
    w.writeMapStart();
    w.write('rows');
    w.writeArrayStart();
    for (let t = 0; t < 40; t++) w.write({ id: t, name: `row ${t}` });
    w.writeEnd();
    w.write('note');
    w.writeStringStart();
    w.write('long ');
    w.writeStringChunk('text');
    w.writeEnd();
    w.writeEnd();
  });
  const value = await plainReader(bytes).readObject();
  assert.strictEqual(value.rows.length, 40);
  assert.deepStrictEqual(value.rows[39], { id: 39, name: 'row 39' });
  assert.strictEqual(value.note, 'long text');
});

test('elements of an array of unknown length, one by one', async () => {
  const bytes = await written((w) => {
    w.writeArrayStart();
    for (let t = 0; t < 100; t++) w.write([t, `name ${t}`, t % 2 === 0]);
    w.writeEnd();
    w.write('after');
  });
  for (const source of [trickle(bytes), bytes, [bytes]]) {
    const r = new MsgPackStreamReader(source, { indexedSchema: false });
    const rows = [];
    for await (const row of r.elements()) rows.push(row);
    assert.strictEqual(rows.length, 100);
    assert.deepStrictEqual(rows[99], [99, 'name 99', false]);
    assert.strictEqual(r.depth, 0);
    assert.strictEqual(await r.readObject(), 'after');
    assert.ok(await r.atEnd());
  }
});

test('atEnd, skip and the values of tokens', async () => {
  const bytes = await written((w) => {
    w.writeArrayStart();
    w.writeArrayHeader(2);
    w.write(1);
    w.write(2);
    w.writeBinaryStart();
    w.writeBinary(new Uint8Array(1000).fill(7));
    w.writeEnd();
    w.write(2n ** 60n);
    w.write(1.5);
    w.write(new Date(Date.UTC(2026, 9, 31)));
    w.writeExtension(42, Uint8Array.of(1, 2, 3));
    w.writeEnd();
  });
  const r = plainReader(bytes);
  await r.read();
  assert.strictEqual(r.length, -1);
  assert.strictEqual(await r.atEnd(), false);
  await r.read();
  assert.strictEqual(r.tokenType, 'arrayStart');
  assert.strictEqual(r.length, 2);
  await r.skip();
  assert.strictEqual(r.tokenType, 'arrayEnd', 'known lengths end too');
  await r.read();
  assert.strictEqual(r.tokenType, 'binaryStart');
  await r.skip();
  assert.strictEqual(r.tokenType, 'binaryEnd');
  await r.read();
  assert.strictEqual(r.getBigInt(), 2n ** 60n);
  await r.read();
  assert.strictEqual(r.getNumber(), 1.5);
  await r.read();
  assert.strictEqual((await r.getDate()).toISOString(), '2026-10-31T00:00:00.000Z');
  await r.read();
  const ext = await r.getExtension();
  assert.ok(ext instanceof MsgPackExtension);
  assert.deepStrictEqual([ext.type, [...ext.data]], [42, [1, 2, 3]]);
  assert.ok(await r.atEnd());
  await r.read();
  assert.strictEqual(r.tokenType, 'arrayEnd');
  assert.strictEqual(r.position, bytes.length);
  assert.strictEqual(await r.read(), false);
});

test('data that is not valid', async () => {
  const fails = async (bytes, pattern) => {
    const r = plainReader(hex(bytes));
    await assert.rejects(async () => { while (await r.read()) await r.skip(); }, (e) => e instanceof MsgPackError && pattern.test(e.message), bytes);
  };
  await fails('c1 91', /0xC1 0x91 is reserved/);
  await fails('c1', /ends with the escape byte/);
  await fails('c1 c0', /is outside of any value of unknown length/);
  await fails('91 c1 c0', /is in an array of known length/);
  await fails('c1 80 a1 61 c1 c0', /where the value of a key of the map comes/);
  await fails('c1 a0 01 c1 c0', /holds only str chunks, found the format 0x01/);
  await fails('c1 c4 a1 61 c1 c0', /holds only bin chunks/);
  await fails('c1 a0 c1 90', /holds only str chunks, not values of unknown length/);
  await fails('c1 90 01', /Unexpected end of data/);
  await fails('92 01', /Unexpected end of data/);
  await fails('a5 61 62', /Unexpected end of data/);
  const deep = new MsgPackStreamReader([new Uint8Array(10).fill(0x91)], { indexedSchema: false, maxDepth: 5 });
  await assert.rejects(() => deep.readObject(), /nested deeper/);
  const deepUnknown = new MsgPackStreamReader([hex('c1 90 c1 90 c1 90')], { indexedSchema: false, maxDepth: 2 });
  await assert.rejects(async () => { while (await deepUnknown.read()); }, /nested deeper than maxDepth \(2\)/);
});

test('the decoders refuse escapes, naming the stream reader', () => {
  assert.throws(() => encodeAndDecode(hex('c1 90 c1 c0')), /only MsgPackStreamReader reads/);
});

function encodeAndDecode(bytes) {
  return new MsgPackStreamReader([]).decoder.decodePlain(bytes);
}

// ---- The writer ----

test('the writer writes what encode writes, values of unknown length with the escapes', async () => {
  const values = [null, true, 0, -1, 300, -70000, 2 ** 40, 1.25, 'text', 'x'.repeat(300), new Uint8Array(3), new Date(0), [1, [2]], { a: 1, $type: 'Dog' }, new Map([[1, 'one']])];
  const bytes = await written((w) => { for (const v of values) w.write(v); });
  assert.deepStrictEqual(bytes, concat(...values.map((v) => encode(v))));
  assert.deepStrictEqual(await written((w) => {
    w.writeArrayStart();
    w.write(1);
    w.write('a');
    w.writeArrayHeader(1);
    w.write(true);
    w.writeEnd();
  }), hex('c1 90 01 a1 61 91 c3 c1 c0'));
  assert.deepStrictEqual(await written((w) => {
    w.writeStringStart();
    w.write('ab');
    w.writeStringChunk('c');
    w.writeEnd();
  }), hex('c1 a0 a2 61 62 a1 63 c1 c0'));
});

test('a chunk never splits a character', async () => {
  const bytes = await written((w) => {
    w.writeStringStart();
    w.writeStringChunk('a\ud83d'); // the high surrogate waits for the next chunk
    w.writeStringChunk('\ude00b');
    w.writeStringChunk('\ud83d');
    w.writeEnd(); // a high surrogate without its low one: U+FFFD
  });
  assert.deepStrictEqual(bytes, concat(hex('c1 a0 a1 61'), encode('😀b'), encode('�'), hex('c1 c0')));
  const r = plainReader(bytes);
  assert.strictEqual(await r.readObject(), 'a😀b�');
});

test('the writer refuses what the format does not allow', async () => {
  const w = new MsgPackStreamWriter(() => {}, { maxDepth: 3 });
  assert.throws(() => w.writeEnd(), /nothing to end/);
  w.writeArrayHeader(2);
  assert.throws(() => w.writeEnd(), /known length, it ends by itself after 2 more elements/);
  w.write(1);
  w.write(2);
  assert.strictEqual(w.depth, 0);
  w.writeMapStart();
  w.write('key');
  assert.throws(() => w.writeEnd(), /after a key/);
  w.writeStringStart();
  assert.throws(() => w.write(1), /holds only its chunks/);
  assert.throws(() => w.writeArrayStart(), /holds only its chunks/);
  w.writeEnd();
  w.writeArrayStart();
  w.writeArrayStart(); // depth 3 with the map
  assert.throws(() => w.writeArrayStart(), /nested deeper than maxDepth \(3\)/);
  assert.throws(() => w.writeStringChunk('a'), /start it with writeStringStart/);
  assert.throws(() => w.write(Symbol('x')), /nested deeper/);
  assert.throws(() => new MsgPackStreamWriter(() => {}).write(Symbol('x')), /cannot be written/);
  assert.throws(() => w.writeArrayHeader(-1), RangeError);
  await w.close();
  assert.throws(() => w.write(1), /closed/);
});

test('a WritableStream sink, backpressure through ready', async () => {
  const chunks = [];
  let closed = false;
  const sink = new WritableStream({ write: async (c) => { await new Promise((r) => setTimeout(r, 1)); chunks.push(c); }, close: () => { closed = true; } });
  const w = new MsgPackStreamWriter(sink, { flushThreshold: 0 });
  w.writeArrayStart();
  for (let t = 0; t < 10; t++) {
    w.write(t);
    await w.ready;
  }
  w.writeEnd();
  await w.close();
  assert.ok(closed);
  const r = plainReader(concat(...chunks));
  assert.deepStrictEqual(await r.readObject(), [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
});

test('a sink that fails: the error comes back, no index is written', async () => {
  const w = new MsgPackStreamWriter(async () => { throw new Error('disk full'); }, { flushThreshold: 0, index: {} });
  w.write(1);
  await assert.rejects(() => w.ready, /disk full/);
  assert.throws(() => w.write(2), /disk full/);
  await assert.rejects(() => w.close(), /disk full/);
});

// ---- Schemas ----

const rowSchema = schemaBytes([['Row', [['Id', 'int'], ['Name', 'string'], ['Tags', ['list', 'string']]]], ['', [['@', ['list', 0]]]]]);
const plainRowSchema = schemaBytes([['Row', ['Id', 'Name', 'Tags']]]);

/** A head (the schema) and its body: an array of unknown length of rows in the array layout. */
async function table(schema, rows) {
  return written((w) => {
    w.writeRaw(schema);
    w.writeArrayStart();
    for (const row of rows) w.write(row);
    w.writeEnd();
  });
}

test('a head with the described schema: the elements get the type of the list', async () => {
  const bytes = await table(rowSchema, [[1, 'one', ['a']], [2, null], [3, 'three', null, 'extra']]);
  const r = new MsgPackStreamReader(trickle(bytes));
  await r.readHead();
  const rows = [];
  for await (const row of r.elements()) rows.push(row);
  assert.deepStrictEqual(rows, [{ id: 1, name: 'one', tags: ['a'] }, { id: 2, name: null, tags: null }, { id: 3, name: 'three', tags: null }]);
  assert.strictEqual(await r.read(), false, 'the head ended with its body');
});

test('a head: the body read whole as its root type, or token by token with readObject per row', async () => {
  const bytes = await table(rowSchema, [[1, 'one'], [2, 'two']]);
  let r = new MsgPackStreamReader(trickle(bytes), { propertyNames: 'dotnet' });
  await r.readHead();
  assert.deepStrictEqual(await r.readObject(), [{ Id: 1, Name: 'one', Tags: null }, { Id: 2, Name: 'two', Tags: null }]);

  r = new MsgPackStreamReader(bytes);
  await r.readHead();
  await r.read();
  assert.strictEqual(r.tokenType, 'arrayStart');
  const rows = [];
  while (!(await r.atEnd())) rows.push(await r.readObject());
  await r.read();
  assert.deepStrictEqual(rows.map((x) => x.name), ['one', 'two']);
});

test('a head with a plain schema: the type of the rows by its name', async () => {
  const bytes = await table(plainRowSchema, [[1, 'one'], [2, 'two', ['x']]]);
  const r = new MsgPackStreamReader(trickle(bytes));
  await r.readHead();
  const rows = [];
  for await (const row of r.elements('Row')) rows.push(row);
  assert.deepStrictEqual(rows, [{ id: 1, name: 'one', tags: null }, { id: 2, name: 'two', tags: ['x'] }]);
});

test('a head that refers to its schema: from the store', async () => {
  const schema = new Schema(rowSchema);
  const reference = concat([0xd8, 0x02], Buffer.from(schema.id, 'hex'));
  const bytes = await table(reference, [[7, 'seven']]);
  await assert.rejects(async () => { await new MsgPackStreamReader(bytes).readHead(); }, /does not hold/);
  const store = new SchemaStore();
  store.register(schema);
  const r = new MsgPackStreamReader(trickle(bytes), { store });
  await r.readHead();
  const rows = [];
  for await (const row of r.elements()) rows.push(row);
  assert.deepStrictEqual(rows, [{ id: 7, name: 'seven', tags: null }]);
});

test('objects with their schema inline, then references to it (PerStream)', async () => {
  const schema = new Schema(rowSchema);
  const reference = concat([0xd8, 0x02], Buffer.from(schema.id, 'hex'));
  const bytes = concat(rowSchema, encode([[1, 'one']]), reference, encode([[2, 'two']]), reference, encode([[3, 'three']]));
  const store = new SchemaStore();
  const r = new MsgPackStreamReader(trickle(bytes), { store });
  const lists = [];
  for await (const list of r.values()) lists.push(list);
  assert.deepStrictEqual(lists.map((l) => l[0].name), ['one', 'two', 'three']);
  assert.ok(store.has(schema.id), 'the inline schema is in the store');
});

test('with the indexed schema an object in an array needs a head', async () => {
  const r = new MsgPackStreamReader(hex('91 01'));
  await r.read();
  await assert.rejects(() => r.readObject(), /readHead/);
});

test('a head over values of unknown length inside the rows', async () => {
  const bytes = await written((w) => {
    w.writeRaw(rowSchema);
    w.writeArrayStart();
    w.writeArrayHeader(3);
    w.write(1);
    w.writeStringStart();
    w.write('o');
    w.write('ne');
    w.writeEnd();
    w.writeArrayStart();
    w.write('a');
    w.write('b');
    w.writeEnd();
    w.writeEnd();
  });
  const r = new MsgPackStreamReader(trickle(bytes));
  await r.readHead();
  const rows = [];
  for await (const row of r.elements()) rows.push(row);
  assert.deepStrictEqual(rows, [{ id: 1, name: 'one', tags: ['a', 'b'] }]);
});

// ---- The index ----

/** Two entries: a table of 10 rows (points every 3) and a value; a hasher that sums the bytes. */
async function indexed(options = {}) {
  let sum = 0;
  const hasher = { algorithm: 'sum', append: (b) => { for (const x of b) sum += x; }, digest: () => { const d = Uint8Array.of(sum & 0xff); sum = 0; return d; } };
  return written((w) => {
    const entry = w.beginEntry('rows', 'application/x-rows');
    entry.metadata = { owner: 'me', when: new Date(0) };
    w.writeArrayStart();
    for (let t = 0; t < 10; t++) w.write([t, `row ${t}`]);
    w.writeEnd();
    w.beginEntry(2);
    w.write('second');
    if (options.after) options.after(w);
  }, { index: { pointInterval: 3, hasher, metadata: { app: 'test' }, ...options.index } });
}

test('the index: entries, points, hashes, metadata', async () => {
  const bytes = await indexed();
  for (const source of [bytes, new Blob([bytes])]) {
    const r = new MsgPackStreamReader(source, { indexedSchema: false });
    const indexes = await r.readIndexes();
    assert.strictEqual(indexes.length, 1);
    const [index] = indexes;
    assert.strictEqual(index.status, StreamIndexStatus.Complete);
    assert.strictEqual(index.writer, '@lsmsgpack/client');
    assert.strictEqual(index.hashAlgorithm, 'sum');
    assert.deepStrictEqual(index.metadata, { app: 'test' });
    const [rows, second] = index.entries;
    assert.strictEqual(rows.identifier, 'rows');
    assert.strictEqual(rows.contentType, 'application/x-rows');
    assert.strictEqual(rows.offset, 0);
    assert.strictEqual(rows.elementCount, 10);
    assert.deepStrictEqual(rows.points.map((p) => p.element), [0, 3, 6, 9]);
    assert.strictEqual(rows.metadata.owner, 'me');
    assert.strictEqual(rows.hash.length, 1);
    assert.strictEqual(second.identifier, 2);
    assert.strictEqual(second.offset, rows.length);
    assert.strictEqual(second.elementCount, undefined);

    await r.seek(second);
    assert.strictEqual(await r.readObject(), 'second');
    await r.seek(rows);
    await r.read();
    await r.seekElement(rows, 7);
    assert.deepStrictEqual(await r.readObject(), [7, 'row 7']);
    await r.seekElement(rows, 8); // from where the reader is
    assert.deepStrictEqual(await r.readObject(), [8, 'row 8']);
    await r.seekElement(rows, 1); // back: from the point before it
    assert.deepStrictEqual(await r.readObject(), [1, 'row 1']);
    await r.seek(rows, rows.points[3]);
    const rest = [];
    while (!(await r.atEnd())) rest.push(await r.readObject());
    assert.deepStrictEqual(rest, [[9, 'row 9']]);
  }
});

test('a reader that does not seek skips the index and its tail', async () => {
  const bytes = await indexed({ index: { checkpointInterval: 1 } });
  const r = new MsgPackStreamReader(trickle(bytes), { indexedSchema: false });
  const values = [];
  for await (const v of r.values()) values.push(v);
  assert.strictEqual(values.length, 2);
  assert.strictEqual(values[1], 'second');
  await assert.rejects(() => r.readIndexes(), /random access source/);
});

test('checkpoints: an index per entry, chained', async () => {
  const bytes = await indexed({ index: { checkpointInterval: 1 } });
  const indexes = await new MsgPackStreamReader(bytes).readIndexes();
  assert.deepStrictEqual(indexes.map((i) => [i.status, i.entries.length]), [[1, 1], [1, 1], [0, 0]]);
  assert.strictEqual(indexes[1].previousTail, indexes[0].offset + indexes[0].length);

  // a crash after the second checkpoint: the stream continues with a cut-off value
  const crashed = concat(bytes.subarray(0, indexes[1].offset + indexes[1].length + 25), hex('c1 90 01 02'));
  assert.deepStrictEqual(await new MsgPackStreamReader(crashed).readIndexes(), [], 'no tail at the end');
  const found = await new MsgPackStreamReader(crashed).readIndexes({ searchBackwards: true });
  assert.deepStrictEqual(found.map((i) => i.entries[0].identifier), ['rows', 2]);
});

test('an interrupted stream: the open entry is incomplete', async () => {
  const bytes = await written((w) => {
    w.write(1);
    w.writeArrayStart();
    w.write(2);
  }, { index: {} });
  const [index] = await new MsgPackStreamReader(bytes).readIndexes();
  assert.strictEqual(index.status, StreamIndexStatus.Interrupted);
  assert.deepStrictEqual(index.entries.map((e) => [e.incomplete ?? false, e.elementCount]), [[false, undefined], [true, 1]]);
});

test('a sidecar of the observer: located by the index, skipped by readers', async () => {
  const seen = [];
  const bytes = await written((w) => {
    w.write('a');
    w.write('b');
  }, { index: { observer: { append: (b) => seen.push(...b), entryEnded: (e) => (e.offset === 0 ? Uint8Array.of(9, 9, 9) : undefined) } } });
  const r = new MsgPackStreamReader(bytes, { indexedSchema: false });
  const [index] = await r.readIndexes();
  const [a] = index.entries;
  assert.deepStrictEqual(bytes.subarray(a.sidecarOffset, a.sidecarOffset + a.sidecarLength), Uint8Array.of(9, 9, 9));
  assert.deepStrictEqual(seen.slice(0, 2), [0xa1, 0x61]);
  const values = [];
  for await (const v of new MsgPackStreamReader(trickle(bytes), { indexedSchema: false }).values()) values.push(v);
  assert.deepStrictEqual(values, ['a', 'b']);
});

test('the index refuses what is not valid', () => {
  const w = new MsgPackStreamWriter(() => {}, { index: {} });
  assert.throws(() => w.beginEntry(1.5), /string or an integer/);
  w.writeArrayStart();
  assert.throws(() => w.beginEntry('x'), /before a root value/);
  assert.throws(() => w.addPoint(Symbol('k')), /plain values/);
  assert.throws(() => new MsgPackStreamWriter(() => {}).beginEntry(), /keeps no index/);
  assert.throws(() => new MsgPackStreamWriter(() => {}, { index: { metadata: { f: () => 1 } } }), /plain values/);
});

test('a damaged index is not used, a moved stream is', async () => {
  const bytes = await indexed();
  const damaged = bytes.slice();
  damaged[damaged.length - 30] ^= 0xff;
  assert.deepStrictEqual(await new MsgPackStreamReader(damaged).readIndexes(), []);
  const moved = concat(hex('c0 c0 c0'), bytes); // three nils before it
  const r = new MsgPackStreamReader(moved, { indexedSchema: false });
  const [index] = await r.readIndexes();
  assert.strictEqual(index.entries[0].offset, 3);
  await r.seek(index.entries[1]);
  assert.strictEqual(await r.readObject(), 'second');
});

// ---- HTTP ----

/** A fetch over the bytes that answers range requests, counting the bytes sent. */
function rangeFetch(bytes) {
  const state = { sent: 0, requests: 0 };
  state.fetch = async (url, init = {}) => {
    state.requests++;
    const range = new Headers(init.headers).get('Range');
    const m = range && /bytes=(\d*)-(\d*)/.exec(range);
    if (!m) {
      state.sent += bytes.length;
      return new Response(bytes, { status: 200, headers: { 'Content-Type': 'application/octet-stream' } });
    }
    let from = Number(m[1]), to = m[2] === '' ? bytes.length - 1 : Math.min(Number(m[2]), bytes.length - 1);
    if (m[1] === '') { from = bytes.length - Number(m[2]); to = bytes.length - 1; }
    if (from >= bytes.length) return new Response(null, { status: 416 });
    const part = bytes.slice(from, to + 1);
    let at = 0;
    const body = new ReadableStream({ // in blocks as the network delivers them: a reader that stops early does not download the rest
      pull(controller) {
        if (at >= part.length) return controller.close();
        const block = part.subarray(at, at + 1024);
        at += block.length;
        state.sent += block.length;
        controller.enqueue(block);
      },
    }, { highWaterMark: 0 });
    return new Response(body, { status: 206, headers: { 'Content-Range': `bytes ${from}-${to}/${bytes.length}` } });
  };
  return state;
}

test('the client: stream a response, read an entry by range requests', async () => {
  const rows = [];
  for (let t = 0; t < 2000; t++) rows.push([t, `row number ${t}`]);
  const bytes = await written((w) => {
    w.beginEntry('rows');
    w.writeArrayStart();
    for (const row of rows) w.write(row);
    w.writeEnd();
  }, { index: { pointInterval: 100 }, flushThreshold: 4096 });
  const http = rangeFetch(bytes);
  const client = new LsMsgPackClient({ baseUrl: 'http://example.test/', fetch: http.fetch, indexedSchema: false });

  const reader = await client.stream('rows.msgpack');
  let count = 0;
  for await (const row of reader.elements()) count += row[0] === count ? 1 : 0;
  assert.strictEqual(count, 2000);

  http.sent = 0;
  const seekable = client.openSeekable('rows.msgpack');
  const [index] = await seekable.readIndexes();
  await seekable.seek(index.entries[0]);
  await seekable.read();
  await seekable.seekElement(index.entries[0], 1234);
  assert.deepStrictEqual(await seekable.readObject(), [1234, 'row number 1234']);
  await seekable.close();
  assert.ok(http.sent < bytes.length, `only parts are downloaded (${http.sent} of ${bytes.length} bytes)`);

  const refusing = { fetch: async () => new Response(bytes, { status: 200 }) };
  await assert.rejects(() => new MsgPackStreamReader(httpSource('http://example.test/x', refusing)).readIndexes(), /does not read ranges/);
});

test('blobSource and bytesSource read parts', async () => {
  const bytes = Uint8Array.from({ length: 100 }, (_, t) => t);
  for (const source of [bytesSource(bytes), blobSource(new Blob([bytes]))]) {
    assert.strictEqual(await source.size(), 100);
    assert.deepStrictEqual([...(await source.read(98, 10))], [98, 99]);
  }
});
