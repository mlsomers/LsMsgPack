const test = require('node:test');
const assert = require('node:assert');
const { readBytes, languageOf, parseByte, parseInteger, CancelledError } = require('../out/debugBytes');

const sample = Uint8Array.from({ length: 1000 }, (_, t) => (t * 7) & 0xff);
const options = (extra) => Object.assign({ chunkSize: 300, maxBytes: 1 << 20 }, extra);

/**
 * A fake C# debugger (like vsdbg): evaluates the expressions the reader sends for a few variables.
 * Strings longer than maxString characters are shortened, as debuggers do.
 */
function dotnetDebugger(maxString = Infinity) {
  const stream = (bytes, seekable, position = 0) => ({ kind: 'stream', bytes, seekable, position, name: seekable ? 'FileStream' : 'NetworkStream' });
  const program = {
    buf: { kind: 'bytes', types: ['byte[]', 'System.Collections.Generic.IEnumerable<byte>'], bytes: sample },
    list: { kind: 'bytes', types: ['System.Collections.Generic.IEnumerable<byte>'], bytes: sample },
    ms: { kind: 'bytes', types: ['System.IO.MemoryStream', 'System.IO.Stream'], bytes: sample },
    fs: Object.assign(stream(sample, true, 5), { types: ['System.IO.Stream'] }),
    ns: Object.assign(stream(sample, false), { types: ['System.IO.Stream'] }),
    text: { kind: 'string', types: ['string'], text: Buffer.from(sample).toString('base64') },
    mem: { kind: 'bytes', types: ['System.Memory<byte>'], bytes: sample },
    nothing: { kind: 'null', types: [] },
    number: { kind: 'other', types: ['int'] }
  };
  const log = [];
  const str = (text) => {
    const quoted = `"${text}"`;
    return { result: quoted.length > maxString ? quoted.substring(0, maxString) + '...' : quoted, variablesReference: 0 };
  };
  const plain = (text) => ({ result: String(text), variablesReference: 0 });
  // The variable an expression is about, through the casts and conversions of the reader
  const target = (expression) => {
    for (const match of expression.matchAll(/\((\w+)\)/g)) {
      if (program[match[1]]) {
        return program[match[1]];
      }
    }
    throw new Error(`error CS0103: The name does not exist: ${expression}`);
  };
  return {
    log,
    program,
    access: {
      sessionType: 'coreclr',
      async evaluate(expression) {
        log.push(expression);
        let m;
        if ((m = /^\((\w+)\) == null$/.exec(expression))) {
          return plain(program[m[1]].kind === 'null');
        }
        if ((m = /^\((\w+)\) is (.+)$/.exec(expression))) {
          if (m[2].startsWith('System.Net.Http')) {
            throw new Error('The type or namespace name \'Http\' does not exist');
          }
          return plain(program[m[1]].types.includes(m[2]));
        }
        const variable = target(expression);
        if ((m = /^System\.Convert\.ToBase64String\(new System\.IO\.BinaryReader\(.*\)\.ReadBytes\((\d+)\)\)$/.exec(expression))) {
          const part = variable.bytes.subarray(variable.position, variable.position + Number(m[1]));
          variable.position += part.length;
          return str(Buffer.from(part).toString('base64'));
        }
        if ((m = /^System\.Convert\.ToBase64String\(.*, (\d+), (\d+)\)$/.exec(expression))) {
          return str(Buffer.from(variable.bytes.subarray(Number(m[1]), Number(m[1]) + Number(m[2]))).toString('base64'));
        }
        if ((m = /\.Seek\((\d+), System\.IO\.SeekOrigin\.Begin\)$/.exec(expression))) {
          assert.ok(variable.seekable);
          variable.position = Number(m[1]);
          return plain(variable.position);
        }
        if ((m = /\.Substring\((\d+), (\d+)\)$/.exec(expression))) {
          return str(variable.text.substr(Number(m[1]), Number(m[2])));
        }
        if (expression.endsWith('.CanRead')) return plain(true);
        if (expression.endsWith('.CanSeek')) return plain(variable.seekable);
        if (expression.endsWith('.Position')) return plain(variable.position);
        if (expression.endsWith('.GetType().Name')) return str(variable.name);
        if (expression.endsWith('.Length')) return plain(variable.kind === 'string' ? variable.text.length : variable.bytes.length);
        throw new Error(`Unexpected expression: ${expression}`);
      },
      async variables() {
        throw new Error('not used');
      }
    }
  };
}

test('languages by debug session type', () => {
  assert.strictEqual(languageOf('coreclr'), 'dotnet');
  assert.strictEqual(languageOf('clr'), 'dotnet');
  assert.strictEqual(languageOf('pwa-node'), 'javascript');
  assert.strictEqual(languageOf('pwa-chrome'), 'javascript');
  assert.strictEqual(languageOf('debugpy'), 'python');
  assert.strictEqual(languageOf('cppdbg'), 'other');
});

test('numbers and bytes as debuggers show them', () => {
  assert.strictEqual(parseInteger('400'), 400);
  assert.strictEqual(parseInteger('0x00000190'), 400);
  assert.strictEqual(parseByte('0x1a', '[0]'), 26);
  assert.strictEqual(parseByte("26 '\\x1a'", '[0]'), 26);
  assert.throws(() => parseByte('300', '[0]'));
});

for (const [name, description] of [['buf', 'byte[]'], ['list', 'IEnumerable<byte>'], ['ms', 'MemoryStream'], ['mem', 'Memory<byte>'], ['text', 'string']]) {
  test(`.NET: ${description}`, async () => {
    const debug = dotnetDebugger();
    const result = await readBytes(debug.access, name, options());
    assert.deepStrictEqual(result.bytes, sample);
    assert.strictEqual(result.description, description);
  });
}

test('.NET: a seekable stream is read from the start and put back at its position', async () => {
  const debug = dotnetDebugger();
  const result = await readBytes(debug.access, 'fs', options());
  assert.deepStrictEqual(result.bytes, sample);
  assert.strictEqual(result.description, 'FileStream');
  assert.strictEqual(debug.program.fs.position, 5);
});

test('.NET: a stream that cannot seek is read after confirming, from where it is', async () => {
  const debug = dotnetDebugger();
  debug.program.ns.position = 10;
  await assert.rejects(readBytes(debug.access, 'ns', options({ confirmConsume: async () => false })), CancelledError);
  const result = await readBytes(debug.access, 'ns', options({ confirmConsume: async () => true }));
  assert.deepStrictEqual(result.bytes, sample.subarray(10));
  assert.match(result.warning, /cannot seek/);
});

test('.NET: chunks get smaller when the debugger shortens long strings', async () => {
  const debug = dotnetDebugger(150);
  const result = await readBytes(debug.access, 'buf', options({ chunkSize: 3000 }));
  assert.deepStrictEqual(result.bytes, sample);
});

test('.NET: at most maxBytes are read, with a warning', async () => {
  const debug = dotnetDebugger();
  const result = await readBytes(debug.access, 'buf', options({ maxBytes: 100 }));
  assert.deepStrictEqual(result.bytes, sample.subarray(0, 100));
  assert.match(result.warning, /first 100 of 1000 bytes/);
});

test('.NET: null and other types are refused, cancelling stops reading', async () => {
  const debug = dotnetDebugger();
  debug.access.variables = async () => [];
  await assert.rejects(readBytes(debug.access, 'nothing', options()), /null/);
  await assert.rejects(readBytes(debug.access, 'number', options()), /Not a byte array/);
  await assert.rejects(readBytes(debug.access, 'buf', options({ isCancelled: () => true })), CancelledError);
});

test('any debugger: the elements of the variable, also in groups of a range', async () => {
  const children = {
    1: [{ name: '[0..499]', value: '', variablesReference: 2 }, { name: '[500..999]', value: '', variablesReference: 3 }, { name: 'Raw View', value: '', variablesReference: 9 }],
    2: Array.from(sample.subarray(0, 500), (b, t) => ({ name: `[${t}]`, value: `0x${b.toString(16)}`, variablesReference: 0 })),
    3: Array.from(sample.subarray(500), (b, t) => ({ name: `[${t + 500}]`, value: String(b), variablesReference: 0 }))
  };
  const access = {
    sessionType: 'cppdbg',
    evaluate: async () => { throw new Error('not used'); },
    variables: async (reference) => children[reference]
  };
  const result = await readBytes(access, undefined, options(), { name: 'data', value: 'byte[1000]', variablesReference: 1 });
  assert.deepStrictEqual(result.bytes, sample);
});

test('any debugger: paged elements (indexedVariables)', async () => {
  const requests = [];
  const access = {
    sessionType: 'lldb',
    evaluate: async () => { throw new Error('evaluation failed'); },
    variables: async (reference, start, count) => {
      requests.push([start, count]);
      return Array.from(sample.subarray(start, start + count), (b, t) => ({ name: String(start + t), value: String(b), variablesReference: 0 }));
    }
  };
  const result = await readBytes(access, 'data', options(), { name: 'data', value: '', variablesReference: 1, indexedVariables: 1000 });
  assert.deepStrictEqual(result.bytes, sample);
  assert.deepStrictEqual(requests, [[0, 1000]]);
});

test('any debugger: a string value holding the bytes', async () => {
  const access = { sessionType: 'other', evaluate: async () => ({ result: '"81 A3 66"', variablesReference: 0 }), variables: async () => [] };
  const result = await readBytes(access, 'hex', options());
  assert.deepStrictEqual(result.bytes, Uint8Array.from([0x81, 0xa3, 0x66]));
});

test('JavaScript and Python: the bytes in base64 chunks', async () => {
  for (const sessionType of ['pwa-node', 'debugpy']) {
    const access = {
      sessionType,
      async evaluate(expression) {
        if (expression.includes('Object.prototype.toString') ) return { result: "'Uint8Array'", variablesReference: 0 };
        if (expression.startsWith('type(')) return { result: "'bytes'", variablesReference: 0 };
        if (expression.endsWith('.length') || expression.startsWith('len(')) return { result: '1000', variablesReference: 0 };
        const m = /subarray\((\d+),(\d+)\)\)$/.exec(expression) || /\[(\d+):(\d+)\]\)\.decode/.exec(expression);
        assert.ok(m, expression);
        return { result: `'${Buffer.from(sample.subarray(Number(m[1]), Number(m[2]))).toString('base64')}'`, variablesReference: 0 };
      },
      variables: async () => []
    };
    const result = await readBytes(access, 'data', options());
    assert.deepStrictEqual(result.bytes, sample, sessionType);
  }
});
