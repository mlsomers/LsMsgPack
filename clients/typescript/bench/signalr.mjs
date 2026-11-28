// The hub protocols of @microsoft/signalr against this client's (npm run build first): a chat message and 50 of them in a list, written and read, per message (node bench/signalr.mjs).
// With the test server built (dotnet build test-server), also the messages as the server writes them, read: "lsmsgpack-indexed" only the server writes with the schema.
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { JsonHubProtocol, NullLogger } from '@microsoft/signalr';
import { LsMsgPackHubProtocol } from '../dist/signalr.js';
import { serverAssembly, startServer } from '../test/helpers.mjs';
const msg = (i) => ({ User: 'user' + i, Text: 'Hello wörld ' + i, Sent: new Date(), Id: '0f8fad5b-d9cb-469f-a165-70867728950e', Amount: 12.34 + i, Mood: 1, Tags: ['a', 'b'], Where: { X: i, Y: -i } });
const small = { type: 1, headers: {}, target: 'Send', arguments: [msg(1), 7], streamIds: [] };
const large = { type: 1, headers: {}, target: 'List', arguments: [Array.from({ length: 50 }, (_, i) => msg(i)), 7], streamIds: [] };
for (const [name, p] of [['lsmsgpack', new LsMsgPackHubProtocol()], ['Microsoft messagepack', new MessagePackHubProtocol()], ['Microsoft json', new JsonHubProtocol()]]) {
  for (const [what, m, n] of [['small', small, 200000], ['50 in a list', large, 5000]]) {
    let bytes; for (let i = 0; i < n / 10; i++) bytes = p.writeMessage(m);
    for (let i = 0; i < n / 10; i++) p.parseMessages(bytes, NullLogger.instance);
    const w = [], r = [];
    for (let run = 0; run < 3; run++) {
      let t = performance.now(); for (let i = 0; i < n; i++) bytes = p.writeMessage(m); w.push((performance.now() - t) * 1e6 / n);
      t = performance.now(); for (let i = 0; i < n; i++) p.parseMessages(bytes, NullLogger.instance); r.push((performance.now() - t) * 1e6 / n);
    }
    w.sort((a, b) => a - b); r.sort((a, b) => a - b);
    const size = typeof bytes === 'string' ? Buffer.byteLength(bytes) : bytes.byteLength;
    console.log(`${(name + ', ' + what).padEnd(40)} ${String(size).padStart(6)} B  write ${w[1].toFixed(0).padStart(7)} ns  read ${r[1].toFixed(0).padStart(7)} ns`);
  }
}

if (serverAssembly) {
  const server = await startServer();
  try {
    for (const name of ['lsmsgpack', 'lsmsgpack-indexed']) {
      const p = new LsMsgPackHubProtocol({ name });
      for (const [what, count, n] of [['small', 0, 200000], ['50 in a list', 50, 5000]]) {
        const bytes = new Uint8Array(await (await fetch(`${server.url}/hub-message?name=${name}&count=${count}`)).arrayBuffer());
        for (let i = 0; i < n / 10; i++) p.parseMessages(bytes, NullLogger.instance);
        const r = [];
        for (let run = 0; run < 3; run++) {
          const t = performance.now(); for (let i = 0; i < n; i++) p.parseMessages(bytes, NullLogger.instance); r.push((performance.now() - t) * 1e6 / n);
        }
        r.sort((a, b) => a - b);
        console.log(`${(name + ', ' + what + ' (server)').padEnd(40)} ${String(bytes.length).padStart(6)} B  ${''.padStart(16)}  read ${r[1].toFixed(0).padStart(7)} ns`);
      }
    }
  } finally {
    server.stop();
  }
}
