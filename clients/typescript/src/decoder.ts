// Reads application/x-lsmsgpack payloads (the schema, or a reference to it, followed by the body) into complete objects:
// every property of the schema entry is set, the ones the writer left out (nil) get their default (of the schema or the type), the ones missing after the end of an array
// keep what the class sets (plain objects: the default).
// An object that came with a type id gets its type name as the property $type (as System.Text.Json names it): the encoder writes it back as the type id.

import { EndOfDataError, MissingSchemaError, MsgPackError } from './errors.js';
import { MsgPackExtension, MsgPackReader, ReaderOptions } from './reader.js';
import type { KeptProperty, ModelSet } from './models.js';
import { nameFunction, PropertyNames } from './names.js';
import { AnyType, Kind, Schema, SchemaEntry, TypeExpr } from './schema.js';
import { toHex } from './sha256.js';
import { convertDecimal, dateOnlyText, decodeDecimal, DecimalMode, EmptyGuid, guidString, MinDateTimeMs, offsetText, timeOnlyText, timeSpanText } from './values.js';

export interface DecodeOptions extends ReaderOptions {
  /** long and ulong properties: 'number' (default, as JSON.parse: precision is lost beyond 2^53) or 'bigint' (always a bigint). Untyped values are a bigint only beyond 2^53. */
  int64?: 'number' | 'bigint';
  /** DateTime and DateTimeOffset properties: 'date' (default, a Date) or 'iso' (text: DateTime in UTC, DateTimeOffset with its offset). Untyped values are always a Date. */
  dates?: 'date' | 'iso';
  /**
   * The generated models (lsmsgpack-ts, defineModels): an entry of their type name is read into a new instance of its class (its fields have the values the .NET constructor sets,
   * which a value the data left out keeps), the declared types of its properties are used when the schema has only names. The schema of the payload still decides which property a value is.
   */
  models?: ModelSet;
  /** Classes to create for schema entries (by the type name of the writer), e.g. { Invoice: Invoice }. A property the class does not initialize gets the default. The models come first. */
  classes?: Record<string, new () => object>;
  /**
   * The names of the properties of the objects: 'camelCase' (default, invoiceDate, as System.Text.Json's JsonNamingPolicy.CamelCase names them, the JSON of ASP.NET Core;
   * also key and value of a KeyValuePair) or 'dotnet' (the names of the .NET properties: InvoiceDate, the default of version 2026.10.22). Never dictionary keys or $type.
   * The models give theirs (the names of their fields) when it is left out.
   */
  propertyNames?: PropertyNames;
  /** The data starts with the indexed schema (default true, as the formatters write application/x-lsmsgpack). False: the body only (UseInexedSchema off). */
  indexedSchema?: boolean;
  /**
   * Generate the code that creates and fills the objects of each schema entry (new Function, the property names as string literals), default true: about a third faster,
   * V8 then sees one object shape per place in the code. Without it (or when a Content-Security-Policy does not allow it) a generic loop does the same.
   */
  generateCode?: boolean;
  /**
   * References (ReferenceHandling.Preserve of the server: an object or collection written twice is the second time an extension of type 5 with the path to its first occurrence):
   * 'copies' (default) reads the value at the path again, so a shared object is read as copies and a cycle fails; 'preserve' gives the same instance, a cycle included
   * (every object and array read is kept by its place while a payload is read, and objects are made before their properties are read, which costs a part of the speed);
   * 'none' leaves an extension of type 5 as a MsgPackExtension (a server without the option that writes its own extension 5).
   * The client writes request bodies with the same setting: with 'preserve' an object met again is a reference (EncodeOptions.references).
   */
  references?: ReferenceHandling;
}

/** How the decoder reads a reference (extension type 5) and the encoder writes an object met again, see DecodeOptions.references and EncodeOptions.references. */
export type ReferenceHandling = 'preserve' | 'copies' | 'none';

type Fill = (r: SchemaReader, o: Record<string, unknown>, n: number) => void;

let codeAllowed = true; // false once new Function failed (Content-Security-Policy without 'unsafe-eval')

/** The property that holds the type name of an object read with a type id, and that the encoder writes as the type id. */
export const TypeProperty = '$type';

type Create = (values: unknown[], typeName: string | null) => Record<string, unknown>;

/**
 * The generated create and fill of a plan (see DecodeOptions.generateCode), undefined when code cannot be generated. Instances of classes only get the fill (create is new).
 * The fill has the read functions of the properties as constants: each call site then always calls the same function (reads[t](r) was one call site for all of them).
 */
function generate(names: string[], reads: Read[], literals: boolean, nil: ((t: number) => unknown) | undefined): { create?: Create; createTyped?: Create; fill: Fill } | undefined {
  if (!codeAllowed || names.length === 0 || names.length > 512 || names.includes('__proto__') || names.includes(TypeProperty)) return undefined; // an object literal would set the prototype, or have $type twice
  const literal = names.map((n, t) => `${JSON.stringify(n)}: v[${t}]`).join(', ');
  let fill = `const ${names.map((_, t) => `read${t} = reads[${t}]`).join(', ')};\nreturn function fill(r, o, n) {\nconst b = r.bytes;\n`;
  names.forEach((n, t) => {
    // a nil is the value the writer left out: already there in an object made with the defaults, set on an instance of a class (its constructor set its own)
    const left = nil === undefined ? 'r.pos++;' : `{ r.pos++; o[${JSON.stringify(n)}] = nil(${t}); }`;
    fill += `if (n === ${t}) return;\nif (b[r.pos] === 0xc0) ${left} else o[${JSON.stringify(n)}] = read${t}(r);\n`;
  });
  fill += `for (let k = ${names.length}; k < n; k++) r.value();\n};`;
  try {
    const fillFn = (new Function('reads', 'nil', fill) as (reads: Read[], nil: ((t: number) => unknown) | undefined) => Fill)(reads, nil);
    if (!literals) return { fill: fillFn };
    return {
      create: new Function('v', `return { ${literal} };`) as Create,
      createTyped: new Function('v', 't', `return { ${JSON.stringify(TypeProperty)}: t, ${literal} };`) as Create, // $type first, as System.Text.Json writes it
      fill: fillFn,
    };
  } catch {
    codeAllowed = false;
    return undefined;
  }
}

/** Reads an object of the array layout (the header of n values is read) and returns it complete. */
type ReadArray = (r: SchemaReader, n: number) => Record<string, unknown>;

/** A value of generateReadArray that is still to be made (a value the data did not have: null is a value). */
const Unread = Object.freeze({});

/** A field a model's class sets that the schema entry does not have (the server no longer writes it): it keeps the class's value. */
interface Extra {
  name: string;
  value: unknown;
  make?: () => unknown;
}

/**
 * Generates the reading of an object of the array layout: the values into variables first (the defaults where the data left them out), then the object at once,
 * so its properties are written once (a literal, or for a model a constructor function with the class's prototype: new of the class would make its initial values
 * only to have them replaced). The defaults that are made per object (makers) are made after the values, as Plan.complete does.
 * A nil is the value the writer left out (nils, nilMakers: the schema's default or the type's), a value missing after the end of the array keeps the default (for a model: what its class sets).
 * @param order the properties in the order the object gets them: an index of names, or an Extra
 * @param proto the prototype of the model's class, undefined for a plain object
 */
function generateReadArray(names: string[], reads: Read[], defaults: unknown[], makers: ((() => unknown) | undefined)[], nils: unknown[], nilMakers: ((() => unknown) | undefined)[],
  order: (number | Extra)[], proto: object | undefined): ReadArray | undefined {
  const count = names.length;
  if (!codeAllowed || count === 0 || count > 512 || names.includes('__proto__') || names.includes(TypeProperty) || order.some((p) => typeof p !== 'number' && p.name === '__proto__')) return undefined;
  const v = names.map((_, t) => `v${t}`);
  defaults = defaults.map((d, t) => makers[t] === undefined ? d : Unread);
  let code = `const ${names.map((_, t) => `read${t} = reads[${t}], d${t} = defaults[${t}], m${t} = makers[${t}], z${t} = nils[${t}], zm${t} = nilMakers[${t}]`).join(', ')};\n`;
  let result: string;
  if (proto === undefined) result = `{ ${order.map((p) => `${JSON.stringify(names[p as number])}: v${p}`).join(', ')} }`;
  else {
    code += `const extras = order.filter((p) => typeof p !== 'number');\n`;
    let extra = 0;
    code += `function Model(${v.join(', ')}) {\n${order.map((p) => typeof p === 'number'
      ? `this[${JSON.stringify(names[p])}] = v${p};\n`
      : `this[${JSON.stringify(p.name)}] = ${p.make === undefined ? `extras[${extra++}].value` : `extras[${extra++}].make()`};\n`).join('')}}\nModel.prototype = proto;\n`;
    result = `new Model(${v.join(', ')})`;
  }
  code += `return function readArray(r, n) {\nconst b = r.bytes;\nlet ${names.map((_, t) => `v${t} = d${t}`).join(', ')};\nvalues: {\n`;
  names.forEach((_, t) => {
    const nil = nilMakers[t] === undefined ? `v${t} = z${t};` : `v${t} = zm${t}();`;
    code += `if (n === ${t}) break values;\nif (b[r.pos] === 0xc0) { r.pos++; ${nil} } else v${t} = read${t}(r);\n`;
  });
  code += `for (let k = ${count}; k < n; k++) r.value();\n}\n`;
  makers.forEach((m, t) => {
    if (m !== undefined) code += `if (v${t} === unread) v${t} = m${t}();\n`;
  });
  code += `return ${result};\n};`;
  try {
    return (new Function('reads', 'defaults', 'makers', 'nils', 'nilMakers', 'order', 'proto', 'unread', code) as (...args: unknown[]) => ReadArray)(reads, defaults, makers, nils, nilMakers, order, proto, Unread);
  } catch {
    codeAllowed = false;
    return undefined;
  }
}

/** The initial value of a field of a model's class (probe: an instance), and how to make it for each object when it is not a primitive. */
function initialOf(cls: new () => object, probe: Record<string, unknown>, name: string): { value: unknown; make?: () => unknown } {
  const value = probe[name];
  if (value === null || (typeof value !== 'object' && typeof value !== 'function')) return { value };
  if (value instanceof Date) {
    const time = value.getTime();
    return { value: null, make: () => new Date(time) };
  }
  if (Array.isArray(value) && value.length === 0 && Object.getPrototypeOf(value) === Array.prototype) return { value: null, make: () => [] };
  return { value: null, make: () => (new cls() as Record<string, unknown>)[name] }; // e.g. a nested object: as the class makes it
}

/** Sets a property, also one named __proto__ (as JSON.parse does: an own property, not the prototype). */
export function setOwn(o: Record<string, unknown>, name: string, value: unknown): void {
  if (name === '__proto__') Object.defineProperty(o, name, { value, writable: true, enumerable: true, configurable: true });
  else o[name] = value;
}

type Read = (r: SchemaReader) => unknown;

interface Default {
  value: unknown;
  /** For values that must not be shared (Date, objects). */
  make?: () => unknown;
}

/** How to read the values of one schema entry. */
class Plan {
  readonly names: string[];
  readonly reads: Read[];
  readonly values: unknown[];
  readonly makers: ((() => unknown) | undefined)[];
  /** The properties whose default is made per object (Date, objects): null until the values are read, then made where the data had none (see complete). */
  made?: number[];
  readonly isObject: boolean;
  content?: Read;
  private _byName?: Map<string, number>;
  private createFn?: Create;
  private createTypedFn?: Create;
  fillFn?: Fill;

  /** An object with the defaults, copied by create without generated code (a spread copies an object of the same shape faster than setting each property). */
  private template?: Record<string, unknown>;

  /**
   * Makes what create and fill use, once the properties are known: the template, and with generateCode their code.
   * @param preserve references are preserved: an object is made before its values are read (so a cycle back to it resolves), not by readArrayFn after them
   */
  prepare(generateCode: boolean, preserve: boolean): void {
    if (this.cls === undefined) {
      const o: Record<string, unknown> = {};
      for (let t = 0; t < this.names.length; t++) setOwn(o, this.names[t], this.values[t]);
      this.template = o;
    }
    if (!generateCode) return;
    const code = generate(this.names, this.reads, this.cls === undefined, this.cls === undefined ? undefined : (t) => this.nilValue(t));
    this.createFn = code?.create;
    this.createTypedFn = code?.createTyped;
    this.fillFn = code?.fill;
    if (!preserve) this.readArrayFn = this.generateReadArray();
  }

  /** readArrayFn for plain objects and the models (not for DecodeOptions.classes: their constructors may do more than set fields). */
  private generateReadArray(): ReadArray | undefined {
    const names = this.names;
    if (this.cls === undefined) return generateReadArray(names, this.reads, this.values, this.makers, this.values, this.makers, names.map((_, t) => t), undefined);
    if (!this.model) return undefined;
    // The generated classes only initialize their fields: the same fields with the same values in the same order, without new of the class.
    const cls = this.cls, probe = new cls() as Record<string, unknown>;
    const defaults = this.values.slice(), makers = this.makers.slice(), order: (number | Extra)[] = [];
    for (const name of Object.keys(probe)) {
      const t = this.indexOf(name), initial = initialOf(cls, probe, name);
      if (t < 0) order.push({ name, ...initial });
      else {
        order.push(t);
        defaults[t] = initial.value;
        makers[t] = initial.make;
      }
    }
    for (let t = 0; t < this.missing.length; t++) order.push(this.missing[t]); // after the class's fields, as create sets them
    return generateReadArray(names, this.reads, defaults, makers, this.values, this.makers, order, cls.prototype);
  }

  /** The value of a nil in the array layout: what the writer left out (the default of the schema or of the type), not what a class sets. */
  nilValue(t: number): unknown {
    const make = this.makers[t];
    return make === undefined ? this.values[t] : make();
  }

  /** Reads an object of the array layout into a new object (see generateReadArray), undefined without generated code. */
  readArrayFn?: ReadArray;

  /** The properties the class does not initialize (they get the defaults of the schema: e.g. a property the server has and the models do not have yet). */
  readonly missing: number[] = [];

  /**
   * @param names the names of the properties in JavaScript (DecodeOptions.propertyNames)
   * @param cls the class of the entry (a model, or one of DecodeOptions.classes)
   * @param model the class is a model: it has $type on its prototype
   */
  constructor(readonly entry: SchemaEntry, names: string[], readonly cls: (new () => object) | undefined, readonly model = false) {
    const count = entry.properties.length;
    this.names = names;
    this.reads = new Array(count);
    this.values = new Array(count);
    this.makers = new Array(count);
    this.isObject = entry.content === undefined;
    if (cls !== undefined) {
      const probe = new cls() as Record<string, unknown>; // the same fields for every instance
      for (let t = 0; t < count; t++) if (probe[this.names[t]] === undefined) this.missing.push(t);
    }
  }

  /** A new object with every property at its default (null for the ones that are made, see complete), in the order of the schema. */
  create(): Record<string, unknown> {
    const names = this.names, values = this.values;
    if (this.createFn !== undefined) return this.createFn(values, null);
    if (this.template !== undefined) return { ...this.template }; // own properties, also one named __proto__
    const o = new this.cls!() as Record<string, unknown>; // new rather than a copy of one instance: the initial values (Date, arrays, nested objects) are not shared
    const missing = this.missing;
    for (let t = 0; t < missing.length; t++) setOwn(o, names[missing[t]], values[missing[t]]);
    return o;
  }

  /** create for an object that came with a type id: with its type name in $type (the first property; on instances of your classes after their own). */
  createTyped(): Record<string, unknown> {
    const typeName = this.entry.name;
    if (typeName === null) return this.create(); // the root entry of a described schema (type ids never refer to it) or a nameless entry
    if (this.createTypedFn !== undefined) return this.createTypedFn(this.values, typeName);
    if (this.template !== undefined) return { [TypeProperty]: typeName, ...this.template };
    const o = this.create();
    if (!this.model) o[TypeProperty] = typeName; // a model has it on its prototype
    return o;
  }

  /** Makes the defaults the data did not replace (a Date or struct is never null, so null means not read). */
  complete(o: Record<string, unknown>): Record<string, unknown> {
    const made = this.made;
    if (made === undefined) return o;
    for (let t = 0; t < made.length; t++) {
      const name = this.names[made[t]];
      if (o[name] === null) o[name] = this.makers[made[t]]!();
    }
    return o;
  }

  /** A new object with every default made (e.g. the default of a struct). */
  createComplete(): Record<string, unknown> {
    return this.complete(this.create());
  }

  indexOf(name: string): number {
    if (this._byName === undefined) {
      this._byName = new Map();
      for (let t = this.names.length - 1; t >= 0; t--) this._byName.set(this.names[t], t);
    }
    return this._byName.get(name) ?? -1;
  }

  private _byWriterName?: Map<string, number>;

  /** The index of a property by the name the writer gave it (a key of ObjectLayout.Map without the schema): the .NET name. */
  indexOfWriterName(name: string): number {
    const properties = this.entry.properties;
    if (this._byWriterName === undefined) {
      this._byWriterName = new Map();
      for (let t = properties.length - 1; t >= 0; t--) this._byWriterName.set(properties[t].name, t);
    }
    return this._byWriterName.get(name) ?? -1;
  }
}

/** The plans of one schema for one set of options, made when first used. */
class Plans {
  private readonly plans: (Plan | undefined)[];
  private readonly inline = new Map<SchemaEntry, Plan>();
  readonly root: Read;
  private readonly field: (name: string) => string;
  private readonly keyName: string;
  private readonly valueName: string;

  constructor(readonly schema: Schema, private readonly options: ResolvedOptions) {
    this.field = nameFunction(options.propertyNames);
    this.keyName = this.field('Key');
    this.valueName = this.field('Value');
    this.plans = new Array(schema.entries.length);
    this.root = schema.root === undefined ? readDynamic : this.compile(schema.root);
  }

  plan(index: number): Plan {
    const plan = this.plans[index];
    if (plan !== undefined) return plan;
    if (!Number.isInteger(index) || index < 0 || index >= this.plans.length)
      throw new MsgPackError(`The type id ${index} is not an entry of the schema (${this.plans.length} entries).`);
    return this.build(index);
  }

  /** The plan of an entry by index, or of a struct described inline. */
  planOf(entry: number | SchemaEntry): Plan {
    if (typeof entry === 'number') return this.plan(entry);
    const plan = this.inline.get(entry);
    return plan ?? this.build(-1, entry);
  }

  private build(index: number, entry = this.schema.entries[index]): Plan {
    const model = entry.name === null ? undefined : this.options.models?.byName(entry.name);
    const plan = new Plan(entry, entry.properties.map((p) => this.field(p.name)), model?.ctor ?? (entry.name === null ? undefined : this.options.classes?.[entry.name]), model !== undefined);
    if (index >= 0) this.plans[index] = plan; // before the properties: they may refer to this entry
    else this.inline.set(entry, plan);
    entry.properties.forEach((p, t) => {
      const type = p.type ?? model?.byName.get(p.name) ?? AnyType; // a plain schema has names only: the models know the types
      plan.reads[t] = this.compile(type);
      const d = p.hasDefault ? this.convertDefault(p.default, type) : this.defaultOf(type);
      plan.values[t] = d.make === undefined ? d.value : null;
      plan.makers[t] = d.make;
      if (d.make !== undefined && (plan.cls === undefined || plan.missing.includes(t))) (plan.made ??= []).push(t);
    });
    if (entry.content !== undefined) plan.content = this.compile(entry.content);
    plan.prepare(this.options.generateCode, this.options.references === 'preserve');
    const models = this.options.models;
    if (models !== undefined && entry.name !== null && index >= 0) {
      if (model === undefined) this.learn(index);
      else
        for (let t = 0; t < entry.properties.length; t++) {
          const p = entry.properties[t];
          if (!model.byName.has(p.name)) models.keep(model, { name: p.name, field: plan.names[t], type: this.writerType(p.type) });
        }
    }
    return plan;
  }

  /** The entries given to ModelSet.learn (once per schema). */
  private learned?: Set<number>;

  /**
   * Tells the models about an entry of a class they do not have (by its type name): its objects are plain objects, the encoder writes them back with the .NET names and the
   * declared types (instead of the field names and the values as they are). Docs: TypeScriptClient.md, "The server changed".
   */
  private learn(index: number): void {
    const learned = this.learned ??= new Set();
    if (learned.has(index)) return;
    learned.add(index); // before the properties: they may refer to this entry
    const entry = this.schema.entries[index];
    const properties: KeptProperty[] = entry.properties.map((p) => ({ name: p.name, field: this.field(p.name), type: this.writerType(p.type) }));
    this.options.models!.learn({ typeName: entry.name!, properties, fields: new Set(properties.map((p) => p.field)) });
  }

  /**
   * A declared type of the schema as the encoder takes it (models.ts KeptProperty): without the entry indexes of this schema (an object is the class of its entry's type name,
   * learned when the models do not have it). Undefined for a plain schema.
   */
  private writerType(type: TypeExpr | undefined): TypeExpr | undefined {
    if (type === undefined) return undefined;
    switch (type.k) {
      case 'kind':
      case 'model': return type;
      case 'object': {
        const entry = typeof type.entry === 'number' ? this.schema.entries[type.entry] : undefined;
        if (entry === undefined || entry.name === null) return AnyType; // a struct described inline: written as it is
        if (this.options.models!.byName(entry.name) === undefined) this.learn(type.entry as number);
        return { k: 'model', name: entry.name, struct: type.struct };
      }
      case 'nullable': return { k: 'nullable', of: this.writerType(type.of)! };
      case 'list': return { k: 'list', of: this.writerType(type.of)! };
      case 'dict': return { k: 'dict', key: this.writerType(type.key)!, value: this.writerType(type.value)! };
      case 'pair': return { k: 'pair', key: this.writerType(type.key)!, value: this.writerType(type.value)! };
      case 'tuple': return { k: 'tuple', items: type.items.map((t) => this.writerType(t)!) };
    }
  }

  private readonly compiled = new WeakMap<TypeExpr, Read>();

  /** compile, once per type expression (the stream reader asks per value). */
  readOf(type: TypeExpr): Read {
    let read = this.compiled.get(type);
    if (read === undefined) this.compiled.set(type, (read = this.compile(type)));
    return read;
  }

  /** A read function for values of the type (each handles nil and values wrapped with a type id). */
  compile(type: TypeExpr): Read {
    switch (type.k) {
      case 'kind': return this.kind(type.kind);
      case 'nullable': return this.compile(type.of);
      case 'object': {
        const entry = type.entry;
        let plan: Plan | undefined; // not when compiled: the entry may refer to itself
        return (r) => r.readObject(plan ?? (plan = this.planOf(entry)));
      }
      case 'list': {
        const element = this.compile(type.of);
        return (r) => r.readList(element);
      }
      case 'dict': {
        const key = this.compile(type.key), value = this.compile(type.value);
        return (r) => r.readDictionary(key, value);
      }
      case 'pair': {
        const key = this.compile(type.key), value = this.compile(type.value), keyName = this.keyName, valueName = this.valueName;
        return (r) => {
          const items = r.readItems([key, value]);
          return Array.isArray(items) ? { [keyName]: items[0], [valueName]: items[1] } : items;
        };
      }
      case 'tuple': {
        const items = type.items.map((t) => this.compile(t));
        return (r) => r.readItems(items);
      }
      case 'model': { // a class of the models: the entry of its name (none: the value is nil, or has a type id)
        const index = this.schema.indexOf(type.name);
        return index < 0 ? readDynamic : (r) => r.readObject(this.plan(index));
      }
    }
  }

  /** The kinds the formatters write most have their usual format read in place, without the dispatch of value(). */
  private kind(kind: Kind): Read {
    switch (kind) {
      case 'guid': return readGuid;
      case 'string': return readString;
      case 'int': return readInt;
      case 'decimal': {
        const mode = this.options.decimal, convert = this.converter(kind)!;
        return (r) => {
          const bytes = r.bytes, p = r.pos;
          if (bytes[p] === 0xd8 && bytes[p + 1] === 1 && p + 18 <= bytes.length) { // fixext 16 of type 1
            r.pos = p + 18;
            return decodeDecimal(bytes, p + 2, mode);
          }
          return convert(r.value());
        };
      }
      case 'datetime':
        if (this.options.dates === 'iso') return (r) => {
          const v = r.readDate();
          return v instanceof Date ? v.toISOString() : v;
        };
        return (r) => r.readDate();
    }
    const convert = this.converter(kind);
    return convert === undefined ? readDynamic : (r) => convert(r.value());
  }

  /** Converts a value of the kind as it was read (undefined: no conversion). Values in another form are returned as they are. */
  converter(kind: Kind): ((value: unknown) => unknown) | undefined {
    const o = this.options;
    switch (kind) {
      case 'long':
      case 'ulong':
        return o.int64 === 'bigint'
          ? (v) => (typeof v === 'number' ? BigInt(v) : v)
          : (v) => (typeof v === 'bigint' ? Number(v) : v);
      case 'bigint': return (v) => (typeof v === 'number' ? BigInt(v) : v);
      case 'char': return (v) => (typeof v === 'number' ? String.fromCharCode(v) : v);
      case 'decimal': return (v) => (v instanceof MsgPackExtension && v.type === 1 && v.data.length === 16 ? decodeDecimal(v.data, 0, o.decimal) : convertDecimal(v, o.decimal));
      case 'guid': return (v) => (v instanceof Uint8Array && v.length === 16 ? guidString(v) : v);
      case 'datetime': return o.dates === 'iso' ? (v) => (v instanceof Date ? v.toISOString() : v) : undefined;
      case 'dto':
      case 'dto-clock': {
        const clock = kind === 'dto-clock';
        const iso = o.dates === 'iso';
        return (v) => {
          if (v instanceof Date) return iso ? offsetText(v, 0) : v; // a bare timestamp (DateTimeOffsetFormat.Timestamp)
          if (!Array.isArray(v) || v.length !== 2 || !(v[0] instanceof Date) || typeof v[1] !== 'number') return v;
          const moment = clock ? new Date(v[0].getTime() - v[1] * 60000) : v[0];
          return iso ? offsetText(moment, v[1]) : moment;
        };
      }
      case 'timespan': return (v) => (typeof v === 'number' || typeof v === 'bigint' ? timeSpanText(v) : v);
      case 'dateonly': return (v) => (typeof v === 'number' ? dateOnlyText(v) : v);
      case 'timeonly': return (v) => (typeof v === 'number' || typeof v === 'bigint' ? timeOnlyText(v) : v);
    }
    return undefined; // string, bool, int, float, bin, uri, any: as read
  }

  /** The value of a property the writer left out: the default of its type. */
  defaultOf(type: TypeExpr): Default {
    const o = this.options;
    switch (type.k) {
      case 'kind':
        switch (type.kind) {
          case 'bool': return { value: false };
          case 'int':
          case 'float': return { value: 0 };
          case 'long':
          case 'ulong': return { value: o.int64 === 'bigint' ? 0n : 0 };
          case 'bigint': return { value: 0n };
          case 'decimal': return { value: convertDecimal(0, o.decimal) };
          case 'char': return { value: '\0' };
          case 'guid': return { value: EmptyGuid };
          case 'datetime': return o.dates === 'iso' ? { value: new Date(MinDateTimeMs).toISOString() } : { value: null, make: () => new Date(MinDateTimeMs) };
          case 'dto':
          case 'dto-clock': return o.dates === 'iso' ? { value: offsetText(new Date(MinDateTimeMs), 0) } : { value: null, make: () => new Date(MinDateTimeMs) };
          case 'timespan': return { value: '00:00:00' };
          case 'dateonly': return { value: '0001-01-01' };
          case 'timeonly': return { value: '00:00:00' };
        }
        return { value: null };
      case 'object':
        if (type.struct) {
          const entry = type.entry;
          return { value: null, make: () => this.planOf(entry).createComplete() };
        }
        return { value: null };
      case 'pair': {
        const key = this.defaultOf(type.key), value = this.defaultOf(type.value), keyName = this.keyName, valueName = this.valueName;
        return { value: null, make: () => ({ [keyName]: key.make ? key.make() : key.value, [valueName]: value.make ? value.make() : value.value }) };
      }
      case 'model': {
        if (!type.struct) return { value: null };
        const ctor = this.options.models?.byName(type.name)?.ctor;
        return ctor === undefined ? { value: null } : { value: null, make: () => new ctor() };
      }
    }
    return { value: null }; // classes, nullables, collections, tuples
  }

  /** A [DefaultValue] of the schema (as read: decimals raw), converted as the values of the property are. */
  private convertDefault(raw: unknown, type: TypeExpr): Default {
    const kind = type.k === 'nullable' ? type.of : type;
    const convert = kind.k === 'kind' ? this.converter(kind.kind) : undefined;
    const value = convert === undefined ? raw : convert(raw);
    if (value instanceof Date) return { value: null, make: () => new Date(value.getTime()) };
    return { value };
  }
}

const readDynamic: Read = (r) => r.value();

/** A string: fixstr and str8 read in place. */
const readString: Read = (r) => {
  const bytes = r.bytes, p = r.pos, b = bytes[p];
  if (b >= 0xa0 && b <= 0xbf) {
    r.pos = p + 1;
    return r.str(b & 0x1f);
  }
  if (b === 0xd9 && p + 1 < bytes.length) {
    r.pos = p + 2;
    return r.str(bytes[p + 1]);
  }
  return r.value();
};

/** An int (also enums): a positive fixint read in place. */
const readInt: Read = (r) => {
  const b = r.bytes[r.pos];
  if (b <= 0x7f) {
    r.pos++;
    return b;
  }
  return r.value();
};

/** A Guid: bin 16 read in place (no copy of the bytes), any other form as the converter does. */
const readGuid: Read = (r) => {
  const bytes = r.bytes, p = r.pos;
  if (bytes[p] === 0xc4 && bytes[p + 1] === 16 && p + 18 <= bytes.length) {
    r.pos = p + 18;
    return guidString(bytes, p + 2);
  }
  const v = r.value();
  return v instanceof Uint8Array && v.length === 16 ? guidString(v) : v;
};

interface ResolvedOptions {
  decimal: DecimalMode;
  int64: 'number' | 'bigint';
  dates: 'date' | 'iso';
  models?: ModelSet;
  classes?: Record<string, new () => object>;
  propertyNames: PropertyNames;
  generateCode: boolean;
  references: ReferenceHandling;
}

/**
 * The reader of a body: values wrapped with a type id ({ "": index, "@": content }, or { "": index, 0: value, ... }) become what their schema entry describes.
 * References (extension type 5, docs/References.md): the content is the path from the root value to the first occurrence (the index in an array, the number of the entry in a map,
 * as positive integers). With 'preserve' every object and array is kept by the offset of its first byte when it is made, before its values, and the reference is that value;
 * otherwise the bytes at the path are read again (a copy).
 */
export class SchemaReader extends MsgPackReader {
  /** 'preserve': the objects and arrays made, by the offset of their first byte. */
  private readonly made: Map<number, unknown> | null;
  private readonly references: ReferenceHandling;
  /** The element offsets of the arrays and maps a path went through, by the offset of the container: the items found so far, and where the next one starts. */
  private walked?: Map<number, { items: number[]; next: number }>;
  /** The offsets of the targets being read again (a path back into one of them is a cycle, which only 'preserve' resolves). */
  private rereading?: Set<number>;

  /**
   * @param root the offset of the root value: where the paths of references start
   * @param prefix in a value of the body of a stream head: the path of this value in the body's root (the paths start with it; the stream writer's paths start at the body's root value)
   */
  constructor(bytes: Uint8Array, options: ReaderOptions, private readonly plans: Plans | null, pos: number, references: ReferenceHandling = 'copies', private readonly root = pos,
    private readonly prefix?: number[]) {
    super(bytes, options, pos);
    this.references = references;
    this.made = references === 'preserve' ? new Map() : null;
  }

  /** The next value is a reference (an extension of type 5). Reads nothing. */
  private isReference(): boolean {
    const bytes = this.bytes, p = this.pos, b = bytes[p];
    if (this.references === 'none') return false;
    if (b >= 0xd4 && b <= 0xd8) return bytes[p + 1] === 5;
    if (b === 0xc7) return bytes[p + 2] === 5;
    if (b === 0xc8) return bytes[p + 3] === 5;
    if (b === 0xc9) return bytes[p + 5] === 5;
    return false;
  }

  /** The value the reference at the current position points to (its extension is read); `read` reads the target again when it was not made (a copy). */
  private reference(read: Read): unknown {
    const bytes = this.bytes, b = bytes[this.pos], at = this.pos;
    let length: number;
    switch (b) {
      case 0xd4: length = 1; this.pos += 2; break;
      case 0xd5: length = 2; this.pos += 2; break;
      case 0xd6: length = 4; this.pos += 2; break;
      case 0xd7: length = 8; this.pos += 2; break;
      case 0xd8: length = 16; this.pos += 2; break;
      case 0xc7: length = bytes[at + 1]; this.pos += 3; break;
      case 0xc8: this.need(3); length = this.view.getUint16(at + 1); this.pos += 4; break;
      default: this.need(5); length = this.view.getUint32(at + 1); this.pos += 6; break;
    }
    this.need(length);
    const start = this.pos;
    this.pos += length;
    return this.resolve(start, length, at, read);
  }

  protected override ext(type: number, length: number): unknown {
    if (type === 5 && this.references !== 'none') {
      this.need(length);
      const start = this.pos;
      this.pos += length;
      return this.resolve(start, length, start - (length === 1 || length === 2 || length === 4 || length === 8 || length === 16 ? 2 : length <= 0xff ? 3 : length <= 0xffff ? 4 : 6), readDynamic);
    }
    return super.ext(type, length);
  }

  /** The value at the path in bytes[start, start + length), the reference's extension at `at`: the instance made there, or the bytes read again with `read`. */
  private resolve(start: number, length: number, at: number, read: Read): unknown {
    const target = this.offsetOf(this.path(start, length, at), at);
    if (target >= at) throw new MsgPackError('A reference points to a value that does not come before it.', at);
    const made = this.made;
    if (made !== null) {
      const value = made.get(target);
      if (value !== undefined) return value;
    }
    const rereading = this.rereading ??= new Set();
    if (rereading.has(target)) throw new MsgPackError(`A reference points into a value that is being read (a cycle): read with references: 'preserve'.`, at);
    rereading.add(target);
    const end = this.pos;
    this.pos = target;
    try {
      const value = read(this);
      if (made !== null && typeof value === 'object' && value !== null) made.set(target, value); // later references to the same place get this one
      return value;
    } finally {
      this.pos = end;
      rereading.delete(target);
    }
  }

  /** The steps of a path (MsgPack positive integers without an array header), without the prefix of this value. */
  private path(start: number, length: number, at: number): number[] {
    const bytes = this.bytes, view = this.view, end = start + length, steps: number[] = [];
    for (let p = start; p < end;) {
      const b = bytes[p];
      if (b <= 0x7f) { steps.push(b); p++; }
      else if (b === 0xcc && p + 2 <= end) { steps.push(bytes[p + 1]); p += 2; }
      else if (b === 0xcd && p + 3 <= end) { steps.push(view.getUint16(p + 1)); p += 3; }
      else if (b === 0xce && p + 5 <= end) { steps.push(view.getUint32(p + 1)); p += 5; }
      else throw new MsgPackError('A reference path is not a run of positive integers.', p);
    }
    const prefix = this.prefix;
    if (prefix === undefined) return steps;
    for (let t = 0; t < prefix.length; t++)
      if (steps[t] !== prefix[t]) throw new MsgPackError('A reference points into another value of the stream.', at);
    return steps.slice(prefix.length);
  }

  /** The offset of the value at the path from the root value. */
  private offsetOf(steps: number[], at: number): number {
    let offset = this.root;
    for (let t = 0; t < steps.length; t++) offset = this.itemAt(offset, steps[t], at);
    return offset;
  }

  /** The offset of the value of step `step` in the array or map at `offset`: an element, or the value of an entry. */
  private itemAt(offset: number, step: number, at: number): number {
    const bytes = this.bytes, b = bytes[offset];
    let count: number, item: number, header: number;
    if (b >= 0x90 && b <= 0x9f) { count = b & 0x0f; header = 1; item = step; }
    else if (b >= 0x80 && b <= 0x8f) { count = 2 * (b & 0x0f); header = 1; item = 2 * step + 1; }
    else if (b === 0xdc || b === 0xde) { count = this.view.getUint16(offset + 1); header = 3; item = step; }
    else if (b === 0xdd || b === 0xdf) { count = this.view.getUint32(offset + 1); header = 5; item = step; }
    else throw new MsgPackError('A reference path steps into a value that is not an array or a map.', at);
    if (b === 0xde || b === 0xdf) { count *= 2; item = 2 * step + 1; }
    if (item >= count) throw new MsgPackError('A reference path steps beyond the end of an array or map.', at);
    const walked = this.walked ??= new Map();
    let items = walked.get(offset);
    if (items === undefined) walked.set(offset, (items = { items: [], next: offset + header }));
    if (item < items.items.length) return items.items[item];
    const skip = new MsgPackReader(bytes, { decimal: 'raw' }, items.next);
    while (items.items.length <= item) {
      items.items.push(skip.pos);
      skip.skip();
    }
    items.next = skip.pos;
    return items.items[item];
  }

  /** The offset of the header of the array or map whose header of n items was just read (the smallest header that holds n, the only one the writers use). */
  private headerStart(n: number, fix: number, f16: number): number {
    const bytes = this.bytes, p = this.pos;
    if (n < 16 && bytes[p - 1] === (fix | n)) return p - 1;
    return n <= 0xffff && bytes[p - 3] === f16 ? p - 3 : p - 5; // array 16 or map 16, else the 32 bit one
  }

  protected override array(n: number): unknown[] {
    const made = this.made;
    if (made === null) return super.array(n);
    this.count(n);
    const start = this.headerStart(n, 0x90, 0xdc);
    this.enter();
    const result = new Array(n);
    made.set(start, result);
    for (let t = 0; t < n; t++) result[t] = this.value();
    this.depth--;
    return result;
  }

  /** The first key of the map whose header was just read is "" (a type id). */
  private firstKeyIsTypeId(): boolean {
    const k = this.bytes[this.pos];
    return k === 0xa0 || (k === 0xd9 && this.bytes[this.pos + 1] === 0);
  }

  protected override map(n: number): unknown {
    const made = this.made;
    if (this.plans !== null && n > 0 && this.firstKeyIsTypeId()) return this.wrapped(n, made === null ? -1 : this.headerStart(n, 0x80, 0xde));
    if (made === null) return super.map(n);
    this.count(n);
    const start = this.headerStart(n, 0x80, 0xde);
    this.enter();
    const result: Record<string, unknown> = {};
    made.set(start, result);
    for (let t = 0; t < n; t++) {
      const key = this.key();
      setOwn(result, typeof key === 'string' ? key : String(key), this.value());
    }
    this.depth--;
    return result;
  }

  /**
   * A value wrapped with a type id, the header (n entries) is read.
   * @param start the offset of the map ('preserve': a reference to the value points to it), -1 otherwise
   */
  private wrapped(n: number, start: number): unknown {
    this.pos += this.bytes[this.pos] === 0xa0 ? 1 : 2; // the key ""
    const index = this.value();
    if (typeof index !== 'number') throw new MsgPackError('A type id is not a schema index.', this.pos);
    const plan = this.plans!.plan(index);
    this.enter();
    let result: unknown = null;
    if (plan.isObject) {
      const o = plan.createTyped();
      if (start >= 0) this.made!.set(start, o);
      for (let t = 1; t < n; t++) {
        const key = this.value();
        if (key === '@') { // the array layout: { "": index, "@": [values] }
          const count = this.arrayLength();
          if (count >= 0) this.fill(plan, o, count);
          else if (this.isReference()) { // { "": index, "@": <reference> }: a reference to an object of another type than the place declares
            result = this.reference((r) => { // read again (a copy): with its type name, as the value would have (the instance made at the target is returned as it is)
              const copy = r.readObject(plan);
              if (!plan.model && typeof copy === 'object' && copy !== null && !Array.isArray(copy) && plan.entry.name !== null && !(TypeProperty in copy)) (copy as Record<string, unknown>)[TypeProperty] = plan.entry.name;
              return copy;
            });
            if (start >= 0) this.made!.set(start, result); // not the object made above
            this.depth--;
            return result;
          } else this.value();
        } else
          this.setByKey(plan, o, key);
      }
      result = plan.complete(o);
    } else {
      for (let t = 1; t < n; t++) {
        const key = this.value();
        if (key === '@') {
          result = plan.content!(this);
          if (start >= 0 && typeof result === 'object' && result !== null) this.made!.set(start, result);
        } else this.value(); // a property of a collection ([SerializeEnumerable(SerializeProperties)]): not kept, a JS array has no place for it
      }
    }
    this.depth--;
    return result;
  }

  /** An object of the plan: an array of its values (the array layout), a map from property index to value, or a value with a type id. */
  readObject(plan: Plan): unknown {
    const b = this.peek(), start = this.pos;
    if (b === 0xc0) {
      this.pos++;
      return null;
    }
    let n = this.arrayLength();
    if (n >= 0) {
      this.enter();
      const readArray = plan.readArrayFn;
      if (readArray !== undefined) {
        const o = readArray(this, n);
        this.depth--;
        return o;
      }
      const o = plan.create();
      if (this.made !== null) this.made.set(start, o);
      this.fill(plan, o, n);
      this.depth--;
      return plan.complete(o);
    }
    n = this.mapLength();
    if (n < 0) return this.notAnObject(plan); // not an object: as it is (or a reference)
    if (n > 0 && this.firstKeyIsTypeId()) return this.wrapped(n, this.made === null ? -1 : start);
    this.enter();
    const o = plan.create();
    if (this.made !== null) this.made.set(start, o);
    for (let t = 0; t < n; t++) this.setByKey(plan, o, this.value());
    this.depth--;
    return plan.complete(o);
  }

  /** A value where an object of the plan is declared that is neither an array nor a map: a reference to one, or any other value as it is. Out of readObject, which stays small. */
  private notAnObject(plan: Plan): unknown {
    return this.isReference() ? this.reference((r) => r.readObject(plan)) : this.value();
  }

  /**
   * Values by position, values beyond the entry are skipped. A nil is the value the writer left out (Plan.nilValue: the object made with the defaults has it, an instance of a class gets it),
   * a value missing after the end of the array keeps what create made (for a class: what its constructor sets).
   */
  private fill(plan: Plan, o: Record<string, unknown>, n: number): void {
    if (plan.fillFn !== undefined) return plan.fillFn(this, o, n);
    const names = plan.names, reads = plan.reads, count = names.length, bytes = this.bytes;
    for (let k = 0; k < n; k++) {
      if (bytes[this.pos] === 0xc0) {
        this.pos++;
        if (plan.cls !== undefined && k < count) setOwn(o, names[k], plan.nilValue(k));
        continue;
      }
      if (k < count) setOwn(o, names[k], reads[k](this));
      else this.value();
    }
  }

  /** ObjectLayout.Map: the key is the index of the property in the entry (or its name). */
  private setByKey(plan: Plan, o: Record<string, unknown>, key: unknown): void {
    const index = typeof key === 'number' ? key : typeof key === 'string' ? plan.indexOfWriterName(key) : -1;
    if (index >= 0 && index < plan.names.length) setOwn(o, plan.names[index], plan.reads[index](this));
    else this.value();
  }

  readList(element: Read): unknown {
    const b = this.peek(), start = this.pos;
    if (b === 0xc0) {
      this.pos++;
      return null;
    }
    const n = this.arrayLength();
    if (n < 0) return this.isReference() ? this.reference((r) => r.readList(element)) : this.value(); // wrapped with a type id, or not a list
    this.enter();
    const result = new Array(n);
    if (this.made !== null) this.made.set(start, result);
    for (let t = 0; t < n; t++) result[t] = element(this);
    this.depth--;
    return result;
  }

  readDictionary(key: Read, value: Read): unknown {
    const b = this.peek(), start = this.pos;
    if (b === 0xc0) {
      this.pos++;
      return null;
    }
    if (this.startsWithTypeId()) return this.value();
    const n = this.mapLength();
    if (n < 0) return this.isReference() ? this.reference((r) => r.readDictionary(key, value)) : this.value();
    this.enter();
    const result: Record<string, unknown> = {};
    if (this.made !== null) this.made.set(start, result);
    for (let t = 0; t < n; t++) {
      const k = key(this);
      setOwn(result, typeof k === 'string' ? k : String(k), value(this));
    }
    this.depth--;
    return result;
  }

  /** An array of items of the given types (pairs, tuples), extra items as they are. */
  readItems(items: Read[]): unknown {
    const b = this.peek();
    if (b === 0xc0) {
      this.pos++;
      return null;
    }
    const n = this.arrayLength();
    if (n < 0) return this.value();
    this.enter();
    const result = new Array(n);
    for (let t = 0; t < n; t++) result[t] = t < items.length ? items[t](this) : this.value();
    this.depth--;
    return result;
  }

  /** A DateTime: the timestamps of 4 and 8 bytes read in place. */
  readDate(): unknown {
    const bytes = this.bytes, p = this.pos, b = bytes[p];
    if (bytes[p + 1] === 0xff && (b === 0xd6 || b === 0xd7)) {
      const length = b === 0xd6 ? 4 : 8;
      if (p + 2 + length <= bytes.length) {
        this.pos = p + 2;
        return this.timestamp(length);
      }
    }
    return this.value();
  }

  readRoot(): unknown {
    return this.plans === null ? this.value() : this.plans.root(this);
  }
}

/** The models' TypeScript types assume the options they were generated with. */
function checkModelOptions(models: ModelSet, o: ResolvedOptions): void {
  const differs = (name: string, model: unknown, actual: unknown) => {
    if (model !== undefined && model !== actual)
      throw new Error(`The models (${models.generator ?? 'lsmsgpack-ts'}) were generated for ${name}: '${model}', the client reads with '${typeof actual === 'function' ? 'a function' : actual}'. Generate them with the same option, or leave it out here.`);
  };
  differs('int64', models.int64, o.int64);
  differs('dates', models.dates, o.dates);
  if (typeof o.decimal !== 'function') differs('decimal', models.decimal, o.decimal); // a function of the text: the user's own type
  differs('propertyNames', models.propertyNames, o.propertyNames); // the field names of the classes
}

/** What decoding a payload found. */
export interface Decoded {
  value: unknown;
  /** The schema of the payload (inline or referred to), undefined without one. */
  schema?: Schema;
  /** The body referred to its schema instead of containing it. */
  reference: boolean;
}

/** Decodes payloads with the given options (one per client). Thread-safe in the sense of JavaScript: no state between calls except the plans per schema. */
export class Decoder {
  private readonly options: ResolvedOptions;
  private readonly readerOptions: ReaderOptions;
  private readonly indexedSchema: boolean;
  private readonly plans = new WeakMap<Schema, Plans>();

  constructor(options: DecodeOptions = {}) {
    const models = options.models;
    this.options = {
      decimal: options.decimal ?? models?.decimal ?? 'number',
      int64: options.int64 ?? models?.int64 ?? 'number',
      dates: options.dates ?? models?.dates ?? 'date',
      models,
      classes: options.classes,
      propertyNames: options.propertyNames ?? models?.propertyNames ?? 'camelCase',
      generateCode: options.generateCode ?? true,
      references: options.references ?? 'copies',
    };
    if (models !== undefined) checkModelOptions(models, this.options);
    this.readerOptions = { decimal: this.options.decimal, maxDepth: options.maxDepth };
    this.indexedSchema = options.indexedSchema ?? true;
  }

  /**
   * Decodes application/x-lsmsgpack data: the schema (or a reference to it) followed by the body.
   * @param lookup finds a schema the data refers to by its id
   * @param inline makes the schema of the inline bytes (e.g. the known instance when the bytes are the same), default: parses them
   */
  decode(bytes: Uint8Array, lookup?: (id: string) => Schema | undefined, inline?: (schemaBytes: Uint8Array) => Schema): Decoded {
    if (bytes.length === 0) return { value: undefined, reference: false };
    const decoded = this.decodeAt(bytes, 0, bytes.length, lookup, inline);
    if (decoded.end !== bytes.length) throw new MsgPackError('Data after the value.', decoded.end);
    return { value: decoded.value, schema: decoded.schema, reference: decoded.reference };
  }

  /**
   * decode for the payload that starts at `start` in bytes[0, end) (internal: the stream reader): returns where it ends instead of refusing data after it.
   * @param type the declared type of the body (default: the root of the schema)
   * @internal
   */
  decodeAt(bytes: Uint8Array, start: number, end: number, lookup?: (id: string) => Schema | undefined, inline?: (schemaBytes: Uint8Array) => Schema, type?: TypeExpr): Decoded & { end: number } {
    if (end > bytes.length) end = bytes.length;
    if (start >= end) throw new EndOfDataError(start);
    const data = end === bytes.length ? bytes : bytes.subarray(0, end);
    const b = data[start];
    if (!this.indexedSchema || b === 0xc0) { // null is written without a schema
      const read = this.readValue(data, start, end, null);
      return { value: read.value, reference: false, end: read.end };
    }

    if (b === 0xd8 && data[start + 1] === 2) { // a reference: fixext 16 of type 2 with the id
      if (end - start < 18) throw new EndOfDataError(end);
      const id = toHex(data, start + 2, start + 18);
      const schema = lookup?.(id);
      if (schema === undefined) throw new MissingSchemaError(id);
      const read = this.readValue(data, start + 18, end, schema, type);
      return { value: read.value, schema, reference: true, end: read.end };
    }

    if (!((b >= 0x80 && b <= 0x8f) || b === 0xde || b === 0xdf))
      throw new MsgPackError(`The data does not start with a schema (0x${b.toString(16)}), set indexedSchema to false for data without it.`, start);
    const skip = new MsgPackReader(data, { decimal: 'raw' }, start);
    skip.skip();
    const schemaBytes = data.subarray(start, skip.pos);
    const schema = inline ? inline(schemaBytes) : new Schema(schemaBytes.slice());
    const read = this.readValue(data, skip.pos, end, schema, type);
    return { value: read.value, schema, reference: false, end: read.end };
  }

  /** Plain MsgPack (application/msgpack): any value, maps as objects. */
  decodePlain(bytes: Uint8Array): unknown {
    if (bytes.length === 0) return undefined;
    return this.read(bytes, null, 0);
  }

  private read(bytes: Uint8Array, schema: Schema | null, pos: number): unknown {
    const reader = new SchemaReader(bytes, this.readerOptions, schema === null ? null : this.plansOf(schema), pos, this.options.references);
    const value = reader.readRoot();
    if (reader.pos !== bytes.length) throw new MsgPackError('Data after the value.', reader.pos);
    return value;
  }

  /**
   * One value at `start` in bytes[0, end) (internal: the stream reader): with the plans of the schema as the declared type says (undefined: the root of the schema,
   * dynamic without one; values with a type id become their entry's objects), or plain (schema null). Returns where it ends.
   * @param prefix the path of the value in the root value of a stream head's body (the paths of its references start with it)
   * @internal
   */
  readValue(bytes: Uint8Array, start: number, end: number, schema: Schema | null, type?: TypeExpr, prefix?: number[]): { value: unknown; end: number } {
    const data = end >= bytes.length ? bytes : bytes.subarray(0, end);
    const plans = schema === null ? null : this.plansOf(schema);
    const reader = new SchemaReader(data, this.readerOptions, plans, start, this.options.references, start, prefix);
    const value = plans === null ? reader.value() : type === undefined ? reader.readRoot() : plans.readOf(type)(reader);
    return { value, end: reader.pos };
  }

  /** The data starts with the indexed schema (internal: the stream reader). @internal */
  get usesIndexedSchema(): boolean {
    return this.indexedSchema;
  }

  /** The models (internal: the stream reader's declared types). @internal */
  get models(): ModelSet | undefined {
    return this.options.models;
  }

  /** The model of a class (internal: the stream reader's declared types). @internal */
  modelOf(ctor: Function): string | undefined {
    return this.options.models?.byConstructor(ctor)?.typeName;
  }

  private plansOf(schema: Schema): Plans {
    let plans = this.plans.get(schema);
    if (plans === undefined) {
      plans = new Plans(schema, this.options);
      this.plans.set(schema, plans);
    }
    return plans;
  }
}
