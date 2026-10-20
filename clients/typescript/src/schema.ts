// The indexed schema of application/x-lsmsgpack: { typeName: [propertyName, ...] }, or the described schema (docs/TypeScriptClient.md#the-described-schema)
// where each property is [name, type] or [name, type, default], a non-object entry has ["@", type] and the last entry "" describes the root.

import { MsgPackError } from './errors.js';
import { MsgPackReader } from './reader.js';
import { sha256, toHex } from './sha256.js';

/** The kinds of the described schema (an unknown kind is read as 'any'). */
export type Kind = 'string' | 'bool' | 'int' | 'long' | 'ulong' | 'float' | 'decimal' | 'char' | 'bigint' | 'guid' | 'bin' | 'uri'
  | 'datetime' | 'dto' | 'dto-clock' | 'timespan' | 'dateonly' | 'timeonly' | 'any';

const Kinds = new Set<string>(['string', 'bool', 'int', 'long', 'ulong', 'float', 'decimal', 'char', 'bigint', 'guid', 'bin', 'uri',
  'datetime', 'dto', 'dto-clock', 'timespan', 'dateonly', 'timeonly', 'any']);

/** The declared type of a property, as the described schema gives it. */
export type TypeExpr =
  | { k: 'kind'; kind: Kind }
  /** An object of a schema entry (by index), or of a struct the schema has no entry for (only its default was written so far: its properties inline). */
  | { k: 'object'; entry: number | SchemaEntry; struct: boolean }
  | { k: 'nullable'; of: TypeExpr }
  | { k: 'list'; of: TypeExpr }
  | { k: 'dict'; key: TypeExpr; value: TypeExpr }
  | { k: 'pair'; key: TypeExpr; value: TypeExpr }
  | { k: 'tuple'; items: TypeExpr[] }
  /** A class of the models by its type name (generated models, see models.ts): the entry of that name in the schema of the payload. */
  | { k: 'model'; name: string; struct: boolean };

export const AnyType: TypeExpr = { k: 'kind', kind: 'any' };

export interface SchemaProperty {
  readonly name: string;
  /** Undefined in a plain schema. */
  readonly type?: TypeExpr;
  /** The [DefaultValue] of the property, as read (converted when the plans are made). */
  readonly default?: unknown;
  readonly hasDefault: boolean;
}

export interface SchemaEntry {
  /** The type name of the writer (short by default, may repeat: entries are found by index). */
  readonly name: string | null;
  /** By position: the index of a property in the data. */
  readonly properties: readonly SchemaProperty[];
  /** The content of a value of this type wrapped with a type id ({ "": index, "@": content }): set for collections and values that are not objects. */
  readonly content?: TypeExpr;
}

/** The format version hashed before the bytes of a schema (SchemaId.FormatVersion) and of a described schema (SchemaId.DescribedFormatVersion). */
export const PlainVersion = 1;
export const DescribedVersion = 2;

export class Schema {
  private _byName?: Map<string, number>;

  /** The index of the entry with the type name (the first one: short names may repeat), -1 when there is none. */
  indexOf(name: string): number {
    if (this._byName === undefined) {
      this._byName = new Map();
      for (let t = this.entries.length - 1; t >= 0; t--) if (this.entries[t].name !== null) this._byName.set(this.entries[t].name!, t);
    }
    return this._byName.get(name) ?? -1;
  }

  /** The entries the type ids of the data refer to (without the root entry of a described schema). */
  readonly entries: readonly SchemaEntry[];
  /** The declared type of the root (described schemas only). */
  readonly root?: TypeExpr;
  readonly described: boolean;

  /** @param bytes the schema exactly as it was received (its id is computed over them) */
  constructor(readonly bytes: Uint8Array) {
    const r = new MsgPackReader(bytes, { decimal: 'raw' }); // defaults are converted per the client's options when the plans are made
    const count = r.mapLength();
    if (count < 0) throw new MsgPackError('The data does not start with a schema (a map).', 0);
    const entries: SchemaEntry[] = [];
    let root: TypeExpr | undefined;
    let described = false;
    for (let t = 0; t < count; t++) {
      const name = r.value();
      if (name !== null && typeof name !== 'string') throw new MsgPackError('A type name of the schema is not a string.', r.pos);
      const items = r.value();
      if (!Array.isArray(items)) throw new MsgPackError(`The schema entry ${name} is not an array.`, r.pos);
      const entry = parseEntry(name, items);
      if (entry.described) described = true;
      if (name === '' && t === count - 1 && entry.entry.content) // the root entry of a described schema
        root = entry.entry.content;
      else
        entries.push(entry.entry);
    }
    if (r.pos !== bytes.length) throw new MsgPackError('Data after the schema.', r.pos);
    this.entries = entries;
    this.root = root;
    this.described = described || root !== undefined;
  }

  /** The id (32 hex digits) of these bytes as a schema of the given version: the first 16 bytes of SHA-256(version + bytes). */
  static idOf(bytes: Uint8Array, version: number): string {
    return toHex(sha256(new Uint8Array([version]), bytes), 0, 16);
  }

  private _id?: string;

  /** The id the server gives this schema (the version follows from the form: described or plain). */
  get id(): string {
    return this._id ?? (this._id = Schema.idOf(this.bytes, this.described ? DescribedVersion : PlainVersion));
  }
}

function parseEntry(name: string | null, items: unknown[]): { entry: SchemaEntry; described: boolean } {
  const properties: SchemaProperty[] = [];
  let content: TypeExpr | undefined;
  let described = false;
  for (const item of items) {
    if (typeof item === 'string') {
      properties.push({ name: item, hasDefault: false });
      continue;
    }
    if (!Array.isArray(item) || item.length < 2 || typeof item[0] !== 'string')
      throw new MsgPackError(`A property of the schema entry ${name} is not a name or [name, type].`);
    described = true;
    const type = parseType(item[1]);
    if (item[0] === '@') {
      content = type;
      continue;
    }
    properties.push(item.length >= 3 ? { name: item[0], type, default: item[2], hasDefault: true } : { name: item[0], type, hasDefault: false });
  }
  return { entry: { name, properties, content }, described };
}

/** Parses a type expression of the described schema. Unknown kinds and forms are read as 'any' (a newer server). */
export function parseType(value: unknown): TypeExpr {
  if (typeof value === 'string') return Kinds.has(value) ? { k: 'kind', kind: value as Kind } : AnyType;
  if (typeof value === 'number') return { k: 'object', entry: value, struct: false };
  if (!Array.isArray(value) || typeof value[0] !== 'string') return AnyType;
  switch (value[0]) {
    case '?': return { k: 'nullable', of: parseType(value[1]) };
    case 'struct':
      if (typeof value[1] === 'number') return { k: 'object', entry: value[1], struct: true };
      if (Array.isArray(value[1])) return { k: 'object', entry: parseEntry(null, value[1]).entry, struct: true };
      return AnyType;
    case 'list': return { k: 'list', of: parseType(value[1]) };
    case 'dict': return { k: 'dict', key: parseType(value[1]), value: parseType(value[2]) };
    case 'pair': return { k: 'pair', key: parseType(value[1]), value: parseType(value[2]) };
    case 'tuple': return { k: 'tuple', items: value.slice(1).map(parseType) };
  }
  return AnyType;
}
