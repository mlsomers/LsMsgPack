# TypeScript / JavaScript client

`@lsmsgpack/client` (in [clients/typescript](../clients/typescript)) reads the `application/x-lsmsgpack` responses of the [web formatters](WebFormatters.md) into complete JavaScript objects. It is for a web application (or Node service) that talks to its own ASP.NET backend:

- **Complete objects.** Every property of the class is there, also the ones the server left out because they were `null` or a default value (`0`, `false`, `Guid.Empty`, `[DefaultValue]`...). The client gives them the default of their .NET type. So `invoice.Lines[3].DiscountPercentage` is `0`, not `undefined`.
- **Polymorphism without attributes.** A property declared as a base class or interface gets an object with the properties of its actual class (the formatters add a type id where the type differs from the declared one).
- **The schema is sent once.** The property names travel once per schema, then the server refers to it (18 bytes). Like the [HttpClient handler](WebFormatters.md#schema-references-applicationx-lsmsgpack) of the .NET client, the client keeps the schemas it received and tells the server which ones it has.
- **Small and fast.** No dependencies, about 1700 lines of TypeScript. In Node it reads as fast as `JSON.parse` of an already decoded string (numbers below).

Request bodies are sent as plain MsgPack (`application/msgpack`, maps keyed by property names), which the formatters read too.

Not published to npm yet: build it from the repository (`cd clients/typescript && npm install && npm run build`, the package is in `dist/`).

## Usage

```ts
import { LsMsgPackClient } from '@lsmsgpack/client';

const api = new LsMsgPackClient({ baseUrl: 'https://example.com/' }); // keep one for the lifetime of the app: it holds the schemas

const invoice = await api.get<Invoice>('api/invoices/42');
const saved = await api.post<Invoice>('api/invoices', invoice); // the body as application/msgpack

interface Invoice { Id: string; InvoiceDate: Date; Total: number; Lines: InvoiceLine[]; /* ... */ }
```

- `get`, `post`, `put`, `patch` and `delete` return the decoded body and throw an `HttpError` (with `status` and the decoded `body`) when the status is not 2xx. `send` returns `{ value, response, schema, reference }`.
- A body that is already text or bytes (`string`, `Uint8Array`, `Blob`, `FormData`...) is sent as it is, anything else is encoded as MsgPack with `Content-Type: application/msgpack`.
- The property names are the names of the .NET properties (as the schema has them, e.g. `InvoiceDate`). Use the same names in request bodies.
- Responses that are not MsgPack (JSON, text) are decoded as such, an empty body (`204 No Content`, a `null` result) is `undefined`.
- Already have a `Response` (your own `fetch`, an interceptor)? `api.read(response)` decodes it and learns its schema. Your own fetch needs the headers below: `Accept` and `MsgPack-Schemas` (`api.store.advertise(origin)`).

### Options

```ts
new LsMsgPackClient({
  baseUrl: 'https://example.com/', // relative urls resolve against it (default: the page)
  decimal: 'number',      // 'number' (default), 'string' (the text of decimal.ToString(), every digit), or a function of that text: (t) => new Decimal(t)
  int64: 'number',        // long/ulong: 'number' (default, as JSON.parse: precision is lost beyond 2^53) or 'bigint'
  dates: 'date',          // DateTime/DateTimeOffset: 'date' (default) or 'iso' (text; DateTimeOffset keeps its offset)
  classes: { Invoice },   // create instances of your classes for these .NET type names (properties your class does not initialize get the defaults)
  store: sharedStore,     // a SchemaStore shared by several clients of the same servers
  headers: { Authorization: 'Bearer ...' }, // added to every request
  fetch: myFetch,         // default: globalThis.fetch
  describedSchema: true,  // ask for the described schema (below)
  negotiateSchemas: true, // advertise the schemas the client holds
  generateCode: true,     // see "Security"
  maxDepth: 256,          // arrays and maps nested deeper are refused
});
```

### Keeping the schemas between page loads

The store lives in memory. To keep it (e.g. in `localStorage` or IndexedDB):

```ts
localStorage.setItem('schemas', JSON.stringify(api.store.export()));
// next time
const store = new SchemaStore();
store.import(JSON.parse(localStorage.getItem('schemas') ?? '{"schemas":[],"origins":{}}'));
const api = new LsMsgPackClient({ baseUrl, store });
```

`import` computes the ids from the bytes again (they are hashes of the content), so a stored schema cannot pass for another one. A server that no longer has a schema (restarted, another instance) sends it inline, nothing breaks.

## The server

Nothing to change: the [ASP.NET Core, Web API and MVC 5 formatters](WebFormatters.md) support it (`LtMsgPackHttpOptions.DescribeSchemas`, on by default).

**Cross-origin requests** (the page is not served by the API's origin) need CORS to allow the request header and expose the response header:

```csharp
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
  .WithOrigins("https://app.example.com")
  .WithHeaders("Content-Type", "MsgPack-Schemas")   // and your other headers (Authorization...)
  .WithExposedHeaders("MsgPack-Schema")));
// ...
app.UseCors();
```

Without them it still works, the client just gets every schema inline (it cannot read the id of a schema, so it does not keep it). The `Accept` header needs no CORS setting.

## Values

| .NET | JavaScript | Default (left out) |
|---|---|---|
| `string`, `Uri`, `Version` | `string` | `null` |
| `bool` | `boolean` | `false` |
| `int`, `short`, `byte`, `uint`..., `float`, `double` | `number` | `0` |
| `long`, `ulong` | `number` (`int64: 'bigint'`: `bigint`) | `0` |
| `decimal` | `number` (`decimal` option) | `0` |
| `BigInteger`, `Int128`, `UInt128` | `bigint` | `0n` |
| enums | `number` (the underlying value) | `0` |
| `char` | `string` (one character) | `'\0'` |
| `Guid` | `string` (`"0f8fad5b-d9cb-469f-a165-70867728950e"`) | `"00000000-0000-0000-0000-000000000000"` |
| `DateTime` | `Date` (`dates: 'iso'`: UTC text) | `0001-01-01T00:00:00Z` (`DateTime.MinValue`) |
| `DateTimeOffset` | `Date` (`dates: 'iso'`: text with the offset) | `0001-01-01T00:00:00+00:00` |
| `TimeSpan` | `string`, `"1.02:03:04.5000000"` (as System.Text.Json) | `"00:00:00"` |
| `DateOnly`, `TimeOnly` | `string`, `"2026-10-13"`, `"12:34:56.2500000"` | `"0001-01-01"`, `"00:00:00"` |
| `byte[]`, `Memory<byte>` | `Uint8Array` | `null` |
| classes | objects (or your `classes`) | `null` |
| structs | objects | an object with its defaults |
| `Nullable<T>` | as `T` | `null` |
| lists, arrays, sets | arrays | `null` |
| dictionaries | objects (keys as text, like JSON) | `null` |
| `KeyValuePair<K, V>` | `{ Key, Value }` | `{ Key: default, Value: default }` |
| tuples | arrays | `null` |
| `object`, interfaces | what the value is (its class with a type id) | `null` |

The rule: what System.Text.Json writes, except dates (`Date`) and bytes (`Uint8Array`). `[DefaultValue(x)]` makes `x` the default.

One difference with a .NET client: for a property the server left out, .NET keeps what the constructor of the class set, the JavaScript client uses the default of the type (it cannot run the constructor). They only differ for a property whose constructor sets something else than the default while the value sent was the default: e.g. `public int Retries { get; set; } = 3;` with the value `0`, which .NET reads back as `3` and the JavaScript client as `0`.

Untyped values (`object` properties, the elements of `List<object>`) are read as they are: numbers, strings, `Date` for timestamps, `Uint8Array` for bytes, objects for values with a type id. The `dates: 'iso'` option does not apply to them.

## The described schema

The indexed schema only has names: `{ "Invoice": ["Id", "InvoiceNumber", ...], ... }`. With the array layout (the default) a value the server left out is `nil`, so a client without the classes cannot know whether that was `0`, `false` or `null`, nor whether a nested array is an object or a list. .NET clients know it from their classes.

The client therefore asks for the **described schema**: `Accept: application/x-lsmsgpack; schema=described, application/x-lsmsgpack; q=0.9`. The server sends the schema with the kind of each property instead of the plain one. The body is the same bytes, and the schema has its own id, so it is negotiated and referred to exactly like a plain one (see [WebFormatters.md](WebFormatters.md#schema-references-applicationx-lsmsgpack)). The second media type is for servers that do not know the parameter, and for ASP.NET Core's content negotiation, which only matches a parameter it lists.

A server that does not describe schemas (an older version, `DescribeSchemas = false`, `NegotiateSchemas = false`) sends the plain schema. The client then still reads everything, with less: values with a type id become objects (left out properties `null`), the root and other objects stay arrays of their values.

### Format

The same map as the schema, with the same keys in the same order (type ids are indexes of the entries, they stay valid), and one entry more at the end. Each value is an array of items:

| Item | Meaning |
|---|---|
| `"Name"` | a property without a description (as in the plain schema) |
| `["Name", type]` | a property and its declared type |
| `["Name", type, default]` | with `[DefaultValue]`: the value the server leaves out, encoded as the body encodes it |
| `["@", type]` | an entry that is not an object (a collection or value that got a type id, e.g. `List<Dog>` declared as `IEnumerable<Animal>`): the type of the content of `{ "": typeId, "@": content }` |

The last entry has the key `""` and the item `["@", type]`: the declared type of the root (e.g. a list of `Invoice`). Body type ids never refer to it.

Types:

- A kind: `string`, `bool`, `int` (32 bits and smaller, and `uint`), `long`, `ulong`, `float` (`float`, `double`, `Half`), `decimal`, `char`, `bigint`, `guid`, `bin`, `uri`, `datetime`, `dto` (`DateTimeOffset` as `[timestamp, offset minutes]` or a bare timestamp), `dto-clock` (`DateTimeOffsetFormat.ClockTimeAndOffset`: `[clock time as UTC timestamp, offset minutes]`), `timespan` (ticks), `dateonly` (day number), `timeonly` (ticks), `any` (`object`, interfaces, abstract classes, custom extensions, multidimensional arrays, a class this schema never wrote). Enums are their underlying kind. A client reads a kind it does not know as `any`.
- An integer `k`: an object of entry `k`.
- `["?", T]`: `Nullable<T>` (default `null`).
- `["struct", k]`: a struct of entry `k` (its default is an object with the defaults of its properties). `["struct", [items]]`: a struct the schema has no entry for (it was only written as its default so far), its properties as an entry has them.
- `["list", T]`, `["dict", K, V]`, `["pair", K, V]`, `["tuple", T1, T2, ...]` (the 8th item of a long tuple is a nested tuple).

Its id is the first 16 bytes of SHA-256 over the byte `2` followed by the bytes (a plain schema: the byte `1`), so the two never have the same id and the client can tell which one it got. The client keeps an inline schema only when that hash equals the `MsgPack-Schema` response header.

Example (one class, a list root):

```
{ "Address": [["Street", "string"], ["City", "string"], ["Zip", ["?", "int"]]],
  "":        [["@", ["list", 0]]] }
```

## Security

- **Schemas from the server are data.** A schema is kept only when its hash is the id the server names, ids are compared in full (SHA-256, 128 bits). Lengths in the data are checked against the rest of the bytes before anything is allocated, nesting is limited to `maxDepth` (256), and data after the value is refused.
- **`__proto__`** as a property name or dictionary key becomes an own property (as `JSON.parse` makes it), never the prototype.
- **Generated code.** For speed, the client generates a function per schema entry that creates and fills its objects (`new Function`). The property names from the schema are only embedded as JSON string literals, a schema with a property `__proto__` uses the generic path. With a Content-Security-Policy without `'unsafe-eval'` the generic path is used automatically (`new Function` throws once). `generateCode: false` turns it off; reading is then about 35% slower (below).

## Performance

100 invoices of the `BenchmarkInvoices` model, written by the ASP.NET Core formatters (`clients/typescript/bench/bench.mjs`, Node 22, i7-7700K, one run):

| | raw | gzip | read |
|---|---|---|---|
| JSON (System.Text.Json, every property) | 316.7 KB | 42.1 KB | 1250-1270 µs `JSON.parse` of the string, 1500-1580 µs with the UTF-8 decoding |
| `application/msgpack` with @msgpack/msgpack | 300.6 KB | 42.8 KB | 2610-2770 µs |
| `application/x-lsmsgpack`, described schema inline | 167.6 KB | 38.4 KB | 1230-1240 µs |
| `application/x-lsmsgpack`, schema reference | 166.9 KB | 38.0 KB | 1230-1240 µs (without generated code: 1880 µs) |

One invoice: JSON 4022 bytes (1048 gzipped), schema reference 2111 bytes (810 gzipped, -23%). The described schema is about 300 bytes larger than the plain one and is sent once per client and schema.

## Not covered (yet)

- Request bodies with type ids (polymorphic bodies): they are sent as plain MsgPack, which has none.
- TypeScript types are not generated: `get<Invoice>` trusts the type you name. The described schema has what a generator needs.
- MsgPack Explorer, the VS Code extension and the MCP server do not understand the described schema yet: they show a captured response with one as two values (the schema as a map of arrays, then the body) and report an error about its type ids.
