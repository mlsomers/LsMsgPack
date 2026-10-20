/** The data is not valid MsgPack, or not what the schema describes. */
export class MsgPackError extends Error {
  constructor(message: string, readonly offset?: number) {
    super(offset === undefined ? message : `${message} (offset ${offset})`);
    this.name = 'MsgPackError';
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
