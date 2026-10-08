// The bridge between the MCP server and the debugger, with a fake debugger. The last test runs the MCP server (LsMsgPackMcpServer, .NET)
// against the bridge: skipped when it is not built (dotnet build ../LsMsgPackMcpServer, or npm run build:mcp).
const test = require('node:test');
const assert = require('node:assert');
const { spawn } = require('child_process');
const fs = require('fs');
const net = require('net');
const os = require('os');
const path = require('path');
const { BridgeServer, findMcpTool, isSimplePath, schemaExpression } = require('../out/mcpBridge');

const root = path.join(__dirname, '..');
const sample = fs.readFileSync(path.join(root, '..', 'LsMsgPackVisualStudioPlugin', 'PluginTester', 'AllSmallTypes.MsgPack'));

test('the .NET tool: on the PATH, in the folder of global tools, or not installed', () => {
  const files = new Set();
  const exists = (file) => files.has(file);
  assert.strictEqual(findMcpTool({ PATH: '/usr/bin:/opt/tools', HOME: '/home/me' }, 'linux', exists), undefined);
  files.add('/home/me/.dotnet/tools/lsmsgpack-mcp');
  assert.strictEqual(findMcpTool({ PATH: '/usr/bin', HOME: '/home/me' }, 'linux', exists), '/home/me/.dotnet/tools/lsmsgpack-mcp');
  files.add('/opt/tools/lsmsgpack-mcp');
  assert.strictEqual(findMcpTool({ PATH: '/usr/bin:/opt/tools', HOME: '/home/me' }, 'linux', exists), 'lsmsgpack-mcp');
  files.add('C:\\Users\\me\\.dotnet\\tools\\lsmsgpack-mcp.exe');
  assert.strictEqual(findMcpTool({ Path: 'C:\\Windows', USERPROFILE: 'C:\\Users\\me' }, 'win32', exists), 'C:\\Users\\me\\.dotnet\\tools\\lsmsgpack-mcp.exe');
});

function fakeHandler(reads) {
  return {
    async status() {
      return { ide: 'vscode', name: 'Fake', workspaceFolders: ['/w'], sessions: [{ id: '1', name: 'Launch', type: 'coreclr', active: true }], frame: { name: 'Main', source: '/w/Program.cs', line: 7, session: 'Launch' }, expressions: 'paths' };
    },
    async locals() {
      return { frame: { name: 'Main' }, variables: [{ scope: 'Locals', name: 'data', type: 'byte[]', value: '{byte[400]}', evaluateName: 'data' }] };
    },
    async read(request) {
      reads.push(request);
      if (request.expression !== 'data') {
        throw new Error(`error CS0103: The name '${request.expression}' does not exist`);
      }
      return { base64: sample.toString('base64'), description: 'byte[]', length: sample.length, session: 'Launch' };
    }
  };
}

function request(port, body) {
  return new Promise((resolve, reject) => {
    const socket = net.connect(port, '127.0.0.1', () => socket.write(JSON.stringify(body) + '\n'));
    let text = '';
    socket.setEncoding('utf8');
    socket.on('data', (part) => { text += part; });
    socket.on('end', () => resolve(JSON.parse(text)));
    socket.on('error', reject);
  });
}

test('only variable and member paths', () => {
  for (const ok of ['buffer', 'this._payload', 'response.Content', 'items[2].Data', 'map["key"]', "d['k']", 'a?.b', 'x!.y', 'a?[0]', 'value!', '@class', '$scope.data', ' spaced . path ']) {
    assert.ok(isSimplePath(ok), ok);
  }
  for (const bad of ['GetBytes()', 'a.b()', 'x = 1', 'a + b', 'list[i]', 'new byte[3]', 'File.Delete("x")', 'a;b', '', 'a[1](2)', '"text"', 'map["a\\"b"]']) {
    assert.ok(!isSimplePath(bad), bad);
  }
});

test('schema expression', () => {
  assert.strictEqual(schemaExpression('_options.SchemaStore', '0123456789abcdef0123456789abcdef'),
    '((LsMsgPack.SchemaStore)(_options.SchemaStore)).GetSchema(LsMsgPack.SchemaId.Parse("0123456789abcdef0123456789abcdef"))');
  assert.throws(() => schemaExpression('s', '"); System.IO.File.Delete("x'), /Not a schema id/);
});

test('requests over TCP, token, lock file', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'lsmsgpack-ide-'));
  process.env.LSMSGPACK_IDE_DIR = dir;
  const reads = [];
  const bridge = new BridgeServer(fakeHandler(reads));
  try {
    const port = await bridge.start();
    assert.ok(port > 0);

    // A stale lock file (a process that is gone) is removed
    fs.writeFileSync(path.join(dir, 'stale.json'), JSON.stringify({ pid: 2147483646, port: 1, token: 'x' }));
    const file = bridge.writeLockFile({ ide: 'vscode', name: 'Fake 1.0', pid: process.pid, workspaceFolders: ['/w'] });
    assert.ok(!fs.existsSync(path.join(dir, 'stale.json')));
    const lock = JSON.parse(fs.readFileSync(file, 'utf8'));
    assert.deepStrictEqual(lock, { ide: 'vscode', name: 'Fake 1.0', pid: process.pid, port, token: bridge.token, workspaceFolders: ['/w'] });
    if (process.platform !== 'win32') {
      assert.strictEqual(fs.statSync(file).mode & 0o777, 0o600);
    }
    bridge.updateWorkspaceFolders(['/w', '/x']);
    assert.deepStrictEqual(JSON.parse(fs.readFileSync(file, 'utf8')).workspaceFolders, ['/w', '/x']);

    assert.deepStrictEqual(await request(port, { token: 'wrong', method: 'status' }), { error: 'Wrong token.' });
    const status = await request(port, { token: bridge.token, method: 'status' });
    assert.strictEqual(status.result.frame.line, 7);
    const read = await request(port, { token: bridge.token, method: 'read', params: { expression: 'data', maxBytes: 100 } });
    assert.strictEqual(Buffer.from(read.result.base64, 'base64').length, sample.length);
    assert.deepStrictEqual(reads[0], { expression: 'data', schemaId: undefined, maxBytes: 100 });
    assert.match((await request(port, { token: bridge.token, method: 'read', params: { expression: 'nope' } })).error, /CS0103/);
    assert.match((await request(port, { token: bridge.token, method: 'read', params: {} })).error, /expression is missing/);
    assert.match((await request(port, { token: bridge.token, method: 'shutdown' })).error, /Unknown method/);
    assert.match((await bridge.handle('{oops')).error, /Not JSON/);

    bridge.dispose();
    assert.ok(!fs.existsSync(file));
  } finally {
    bridge.dispose();
    delete process.env.LSMSGPACK_IDE_DIR;
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

const mcpServer = [path.join(root, 'dist', 'mcp', 'LsMsgPackMcp.dll'), path.join(root, '..', 'LsMsgPackMcpServer', 'bin', 'Debug', 'net8.0', 'LsMsgPackMcp.dll'), path.join(root, '..', 'LsMsgPackMcpServer', 'bin', 'Release', 'net8.0', 'LsMsgPackMcp.dll')]
  .find((file) => fs.existsSync(file));

test('the MCP server reads through the bridge', { skip: mcpServer ? false : 'the MCP server is not built' }, async () => {
  const reads = [];
  const bridge = new BridgeServer(fakeHandler(reads));
  const port = await bridge.start();
  // As VS Code starts it (McpStdioServerDefinition with the port and token in its environment)
  const server = spawn(process.env.DOTNET || 'dotnet', [mcpServer], { env: { ...process.env, LSMSGPACK_IDE_PORT: String(port), LSMSGPACK_IDE_TOKEN: bridge.token }, stdio: ['pipe', 'pipe', 'pipe'] });
  try {
    const responses = new Map();
    let buffer = '';
    server.stdout.setEncoding('utf8');
    server.stdout.on('data', (text) => {
      buffer += text;
      let newline;
      while ((newline = buffer.indexOf('\n')) >= 0) {
        const message = JSON.parse(buffer.substring(0, newline));
        buffer = buffer.substring(newline + 1);
        responses.get(message.id)?.(message);
      }
    });
    let id = 0;
    const call = (method, params) => new Promise((resolve) => {
      const current = ++id;
      responses.set(current, resolve);
      server.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: current, method, params }) + '\n');
    });

    const init = await call('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'test', version: '1' } });
    assert.strictEqual(init.result.serverInfo.name, 'lsmsgpack');
    server.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');

    const status = await call('tools/call', { name: 'msgpack_debug_status', arguments: {} });
    assert.match(status.result.content[0].text, /Paused in Main at \/w\/Program\.cs:7/);

    const read = await call('tools/call', { name: 'msgpack_debug_read', arguments: { expression: 'data', maxNodes: 20 } });
    assert.strictEqual(read.result.isError, false, read.result.content[0].text);
    assert.match(read.result.content[0].text, /MsgPack "doc1" 400 bytes from data \(byte\[\], Launch\)/);
    assert.match(read.result.content[0].text, /"Null type:"/);
    assert.strictEqual(reads.length, 1);

    const missing = await call('tools/call', { name: 'msgpack_debug_read', arguments: { expression: 'nope' } });
    assert.strictEqual(missing.result.isError, true);
    assert.match(missing.result.content[0].text, /CS0103/);
  } finally {
    server.stdin.end();
    server.kill();
    bridge.dispose();
  }
});
