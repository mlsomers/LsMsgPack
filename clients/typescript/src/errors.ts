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

/** A response that is not successful (status outside 200-299). The body is kept as text (or the decoded value for MsgPack). */
export class HttpError extends Error {
  constructor(readonly status: number, readonly statusText: string, readonly body: unknown, readonly response: Response) {
    super(`${status} ${statusText}${typeof body === 'string' && body.length > 0 ? ': ' + body : ''}`);
    this.name = 'HttpError';
  }
}
