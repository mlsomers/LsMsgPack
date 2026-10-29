# TypeScript models and schemas (lsmsgpack-ts)

`lsmsgpack-ts` writes TypeScript for the .NET classes a server sends and receives with the [web formatters](WebFormatters.md):

- **Models**: a class per .NET class and struct, an interface per .NET interface, a const object and type per enum. The fields start with the values the .NET constructors set, so an object the client reads has what a .NET client would have (also for properties the server left out).
- **A schema set**: the schemas the server will write for its response types, so the client holds them before its first request ("hot-loaded"). The first response of each type then refers to its schema (18 bytes) instead of carrying it.

The TypeScript client (`@lsmsgpack/client`, in `clients/typescript`) uses both. It also works without them: the models are an optional layer for type safety, the constructor values and speed.

It reads the compiled assemblies (the build output of the server or its models) and runs the server's own code to make the schemas, so the bytes are the server's. It only describes models, never endpoints: the protocol sits underneath an existing API, next to whatever describes it (OpenAPI, Swagger).

## Usage

```bash
dotnet tool install -g LsMsgPack.TypeScript

lsmsgpack-ts --assembly Shop.Api/bin/Release/net8.0/Shop.Api.dll --solution Shop.sln --out web/src/api/models.ts
```

This writes `models.ts` and `models.schemas.ts`. Run it again when the classes change (e.g. as a build step of the front end).

Which classes (at least one):

| Argument | |
|---|---|
| `--root <type>` | A type the server responds with, e.g. `Shop.Invoice[]` or `List<Shop.Invoice>` (C# notation, short names when unique). Repeatable. |
| `--request <type>` | A type the server reads (a request body): a model, no schema. Repeatable. |
| `[LtMsgPackSerializable(typeof(T))]` | The types of the contexts of LtMsgPack's [source generator](SourceGenerator.md) in the assemblies are roots too. |
| `--solution <sln\|csproj>` | Finds the roots as the [analyzers](Analyzers.md) do: the results (`Task<ActionResult<T>>` is `T`, `IAsyncEnumerable<T>` a `List<T>` as the formatters write it) and parameters of controller actions in projects that reference a formatter, the values of `MsgPackResults.MsgPack(value)` and the bodies (`MsgPackBody<T>`) of minimal APIs, and the types handed to the serializers. The project must be built: the types are taken from `--assembly`. |
| `--all-types` | Every public class, struct and enum of the assemblies (models only). |

Options:

| Argument | |
|---|---|
| `--out <file.ts>` | The models. |
| `--schemas <file.ts>` | The schema set, by default next to `--out` with `.schemas.ts`. `--no-schemas`: none. |
| `--int64 number\|bigint`, `--dates date\|iso`, `--decimal number\|string` | How the client reads `long`, `DateTime`/`DateTimeOffset` and `decimal` (the TypeScript types depend on it). The client checks that it reads with the same options. |
| `--property-names camelCase\|dotnet` | The field names: camelCase as System.Text.Json's `JsonNamingPolicy.CamelCase` writes them (default: `invoiceDate`, `id` for `ID`; also `key`/`value` of a `KeyValuePair`), or the names of the .NET properties. `defineModels` gets `propertyNames`, the client reads with the same and writes request bodies with the .NET names. Two properties of a class with the same camelCase name (`Id` and `ID`) are refused. |
| `--options <Type.Member>` | A static property, field or method of the assemblies that returns the server's `LtMsgPackHttpOptions` (or the `LtMsgPackOptions` of `application/x-lsmsgpack`). The static filters, `PropertyOrder` and `AddTypeIdOptions` decide which properties the models have and what the schemas are. Default: the defaults of the formatters. |
| `--import <module>` | Where the models import `defineModels` from (default `@lsmsgpack/client`). |

Warnings go to the standard error: e.g. a constructor that sets another value each time (`DateTime.Now`, `Guid.NewGuid()`), whose field gets the default of its type.

## The models

```ts
/** .NET Shop.InvoiceStatus */
export const InvoiceStatus = { Draft: 0, Sent: 1, Paid: 2 } as const;
export type InvoiceStatus = (typeof InvoiceStatus)[keyof typeof InvoiceStatus];

/** .NET Shop.Invoice */
export class Invoice {
  declare readonly $type: 'Invoice';
  id: string = '00000000-0000-0000-0000-000000000000';
  due: Date = new Date(-62135596800000);
  retries: number = 3;                                    // public int Retries { get; set; } = 3;
  status: InvoiceStatus = InvoiceStatus.Draft;
  lines: InvoiceLine[] | null = null;
  home: Address | null = Object.assign(new Address(), { city: 'Utrecht' });
}

export abstract class Animal {
  declare readonly $type: 'Cat' | 'Dog';
  name: string | null = null;
}

export class Dog extends Animal {
  declare readonly $type: 'Dog';
  barks: number = 0;
}

export const models = defineModels({ int64: 'number', dates: 'date', decimal: 'number', propertyNames: 'camelCase', classes: [
  [Invoice, 'Invoice', [['Id', 'guid'], ['Due', 'datetime'], /* ... */]],
  /* ... */
] });
```

- **Properties**: the ones the server writes (its static filters: settable or taken by a constructor, not ignored, not static), camelCase by default (`--property-names`); `defineModels` has the .NET names.
- **Types**: as the client reads them, the same rules as System.Text.Json except dates (`Date`) and bytes (`Uint8Array`): see the [client's table of values](TypeScriptClient.md#values). Reference types are `| null`. With nullable reference types enabled in the .NET project, the annotations decide (`string?` or `string`); without them a property can be null, the elements of a collection and values of a dictionary are taken as not null.
- **Initial values**: what the constructor sets (the class is created as the serializer creates it: its parameterless constructor, otherwise uninitialized), as TypeScript. Nested objects are `new X()` with the values that differ from what `X` sets. A value the constructor sets differently each time gets the default of its type, with a warning.
- **`$type`**: the type name the server writes as the type id (the short name, or the full name with `AddTypeIdOption.FullName`). It is not a field: `defineModels` puts it on the prototype, so it costs nothing per object and `JSON.stringify` leaves it out. A base class declares the names of its classes, so `switch (animal.$type)` narrows.
- **Inheritance**: `extends`, base classes first. A derived class that sets an inherited property in its constructor overrides the field.
- **`defineModels`**: the type name and the declared type of every property (in the form of the [described schema](schema.md#version-2-the-declared-types), classes by their type name). The client uses it to read payloads whose schema has only names (a server without `DescribeSchemas`), and to write request bodies (the kinds, and `$type` only where the declared type is not the class).
- Structs are classes as well (their default is an object, `new Point()`), interfaces are TypeScript interfaces with their properties optional.

## The schema set

```ts
export const schemas: Readonly<Record<string, string>> = {
  // 1767e9b3b699edb24ddcc25156ea66d6, 696 bytes
  'Shop.Invoice[]': 'hadJbnZvaWNl...',
};
```

The schemas (version 2, as the client asks for them) of the response types, base64. The client registers them and lists their ids in `MsgPack-Schemas`, so the server refers to them from the first response on. A page served by another origin than the API's then needs CORS to allow that request header, see [TypeScriptClient.md](TypeScriptClient.md#the-server).

This works because the server makes **complete schemas** (`LtMsgPackHttpOptions.CompleteSchemas`, on by default, see [WebFormatters.md](WebFormatters.md#complete-schemas)): the schema of a response type has every class its values can hold, in a fixed order, so its id depends on the classes and the options only, not on the data. `lsmsgpack-ts` makes them with the same code (`LtMsgPackHttpSerializer.PrepareSchema`).

When they differ anyway, nothing breaks: the response carries its schema inline, and the client keeps it as it keeps any other. That happens when:

- the classes or the server's options changed since the set was generated (generate it again);
- a value has a class the complete schema does not have: a value in a property declared as `object`, a class of an assembly the root type does not reach, a collection in a property declared as an interface (`IEnumerable<Address>` holding a `List<Address>` gets a type id). The schema grows, once per server process.

## How it relates to the other generators

- LtMsgPack's [source generator](SourceGenerator.md) writes C# for the serializer (speed, NativeAOT). `lsmsgpack-ts` writes TypeScript for clients. They share the roots (`[LtMsgPackSerializable]`).
- The [analyzers](Analyzers.md) find the same roots in source code (`--solution` uses their code), and warn where a class's constructor values and `[DefaultValue]` disagree (which matters for JavaScript clients without the models).
