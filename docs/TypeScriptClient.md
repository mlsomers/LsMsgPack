# TypeScript / JavaScript client

`@lsmsgpack/client` (in [clients/typescript](../clients/typescript)) reads the `application/x-lsmsgpack` responses of the [web formatters](WebFormatters.md) into complete JavaScript objects and writes request bodies they read. It is for a web application (or Node service) that talks to its own ASP.NET backend, as a faster replacement for JSON:

- **Complete objects.** Every property of the class is there, also the ones the server left out because they were `null` or a default value (`0`, `false`, `Guid.Empty`, `[DefaultValue]`...). So `invoice.Lines[3].DiscountPercentage` is `0`, not `undefined`.
- **Polymorphism without attributes.** A property declared as a base class or interface gets an object with the properties of its actual class (the formatters add a type id where the type differs from the declared one), and its class name in `$type`. Request bodies send it back as the type id, so the server creates the same classes.
- **The schema is sent once.** The property names travel once per schema, then the server refers to it (18 bytes). With the generated schema set, not even once.
- **Small and fast.** No dependencies. Reads as fast as `JSON.parse` of an already decoded string, writes about as fast as `JSON.stringify` (numbers below).

It works in two ways, which can be mixed:

| | With the generated models | Without models |
|---|---|---|
| What you need | Run [lsmsgpack-ts](TypeScriptModels.md) over the server's compiled classes | Nothing: install and call |
| Objects | Instances of the generated classes (`instanceof`, typed) | Plain objects (you write the interfaces, or none) |
| A property the server left out | The value the .NET constructor sets, as a .NET client reads it | The default of its type, as JSON clients read it |
| The first response of a type | Refers to its schema (hot-loaded) | Carries its schema, then refers to it |
| Request bodies | Written as declared (Guids as 16 bytes, TimeSpan text as ticks...), type ids only where needed | Plain values, `$type` where you set it |

Without models the client gets what it needs at runtime from the **described schema** (version 2 of the indexed schema: the kind and default of each property). That is also the fallback with models: a server newer than the models (a property or class they don't have yet), classes the models don't cover, a schema that varies with the data. The schema of the payload always decides which value is which property, the models only add the class, the constructor values and the types.

Not published to npm yet: build it from the repository (`cd clients/typescript && npm install && npm run build`, the package is in `dist/`).

## Usage

```ts
import { LsMsgPackClient } from '@lsmsgpack/client';

const api = new LsMsgPackClient({ baseUrl: 'https://example.com/' }); // keep one for the lifetime of the app: it holds the schemas

const invoice = await api.get<Invoice>('api/invoices/42');
const saved = await api.post<Invoice>('api/invoices', invoice); // the body as application/msgpack

interface Invoice { Id: string; InvoiceDate: Date; Total: number; Lines: InvoiceLine[]; /* ... */ }
```

With the generated models and schemas (see [TypeScriptModels.md](TypeScriptModels.md)):

```ts
import { LsMsgPackClient } from '@lsmsgpack/client';
import { models, Invoice, Order, Dog } from './api/models';
import { schemas } from './api/models.schemas';

const api = new LsMsgPackClient({ baseUrl: 'https://example.com/', models, schemas });

const invoice = await api.get<Invoice>('api/invoices/42'); // an Invoice: invoice instanceof Invoice, the schema was held already
const order = new Order();                                 // the values the .NET constructor sets
order.Pet = Object.assign(new Dog(), { Name: 'Rex' });     // declared as Animal: written with the type id Dog
await api.post('api/orders', order);
```

- `get`, `post`, `put`, `patch` and `delete` return the decoded body and throw an `HttpError` (with `status` and the decoded `body`) when the status is not 2xx. `send` returns `{ value, response, schema, reference }`.
- A body that is already text or bytes (`string`, `Uint8Array`, `Blob`, `FormData`...) is sent as it is, anything else is encoded as MsgPack with `Content-Type: application/msgpack`.
- The property names are the names of the .NET properties (as the schema has them, e.g. `InvoiceDate`). Use the same names in request bodies.
- Responses that are not MsgPack (JSON, text) are decoded as such, an empty body (`204 No Content`, a `null` result) is `undefined`.
- Already have a `Response` (your own `fetch`, an interceptor)? `api.read(response)` decodes it and learns its schema. Your own fetch needs the headers below: `Accept` and `MsgPack-Schemas` (`api.store.advertise(origin)`).
- `Decoder` and `encode` work without HTTP (e.g. for MsgPack from a WebSocket): `new Decoder({ models }).decode(bytes)`, `encode(value, { models })`.

### Options

```ts
new LsMsgPackClient({
  baseUrl: 'https://example.com/', // relative urls resolve against it (default: the page)
  models,                 // the generated models (defineModels): instances of their classes, their constructor values, typed request bodies
  schemas,                // the generated schema set: held from the start (or store.preload(schemas, origin) for a shared store)
  decimal: 'number',      // 'number' (default), 'string' (the text of decimal.ToString(), every digit), or a function of that text: (t) => new Decimal(t)
  int64: 'number',        // long/ulong: 'number' (default, as JSON.parse: precision is lost beyond 2^53) or 'bigint'
  dates: 'date',          // DateTime/DateTimeOffset: 'date' (default) or 'iso' (text; DateTimeOffset keeps its offset)
  classes: { Invoice },   // without models: create instances of your own classes for these .NET type names
  store: sharedStore,     // a SchemaStore shared by several clients of the same servers
  headers: { Authorization: 'Bearer ...' }, // added to every request
  fetch: myFetch,         // default: globalThis.fetch
  describedSchema: true,  // ask for the described schema (below)
  negotiateSchemas: true, // advertise the schemas the client holds
  generateCode: true,     // see "Security"
  maxDepth: 256,          // arrays and maps nested deeper are refused
});
```

The models were generated for `int64`, `dates` and `decimal` (their TypeScript types depend on them): the client takes those, and throws when you pass others.

### The models

- An object of a class the models have is created with `new` (every object gets its own initial values: dates, lists and nested objects are not shared), then the values of the payload are set by the positions of its schema. A value the server left out (`nil`, or missing) keeps what the class set: the value of the .NET constructor, which a .NET client keeps as well (e.g. `public int Retries { get; set; } = 3;` sent as `0` is read back as `3` by both).
- `$type` is on the prototype of each class: `dog.$type === 'Dog'`, `JSON.stringify(dog)` leaves it out, a spread copy (`{ ...dog }`) loses it and the class.
- **The server changed** (newer than the models): a property the models don't have is set too (with its default from the schema when left out), a property the server no longer writes keeps the class's value, a class the models don't have becomes a plain object. Generate the models again to get the types.
- **Request bodies**: an instance of a model class is written with its properties as declared (`Guid` text as its 16 bytes, `TimeSpan`, `DateOnly`, `TimeOnly` and `char` text as the numbers .NET writes, decimal text as the decimal extension, ISO text as timestamps), then other own properties as they are. The type id where the declared type is not its class (a `Dog` where an `Animal` is declared, anything where `object` is), or, where the declared type is not known (the root, the elements of an array at the root), when the class extends another one.
- A schema with names only (a server with `DescribeSchemas = false`): the models give the kinds of their properties.

### Polymorphic request bodies (without models)

An object that came with a type id has the name of its .NET class in `$type`, its first property (as System.Text.Json names it): the elements of a `List<Animal>`, a property declared as an abstract class, an interface or `object`. The encoder writes `$type` as the type id (the key `""`, first), so a body that was read, changed and sent back keeps its classes. For new objects, set it yourself:

```ts
const zoo = await api.get<Zoo>('api/zoo');
zoo.Animals.push({ $type: 'Cat', Name: 'Kit', Indoor: true });
await api.post('api/zoo', zoo); // the server creates a Cat
```

- It is only needed where the declared type does not tell the class. The server creates everything else from its own declared types (a `$type` there does no harm).
- The name is the type name as the server writes it: the short class name by default (the full name when the server uses `AddTypeIdOption.FullName`). A class in another assembly than the declared types needs `MsgPackTypes.CacheAssemblyTypes` on the server (see [schema.md](schema.md)).
- The server refuses a class that is not assignable to the declared type, and asks its `TypeGuard` (see [security.md](security.md)): `$type` cannot make it create anything else. A request with an abstract class and no `$type` is refused too (400).
- `$type` must be a string. Collections don't get one (a JavaScript array has no place for it): a `List<Dog>` in a property declared as `object` comes back as an array.
- A dictionary with a key `"$type"` (read as an object) would be sent with a type id: send such a dictionary as a `Map`, which is written as it is.

### Keeping the schemas between page loads

The store lives in memory. To keep it (e.g. in `localStorage` or IndexedDB):

```ts
localStorage.setItem('schemas', JSON.stringify(api.store.export()));
// next time
const store = new SchemaStore();
store.import(JSON.parse(localStorage.getItem('schemas') ?? '{"schemas":[],"origins":{}}'));
const api = new LsMsgPackClient({ baseUrl, store });
```

`import` computes the ids from the bytes again (they are hashes of the content), so a stored schema cannot pass for another one. A server that no longer has a schema (restarted, another instance) sends it inline, nothing breaks. The generated schema set needs no storage: it is in the code.

## The server

Nothing to change: the [ASP.NET Core, Web API and MVC 5 formatters](WebFormatters.md) support it (`LtMsgPackHttpOptions.DescribeSchemas` and `CompleteSchemas`, on by default).

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

| .NET | JavaScript | Default (left out, without models) |
|---|---|---|
| `string`, `Uri`, `Version` | `string` | `null` |
| `bool` | `boolean` | `false` |
| `int`, `short`, `byte`, `uint`..., `float`, `double` | `number` | `0` |
| `long`, `ulong` | `number` (`int64: 'bigint'`: `bigint`) | `0` |
| `decimal` | `number` (`decimal` option) | `0` |
| `BigInteger`, `Int128`, `UInt128` | `bigint` | `0n` |
| enums | `number` (the underlying value; the models have a const object with the names) | `0` |
| `char` | `string` (one character) | `'\0'` |
| `Guid` | `string` (`"0f8fad5b-d9cb-469f-a165-70867728950e"`) | `"00000000-0000-0000-0000-000000000000"` |
| `DateTime` | `Date` (`dates: 'iso'`: UTC text) | `0001-01-01T00:00:00Z` (`DateTime.MinValue`) |
| `DateTimeOffset` | `Date` (`dates: 'iso'`: text with the offset) | `0001-01-01T00:00:00+00:00` |
| `TimeSpan` | `string`, `"1.02:03:04.5000000"` (as System.Text.Json) | `"00:00:00"` |
| `DateOnly`, `TimeOnly` | `string`, `"2026-10-13"`, `"12:34:56.2500000"` | `"0001-01-01"`, `"00:00:00"` |
| `byte[]`, `Memory<byte>` | `Uint8Array` | `null` |
| classes | objects (instances of the models) | `null` |
| structs | objects | an object with its defaults |
| `Nullable<T>` | as `T` | `null` |
| lists, arrays, sets | arrays | `null` |
| dictionaries | objects (keys as text, like JSON) | `null` |
| `KeyValuePair<K, V>` | `{ Key, Value }` | `{ Key: default, Value: default }` |
| tuples | arrays | `null` |
| `object`, interfaces | what the value is (an object of its class with a type id, its name in `$type`) | `null` |

The rule: what System.Text.Json writes, except dates (`Date`) and bytes (`Uint8Array`). `[DefaultValue(x)]` makes `x` the default. With models the default is what the class sets (the .NET constructor's value).

Untyped values (`object` properties, the elements of `List<object>`) are read as they are: numbers, strings, `Date` for timestamps, `Uint8Array` for bytes, objects (with `$type`) for values with a type id. The `dates: 'iso'` option does not apply to them.

## The described schema

The indexed schema only has names: `{ "Invoice": ["Id", "InvoiceNumber", ...], ... }`. With the array layout (the default) a value the server left out is `nil`, so a client without the classes cannot know whether that was `0`, `false` or `null`, nor whether a nested array is an object or a list. .NET clients know it from their classes, this client from the generated models or from version 2 of the schema.

The client asks for it: `Accept: application/x-lsmsgpack; schema=described, application/x-lsmsgpack; q=0.9`. The server sends the schema with the type of each property (format: [schema.md, version 2](schema.md#version-2-the-declared-types)). The body is the same bytes, and the schema has its own id, so it is negotiated and referred to like a plain one (see [WebFormatters.md](WebFormatters.md#schema-version-2-for-clients-without-the-classes)). The second media type is for servers that do not know the parameter, and for ASP.NET Core's content negotiation, which only matches a parameter it lists.

A server that does not describe schemas (`DescribeSchemas = false`, `NegotiateSchemas = false`) sends the plain schema. With models the client still reads everything (the models know the types, and the root when it has a type id). Without them values with a type id become objects (left out properties `null`), the root and other objects stay arrays of their values.

The client keeps an inline schema only when its hash equals the `MsgPack-Schema` response header (SHA-256 over the version byte and the bytes, first 16 bytes).

## Security

- **Schemas from the server are data.** A schema is kept only when its hash is the id the server names, ids are compared in full (SHA-256, 128 bits). Lengths in the data are checked against the rest of the bytes before anything is allocated, nesting is limited to `maxDepth` (256), and data after the value is refused.
- **`__proto__`** as a property name or dictionary key becomes an own property (as `JSON.parse` makes it), never the prototype.
- **Generated code.** For speed, the client generates a function per schema entry that creates and fills its objects (`new Function`). The property names from the schema are only embedded as JSON string literals, a schema with a property `__proto__` uses the generic path. With a Content-Security-Policy without `'unsafe-eval'` the generic path is used automatically (`new Function` throws once). `generateCode: false` turns it off; reading is then 20% slower with models, 55% without (below).
- **Types from the data**: a type id only picks a class of the models or makes a plain object, nothing else is created.

## Performance

100 invoices of the `BenchmarkInvoices` model, written by the ASP.NET Core formatters (`npm run bench` in `clients/typescript`, Node 22, i7-7700K, one run, October 2026):

| | raw | gzip | read |
|---|---|---|---|
| JSON (System.Text.Json, every property) | 316.7 KB | 42.1 KB | 1080 µs `JSON.parse` of the string, 1290-1310 µs with the UTF-8 decoding |
| `application/msgpack` with @msgpack/msgpack | 300.6 KB | 42.8 KB | 2200 µs |
| `application/x-lsmsgpack`, schema reference | 166.9 KB | 38.0 KB | 1000-1035 µs; with the models 1036-1050 µs |
| the same without generated code (CSP) | | | 1590 µs; with the models 1256-1259 µs |

One invoice: JSON 4022 bytes (1048 gzipped), schema reference 2111 bytes (810 gzipped, -23%). The described schema is about 300 bytes larger than the plain one and is sent once per client and schema (with the generated schema set: never).

Writing the same objects as a request body: `JSON.stringify` 1270 µs (1600 µs with the UTF-8 encoding), this client 1840-1880 µs, with the models 1720 µs, @msgpack/msgpack 2420 µs. In the browser both formats cost about the same; the server reads MsgPack about 3.5x faster than System.Text.Json reads JSON (LtMsgPack, names).

## Not covered (yet)

- The root of a payload with a plain schema (no described schema) is only an object when it has a type id.
- Property names are the .NET names (`InvoiceDate`); a camelCase option would have to apply to reading and writing alike, never to dictionary keys.
- Generic classes are generated per closed type (`Page<Invoice>` is `class Page_Invoice`).
