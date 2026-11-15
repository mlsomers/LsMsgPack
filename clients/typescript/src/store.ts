// Keeps the schemas received from servers, so the next requests can say which ones the client holds (request header MsgPack-Schemas)
// and the server refers to them (18 bytes) instead of sending them. As LsMsgPackHttpSerializer.GetSchemasHeader / LearnSchema on the .NET side.

import { Schema } from './schema.js';

export interface SchemaStoreOptions {
  /** The most schemas kept (1024 by default). A full store keeps working, it stops learning new schemas (they keep coming inline). */
  maxSchemas?: number;
  /** The most schema ids sent per server (the most recently used ones, then the preloaded ones), 64 by default. */
  maxAdvertisedSchemas?: number;
  /**
   * A request lists the schemas received for its route (its path, in which segments that look like ids count as one: all digits, or 16 or more hexadecimal digits and dashes),
   * at most 4, and the most recently used ones (then the preloaded ones) only for a route it has not called yet. Default true: a server with more response types than
   * maxAdvertisedSchemas still refers to them, with a header of one id. A route the rule does not see (a path with a name in it) only costs an inline schema at its first call.
   */
  advertiseByRoute?: boolean;
}

/** The most schema ids kept per route. */
const MaxPerRoute = 4;
/** The most routes kept per origin, then they start again (e.g. paths with names in them). */
const MaxRoutes = 1024;

/** The route of a path: segments that look like ids (all digits, or 16 or more hexadecimal digits and dashes: Guids, hashes) become '#', so /orders/17 and /orders/18 are one route. */
export function routeOf(path: string): string {
  const end = path.search(/[?#]/);
  const segments = (end >= 0 ? path.slice(0, end) : path).split('/');
  for (let t = 0; t < segments.length; t++) {
    const segment = segments[t];
    if (/^[0-9]+$/.test(segment) || (segment.length >= 16 && /^[0-9a-fA-F-]+$/.test(segment))) segments[t] = '#';
  }
  return segments.join('/');
}

/** What export() writes: JSON-compatible, e.g. for localStorage or IndexedDB. */
export interface SchemaStoreExport {
  /** The schema bytes, base64. Their ids are computed again when imported. */
  schemas: string[];
  /** The ids per origin, most recent first. */
  origins: Record<string, string[]>;
  /** The ids per origin and route (routeOf), most recent first. */
  routes?: Record<string, Record<string, string[]>>;
}

export class SchemaStore {
  readonly maxSchemas: number;
  readonly maxAdvertisedSchemas: number;
  readonly advertiseByRoute: boolean;
  private readonly schemas = new Map<string, Schema>();
  private readonly origins = new Map<string, string[]>();
  private readonly routes = new Map<string, Map<string, string[]>>();
  /** The ids of the preloaded schemas: advertised to every server (or to the origins they were preloaded for). */
  private readonly preloaded: string[] = [];
  private readonly preloadedFor = new Map<string, string[]>();

  constructor(options: SchemaStoreOptions = {}) {
    this.maxSchemas = options.maxSchemas ?? 1024;
    this.maxAdvertisedSchemas = options.maxAdvertisedSchemas ?? 64;
    this.advertiseByRoute = options.advertiseByRoute ?? true;
  }

  /**
   * Holds schemas before the server sends them (the schema set of lsmsgpack-ts: base64 per response type), so the first response of each type refers to its schema.
   * Their ids are computed from the bytes. A schema the server does not write (other classes or options) does no harm: it is listed until the limit pushes it out.
   * @param schemas base64 (or bytes) of schemas, e.g. the schemas export of the generated file
   * @param origin the server (scheme://host:port) they are for, default: every server
   * @returns the number of schemas held
   */
  preload(schemas: Readonly<Record<string, string | Uint8Array>> | Iterable<string | Uint8Array>, origin?: string): number {
    const values = Symbol.iterator in Object(schemas) ? [...(schemas as Iterable<string | Uint8Array>)] : Object.values(schemas as Record<string, string | Uint8Array>);
    const ids = origin === undefined ? this.preloaded : (this.preloadedFor.get(origin) ?? (this.preloadedFor.set(origin, []), this.preloadedFor.get(origin)!));
    let held = 0;
    for (const value of values) {
      const schema = new Schema(typeof value === 'string' ? fromBase64(value) : value); // throws for bytes that are not a schema: the file is generated, a mistake should show
      if (!this.schemas.has(schema.id)) {
        if (this.schemas.size >= this.maxSchemas) break;
        this.schemas.set(schema.id, schema);
      }
      if (!ids.includes(schema.id)) ids.push(schema.id);
      held++;
    }
    return held;
  }

  get size(): number {
    return this.schemas.size;
  }

  get(id: string): Schema | undefined {
    return this.schemas.get(id.toLowerCase());
  }

  has(id: string): boolean {
    return this.schemas.has(id.toLowerCase());
  }

  /**
   * Learns the schema a server sent inline: kept when its id (the hash of its bytes) is the one of the response header MsgPack-Schema, and from now on advertised to that origin.
   * @param path the path of the request (its route), see advertiseByRoute
   * @returns true when the store holds it
   */
  learn(origin: string, schemaHeader: string | null | undefined, schema: Schema, path?: string): boolean {
    if (!schemaHeader || this.maxAdvertisedSchemas <= 0) return false;
    const id = schemaHeader.trim().toLowerCase();
    if (!this.schemas.has(id)) {
      if (schema.id !== id || this.schemas.size >= this.maxSchemas) return false; // not the schema the header claims, or full
      this.schemas.set(id, schema);
    }
    this.touch(origin, id, path);
    return true;
  }

  /**
   * Holds a schema found in data that is not a response of a server, e.g. inline in a stream (MsgPackStreamReader): later references to it can then be read.
   * Not advertised to any server. The id is the hash of the bytes, so a schema cannot pass for another one.
   * @returns true when the store holds it (false: full)
   */
  register(schema: Schema): boolean {
    if (this.schemas.has(schema.id)) return true;
    if (this.schemas.size >= this.maxSchemas) return false;
    this.schemas.set(schema.id, schema);
    return true;
  }

  /** Remembers that the server of the origin (and the route of the path) uses the schema (most recent first). */
  touch(origin: string, id: string, path?: string): void {
    id = id.toLowerCase();
    if (!this.schemas.has(id)) return;
    if (this.advertiseByRoute && path !== undefined && !this.touchRoute(origin, routeOf(path), id)) return; // the recent ones are for routes not called yet: they change only for ids new to a route
    let ids = this.origins.get(origin);
    if (ids === undefined) this.origins.set(origin, (ids = []));
    if (ids[0] === id) return;
    const at = ids.indexOf(id);
    if (at >= 0) ids.splice(at, 1);
    ids.unshift(id);
    if (ids.length > this.maxAdvertisedSchemas) ids.length = this.maxAdvertisedSchemas;
  }

  /** @returns whether the id is new for the route */
  private touchRoute(origin: string, route: string, id: string): boolean {
    let routes = this.routes.get(origin);
    if (routes === undefined) this.routes.set(origin, (routes = new Map()));
    let ids = routes.get(route);
    if (ids === undefined) {
      if (routes.size >= MaxRoutes) routes.clear();
      routes.set(route, (ids = []));
    }
    if (ids[0] === id) return false;
    const at = ids.indexOf(id);
    if (at >= 0) ids.splice(at, 1);
    ids.unshift(id);
    if (ids.length > MaxPerRoute) ids.length = MaxPerRoute;
    return at < 0;
  }

  /**
   * The value of the request header MsgPack-Schemas for the origin (undefined when the client holds none of its schemas): with the path (and advertiseByRoute) the ones received for its route,
   * otherwise (or for a route not called yet) the ones it used most recently, then the preloaded ones.
   */
  advertise(origin: string, path?: string): string | undefined {
    if (this.maxAdvertisedSchemas <= 0) return undefined;
    if (this.advertiseByRoute && path !== undefined) {
      const route = this.routes.get(origin)?.get(routeOf(path));
      if (route !== undefined) {
        const held = route.filter((id) => this.schemas.has(id)).slice(0, this.maxAdvertisedSchemas);
        if (held.length > 0) return held.join(',');
      }
    }
    const ids = this.origins.get(origin) ?? [];
    const preloaded = this.preloadedFor.get(origin);
    let held = ids.filter((id) => this.schemas.has(id));
    if (preloaded !== undefined || this.preloaded.length > 0) {
      const seen = new Set(held);
      for (const id of [...(preloaded ?? []), ...this.preloaded]) {
        if (held.length >= this.maxAdvertisedSchemas) break;
        if (!seen.has(id) && this.schemas.has(id)) {
          seen.add(id);
          held.push(id);
        }
      }
    }
    if (held.length > this.maxAdvertisedSchemas) held = held.slice(0, this.maxAdvertisedSchemas);
    return held.length === 0 ? undefined : held.join(',');
  }

  /** Forgets everything, also the preloaded schemas. */
  clear(): void {
    this.schemas.clear();
    this.origins.clear();
    this.routes.clear();
    this.preloaded.length = 0;
    this.preloadedFor.clear();
  }

  export(): SchemaStoreExport {
    const origins: Record<string, string[]> = {};
    for (const [origin, ids] of this.origins) origins[origin] = ids.slice();
    const routes: Record<string, Record<string, string[]>> = {};
    for (const [origin, byRoute] of this.routes) {
      routes[origin] = {};
      for (const [route, ids] of byRoute) routes[origin][route] = ids.slice();
    }
    return { schemas: [...this.schemas.values()].map((s) => toBase64(s.bytes)), origins, routes };
  }

  /** Adds what export() wrote (the ids are computed from the bytes again, schemas that do not parse are skipped). */
  import(data: SchemaStoreExport): void {
    for (const encoded of data.schemas ?? []) {
      if (this.schemas.size >= this.maxSchemas) break;
      try {
        const schema = new Schema(fromBase64(encoded));
        this.schemas.set(schema.id, schema);
      } catch {
        // not a schema: skipped
      }
    }
    for (const origin of Object.keys(data.origins ?? {})) {
      const ids = data.origins[origin];
      for (let t = ids.length - 1; t >= 0; t--) this.touch(origin, ids[t]);
    }
    for (const origin of Object.keys(data.routes ?? {})) {
      const byRoute = data.routes![origin];
      for (const route of Object.keys(byRoute)) {
        const ids = byRoute[route];
        for (let t = ids.length - 1; t >= 0; t--) {
          const id = ids[t].toLowerCase();
          if (this.schemas.has(id)) this.touchRoute(origin, route, id); // the keys as routeOf wrote them
        }
      }
    }
  }
}

function toBase64(bytes: Uint8Array): string {
  let s = '';
  for (let t = 0; t < bytes.length; t++) s += String.fromCharCode(bytes[t]);
  return btoa(s);
}

function fromBase64(text: string): Uint8Array {
  const s = atob(text);
  const bytes = new Uint8Array(s.length);
  for (let t = 0; t < s.length; t++) bytes[t] = s.charCodeAt(t);
  return bytes;
}
