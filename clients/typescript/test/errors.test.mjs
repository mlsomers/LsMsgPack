// Error responses: problem details (RFC 9457) as ASP.NET Core sends them, texts, MsgPack values, and JSON that is not JSON.
import assert from 'node:assert';
import { test } from 'node:test';
import { HttpError, LsMsgPackClient, encode } from '../dist/index.js';

/** A client whose fetch answers every request with this response. */
const answering = (body, status, statusText, contentType) => new LsMsgPackClient({
  baseUrl: 'http://localhost/',
  fetch: async () => new Response(body, { status, statusText, headers: contentType === undefined ? {} : { 'Content-Type': contentType } }),
});

const failure = async (client) => {
  try {
    await client.get('/x');
  } catch (e) {
    assert.ok(e instanceof HttpError, `an HttpError, not ${e}`);
    return e;
  }
  assert.fail('no error');
};

test('problem details: the title, the detail and the validation errors are in the message', async () => {
  const validation = {
    type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1', title: 'One or more validation errors occurred.', status: 400,
    errors: { Customer: ['The Customer field is required.'], Count: ['The field Count must be between 1 and 5.'] }, traceId: '00-1',
  };
  const e = await failure(answering(JSON.stringify(validation), 400, 'Bad Request', 'application/problem+json; charset=utf-8'));
  assert.strictEqual(e.status, 400);
  assert.deepStrictEqual(e.body, validation);
  assert.strictEqual(e.message, '400 Bad Request: One or more validation errors occurred. Customer: The Customer field is required. Count: The field Count must be between 1 and 5.');

  const problem = await failure(answering(JSON.stringify({ title: 'Locked', status: 409, detail: 'Order 3 is locked.' }), 409, 'Conflict', 'application/problem+json'));
  assert.strictEqual(problem.message, '409 Conflict: Locked Order 3 is locked.');

  const notFound = await failure(answering(JSON.stringify({ title: 'Not Found', status: 404 }), 404, 'Not Found', 'application/problem+json'));
  assert.strictEqual(notFound.message, '404 Not Found', 'a title that is the reason phrase is not repeated');
});

test('a text, an empty body and a MsgPack value', async () => {
  assert.strictEqual((await failure(answering('Down for maintenance', 503, 'Service Unavailable', 'text/plain'))).message, '503 Service Unavailable: Down for maintenance');
  assert.strictEqual((await failure(answering('x'.repeat(1000), 500, 'Internal Server Error', 'text/html'))).message, `500 Internal Server Error: ${'x'.repeat(200)}...`);
  const empty = await failure(answering(null, 404, 'Not Found'));
  assert.strictEqual(empty.message, '404 Not Found');
  assert.strictEqual(empty.body, undefined);
  assert.strictEqual((await failure(answering(null, 404, ''))).message, '404', 'HTTP/2 has no reason phrase');

  const value = await failure(answering(encode({ Customer: 'c8' }), 404, 'Not Found', 'application/msgpack'));
  assert.deepStrictEqual(value.body, { Customer: 'c8' });
  assert.strictEqual(value.message, '404 Not Found');
});

test('an error that is not JSON after all keeps its text, a successful one throws', async () => {
  const e = await failure(answering('<html>proxy error</html>', 502, 'Bad Gateway', 'application/json'));
  assert.strictEqual(e.body, '<html>proxy error</html>');
  assert.strictEqual(e.message, '502 Bad Gateway: <html>proxy error</html>');
  await assert.rejects(answering('{', 200, 'OK', 'application/json').get('/x'), SyntaxError);
});

test('stream: an error has the problem details too', async () => {
  const client = answering(JSON.stringify({ title: 'Locked', detail: 'Order 3 is locked.' }), 409, 'Conflict', 'application/problem+json');
  await assert.rejects(client.stream('/x'), (e) => e instanceof HttpError && e.message === '409 Conflict: Locked Order 3 is locked.' && e.body.detail === 'Order 3 is locked.');
});
