/** The data is not valid MsgPack, or not what the schema describes. */
export class MsgPackError extends Error {
  constructor(message: string, readonly offset?: number) {
    super(offset === undefined ? message : `${message} (offset ${offset})`);
    this.name = 'MsgPackError';
  }
}

/** The data ends inside a value. The stream reader reads more and tries again (internal: not exported). */
export class EndOfDataError extends MsgPackError {
  constructor(offset: number) {
    super('Unexpected end of data.', offset);
  }
}

/** The byte 0xC1, which starts an escape sequence (a value of unknown length, msgpack/msgpack#347): only MsgPackStreamReader reads them (internal: not exported). */
export class EscapeError extends MsgPackError {
  constructor(offset: number) {
    super('0xC1 is not a MsgPack type. It starts an escape sequence (an array, map, str or bin of unknown length, as proposed in msgpack/msgpack#347), which only MsgPackStreamReader reads.', offset);
  }
}

/** The body refers to a schema the store does not hold (e.g. the store was cleared between the request and the response). */
export class MissingSchemaError extends MsgPackError {
  constructor(readonly schemaId: string) {
    super(`The data refers to the schema ${schemaId}, which the store does not hold.`);
    this.name = 'MissingSchemaError';
  }
}

/**
 * A response that is not successful (status outside 200-299), with its decoded body: an object for JSON (ASP.NET Core sends problem details, RFC 9457, as application/problem+json
 * also when the request asks for MsgPack: `title`, `detail`, and `errors` per property for its automatic 400), the decoded value for MsgPack, otherwise the text.
 * The message has the title, detail and errors of problem details, or the start of a text.
 */
export class HttpError extends Error {
  constructor(readonly status: number, readonly statusText: string, readonly body: unknown, readonly response: Response) {
    super(errorMessage(status, statusText, body));
    this.name = 'HttpError';
  }
}

const MaxMessageText = 200;

function errorMessage(status: number, statusText: string, body: unknown): string {
  const head = statusText.length > 0 ? `${status} ${statusText}` : `${status}`; // HTTP/2 has no reason phrase
  if (typeof body === 'string') {
    const text = body.trim();
    return text.length === 0 ? head : `${head}: ${text.length > MaxMessageText ? text.slice(0, MaxMessageText) + '...' : text}`;
  }
  if (body === null || typeof body !== 'object' || Array.isArray(body)) return head;
  const problem = body as { title?: unknown; detail?: unknown; errors?: unknown };
  const parts: string[] = [];
  if (typeof problem.title === 'string' && problem.title !== statusText) parts.push(problem.title);
  if (typeof problem.detail === 'string') parts.push(problem.detail);
  if (problem.errors !== null && typeof problem.errors === 'object' && !Array.isArray(problem.errors)) {
    for (const [name, messages] of Object.entries(problem.errors as Record<string, unknown>)) {
      const texts = (Array.isArray(messages) ? messages : [messages]).filter((m): m is string => typeof m === 'string');
      if (texts.length > 0) parts.push(`${name}: ${texts.join(' ')}`);
    }
  }
  return parts.length === 0 ? head : `${head}: ${parts.join(' ')}`;
}
