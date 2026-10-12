# Handoff: a TypeScript/JavaScript client for application/x-lsmsgpack

Status: assessment only (October 2026), nothing implemented. The measurements are reproducible with the scripts in [typescript-client/](typescript-client/).

## The question

Would a TypeScript/JavaScript implementation that supports the cached schema of `application/x-lsmsgpack` be worth it? If so, should it be built on an existing library or be a port of (part of) LtMsgPack? Or should JavaScript users use an existing library with `application/msgpack`, because a dynamic language would not get faster from the indexed schema?

## Conclusion

- **Worth building, as a small client library on top of `@msgpack/msgpack`, not as a port of LtMsgPack.**
- The performance hunch is mostly right: with off-the-shelf decoding x-lsmsgpack is no faster in JavaScript. Only a reader that builds the objects from the schema directly is 20-25% faster (measured below). Speed is a bonus, not the reason.
- The reason is **size**. Once a response is compressed, `application/msgpack` is the same size as JSON, so it gives a browser almost nothing over JSON except type fidelity. The schema reference still saves ~30% per small response after gzip (~15% on one large response).
- The real obstacle is not performance: **a JavaScript client has no declared types**, and an x-lsmsgpack body depends on them (see [Declared types](#the-real-problem-declared-types)). That needs a decision: generated descriptors, or type ids on every object for clients that ask for them.
- The audience is an SPA talking to its own ASP.NET backend: the "own client and own server" case x-lsmsgpack is made for, where polymorphism without `[JsonDerivedType]` attributes is a real draw. Public APIs and other JavaScript consumers keep `application/msgpack` (or JSON).

## Measurements

`dotnet` was not available in the session that made this assessment (the SDK download was blocked), so the numbers come from the `BenchmarkInvoices` model ported to JavaScript (`typescript-client/data.mjs`) and encoded with `@msgpack/msgpack` 3.1.2 on Node 22, one run each (repeated runs varied by ~5-10%). Differences with what the .NET formatters write: decimals are strings here (x-lsmsgpack writes extension type 1), no type ids (the invoice model has no polymorphism), the seeded random numbers differ from .NET's `Random`. The x-lsmsgpack shape is the default: the indexed schema, then objects as arrays of their values in schema order, nil for a value the filters leave out.

### Size

| | 100 invoices raw | gzip (6) | brotli (5) | 1 invoice raw | gzip (6) |
|---|---|---|---|---|---|
| JSON (names) | 298 KB | 41.5 KB | 38.0 KB | 2005 | 782 |
| msgpack names (`application/msgpack`) | 242 KB | 40.8 KB | 38.4 KB | 1586 | 808 |
| x-lsmsgpack, schema inline | 108 KB | 34.8 KB | 33.8 KB | 1089 | 793 |
| x-lsmsgpack, schema reference | 108 KB | 34.5 KB | 33.6 KB | 704 | **565** |

The schema alone is 403 bytes. Compression removes the repeated names within one response, but cannot remove the schema repeated across responses, which the reference does. For comparison, the .NET formatters measured 214.7 KB → 176.2 KB (-18%) for 100 invoices as 100 separate payloads, uncompressed (docs/WebFormatters.md).

### Reading (100 invoices, bytes → named JavaScript objects)

| | µs |
|---|---|
| `JSON.parse` of a string | 1650-1940 |
| `JSON.parse` including the UTF-8 decoding | 2650-2850 |
| `@msgpack/msgpack`, names | 2520-2740 |
| `@msgpack/msgpack`, x-lsmsgpack body (arrays, not yet objects) | 1660-1820 |
| `@msgpack/msgpack`, x-lsmsgpack body, then mapped to names | 2340-2630 |
| minimal hand-written reader, names (generic maps, cached keys) | 1650-1830 |
| same reader, x-lsmsgpack body read straight into objects (plans per schema entry) | **1330-1460** |

- Decoding the arrays is a third faster than decoding maps, but mapping them to named objects afterwards takes that back.
- The plan-driven reader (`typescript-client/fused.mjs`, ~150 lines: per schema entry the names and a constructor with the properties in a fixed order, like LtMsgPack's `BoundSchema`) is 20-25% faster than a reader of the same quality for names, and a bit faster than `JSON.parse`. It is minimal (no str32, no big integers, no extension codecs): a complete one would be somewhat slower.
- Writing: `JSON.stringify` 1170-1210 µs, `@msgpack/msgpack` names 2290-2430 µs, x-lsmsgpack (to arrays, then encode) 1800-1940 µs.

### A type id on every object (`AddTypeIdOption.Always`)

| | raw | gzip |
|---|---|---|
| x-lsmsgpack inline, `IfAmbiguious` (default) | 108014 | 34798 |
| x-lsmsgpack inline, `Always`, arrays (`{ "": typeIndex, "@": [values] }`) | 116509 (+7.9%) | 35435 (+1.8%) |
| x-lsmsgpack inline, `Always`, `ObjectLayout.Map` (`{ "": typeIndex, 0: v, ... }`) | 122765 (+13.7%) | not measured |
| msgpack names | 241608 | 40842 |

## The real problem: declared types

An x-lsmsgpack body says which class an object is only where the type differs from the declared type (`AddTypeIdOption.IfAmbiguious`). Elsewhere the reader knows it from the declared type of the property (`IndexedSchemaTypeResolver.BindReaderTypes` pairs them up from the root class). With the array layout, `[ "Ann", [[...], [...]] ]` cannot be turned into named objects without knowing that the second value is a list of `Pet`. ObjectDebugger infers it (`ObjectReconstructor`), but marks those types `TypeIsGuess`: fine for an explorer, not for a client.

Two ways around it:

1. **Generated descriptors**: a tool (a `dotnet` tool reading the model assembly, or from OpenAPI) that writes TypeScript types plus runtime descriptors (the class name, its properties and which of them hold objects, lists or dictionaries of which class, and the conversions: Guid, decimal, `DateTimeOffset`, `TimeSpan`, 64 bit integers). The best developer experience (typed objects), enables the fast reader and the writing of x-lsmsgpack requests (type ids need the .NET type names). A code generator to build and keep in sync.
2. **Type ids on every object for clients that ask for them**, e.g. `Accept: application/x-lsmsgpack; typeids=always` or a request header: the formatters already support `AddTypeIdOption.Always`. +8% raw, +2% gzipped (above), still half the size of names. A generic JavaScript decoder then needs only the schema (every object names its schema entry, which also tells an object from a dictionary with string keys), and the fast reader works per schema index. A protocol addition: the maintainer decides.

Recommendation: start with 2, add descriptors later if users want typed objects.

## Build on a library, or port LtMsgPack?

**Build on `@msgpack/msgpack`.** LtMsgPack's speed comes from .NET specifics: typed `ValueHandler<T>`/`ValueReader<T>` without boxing, delegates instead of reflection, plans per `Type`, avoiding the `MsgPackItem` tree, Core's type resolving and type injection checks. None of that carries over to JavaScript (there are no types to instantiate from names). What carries over is small:

- schema map followed by the body: `Decoder.decodeMulti`;
- the schema reference `D8 02` + 16 bytes is a fixext 16 of type 2: an `ExtensionCodec` entry;
- the reserved keys `""` (type id, a schema index) and `"@"` (the content of a wrapped value);
- the .NET value formats: decimal as extension type 1, `BigInteger`/`Int128` as extension type -2 (big-endian two's complement), Guid as bin 16 in .NET byte order, `DateTimeOffset` as `[timestamp, offset minutes]`, `TimeSpan` as ticks, enums as integers, `int64` → `bigint` where needed (`useBigInt64`);
- a schema store: `Map<id, schema>`, at most 32 ids advertised per origin (`MaxAdvertisedSchemas`), optionally persisted in IndexedDB (safe: ids are content hashes, and a server that does not hold an id writes the schema inline);
- a `fetch` wrapper: request header `MsgPack-Schemas` (comma separated ids of 32 hex digits), response header `MsgPack-Schema`; register an inline schema only when SHA-256 over the format version byte `1` + the schema bytes as received (first 16 bytes) equals the header's id, as `LtMsgPackHttpSerializer.LearnSchema` / `SchemaStore.RegisterInline` do (`crypto.subtle.digest`, async, secure contexts only).

If reading speed matters later, replace the decoding of bodies by a plan-driven reader (`typescript-client/fused.mjs` is the sketch).

## Things to plan for

- **Request bodies**: send `application/msgpack` (plain maps) at first; request bodies carry the schema inline anyway (by design), and writing x-lsmsgpack needs the .NET type names for type ids. Polymorphic request bodies are the exception (plain has no type ids): they need x-lsmsgpack and the descriptors.
- **CORS**: a cross-origin browser client needs `MsgPack-Schemas` in `Access-Control-Allow-Headers` and `MsgPack-Schema` in `Access-Control-Expose-Headers`. docs/WebFormatters.md does not mention it yet.
- **Tests**: CI already runs Node for the VS Code extension; payloads written by the .NET formatters can be checked by the TypeScript reader the way `LsMsgPackVsCode/test/mcpBridge.test.js` runs the .NET server against the TypeScript bridge. Keep docs/Compatibility.md in sync.
- **Packaging**: a new npm package (name to decide), next to the not yet published VS Code extension.

## Not verified

- The .NET side was not run (no `dotnet` in that session): the sizes are from the JavaScript port of the model, not from the formatters' output.
- Whether `AddTypeIdOption.Always` also wraps collections (e.g. `List<InvoiceLine>` as `{ "": typeIndex, "@": [...] }`), which would add a little to the +8%.
- Whether the server writes the same schema (so the same id) after a restart: it depends on the order in which types are first written. Harmless either way (the schema then comes inline once).

## Reproducing

```bash
cd docs/handoffs/typescript-client
npm install
node bench.mjs 100   # sizes, and @msgpack/msgpack timings for 100 invoices
node bench.mjs 1     # sizes for one invoice
node fused.mjs       # the minimal hand-written reader: names vs plan-driven x-lsmsgpack
node always.mjs      # what AddTypeIdOption.Always costs
```
