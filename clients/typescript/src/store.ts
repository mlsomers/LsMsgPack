// Keeps the schemas received from servers, so the next requests can say which ones the client holds (request header MsgPack-Schemas)
// and the server refers to them (18 bytes) instead of sending them. As LtMsgPackHttpSerializer.GetSchemasHeader / LearnSchema on the .NET side.

import { Schema } from './schema.js';

export interface SchemaStoreOptions {
  /** The most schemas kept (1024 by default). A full store keeps working, it stops learning new schemas (they keep coming inline). */
  maxSchemas?: number;
  /** The most schema ids sent per server (the most recently received ones), 32 by default. */
  maxAdvertisedSchemas?: number;
}

/** What export() writes: JSON-compatible, e.g. for localStorage or IndexedDB. */
export interface SchemaStoreExport {
  /** The schema bytes, base64. Their ids are computed again when imported. */
  schemas: string[];
  /** The ids per origin, most recent first. */
  origins: Record<string, string[]>;
}

export class SchemaStore {
  readonly maxSchemas: number;
  readonly maxAdvertisedSchemas: number;
  private readonly schemas = new Map<string, Schema>();
  private readonly origins = new Map<string, string[]>();

  constructor(options: SchemaStoreOptions = {}) {
    this.maxSchemas = options.maxSchemas ?? 1024;
    this.maxAdvertisedSchemas = options.maxAdvertisedSchemas ?? 32;
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
   * @returns true when the store holds it
   */
  learn(origin: string, schemaHeader: string | null | undefined, schema: Schema): boolean {
    if (!schemaHeader || this.maxAdvertisedSchemas <= 0) return false;
    const id = schemaHeader.trim().toLowerCase();
    if (!this.schemas.has(id)) {
      if (schema.id !== id || this.schemas.size >= this.maxSchemas) return false; // not the schema the header claims, or full
      this.schemas.set(id, schema);
    }
    this.touch(origin, id);
    return true;
  }

  /** Remembers that the server of the origin uses the schema (most recent first). */
  touch(origin: string, id: string): void {
    id = id.toLowerCase();
    if (!this.schemas.has(id)) return;
    let ids = this.origins.get(origin);
    if (ids === undefined) this.origins.set(origin, (ids = []));
    if (ids[0] === id) return;
    const at = ids.indexOf(id);
    if (at >= 0) ids.splice(at, 1);
    ids.unshift(id);
    if (ids.length > this.maxAdvertisedSchemas) ids.length = this.maxAdvertisedSchemas;
  }

  /** The value of the request header MsgPack-Schemas for the origin (undefined when the client holds none of its schemas). */
  advertise(origin: string): string | undefined {
    const ids = this.origins.get(origin);
    if (ids === undefined || this.maxAdvertisedSchemas <= 0) return undefined;
    const held = ids.filter((id) => this.schemas.has(id));
    return held.length === 0 ? undefined : held.join(',');
  }

  clear(): void {
    this.schemas.clear();
    this.origins.clear();
  }

  export(): SchemaStoreExport {
    const origins: Record<string, string[]> = {};
    for (const [origin, ids] of this.origins) origins[origin] = ids.slice();
    return { schemas: [...this.schemas.values()].map((s) => toBase64(s.bytes)), origins };
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
