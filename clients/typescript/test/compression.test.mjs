// LZ4 compressed bodies (MsgPackOptions.Compression of LsMsgPack, MessagePack-CSharp's Lz4Block and Lz4BlockArray), read with the optional '@lsmsgpack/client/lz4'.
// The server tests run against test-server/ (GET /compressed/invoices), skipped when it is not built.
import assert from 'node:assert';
import { after, before, test } from 'node:test';
import { Decoder, LsMsgPackClient, MsgPackError, MsgPackExtension, MsgPackStreamReader } from '../dist/index.js';
import { decodeBlock, lz4 } from '../dist/lz4.js';
import { assertLikeJson, serverAssembly, startServer } from './helpers.mjs';

const skip = serverAssembly ? false : 'the test server is not built';
let server;
before(async () => { if (!skip) server = await startServer(); });
after(() => server?.stop());

const bytes = (hex) => Uint8Array.from(Buffer.from(hex, 'hex'));
const concat = (...parts) => Uint8Array.from(Buffer.concat(parts.map((p) => Buffer.from(p))));

// Six { Customer, Quantity, Product } maps as MessagePack-CSharp 3.1.12 writes them (contractless) with Lz4Block and with Lz4BlockArray
const theirBlock = bytes('c77263d200000127c49683a8437573746f6d6572aa0900f3062030a85175616e7469747900a750726f64756374a908002f2030310001153131001d0131001f31310001153231001d0231001f323100010693001d0331001f333100010693001d0431001f3431000106930019053100507563742030');
const theirBlocks = bytes('92c70362cd0127c60000006dc49683a8437573746f6d6572aa0900f3062030a85175616e7469747900a750726f64756374a908002f2030310001153131001d0131001f31310001153231001d0231001f323100010693001d0331001f333100010693001d0431001f3431000106930019053100507563742030');
const theirOrders = Array.from({ length: 6 }, (_, i) => ({ Customer: `Customer ${i % 3}`, Quantity: i, Product: `Product ${i % 5}` }));

test('MessagePack-CSharp\'s Lz4Block and Lz4BlockArray, plain and as the body', () => {
  const decoder = new Decoder({ indexedSchema: false, compression: lz4 });
  assert.deepStrictEqual(decoder.decode(theirBlock).value, theirOrders);
  assert.deepStrictEqual(decoder.decode(theirBlocks).value, theirOrders);
  assert.deepStrictEqual(decoder.decodePlain(theirBlock), theirOrders);
  assert.deepStrictEqual(decoder.decodePlain(theirBlocks), theirOrders);

  // Without the option a compressed body is what it is in MsgPack: an extension (type 99), or an array starting with one (type 98)
  const without = new Decoder({ indexedSchema: false });
  assert.ok(without.decodePlain(theirBlock) instanceof MsgPackExtension);
  assert.strictEqual(without.decodePlain(theirBlocks)[0].type, 98);

  // Uncompressed bodies are read as before
  assert.deepStrictEqual(decoder.decodePlain(bytes('93010203')), [1, 2, 3]);
});

test('a stream of compressed values, read in chunks of 3 bytes', async () => {
  const data = concat(theirBlock, theirBlocks, [0x93, 0x01, 0x02, 0x03]);
  const chunks = [];
  for (let t = 0; t < data.length; t += 3) chunks.push(data.subarray(t, t + 3));
  const reader = new MsgPackStreamReader(chunks, { indexedSchema: false, compression: lz4 });
  assert.deepStrictEqual(await reader.readObject(), theirOrders);
  assert.deepStrictEqual(await reader.readObject(), theirOrders);
  assert.deepStrictEqual(await reader.readObject(), [1, 2, 3]);
  assert.ok(await reader.atEnd());
});

test('LZ4 blocks: literals, overlapping matches, and blocks that are not valid', () => {
  const run = (block, length) => {
    const target = new Uint8Array(length);
    const written = decodeBlock(block, 0, block.length, target, 0, length);
    return { written, text: Buffer.from(target.subarray(0, Math.max(0, written))).toString('latin1') };
  };
  assert.deepStrictEqual(run(Uint8Array.from([0x50, ...Buffer.from('hello')]), 5), { written: 5, text: 'hello' }); // literals only
  assert.deepStrictEqual(run(Uint8Array.from([0x16, 0x61, 0x01, 0x00]), 11), { written: 11, text: 'a'.repeat(11) }); // 'a', then 10 more from 1 back: the bytes repeat
  assert.deepStrictEqual(run(Uint8Array.from([0x22, 0x61, 0x62, 0x02, 0x00, 0x30, ...Buffer.from('xyz')]), 11), { written: 11, text: 'abababab' + 'xyz' }); // a match, then the last literals
  const long = Uint8Array.from([0x1f, 0x7a, 0x01, 0x00, 0xff, 0x0a]); // a match length of 15 + 255 + 10 + 4
  assert.deepStrictEqual(run(long, 285), { written: 285, text: 'z'.repeat(285) });

  assert.strictEqual(run(Uint8Array.from([0x16, 0x61, 0x02, 0x00]), 11).written, -1, 'a match before the start of the output');
  assert.strictEqual(run(Uint8Array.from([0x16, 0x61, 0x00, 0x00]), 11).written, -1, 'offset 0');
  assert.strictEqual(run(Uint8Array.from([0x16, 0x61, 0x01, 0x00]), 10).written, -1, 'more than the target holds');
  assert.strictEqual(run(Uint8Array.from([0x50, 0x61]), 5).written, -1, 'literals cut off');
  assert.strictEqual(run(Uint8Array.from([0x16, 0x61, 0x01]), 11).written, -1, 'an offset cut off');
});

test('data from outside: claims are checked before anything is allocated', () => {
  const decoder = new Decoder({ indexedSchema: false, compression: lz4 });
  const refused = (data, message, options) => assert.throws(() => (options ? new Decoder({ indexedSchema: false, compression: lz4, ...options }) : decoder).decodePlain(data),
    (e) => e instanceof MsgPackError && e.message.includes(message), message);

  // ext 99 of MessagePack-CSharp's block with another claimed length (bytes 4-7: the int 32 after d2)
  const claim = (n) => { const b = theirBlock.slice(); new DataView(b.buffer).setInt32(4, n); return b; };
  refused(claim(1_000_000_000), 'more than LZ4 can produce');
  refused(claim(296), 'not valid');
  refused(claim(294), 'not valid');
  refused(claim(-5), 'negative');
  refused(theirBlock, 'maxDecompressedLength (100)', { maxDecompressedLength: 100 });
  assert.deepStrictEqual(new Decoder({ indexedSchema: false, compression: lz4, maxDecompressedLength: 295 }).decodePlain(theirBlock), theirOrders);

  // An array of blocks claiming 2 GB from 3 bytes, a block that is no bin, more lengths than blocks
  refused(bytes('92c70562ce7fffffffc403010203'), 'more than LZ4 can produce');
  refused(bytes('92c70362cd0127a3616263'), 'no binary value');
  refused(bytes('92c7046201020304c40100'), 'more lengths than blocks');
  // A block cut off: the data ends early
  assert.throws(() => decoder.decodePlain(theirBlock.subarray(0, 50)), MsgPackError);
  assert.ok(!lz4.startsAt(concat([0x92, 0x01]), 0, 2), 'an array of other values');
});

for (const layout of ['block', 'blocks', 'small-blocks', 'hc']) {
  test(`LsMsgPack's compressed invoices (${layout}) with the described schema`, { skip }, async () => {
    const client = new LsMsgPackClient({ baseUrl: server.url, propertyNames: 'dotnet', compression: lz4 });
    const invoices = await client.get(`/compressed/invoices?count=50&layout=${layout}`);
    const expected = await (await fetch(`${server.url}/expected/invoices?count=50`)).json();
    assertLikeJson(invoices, expected);
  });

  test(`LsMsgPack's compressed invoices (${layout}) without a schema`, { skip }, async () => {
    const read = async (l) => new Uint8Array(await (await fetch(`${server.url}/compressed/invoices?count=50&schema=false&layout=${l}`)).arrayBuffer());
    const compressed = await read(layout);
    const plain = await read('none');
    assert.ok(compressed.length < plain.length * (layout === 'small-blocks' ? 0.7 : 0.5), `${compressed.length} of ${plain.length} bytes`); // blocks of 1 KiB each start without the data before them
    const decoder = new Decoder({ indexedSchema: false, compression: lz4 });
    assert.deepStrictEqual(decoder.decodePlain(compressed), decoder.decodePlain(plain));
  });
}
