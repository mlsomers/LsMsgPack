// Reads application/x-lsmsgpack payloads (the schema, or a reference to it, followed by the body) into complete objects:
// every property of the schema entry is set, the ones the writer left out (nil) get their default (of the schema or the type), the ones missing after the end of an array
// keep what the class sets (plain objects: the default).
// An object that came with a type id gets its type name as the property $type (as System.Text.Json names it): the encoder writes it back as the type id.

import { MissingSchemaError, MsgPackError } from './errors.js';
import { MsgPackExtension, MsgPackReader, ReaderOptions } from './reader.js';
import type { ModelSet } from './models.js';
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
}

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

  /** Makes what create and fill use, once the properties are known: the template, and with generateCode their code. */
  prepare(generateCode: boolean): void {
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
    this.readArrayFn = this.generateReadArray();
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
    plan.prepare(this.options.generateCode);
    return plan;
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
}

/** The reader of a body: values wrapped with a type id ({ "": index, "@": content }, or { "": index, 0: value, ... }) become what their schema entry describes. */
export class SchemaReader extends MsgPackReader {
  constructor(bytes: Uint8Array, options: ReaderOptions, private readonly plans: Plans | null, pos: number) {
    super(bytes, options, pos);
  }

  /** The first key of the map whose header was just read is "" (a type id). */
  private firstKeyIsTypeId(): boolean {
    const k = this.bytes[this.pos];
    return k === 0xa0 || (k === 0xd9 && this.bytes[this.pos + 1] === 0);
  }

  protected override map(n: number): unknown {
    if (this.plans !== null && n > 0 && this.firstKeyIsTypeId()) return this.wrapped(n);
    return super.map(n);
  }

  /** A value wrapped with a type id, the header (n entries) is read. */
  private wrapped(n: number): unknown {
    this.pos += this.bytes[this.pos] === 0xa0 ? 1 : 2; // the key ""
    const index = this.value();
    if (typeof index !== 'number') throw new MsgPackError('A type id is not a schema index.', this.pos);
    const plan = this.plans!.plan(index);
    this.enter();
    let result: unknown = null;
    if (plan.isObject) {
      const o = plan.createTyped();
      for (let t = 1; t < n; t++) {
        const key = this.value();
        if (key === '@') { // the array layout: { "": index, "@": [values] }
          const count = this.arrayLength();
          if (count >= 0) this.fill(plan, o, count);
          else this.value();
        } else
          this.setByKey(plan, o, key);
      }
      result = plan.complete(o);
    } else {
      for (let t = 1; t < n; t++) {
        const key = this.value();
        if (key === '@') result = plan.content!(this);
        else this.value(); // a property of a collection ([SerializeEnumerable(SerializeProperties)]): not kept, a JS array has no place for it
      }
    }
    this.depth--;
    return result;
  }

  /** An object of the plan: an array of its values (the array layout), a map from property index to value, or a value with a type id. */
  readObject(plan: Plan): unknown {
    const b = this.peek();
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
      this.fill(plan, o, n);
      this.depth--;
      return plan.complete(o);
    }
    n = this.mapLength();
    if (n < 0) return this.value(); // not an object: as it is
    if (n > 0 && this.firstKeyIsTypeId()) return this.wrapped(n);
    this.enter();
    const o = plan.create();
    for (let t = 0; t < n; t++) this.setByKey(plan, o, this.value());
    this.depth--;
    return plan.complete(o);
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
    const b = this.peek();
    if (b === 0xc0) {
      this.pos++;
      return null;
    }
    const n = this.arrayLength();
    if (n < 0) return this.value(); // wrapped with a type id, or not a list
    this.enter();
    const result = new Array(n);
    for (let t = 0; t < n; t++) result[t] = element(this);
    this.depth--;
    return result;
  }

  readDictionary(key: Read, value: Read): unknown {
    const b = this.peek();
    if (b === 0xc0) {
      this.pos++;
      return null;
    }
    if (this.startsWithTypeId()) return this.value();
    const n = this.mapLength();
    if (n < 0) return this.value();
    this.enter();
    const result: Record<string, unknown> = {};
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
    if (!this.indexedSchema || bytes[0] === 0xc0) // null is written without a schema
      return { value: this.read(bytes, null, 0), reference: false };

    if (bytes[0] === 0xd8 && bytes[1] === 2) { // a reference: fixext 16 of type 2 with the id
      if (bytes.length < 18) throw new MsgPackError('Unexpected end of data.', bytes.length);
      const id = toHex(bytes, 2, 18);
      const schema = lookup?.(id);
      if (schema === undefined) throw new MissingSchemaError(id);
      return { value: this.read(bytes, schema, 18), schema, reference: true };
    }

    const b = bytes[0];
    if (!((b >= 0x80 && b <= 0x8f) || b === 0xde || b === 0xdf))
      throw new MsgPackError(`The data does not start with a schema (0x${b.toString(16)}), set indexedSchema to false for data without it.`, 0);
    const skip = new MsgPackReader(bytes, { decimal: 'raw' });
    skip.skip();
    const schemaBytes = bytes.subarray(0, skip.pos);
    const schema = inline ? inline(schemaBytes) : new Schema(schemaBytes);
    return { value: this.read(bytes, schema, skip.pos), schema, reference: false };
  }

  /** Plain MsgPack (application/msgpack): any value, maps as objects. */
  decodePlain(bytes: Uint8Array): unknown {
    if (bytes.length === 0) return undefined;
    return this.read(bytes, null, 0);
  }

  private read(bytes: Uint8Array, schema: Schema | null, pos: number): unknown {
    const reader = new SchemaReader(bytes, this.readerOptions, schema === null ? null : this.plansOf(schema), pos);
    const value = reader.readRoot();
    if (reader.pos !== bytes.length) throw new MsgPackError('Data after the value.', reader.pos);
    return value;
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
