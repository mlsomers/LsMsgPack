// Generated models (lsmsgpack-ts, docs/TypeScriptModels.md): classes whose fields start with the values the .NET constructors set, and what defineModels gets for each:
// the type name (the server's type id, also the name of its schema entry) and the declared type of every property, in the form of the described schema with
// ['class', typeName] / ['struct', typeName] for classes. The decoder creates the classes for their entries (new: every object gets its own initial values),
// the encoder writes their properties as declared and the type id only where the declared type is not the class.

import { AnyType, parseType, TypeExpr } from './schema.js';
import { TypeProperty } from './decoder.js';

/** A declared type as the generator writes it. */
export type ModelType = string | readonly unknown[];

/** A class of the models: [constructor, type name, [[property, declared type], ...]]. */
export type ModelClassDefinition = readonly [new () => object, string, readonly (readonly [string, ModelType])[]];

/** What the generated code passes to defineModels. */
export interface ModelsDefinition {
  /** The generator and its version, for messages. */
  generator?: string;
  /** How the models were generated to be read (their TypeScript types depend on it): the client must read with the same. */
  int64?: 'number' | 'bigint';
  dates?: 'date' | 'iso';
  decimal?: 'number' | 'string';
  classes: readonly ModelClassDefinition[];
}

export interface ModelProperty {
  readonly name: string;
  readonly type: TypeExpr;
}

export interface ModelClass {
  readonly ctor: new () => object;
  /** The type id of the server (short or full type name). */
  readonly typeName: string;
  /** The serialized properties in the server's order (inherited ones too). */
  readonly properties: readonly ModelProperty[];
  readonly byName: ReadonlyMap<string, TypeExpr>;
  /** Extends another class (its .NET base class): where the declared type is unknown (the root of a request body), the encoder writes its type id. */
  readonly derived: boolean;
}

/** The models of one generated file (defineModels), for LsMsgPackClient({ models }) or the decoder and encoder. */
export class ModelSet {
  readonly generator?: string;
  readonly int64?: 'number' | 'bigint';
  readonly dates?: 'date' | 'iso';
  readonly decimal?: 'number' | 'string';
  private readonly names = new Map<string, ModelClass>();
  private readonly ctors = new Map<Function, ModelClass>();

  constructor(definition: ModelsDefinition) {
    this.generator = definition.generator;
    this.int64 = definition.int64;
    this.dates = definition.dates;
    this.decimal = definition.decimal;
    const classes = definition.classes ?? [];
    for (const [ctor, typeName, props] of classes) {
      if (typeof ctor !== 'function' || typeof typeName !== 'string') throw new Error('defineModels: a class is [constructor, type name, properties].');
      const properties = (props ?? []).map(([name, type]) => ({ name, type: parseModelType(type) }));
      const derived = Object.getPrototypeOf(ctor) !== Function.prototype; // generated classes only extend their .NET base class (abstract ones are not in the list)
      const model: ModelClass = { ctor, typeName, properties, byName: new Map(properties.map((p) => [p.name, p.type])), derived };
      this.names.set(typeName, model);
      this.ctors.set(ctor, model);
      // The type name on the prototype: every instance has it, none pays for it (and JSON.stringify leaves it out). Writable: assigning it on an instance shadows it.
      Object.defineProperty(ctor.prototype, TypeProperty, { value: typeName, writable: true, enumerable: false, configurable: true });
    }
  }

  /** The model of a type name of the server (a schema entry, a type id). */
  byName(typeName: string): ModelClass | undefined {
    return this.names.get(typeName);
  }

  /** The model of an instance (its constructor), for writing. */
  of(value: object): ModelClass | undefined {
    return this.ctors.get(value.constructor);
  }

  get size(): number {
    return this.names.size;
  }
}

/** Registers the generated classes (called by the generated models file). */
export function defineModels(definition: ModelsDefinition): ModelSet {
  return new ModelSet(definition);
}

/** A declared type of the models: the described schema's form, classes by their type name. */
function parseModelType(value: unknown): TypeExpr {
  if (Array.isArray(value) && (value[0] === 'class' || value[0] === 'struct') && typeof value[1] === 'string')
    return { k: 'model', name: value[1], struct: value[0] === 'struct' };
  if (Array.isArray(value) && typeof value[0] === 'string') {
    switch (value[0]) {
      case '?': return { k: 'nullable', of: parseModelType(value[1]) };
      case 'list': return { k: 'list', of: parseModelType(value[1]) };
      case 'dict': return { k: 'dict', key: parseModelType(value[1]), value: parseModelType(value[2]) };
      case 'pair': return { k: 'pair', key: parseModelType(value[1]), value: parseModelType(value[2]) };
      case 'tuple': return { k: 'tuple', items: value.slice(1).map(parseModelType) };
    }
    return AnyType;
  }
  return parseType(value);
}
