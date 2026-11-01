// The extension with the inspector (Server, .NET): skipped when it is not built (dotnet build Server -c Debug, or npm run build:server).
const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const { InspectorClient } = require('../out/inspectorClient');

const root = path.join(__dirname, '..');
const assembly = [path.join(root, 'dist', 'inspector', 'LsMsgPackInspector.dll'), path.join(root, 'Server', 'bin', 'Debug', 'net8.0', 'LsMsgPackInspector.dll'), path.join(root, 'Server', 'bin', 'Release', 'net8.0', 'LsMsgPackInspector.dll')]
  .find((file) => fs.existsSync(file));
const skip = assembly ? false : 'the inspector is not built';
const sample = fs.readFileSync(path.join(root, '..', 'LsMsgPackVisualStudioPlugin', 'PluginTester', 'AllSmallTypes.MsgPack'));
const settings = { continueOnError: false, endian: 'SwapIfCurrentSystemIsLittleEndian', displayLimit: 1000, objects: 'auto' };

test('load, search, settings and errors', { skip }, async () => {
  const client = new InspectorClient(process.env.DOTNET || 'dotnet', assembly);
  try {
    assert.match(await client.version(), /^\d+\.\d+/);

    const model = await client.load('a', sample.toString('base64'), settings);
    assert.strictEqual(model.length, sample.length);
    assert.strictEqual(model.items[0].kind, 'array');
    assert.strictEqual(model.items[0].offset, 0);
    assert.strictEqual(model.items[0].length, sample.length);
    assert.strictEqual(model.items[0].typeBytes, 1);
    assert.strictEqual(model.items[0].lengthBytes, 2); // array 16
    assert.strictEqual(model.items[1].text, 'String (fixstr) with the value "Null type:"');
    assert.ok(model.items[1].props.some((p) => p.name === 'Offset' && p.value === '3'));
    assert.strictEqual(model.hasSchema, false);
    assert.strictEqual(model.objectConfidence, 'Low'); // a map with text keys
    assert.strictEqual(model.objects, undefined); // auto: below 'high'
    assert.ok(model.issues.some((issue) => issue.severity === 'Warning' && issue.bytes === 1));

    const found = await client.search('a', 'null type', false);
    assert.deepStrictEqual(found, { displayed: [1], total: 1 });
    assert.deepStrictEqual(await client.search('a', 'null type', true), { displayed: [], total: 0 });

    // Again without the data, with other settings
    const limited = await client.load('a', undefined, Object.assign({}, settings, { displayLimit: 5, objects: 'show' }));
    assert.strictEqual(limited.truncated, true);
    assert.ok(limited.items.length < model.items.length);
    assert.ok(limited.objects.nodes.length > 0);
    assert.strictEqual(limited.objects.itemObjects.length, limited.items.length);

    // Records without a schema (e.g. from Python: [{"a": 1, "b": "x"}, {"a": 2, "b": "y"}]) open the objects from 'high' on
    const records = Buffer.from('9282a16101a162a17882a16102a162a179', 'hex').toString('base64');
    const auto = await client.load('r', records, settings);
    assert.strictEqual(auto.objectConfidence, 'High');
    assert.match(auto.objectReason, /a, b/);
    assert.ok(auto.objects.nodes.length > 0);
    assert.strictEqual((await client.load('r', undefined, Object.assign({}, settings, { showObjectsAt: 'certain' }))).objects, undefined);

    // A schema naming a gadget ({ "Process": ["FileName"] }, then { "": 0, 0: "calc" }): validated with the schema, the issue is shown with the type name
    const gadget = Buffer.from('81a750726f6365737391a846696c654e616d6582a00000a463616c63', 'hex').toString('base64');
    const attack = await client.load('g', gadget, settings);
    const security = attack.issues.find((issue) => issue.severity === 'SecurityVulnerability');
    assert.ok(security, JSON.stringify(attack.issues));
    assert.match(security.message, /gadget/);
    assert.strictEqual(attack.items[security.item].text, 'String (fixstr) with the value "Process"');

    const broken = await client.load('b', sample.subarray(0, 50).toString('base64'), settings);
    assert.ok(broken.error || broken.issues.some((issue) => issue.severity === 'ReadAbortError'));

    // An extension cut off in its content (ext 8 of 5 bytes, 1 there): its text threw a NullReferenceException, the whole load failed
    const cutExtension = await client.load('e', Buffer.from([0xc7, 0x05, 0x01, 0xaa]).toString('base64'), settings);
    assert.ok(cutExtension.items.some((item) => /the data ended before its content/.test(item.text)), JSON.stringify(cutExtension.items));

    // An array of 5 with an error at its second value: the 3 slots after it were not read, they showed as NULL items
    const stopped = await client.load('s', Buffer.from([0x95, 0x01, 0xc1, 0x02, 0x03, 0x04]).toString('base64'), settings);
    assert.ok(!stopped.items.some((item) => item.text === 'NULL'), JSON.stringify(stopped.items.map((item) => item.text)));
    const map = await client.load('m', Buffer.from([0x84, 0x01, 0x02, 0xc1, 0x03, 0x04, 0x05, 0x06, 0x07]).toString('base64'), settings);
    assert.ok(!map.items.some((item) => item.text === 'NULL'), JSON.stringify(map.items.map((item) => item.text)));

    // A stream (LtMsgPack's MsgPackStreamWriter): values of unknown length (0xC1 escapes), then its index (ext 3) and the tail
    const body = Buffer.from('92c1a0a26162a163c1c0c19001c1c0', 'hex'); // ["abc" in 2 chunks, [1] of unknown length]
    // [version 1, status Complete, writer, previous tail, hash algorithm: nil, no schemas, entries: [[offset 0, length]]]
    const content = Buffer.from([0x97, 0x01, 0x00, 0xc0, 0xc0, 0xc0, 0x90, 0x91, 0x92, 0x00, body.length]);
    const index = Buffer.concat([Buffer.from([0xc7, content.length, 0x03]), content]);
    const tail = Buffer.alloc(25);
    tail[0] = 0x94; tail[1] = 0xcf; tail.writeBigUInt64BE(BigInt(body.length), 2);
    tail[10] = 0xce; tail.writeUInt32BE(index.length, 11);
    tail[15] = 0xce; tail.writeUInt32BE(crc32(index), 16);
    tail[20] = 0xa4; tail.write('LSIX', 21, 'ascii');
    const stream = await client.load('st', Buffer.concat([body, index, tail]).toString('base64'), Object.assign({}, settings, { objects: 'show' }));
    const str = stream.items.find((item) => item.kind === 'str' && item.role !== 'chunk');
    assert.strictEqual(str.offset, 1);
    assert.strictEqual(str.typeBytes, 2); // C1 A0
    assert.strictEqual(str.lengthBytes, 0);
    assert.strictEqual(str.endBytes, 2); // C1 C0
    assert.match(str.text, /unknown length in 2 chunks/);
    assert.deepStrictEqual(stream.items.filter((item) => item.role === 'chunk').map((item) => [item.offset, item.length]), [[3, 3], [6, 2]]);
    const array = stream.items.find((item) => item.kind === 'array' && item.offset === 10);
    assert.strictEqual(array.typeBytes, 2);
    assert.strictEqual(array.endBytes, 2);
    assert.ok(array.props.some((p) => p.name === 'Unknown length' && p.value === 'True'), JSON.stringify(array.props));
    const indexItem = stream.items.find((item) => item.kind === 'index');
    assert.strictEqual(indexItem.offset, body.length);
    assert.match(indexItem.text, /Stream index \(Complete\) of 1 entries, CRC-32 matches/);
    assert.ok(stream.items.some((item) => item.role === 'content' && item.parent === indexItem.id));
    assert.ok(stream.items.some((item) => item.role === 'entries'));
    assert.strictEqual(stream.items.find((item) => item.kind === 'tail').offset, body.length + index.length);
    assert.ok(!stream.issues.some((issue) => issue.severity === 'Error' || issue.severity === 'ReadAbortError'), JSON.stringify(stream.issues));
    assert.ok(!stream.issues.some((issue) => /smaller container/.test(issue.message)), 'the tail has fixed types');
    assert.strictEqual(stream.objects.nodes[0].kind, 'root');
    assert.match(stream.objects.nodes[0].text, /^Root : entry : /); // the payload of the only entry (without an identifier); the index is no data

    await assert.rejects(client.search('unknown', 'x', false), /Unknown document/);
    await client.close('a');
    await assert.rejects(client.load('a', undefined, settings), /Unknown document/);
  } finally {
    client.dispose();
  }
});

test('started again after it stopped', { skip }, async () => {
  const client = new InspectorClient(process.env.DOTNET || 'dotnet', assembly);
  try {
    await client.version();
    const first = client.generation;
    client.dispose();
    assert.strictEqual(client.generation, 0);
    await client.version();
    assert.ok(client.generation > first);
  } finally {
    client.dispose();
  }
});

test('a missing dotnet is reported', async () => {
  const client = new InspectorClient(path.join(root, 'no-such-dotnet'), 'x.dll');
  await assert.rejects(client.version(), /needs the .NET runtime/);
  client.dispose();
});

/** CRC-32 (IEEE), as the tail of a stream index has it. */
function crc32(bytes) {
  let crc = 0xffffffff;
  for (const b of bytes) {
    crc ^= b;
    for (let k = 0; k < 8; k++) {
      crc = crc & 1 ? 0xedb88320 ^ (crc >>> 1) : crc >>> 1;
    }
  }
  return (crc ^ 0xffffffff) >>> 0;
}
