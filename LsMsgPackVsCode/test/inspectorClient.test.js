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
