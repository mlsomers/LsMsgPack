// The hub protocol (src/signalr.ts): the envelope against Microsoft's JS MessagePack protocol, and @microsoft/signalr against the test server's hub (test-server/TestHub.cs,
// LsMsgPack.SignalR with both names). The live tests are skipped when the test server is not built (dotnet build clients/typescript/test-server).
import assert from 'node:assert';
import { after, before, test } from 'node:test';
import * as signalR from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { LsMsgPackHubProtocol, HubMessageType } from '../dist/signalr.js';
import { assertLikeJson, serverAssembly, startServer } from './helpers.mjs';

if (!serverAssembly && process.env.LSMSGPACK_TS_REQUIRE_SERVER) throw new Error('The test server is not built (dotnet build clients/typescript/test-server).');
const skip = serverAssembly ? false : 'the test server is not built';
let server;

before(async () => { if (!skip) server = await startServer(); });
after(() => server?.stop());

const messages = [
  { type: HubMessageType.Invocation, headers: { a: 'b' }, invocationId: '7', target: 'Send', arguments: [1, 'text', null, true, -300, 1.5], streamIds: ['s1'] },
  { type: HubMessageType.Invocation, headers: {}, target: 'Send', arguments: [], streamIds: [] },
  { type: HubMessageType.StreamInvocation, headers: {}, invocationId: '8', target: 'Count', arguments: [70000], streamIds: [] },
  { type: HubMessageType.StreamItem, headers: {}, invocationId: '8', item: 'x' },
  { type: HubMessageType.Completion, headers: {}, invocationId: '1', result: 42 },
  { type: HubMessageType.Completion, headers: {}, invocationId: '2', error: 'failed' },
  { type: HubMessageType.Completion, headers: {}, invocationId: '3' },
  { type: HubMessageType.CancelInvocation, headers: {}, invocationId: '4' },
  { type: HubMessageType.Ping },
  { type: HubMessageType.Ack, sequenceId: 12 },
  { type: HubMessageType.Sequence, sequenceId: 300 },
];

const ours = new LsMsgPackHubProtocol();

test('names and version', () => {
  assert.strictEqual(ours.name, 'lsmsgpack');
  assert.strictEqual(new LsMsgPackHubProtocol({ name: 'messagepack' }).name, 'messagepack');
  assert.strictEqual(ours.version, 2);
  assert.strictEqual(ours.transferFormat, signalR.TransferFormat.Binary);
  assert.strictEqual(HubMessageType.Sequence, signalR.MessageType.Sequence);
});

test('round trip, several messages in one buffer', () => {
  const parts = messages.map((m) => new Uint8Array(ours.writeMessage(m)));
  const all = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
  let at = 0;
  for (const p of parts) { all.set(p, at); at += p.length; }
  const read = ours.parseMessages(all.buffer, null);
  assert.strictEqual(read.length, messages.length);
  for (let t = 0; t < messages.length; t++) {
    const m = messages[t];
    for (const key of Object.keys(m)) assert.deepStrictEqual(read[t][key], m[key], `message ${t}.${key}`);
  }
});

test("Microsoft's JS protocol reads ours and we read Microsoft's", () => {
  const microsoft = new MessagePackHubProtocol();
  for (let t = 0; t < messages.length; t++) {
    const m = messages[t];
    if (m.type === HubMessageType.StreamInvocation || m.type === HubMessageType.CancelInvocation) continue; // a client does not read these (Microsoft's ignores them)
    const theirs = microsoft.parseMessages(ours.writeMessage(m), signalR.NullLogger.instance)[0];
    const back = ours.parseMessages(microsoft.writeMessage(m), null)[0];
    for (const key of Object.keys(m)) {
      if (key !== 'streamIds') assert.deepStrictEqual(theirs[key], m[key], `Microsoft reading message ${t}.${key}`);
      assert.deepStrictEqual(back[key], m[key], `reading Microsoft's message ${t}.${key}`);
    }
  }
});

test('the length prefix of a large message', () => {
  const text = 'x'.repeat(300000);
  const bytes = new Uint8Array(ours.writeMessage({ type: HubMessageType.StreamItem, invocationId: '1', item: text }));
  assert.ok(bytes[0] & 0x80 && bytes[1] & 0x80 && !(bytes[2] & 0x80), 'three bytes');
  assert.strictEqual(ours.parseMessages(bytes, null)[0].item, text);
});

test('an unknown message type is logged and ignored', () => {
  const logged = [];
  const read = ours.parseMessages(new Uint8Array([0x02, 0x91, 0x63]), { log: (level, text) => logged.push(text) });
  assert.deepStrictEqual(read, []);
  assert.match(logged[0], /99/);
});

test('type ids of the names mode are $type, wrapped collections their value', () => {
  // [3, {}, "1", 3, {"": "Dog", "Name": "Rex"}] and [3, {}, "2", 3, {"": "Pack", "@": [1, 2]}]
  const dog = Uint8Array.from([0x15, 0x95, 0x03, 0x80, 0xa1, 0x31, 0x03, 0x82, 0xa0, 0xa3, 0x44, 0x6f, 0x67, 0xa4, 0x4e, 0x61, 0x6d, 0x65, 0xa3, 0x52, 0x65, 0x78]);
  const result = ours.parseMessages(dog, null)[0].result;
  assert.deepStrictEqual(Object.keys(result), ['$type', 'Name']);
  assert.strictEqual(result.$type, 'Dog');
  const pack = Uint8Array.from([0x12, 0x95, 0x03, 0x80, 0xa1, 0x32, 0x03, 0x82, 0xa0, 0xa4, 0x50, 0x61, 0x63, 0x6b, 0xa1, 0x40, 0x92, 0x01, 0x02]);
  assert.deepStrictEqual(ours.parseMessages(pack, null)[0].result, [1, 2]);
});

async function connect(protocol) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${server.url}/hub`)
    .withHubProtocol(protocol)
    .configureLogging(signalR.LogLevel.None)
    .build();
  await connection.start();
  return connection;
}

for (const name of ['lsmsgpack', 'messagepack']) {
  test(`a hub: values, streams, calls to the client (${name})`, { skip }, async () => {
    const connection = await connect(new LsMsgPackHubProtocol({ name }));
    try {
      const invoices = await connection.invoke('Invoices', 3);
      const expected = JSON.parse(await connection.invoke('InvoicesJson', 3));
      if (name === 'lsmsgpack') assertLikeJson(invoices, expected);
      else assertLikeJson(invoices.map(asJsonOfMessagePack), expected); // decimals as text, enums by name: as MessagePack-CSharp writes them
      assert.strictEqual(typeof invoices[0].Id, 'string', 'a Guid is text');
      assert.ok(invoices[0].InvoiceDate instanceof Date);

      // sent back as they came: the server reads the same invoices (names matched ignoring case: also camelCase)
      const echoed = JSON.parse(await connection.invoke('EchoInvoicesJson', invoices));
      assertLikeJson(echoed, expected);

      const streamed = [];
      await new Promise((resolve, reject) => connection.stream('Count', 5).subscribe({ next: (n) => streamed.push(n), complete: resolve, error: reject }));
      assert.deepStrictEqual(streamed, [0, 1, 2, 3, 4]);

      const numbers = new signalR.Subject();
      const sum = connection.invoke('Sum', numbers);
      for (let n = 1; n <= 4; n++) numbers.next(n);
      numbers.complete();
      assert.strictEqual(await sum, 10);

      const notified = new Promise((resolve) => connection.on('Notified', (invoice, n) => resolve({ invoice, n })));
      await connection.invoke('Notify', 7);
      const { invoice, n } = await notified;
      assert.strictEqual(n, 7);
      assert.strictEqual(invoice.InvoiceNumber, invoices[0].InvoiceNumber);

      connection.on('Answer', (question) => ({ City: 'Utrecht', Street: question }));
      assert.strictEqual(await connection.invoke('Ask', 'Oudegracht'), 'Utrecht/Oudegracht');
    } finally {
      await connection.stop();
    }
  });
}

test('a hub: polymorphic values have $type, sent back the server makes the same classes (lsmsgpack)', { skip }, async () => {
  const connection = await connect(new LsMsgPackHubProtocol());
  try {
    const zoo = await connection.invoke('Zoo');
    assert.ok(zoo.Animals[0].$type.endsWith('Dog'), zoo.Animals[0].$type);
    assert.ok(zoo.Favorite.$type.endsWith('Cat'), zoo.Favorite.$type);
    assert.strictEqual(zoo.Animals[1].Indoor, true);
    assert.strictEqual(await connection.invoke('EchoZoo', zoo), 'Dog:Rex,Cat:Tom;Cat;Dog');
  } finally {
    await connection.stop();
  }
});

test("Microsoft's JS MessagePack client against LsMsgPack as \"messagepack\"", { skip }, async () => {
  const connection = await connect(new MessagePackHubProtocol());
  try {
    const invoices = await connection.invoke('Invoices', 2);
    assertLikeJson(invoices.map(asJsonOfMessagePack), JSON.parse(await connection.invoke('InvoicesJson', 2)));
    const streamed = [];
    await new Promise((resolve, reject) => connection.stream('Count', 3).subscribe({ next: (n) => streamed.push(n), complete: resolve, error: reject }));
    assert.deepStrictEqual(streamed, [0, 1, 2]);
  } finally {
    await connection.stop();
  }
});

const statuses = ['Draft', 'Sent', 'Paid', 'Overdue', 'Cancelled'];
const units = ['Piece', 'Hour', 'Kilogram', 'Meter', 'Liter', 'Box'];

/** An invoice as MessagePack-CSharp's values (decimals as text, enums by name) turned into the numbers of System.Text.Json's text, to compare with it. */
function asJsonOfMessagePack(invoice) {
  const decimal = (v) => (typeof v === 'string' ? Number(v) : v);
  return {
    ...invoice,
    Status: statuses.indexOf(invoice.Status),
    SubTotal: decimal(invoice.SubTotal), VatTotal: decimal(invoice.VatTotal), Total: decimal(invoice.Total),
    Lines: invoice.Lines.map((l) => ({
      ...l, Unit: units.indexOf(l.Unit), Quantity: decimal(l.Quantity), UnitPrice: decimal(l.UnitPrice),
      DiscountPercentage: decimal(l.DiscountPercentage), VatPercentage: decimal(l.VatPercentage), LineTotal: decimal(l.LineTotal),
    })),
  };
}
