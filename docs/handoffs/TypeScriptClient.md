# Handoff: the TypeScript client, what is left

Status (October 2026): the client is implemented and committed (9c8df0a), see [docs/TypeScriptClient.md](../TypeScriptClient.md) and `CLAUDE.md` ("TypeScript client"). This handoff lists what it does not do yet, with what was found while building it. The first version of this handoff, the assessment that led to it, is in the git history (315c9a8); its measurement scripts are still in [typescript-client/](typescript-client/).

## Where things are

- `clients/typescript/`: the package `@lsmsgpack/client` (`"private": true` until the npm name is settled). `src/decoder.ts` (plans per schema entry, defaults, type ids), `src/schema.ts` (plain and described schema), `src/store.ts`, `src/client.ts` (fetch wrapper), `src/writer.ts` (plain MsgPack request bodies).
- `clients/typescript/test-server/`: ASP.NET Core with the formatters on 127.0.0.1. `/expected/...` returns System.Text.Json of the same values (the oracle for complete objects), `/echo/...` reads a request body and answers it as JSON. `test/server.test.mjs` runs against it.
- Server side: `LtMsgPack/Http/DescribedSchema.cs` writes the described schema from the frozen resolver of the writer (`SchemaStore.GetWriterResolver`, recorded only when `SchemaStore.RecordWriters` is set by `LtMsgPackHttpSerializer`). Its format is specified in [docs/TypeScriptClient.md#format](../TypeScriptClient.md#format).
- Tests: `npm test` in `clients/typescript` (the live tests need `dotnet build clients/typescript/test-server`), `LsMsgPackFormattersTests/DescribedSchemaTests.cs`. Performance: `npm run bench`, and the `lsmsgpack-perf` skill for anything that touches the server.

## 1. Request bodies with type ids (polymorphic requests)

**Now:** request bodies are plain MsgPack (`application/msgpack`): maps keyed by property names, no type ids. The server reads them with its `Plain` options. A property declared as an abstract class or interface cannot be filled from such a body (the server does not know which class to create), so a client cannot send e.g. a `Zoo` with `Dog` and `Cat` in `List<Animal>`.

**What it needs:** writing `application/x-lsmsgpack` bodies: the schema inline (always, by design: see WebFormatters.md, "Request bodies always carry their schema inline"), then the body with type ids where the class differs from the declared type.

- **The class of each object.** JavaScript objects do not have one. Options: the `classes` option in reverse (constructor → .NET name), a property the caller sets (`$type`?), or the entry an object was read from (the decoder could remember it in a `WeakMap` per object, so an object that was read, changed and sent back keeps its class).
- **The declared types.** The client does not know them for a request (a described schema is per root type of a *response*). Simplest: write a type id on every object that has a class (as `AddTypeIdOption.Always` does for objects). The readers accept a type id wherever one is, and check that the class is assignable to the declared type (`TypeResolver.Resolve`, docs/security.md).
- **The schema.** Build it while writing, as LtMsgPack's `InlineSchema` does: an entry when a class is first written, its property names in the order the client writes them (the readers match positions by the names of the payload's schema, any order reads). Type ids are entry indexes. Arrays of values (the default layout) or maps from index to value: both are read.
- **Names.** Type names are short by default: the server resolves them among the assemblies its root type reaches (`TypeResolver.CacheReachableAssemblies`), a class in another assembly needs `MsgPackTypes.CacheAssemblyTypes` on the server. The names the client sends must be the server's `ComplexTypeDef.TypeName` (the entry names of a described schema it received are exactly those).
- **Values.** Guid as a string, decimals as numbers and dates as timestamps are converted by the server (`ValueConverter`). This is already tested for plain bodies (`request bodies: plain MsgPack the server reads`).
- **Test:** add a polymorphic `/echo/zoo` to the test server and compare its JSON with what was sent.

Open decision for the maintainer: how a caller names the class of an object (the three options above, or several of them).

## 2. TypeScript types

**Now:** `api.get<Invoice>(url)` trusts the type the caller names. The caller writes the interfaces by hand.

**What exists:** a described schema has what a generator needs for the types it contains: entry names, property names in order, kinds, nullables, lists, dictionaries, structs, and which entry a property holds. Kinds map to TypeScript per decode options: `long` is `number` or `bigint` (`int64`), `decimal` is `number` or `string` (`decimal`), `datetime` is `Date` or `string` (`dates`).

**Limits of generating from the schema:**

- A schema only has the classes the server wrote so far for that root type, so a class never sent is missing. Call a few endpoints, or keep the store (`SchemaStore.export()`) and generate from all the schemas in it, merging entries by name. Two classes with the same short name are separate entries with the same key.
- **Inheritance is not in the schema.** A property declared as `Animal` is `any`. To generate `Dog | Cat` (or a base interface) the described schema would need the base classes of each entry (a format addition: e.g. an item `["^", baseName...]`), or the generator reads the .NET assembly instead.

**Two ways:**

1. A small CLI in the package (`npx lsmsgpack-ts types <url or exported store> > api.d.ts`). No build integration, and it only knows what the server sent.
2. A `dotnet` tool that reflects over the model assembly (or Roslyn, like `LtMsgPack.Generator`'s `TypeWalker`; a source generator cannot write `.ts` files). It knows every class and the inheritance, and can also write the runtime descriptors the decoder would otherwise get from the schema.

Recommendation: 1 first (cheap, uses what is there), 2 when users ask for unions of derived classes.

## 3. The explorers and the MCP server do not understand described schemas

**Now:** a captured response with a described schema (e.g. from the browser's network tab) is shown as two values: the schema as a map of arrays, then the body, with the error "The type id of [0] is a fixarray: type ids are names, or indexes of the schema" (`ObjectDebugger/PayloadValidation.cs`). Checked with `msgpack_decode` of the MCP server.

**Where:**

- `ObjectDebugger/RootObject.cs`, `ReadSchemaTypes`: returns null (not a schema) as soon as an item is not a string or nil. It should take an array item `[name, type, default?]` by its first element, skip `["@", type]` items, and leave out the last entry when its name is `""` (the root).
- `SchemaType` (`ObjectDebugger/SchemaInfo.cs`) could keep the kinds and defaults. Then `ObjectReconstructor` does not need to guess what the described schema tells:
  - the class of a nested value;
  - object or list;
  - the root type.

  Such values would no longer be `TypeIsGuess`, and `ObjectAssessment` could rate them `Certain`. The defaults could be shown for left out values.
- References to a described schema: the MCP server's `schemas` parameter and the explorers' stores use `SchemaStore.Register`, which parses with `SchemaBytes.Parse` (its `ReadName` refuses an array) and hashes with format version 1. A described schema needs version 2 (`SchemaId.Compute(bytes, SchemaId.DescribedFormatVersion)`) and a parser that takes the items by their first element. Core keeps parsing plain schemas strictly: the serializers must never take a described schema for a plain one.
- `PayloadValidation` could check the described schema against the body: a value's kind, an object of the entry the property names, `[DefaultValue]`.
- Three UIs use this (MsgPackExplorer, the VS Code inspector, the MCP server, all through ObjectDebugger), so one change covers them. MsgPackExplorer can only be built and tried on Windows.

## 4. Constructor values of left out properties

**Now:** for a property the server left out, a .NET reader keeps what the constructor set, while the JavaScript client sets the default of the type. They differ only for a property whose constructor sets something else than the default and whose value was the default (documented in docs/TypeScriptClient.md, "Values"):

```csharp
public int Retries { get; set; } = 3; // sent as 0: left out; .NET reads 3, JavaScript 0
```

**Option:** the described schema could carry the constructor's value as the property's default (the third item, as `[DefaultValue]` does now). `DescribedSchema` would create one instance per class (`Instances.Create`, the server's own model classes) and read the properties whose values differ from the type's default. That is once per class and schema, cached, nothing per call.

**Things to decide:**

- **Value types only.** A constructor that creates a list (`Items = new List<X>()`) would make the default an empty list. Reference types the server writes anyway (nil for null), and a nil in an array leaves the constructor's value in .NET too: the accepted difference `ExplicitNullKeepsTheConstructorValue` (CLAUDE.md, "Array layout"). So the difference is only real for value types.
- **Classes without a parameterless constructor**, or `ObjectCreation.Uninitialized`, have no constructor values: they keep the type defaults.
- **Constructors with side effects** run once on the server, when the schema is described.

Recommendation: value types only, behind an option (`LtMsgPackHttpOptions.DescribeConstructorValues`?). Or leave it documented as it is: it is rare, and JSON clients have the same difference.

## Smaller follow-ups

- **npm.** Publish when the name is settled (`@lsmsgpack/client` needs the `lsmsgpack` scope on npm), then remove `"private": true`. The package has no build step beyond `tsc`, no bundle.
- **camelCase.** The property names are the .NET names (`InvoiceDate`). Apps that come from ASP.NET Core's JSON (camelCase by default) would like a naming option. It must apply to reading and writing alike, and never to dictionary keys.
- **The Accept check for described clients** costs ~0.3 µs per response (`MsgPackMediaTypes.AcceptsDescribedSchema` splits the header). Clients that do not ask pay ~45 ns. It could scan without splitting if it ever matters.
- **A dictionary whose first key is `""`** is read as a value with a type id (`{ "": typeId, ... }`). The .NET readers have the same ambiguity, wider: `ValueConverter.ConvertMap` takes a key `""` in any position as the type id.
- **`dates: 'iso'`** applies to typed properties only. Untyped values (`object`) stay `Date`.
- **No IndexedDB adapter.** `SchemaStore.export()`/`import()` give JSON for any storage.
