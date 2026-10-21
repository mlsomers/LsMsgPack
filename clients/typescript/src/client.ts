// A fetch wrapper for the ASP.NET formatters of LsMsgPack: asks for application/x-lsmsgpack with the described schema, tells the server which schemas it holds,
// learns the schemas of the responses and decodes the bodies into complete objects. Request bodies are sent as plain MsgPack (application/msgpack).

import { Decoded, Decoder, DecodeOptions } from './decoder.js';
import { HttpError, MissingSchemaError } from './errors.js';
import { Schema } from './schema.js';
import { SchemaStore } from './store.js';
import type { ModelSet } from './models.js';
import { encode } from './writer.js';

export const MediaTypes = {
  MsgPack: 'application/msgpack',
  XMsgPack: 'application/x-msgpack',
  XLsMsgPack: 'application/x-lsmsgpack',
} as const;

/** Request header: the ids of the schemas the client holds. */
export const SchemasHeader = 'MsgPack-Schemas';
/** Response header: the id of the schema of the body. */
export const SchemaHeader = 'MsgPack-Schema';

/** The Accept header of the client: the described schema, and application/x-lsmsgpack for servers (and content negotiation) that do not know the parameter. */
export const DescribedAccept = `${MediaTypes.XLsMsgPack}; schema=described, ${MediaTypes.XLsMsgPack}; q=0.9`;

export interface ClientOptions extends DecodeOptions {
  /** Relative urls are resolved against it (default: the location of the page, if any). */
  baseUrl?: string | URL;
  /** The fetch to use (default: globalThis.fetch). */
  fetch?: typeof globalThis.fetch;
  /** Where the schemas are kept, share one between clients of the same servers (default: a new store). */
  store?: SchemaStore;
  /** Ask for the described schema, which gives omitted values their defaults and nested objects their properties (default true). */
  describedSchema?: boolean;
  /** Tell servers which schemas the client holds, so they refer to them instead of sending them (default true). */
  negotiateSchemas?: boolean;
  /** Headers added to every request. */
  headers?: HeadersInit;
  /**
   * The schema set of lsmsgpack-ts (its schemas export): held from the start, so the first response of each type refers to its schema instead of carrying it.
   * Shared stores: call store.preload once instead.
   */
  schemas?: Readonly<Record<string, string | Uint8Array>> | Iterable<string | Uint8Array>;
}

/** A request: the options of fetch, with a body that is encoded as MsgPack unless it is already text or bytes. */
export interface RequestOptions extends Omit<RequestInit, 'body'> {
  body?: unknown;
}

export interface MsgPackResponse<T> {
  value: T;
  response: Response;
  /** The schema of an application/x-lsmsgpack body. */
  schema?: Schema;
  /** The body referred to its schema instead of containing it. */
  reference: boolean;
}

export class LsMsgPackClient {
  readonly store: SchemaStore;
  readonly decoder: Decoder;
  private readonly fetchFn: typeof globalThis.fetch;
  private readonly baseUrl?: string | URL;
  private readonly described: boolean;
  private readonly negotiate: boolean;
  private readonly headers?: HeadersInit;
  private readonly models?: ModelSet;
  private readonly generateCode?: boolean;

  constructor(options: ClientOptions = {}) {
    this.store = options.store ?? new SchemaStore();
    this.decoder = new Decoder(options);
    this.models = options.models;
    this.generateCode = options.generateCode;
    if (options.schemas !== undefined) this.store.preload(options.schemas);
    this.fetchFn = options.fetch ?? globalThis.fetch.bind(globalThis);
    this.baseUrl = options.baseUrl;
    this.described = options.describedSchema ?? true;
    this.negotiate = options.negotiateSchemas ?? true;
    this.headers = options.headers;
  }

  async get<T = unknown>(url: string | URL, init?: RequestOptions): Promise<T> {
    return (await this.send<T>(url, { ...init, method: 'GET' })).value;
  }

  async post<T = unknown>(url: string | URL, body?: unknown, init?: RequestOptions): Promise<T> {
    return (await this.send<T>(url, { ...init, method: 'POST', body })).value;
  }

  async put<T = unknown>(url: string | URL, body?: unknown, init?: RequestOptions): Promise<T> {
    return (await this.send<T>(url, { ...init, method: 'PUT', body })).value;
  }

  async patch<T = unknown>(url: string | URL, body?: unknown, init?: RequestOptions): Promise<T> {
    return (await this.send<T>(url, { ...init, method: 'PATCH', body })).value;
  }

  async delete<T = unknown>(url: string | URL, init?: RequestOptions): Promise<T> {
    return (await this.send<T>(url, { ...init, method: 'DELETE' })).value;
  }

  /**
   * Sends a request and decodes the response. Throws an HttpError for a status outside 200-299 (with the decoded body).
   * A GET or HEAD whose response refers to a schema the store no longer holds is repeated once without advertising schemas.
   */
  async send<T = unknown>(url: string | URL, init: RequestOptions = {}): Promise<MsgPackResponse<T>> {
    const target = this.resolve(url);
    const headers = new Headers(this.headers);
    new Headers(init.headers).forEach((value, name) => headers.set(name, value));
    if (!headers.has('Accept')) headers.set('Accept', this.described ? DescribedAccept : MediaTypes.XLsMsgPack);
    const advertised = this.negotiate && !headers.has(SchemasHeader) ? this.store.advertise(target.origin) : undefined;
    if (advertised !== undefined) headers.set(SchemasHeader, advertised);

    let body: BodyInit | null | undefined;
    if (init.body === undefined || init.body === null || isBodyInit(init.body)) body = init.body as BodyInit | null | undefined;
    else {
      body = encode(init.body, { models: this.models, generateCode: this.generateCode }) as unknown as BodyInit;
      if (!headers.has('Content-Type')) headers.set('Content-Type', MediaTypes.MsgPack);
    }

    const request = { ...init, headers, body };
    let result: MsgPackResponse<T>;
    try {
      result = await this.read<T>(await this.fetchFn(target, request), target.origin);
    } catch (e) {
      const method = (init.method ?? 'GET').toUpperCase();
      if (!(e instanceof MissingSchemaError) || advertised === undefined || (method !== 'GET' && method !== 'HEAD')) throw e;
      headers.delete(SchemasHeader); // repeating a GET is safe: the server sends the schema inline
      result = await this.read<T>(await this.fetchFn(target, request), target.origin);
    }
    if (!result.response.ok) throw new HttpError(result.response.status, result.response.statusText, result.value, result.response);
    return result;
  }

  /** Decodes a response (e.g. of your own fetch): MsgPack by its Content-Type, JSON, otherwise text. Learns the schema of an application/x-lsmsgpack body. */
  async read<T = unknown>(response: Response, origin?: string): Promise<MsgPackResponse<T>> {
    const mediaType = (response.headers.get('Content-Type') ?? '').split(';')[0].trim().toLowerCase();
    if (mediaType === MediaTypes.XLsMsgPack) {
      const bytes = new Uint8Array(await response.arrayBuffer());
      const decoded = this.decode(bytes, origin ?? originOf(response.url), response.headers.get(SchemaHeader));
      return { value: decoded.value as T, response, schema: decoded.schema, reference: decoded.reference };
    }
    if (mediaType === MediaTypes.MsgPack || mediaType === MediaTypes.XMsgPack) {
      const bytes = new Uint8Array(await response.arrayBuffer());
      return { value: this.decoder.decodePlain(bytes) as T, response, reference: false };
    }
    const text = await response.text();
    let value: unknown = text.length === 0 ? undefined : text; // e.g. 204 No Content for null
    if (/[/+]json$/.test(mediaType) && text.length > 0) value = JSON.parse(text);
    return { value: value as T, response, reference: false };
  }

  /**
   * Decodes an application/x-lsmsgpack body.
   * @param origin the server (scheme://host:port), to learn its schema
   * @param schemaHeader the response header MsgPack-Schema: an inline schema is kept only when its hash is this id
   */
  decode(bytes: Uint8Array, origin?: string, schemaHeader?: string | null): Decoded {
    const id = schemaHeader?.trim().toLowerCase();
    const decoded = this.decoder.decode(bytes, (ref) => this.store.get(ref), (schemaBytes) => {
      const known = id ? this.store.get(id) : undefined;
      return known !== undefined && sameBytes(known.bytes, schemaBytes) ? known : new Schema(schemaBytes); // parsed once per schema while it is sent inline
    });
    if (this.negotiate && origin && decoded.schema !== undefined) {
      if (decoded.reference) this.store.touch(origin, decoded.schema.id);
      else this.store.learn(origin, id, decoded.schema);
    }
    return decoded;
  }

  private resolve(url: string | URL): URL {
    const base = this.baseUrl ?? (globalThis as { location?: { href: string } }).location?.href;
    return base === undefined ? new URL(url) : new URL(url, base);
  }
}

function originOf(url: string): string | undefined {
  try {
    return url ? new URL(url).origin : undefined;
  } catch {
    return undefined;
  }
}

function isBodyInit(body: unknown): boolean {
  return typeof body === 'string' || body instanceof Uint8Array || body instanceof ArrayBuffer || ArrayBuffer.isView(body)
    || (typeof Blob !== 'undefined' && body instanceof Blob)
    || (typeof FormData !== 'undefined' && body instanceof FormData)
    || (typeof URLSearchParams !== 'undefined' && body instanceof URLSearchParams)
    || (typeof ReadableStream !== 'undefined' && body instanceof ReadableStream);
}

function sameBytes(a: Uint8Array, b: Uint8Array): boolean {
  if (a.length !== b.length) return false;
  for (let t = 0; t < a.length; t++) if (a[t] !== b[t]) return false;
  return true;
}
