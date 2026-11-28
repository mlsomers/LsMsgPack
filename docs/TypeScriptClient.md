# TypeScript / JavaScript client

`@lsmsgpack/client` (in [clients/typescript](../clients/typescript)) reads the `application/x-lsmsgpack` responses of the [web formatters](WebFormatters.md) into complete JavaScript objects and writes request bodies they read. It is for a web application (or Node service) that talks to its own ASP.NET backend, as a faster replacement for JSON:

- **Complete objects.** Every property of the class is there, also the ones the server left out because they were `null` or a default value (`0`, `false`, `Guid.Empty`, `[DefaultValue]`...). So `invoice.lines[3].discountPercentage` is `0`, not `undefined`.
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

Install it with `npm install @lsmsgpack/client`. Its version is the NuGet version of the server packages, `yyyy.M.d.build` written as `yyyy.M.(d*100+build)` (npm takes three numbers): 2026.10.2201 goes with 2026.10.22.1.

## Usage

```ts
import { LsMsgPackClient } from '@lsmsgpack/client';

const api = new LsMsgPackClient({ baseUrl: 'https://example.com/' }); // keep one for the lifetime of the app: it holds the schemas

const invoice = await api.get<Invoice>('api/invoices/42');
const saved = await api.post<Invoice>('api/invoices', invoice); // the body as application/msgpack

interface Invoice { id: string; invoiceDate: Date; total: number; lines: InvoiceLine[]; /* ... */ }
```

With the generated models and schemas (see [TypeScriptModels.md](TypeScriptModels.md)):

```ts
import { LsMsgPackClient } from '@lsmsgpack/client';
import { models, Invoice, Order, Dog } from './api/models';
import { schemas } from './api/models.schemas';

const api = new LsMsgPackClient({ baseUrl: 'https://example.com/', models, schemas });

const invoice = await api.get<Invoice>('api/invoices/42'); // an Invoice: invoice instanceof Invoice, the schema was held already
const order = new Order();                                 // the values the .NET constructor sets
order.pet = Object.assign(new Dog(), { name: 'Rex' });     // declared as Animal: written with the type id Dog
await api.post('api/orders', order);
```

- `get`, `post`, `put`, `patch` and `delete` return the decoded body and throw an `HttpError` (with `status` and the decoded `body`) when the status is not 2xx. ASP.NET Core sends errors as problem details (`application/problem+json`, also when the request asks for MsgPack, see [error responses](WebFormatters.md#error-responses)): `body` is then that object (`title`, `detail`, and `errors` per property for a model that isn't valid), and the message has them: `400 Bad Request: One or more validation errors occurred. Customer: The Customer field is required.` `send` returns `{ value, response, schema, reference }`.
- A body that is already text or bytes (`string`, `Uint8Array`, `Blob`, `FormData`...) is sent as it is, anything else is encoded as MsgPack with `Content-Type: application/msgpack`.
- The property names are camelCase, as the JSON of ASP.NET Core has them (`invoiceDate` for `InvoiceDate`), or the names of the .NET properties with `propertyNames: 'dotnet'` (below).
- Responses that are not MsgPack (JSON, text) are decoded as such, an empty body (`204 No Content`, a `null` result) is `undefined`.
- Already have a `Response` (your own `fetch`, an interceptor)? `api.read(response)` decodes it and learns its schema. Your own fetch needs the headers below: `Accept` and `MsgPack-Schemas` (`api.store.advertise(origin, path)`), and `api.read(response, origin, path)` with the path of the request when `response.url` is not its url.
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
  propertyNames: 'camelCase', // 'camelCase' (default, invoiceDate) or 'dotnet' (InvoiceDate), see below
  classes: { Invoice },   // without models: create instances of your own classes for these .NET type names
  store: sharedStore,     // a SchemaStore shared by several clients of the same servers
  headers: { Authorization: 'Bearer ...' }, // added to every request
  fetch: myFetch,         // default: globalThis.fetch
  describedSchema: true,  // ask for the described schema (below)
  negotiateSchemas: true, // advertise the schemas the client holds
  generateCode: true,     // see "Security"
  references: 'default',  // a server with ReferenceHandling.Preserve: 'default' or 'preserve' (the same instance, cycles too; 'preserve' also writes references) or 'none', see below
  maxDepth: 256,          // arrays and maps nested deeper are refused
});
```

The models were generated for `int64`, `dates`, `decimal` and `propertyNames` (their TypeScript types and field names depend on them): the client takes those, and throws when you pass others.

### Property names

By default (`propertyNames: 'camelCase'`) the properties have the names System.Text.Json's `JsonNamingPolicy.CamelCase` gives them, so the objects look like the JSON of an API with ASP.NET Core's defaults: `invoiceDate`, `id` for `ID`, `urlValue` for `URLValue`. Code written against that JSON keeps working. `propertyNames: 'dotnet'` keeps the names of the .NET properties (`InvoiceDate`), as the schema has them. The names are converted once per schema entry, not per object: reading is as fast either way.

- It applies to the properties of objects and to `Key` and `Value` of a `KeyValuePair` (`{ key, value }`). Dictionary keys and `$type` stay as they are, and so do the keys of `application/msgpack` responses (maps without a schema).
- **Models**: lsmsgpack-ts names the fields camelCase by default (`--property-names dotnet` for the .NET names, see [TypeScriptModels.md](TypeScriptModels.md)). The client reads with the names of the models, and an instance is written with the .NET names.
- **Other request bodies** (plain objects) are written as they are, with camelCase keys. A property or class the models don't have but a schema the client read did gets its .NET name back (see [The models](#the-models)). The formatters match names ignoring case when no property has the exact name (`PropertyNameCaseInsensitive`, on by default, see [WebFormatters.md](WebFormatters.md#media-types-and-settings)). That costs the server about 10% more to read than the .NET names (2000 invoices with every key, as the client writes plain objects: 8.4 ms against 7.7 ms, October 2026), so post instances of the models when that matters (the writer cannot rename the keys of plain objects: it does not know which ones are dictionaries).
- Two properties that differ only in case (`Id` and `ID`) have the same camelCase name. The generator refuses them, System.Text.Json does too, and the server sets neither for a key `id`.

### The models

- An object of a class the models have is created with `new` (every object gets its own initial values: dates, lists and nested objects are not shared), then the values of the payload are set by the positions of its schema. A value the server left out (`nil`) is the value it left out, the schema's default or the type's (e.g. `public int Retries { get; set; } = 3;` sent as `0` is read back as `0`), as a .NET client reads it. A value missing after the end of the array (a server with fewer properties) keeps what the class set: the value of the .NET constructor, which a .NET client keeps as well.
- `$type` is on the prototype of each class: `dog.$type === 'Dog'`, `JSON.stringify(dog)` leaves it out, a spread copy (`{ ...dog }`) loses it and the class.
- **The server changed** (newer than the models): a property the models don't have is set too (with its default from the schema when left out), a property the server no longer writes keeps the class's value, a class the models don't have becomes a plain object. Generate the models again to get the types.
- **Sent back, nothing is lost**: an object read from a newer server, changed and posted back keeps the properties the models don't have, as .NET classes do with [extension data](ExtensionData.md). The client remembers what the schemas it read say about them (per class of the models: the .NET name and, from the described schema, the declared type; the classes the models don't have by their type names), and writes them the way it writes a declared property: with the .NET name, a `Guid` as its 16 bytes, a `TimeSpan` as its ticks, an object of a class the models don't have with its .NET names and kinds, and the type id it came with. This also holds for objects you make yourself once the client has read the class. With the plain schema (`DescribeSchemas = false`) only the names are known: the values are written as they were read. An object in such a value was an array of its values (the array layout, the default), which the server reads back by position; in the map layout its keys are the schema's indexes (`"0"`, `"1"`), which the server can't match, so those values are lost.
- **Request bodies**: an instance of a model class is written with its properties as declared (`Guid` text as its 16 bytes, `TimeSpan`, `DateOnly`, `TimeOnly` and `char` text as the numbers .NET writes, decimal text as the decimal extension, ISO text as timestamps), then the properties of the server it doesn't have (above), then other own properties as they are. The type id where the declared type is not its class (a `Dog` where an `Animal` is declared, anything where `object` is), or, where the declared type is not known (the root, the elements of an array at the root), when the class extends another one.
- A schema with names only (a server with `DescribeSchemas = false`): the models give the kinds of their properties.

### Polymorphic request bodies (without models)

An object that came with a type id has the name of its .NET class in `$type`, its first property (as System.Text.Json names it): the elements of a `List<Animal>`, a property declared as an abstract class, an interface or `object`. The encoder writes `$type` as the type id (the key `""`, first), so a body that was read, changed and sent back keeps its classes. For new objects, set it yourself:

```ts
const zoo = await api.get<Zoo>('api/zoo');
zoo.animals.push({ $type: 'Cat', name: 'Kit', indoor: true });
await api.post('api/zoo', zoo); // the server creates a Cat
```

- It is only needed where the declared type does not tell the class. The server creates everything else from its own declared types (a `$type` there does no harm).
- The name is the type name as the server writes it: the short class name by default (the full name when the server uses `AddTypeIdOption.FullName`). A class in another assembly than the declared types needs `MsgPackTypes.CacheAssemblyTypes` on the server (see [schema.md](schema.md)).
- The server refuses a class that is not assignable to the declared type, and asks its `TypeGuard` (see [security.md](security.md)): `$type` cannot make it create anything else. A request with an abstract class and no `$type` is refused too (400).
- `$type` must be a string. Collections don't get one (a JavaScript array has no place for it): a `List<Dog>` in a property declared as `object` comes back as an array.
- A dictionary with a key `"$type"` (read as an object) would be sent with a type id: send such a dictionary as a `Map`, which is written as it is.

### Shared objects and cycles

A server with [`ReferenceHandling.Preserve`](References.md) writes an object or collection that appears twice once, and the second time a reference to it: an extension of type 5 with the path to its first occurrence. The client reads it as the `references` option says:

- `'default'` and `'preserve'`: the same instance, cycles included (`order.customer === invoice.customer`, `node.parent.children[0] === node`). Objects and arrays are kept by their place while a response is read, and the objects of the array layout are made before their values are read, not by the generated constructor function after them, which costs a part of the speed. `'default'` reads without that until it meets the first reference, then reads the response again as `'preserve'` does: responses without references keep the full speed, responses with them are read twice. Use `'preserve'` when most responses have references.
- `'none'`: the extension as a `MsgPackExtension` (for a server that uses type 5 for an extension of its own).

Each value a reference points to is read once. Reading it again for each reference (as copies) would grow exponentially with values that hold references to each other: 341 bytes of data with 18 levels took 249 MB and half a second that way. A reference with a type id (an object of another class than the place declares, a `Dog` where an `Animal` is declared) puts the type name in `$type` of the shared object when it has none (it was read where `Dog` was the declared type, in a `List<Dog>`), so the object keeps its class when it is sent back.

With `'preserve'` the client also writes request bodies with references: an object, array or `Map` met again is a reference to its first occurrence, which the server reads as one instance (with or without `Preserve` in its options: data with references is read as with the option). Strings, `Date`s, bytes and the keys of a `Map` are always written in full. A reference where the server's declared type differs from the class gets the object's type id, as the value would: an instance of a model, or a plain object with `$type`. A plain object without `$type` (made by your code, or read only where `Dog` was declared) sent again where an `Animal` is declared has none, there or at its first place; the server then reads the reference as the object of its first place.

In a stream each value (`MsgPackStreamWriter.write`, each `readObject`) has its own references: they never point into another value. In the body of a stream head of the server the paths start at the body's root value (the element's position first); the reader takes that into account.

### Compressed bodies

LZ4 compressed bodies ([Compression.md](Compression.md): `Compression = Lz4Compression.Block` or `BlockArray` of LsMsgPack, MessagePack-CSharp's `Lz4Block` and `Lz4BlockArray`) are read with the optional `lz4` of `@lsmsgpack/client/lz4`, a decoder in TypeScript without dependencies. It is a separate entry point, so applications that don't import it don't carry it:

```ts
import { LsMsgPackClient, Decoder } from '@lsmsgpack/client';
import { lz4 } from '@lsmsgpack/client/lz4';

const api = new LsMsgPackClient({ baseUrl: 'https://example.com/', compression: lz4 });
const cached = new Decoder({ indexedSchema: false, compression: lz4 }).decodePlain(bytes);
```

With the option, the body after the schema (or the whole payload without one) is decompressed when it is a compressed body, in either layout; uncompressed bodies are read as before, and so are the values of a stream (`MsgPackStreamReader` takes the same option). The client only reads LZ4, it doesn't write it. A claimed length is checked before anything is allocated: against what the blocks can produce (LZ4 makes at most about 255 bytes of each byte), and against `maxDecompressedLength` (unlimited by default). Decompressing 168 KB (100 invoices) takes about 0.55 ms in Node 22, about as long as reading them: for responses over HTTP the browser's own decompression (the server's response compression) is faster.

### Keeping the schemas between page loads

The store lives in memory. To keep it (e.g. in `localStorage` or IndexedDB):

```ts
localStorage.setItem('schemas', JSON.stringify(api.store.export()));
// next time
const store = new SchemaStore();
store.import(JSON.parse(localStorage.getItem('schemas') ?? '{"schemas":[],"origins":{}}')); // the schemas, and the ids per server and route
const api = new LsMsgPackClient({ baseUrl, store });
```

A request lists the schemas its route received (`new SchemaStore({ advertiseByRoute: false })` for the most recent ones of the server, as for a route not called yet), see [WebFormatters.md](WebFormatters.md#schema-references-applicationx-lsmsgpack): the route is the path, with segments of digits and Guids as one. `import` computes the ids from the bytes again (they are hashes of the content), so a stored schema cannot pass for another one. A server that no longer has a schema (restarted, another instance) sends it inline, nothing breaks. The generated schema set needs no storage: it is in the code.

## Streams

`MsgPackStreamReader` and `MsgPackStreamWriter` read and write the streams of LsMsgPack's stream writer and reader (see [Streaming.md](Streaming.md)): values one after another, arrays, maps, str and bin of unknown length, a table as one schema (a head) followed by its rows, and an index at the end of the stream. Use them for data too large to hold at once: the rows are read as they arrive.

```ts
import { LsMsgPackClient } from '@lsmsgpack/client';

const api = new LsMsgPackClient({ baseUrl: 'https://example.com/' });
const reader = await api.stream('exports/orders.msgpack'); // the response body, read as it arrives
await reader.readHead();                                    // the schema of the table
for await (const order of reader.elements<Order>()) show(order); // the rows of an array of unknown length
```

- **Sources**: `api.stream(url)` reads a response body (`HttpError` for a status outside 200-299). Its `Accept` header asks for streams (`stream=unknown-length`), so an ASP.NET Core action returning an `IAsyncEnumerable<T>` sends its items as they come: `readHead()`, then `elements()` (see [streaming responses](WebFormatters.md#streaming-responses)). `new MsgPackStreamReader(source, options)` reads a `ReadableStream`, a `Response`, an (async) iterable of `Uint8Array` chunks (e.g. a Node stream), bytes, or a `Blob` or `File`. The options are the decoder's (`models`, `propertyNames`, `decimal`...), or a `decoder` and a `store`.
- **Objects**: `readObject()` reads the next value whole. With the indexed schema (the default) an object is its schema or a reference to it followed by the object, so outside a head it reads a root value of the stream. An inline schema goes into the store (`store.register`), so the objects after it that refer to it are read too. `values()` reads the root values until the stream ends.
- **A head and its body**: `readHead()` reads the schema that the .NET `WriteHead<TRoot>()` wrote. The next root value is its body, and `readObject()` and `elements()` read the objects in it at any depth, without a schema each.
- **Declared types**: with the described schema (`SchemaVersion.Described` in the writer's options) the reader knows the type of every place in the body: the rows of a `List<Order>` are `Order` objects without a type argument. With the plain schema, pass the type: `elements(Order)` (a class of the models) or `elements('Order')` (the type name of the schema entry). Without one, values are read as they are (arrays of values; values with a type id still become objects of their class). Write streams for JavaScript readers with the described schema.
- **Tokens**: `read()` moves to the next token: `tokenType` (`'arrayStart'`, `'integer'`, `'stringStart'`...), `length` (-1 for an unknown length), `depth`, `isKey`, `position`. Then `getValue()` (the whole value at a start token), `getString()`, `getBytes()`, `getDate()`, `getExtension()`, `getInteger()`, `getNumber()`, `getBigInt()`, `getBoolean()`, `skip()`, and `atEnd()` for loops over an array of unknown length.
- **Errors**: data that is not valid throws a `MsgPackError` with its position in the stream, with the same checks as the .NET reader (misplaced end sequences, a str of unknown length with other chunks than strings, nesting deeper than `maxDepth`, data that ends inside a value).
- **Memory**: one buffer holds the token being read, or the value being read whole. It grows only as far as data arrives and drops what was read when it needs room.

**The index.** With a source that reads at any position, the reader goes to an entry or a row without reading what comes before it: bytes, a `Blob` or `File`, `fileSource(handle)` for a file opened with Node's `fs/promises`, or HTTP range requests (`api.openSeekable(url)`, or `httpSource(url, { fetch, headers })`; the server must answer `Range` requests: a static file, or ASP.NET Core's `File(..., enableRangeProcessing: true)`).

```ts
const reader = api.openSeekable('exports/orders.msgpack');
const indexes = await reader.readIndexes();               // from the end of the stream: a few small requests
const orders = indexes.flatMap((i) => i.entries).filter((e) => e.identifier === 'orders').pop()!; // the last one is the current one
await reader.seek(orders);
await reader.readHead();
await reader.read();                                       // the arrayStart of the rows
await reader.seekElement(orders, 123456);                  // from the point before it
const order = await reader.readObject<Order>();
```

`readIndexes({ searchBackwards: true })` finds the last checkpoint of a stream whose writer stopped. A reader that reads the stream from the start skips the index.

**Writing.**

```ts
import { MsgPackStreamWriter } from '@lsmsgpack/client';

const writer = new MsgPackStreamWriter(writable, { models, index: { pointInterval: 1000 } }); // a WritableStream, or (chunk) => ...
writer.beginEntry('orders');
writer.writeArrayStart();
for await (const order of orders) {
  writer.write(order);   // as encode writes it: plain MsgPack, names, $type as the type id
  await writer.ready;    // the sink took the chunks so far
}
writer.writeEnd();
await writer.close();    // writes the index and closes the sink
```

- `write(value)` writes any value as `encode` does. `writeArrayHeader(n)` and `writeMapHeader(n)` start ones of known length. `writeArrayStart()`, `writeMapStart()`, `writeStringStart()` and `writeBinaryStart()` start ones of unknown length, `writeEnd()` ends them. In a str or bin of unknown length, `write` takes its chunks (a chunk never splits a character). Also `writeBinary`, `writeExtension`, `writeRaw`, `depth` and `position`.
- The writer refuses what the format does not allow, with the same checks as the .NET writer.
- The buffer goes to the sink at `flushThreshold` bytes (64 KiB). Await `ready` now and then so the chunks do not pile up while the sink is slow.
- With `index` it writes the index (entries, `pointInterval`, `checkpointInterval`, `metadata`, a `hasher`, an `observer`, `beginEntry`, `addPoint`), as the .NET writer does.
- Objects are written as plain MsgPack, like request bodies: maps of names, without a schema or a head. A .NET reader reads them with a serializer without the indexed schema (`UseIndexedSchema = false`, `PropertyNameCaseInsensitive = true` for camelCase names).

Not supported: writing heads or the indexed schema, a byte order other than the specification's (`EndianAction`), and appending to a stream.

## SignalR

`@lsmsgpack/client/signalr` is a hub protocol for `@microsoft/signalr` (version 8 and up), for a server with LsMsgPack.SignalR ([SignalR.md](SignalR.md)):

```ts
import { HubConnectionBuilder } from '@microsoft/signalr';
import { LsMsgPackHubProtocol } from '@lsmsgpack/client/signalr';

const connection = new HubConnectionBuilder()
  .withUrl('/chat')
  .withHubProtocol(new LsMsgPackHubProtocol({ decimal: 'string' }))
  .build();
```

- `name`: `'lsmsgpack'` (default, the server's `AddLsMsgPackProtocol()`), `'lsmsgpack-indexed'` (`AddLsMsgPackIndexedProtocol()`: the server writes each value with the indexed schema, smaller and faster to read for lists of objects, see [SignalR.md](SignalR.md#the-indexed-schema-lsmsgpack-indexed)) or `'messagepack'` (`AddLsMsgPackAsMessagePackProtocol()`, or Microsoft's `AddMessagePackProtocol()`).
- The other options are the decoder's and the encoder's: `decimal`, `int64`, `references`, `maxDepth`, `generateCode`.
- The protocol imports nothing of `@microsoft/signalr` (it has the shape of its `IHubProtocol`), so it adds no dependency.
- With `'lsmsgpack'` the messages have no schema (SignalR writes a message once for every connection it goes to): objects have the .NET property names (`message.User`), whatever `propertyNames` says, and only the properties in the data. LsMsgPack's server writes every value, so they are all there. A type id becomes `$type`, Guids are text, and DateTimes are `Date`s. The values follow the table in [Values](#values) for what the data holds.
- With `'lsmsgpack-indexed'` the values the server writes carry the described schema: they are read as over HTTP (declared types, defaults, type ids as `$type`, Guids as text), but with the .NET names unless you pass `propertyNames`. The values the client writes stay plain (`[nil, value]`), as its request bodies.
- Sent objects may use camelCase names (the server reads names ignoring case); `$type` is written as the type id, as in request bodies.
- Each argument, stream item and result is read and written on its own: with `references: 'preserve'` a shared object within one argument is one instance.

A decoder with `indexedSchema: false` (data without the schema, as the hub protocol reads it) also makes `$type` objects of type ids, and the value of a wrapped collection (`{ "": name, "@": [...] }`) its array. `decodePlain` (plain MsgPack of other writers) leaves a key `""` as it is.

## The server

Nothing to change: the [ASP.NET Core, Web API and MVC 5 formatters](WebFormatters.md) support it (`LsMsgPackHttpOptions.DescribeSchemas`, `CompleteSchemas` and, for camelCase request bodies, `PropertyNameCaseInsensitive`, on by default).

**Cross-origin requests** (the page is not served by the API's origin) need CORS to allow the request header and expose the response header:

```csharp
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
  .WithOrigins("https://app.example.com")
  .WithHeaders("Content-Type", "MsgPack-Schemas")   // and your other headers (Authorization...)
  .WithExposedHeaders("MsgPack-Schema")
  .SetPreflightMaxAge(TimeSpan.FromHours(2))));     // see below
// ...
app.UseCors();
```

What happens without them depends on the generated schema set (`schemas` or `store.preload`):

- **Without the schema set** it still works: the client cannot read `MsgPack-Schema`, so it keeps no schema, never sends `MsgPack-Schemas` and gets every schema inline.
- **With the schema set** the client sends `MsgPack-Schemas` on every request from the first one on, GETs too. A policy that does not allow that header (`WithHeaders(...)` without it) makes the browser refuse **every request** (the preflight fails, `fetch` throws a `TypeError`). `AllowAnyHeader()` allows it. Without the exposed header the server still refers to the preloaded schemas, so the responses can be read; only schemas outside the set come inline every time.

A request with `MsgPack-Schemas` is not a "simple" request, so the browser sends a preflight (`OPTIONS`) before it, GETs included. It keeps the answer for `Access-Control-Max-Age` seconds, 5 when the server does not send it: set `SetPreflightMaxAge` (browsers cap it, Chromium at 2 hours). A POST with a MsgPack body has the preflight anyway (its `Content-Type` is not one a form can send). If the preflights cost more than the schemas, pass `negotiateSchemas: false`: GETs are simple again and every response carries its schema inline.

The `Accept` header needs no CORS setting.

## Values

| .NET | JavaScript | Default (left out, without models) |
|---|---|---|
| `string`, `Uri`, `Version` | `string` | `null` |
| `bool` | `boolean` | `false` |
| `int`, `short`, `byte`, `uint`..., `float`, `double` | `number` | `0` |
| `long`, `ulong` | `number` (`int64: 'bigint'`: `bigint`) | `0` |
| `decimal` | `number` (`decimal` option) | `0` |
| `BigInteger`, `Int128`, `UInt128` | `bigint` | `0n` |
| enums | `number` (the underlying value; the models have a const object with the names), `string` for an enum the server writes by name ([Enums.md](Enums.md)) | `0` |
| `char` | `string` (one character) | `'\0'` |
| `Guid` | `string` (`"0f8fad5b-d9cb-469f-a165-70867728950e"`) | `"00000000-0000-0000-0000-000000000000"` |
| `DateTime` | `Date` (`dates: 'iso'`: UTC text), from LsMsgPack's extension type 6 (the server's default) or a timestamp: a `Utc` or `Local` value is its moment, an `Unspecified` one its clock time as UTC | `0001-01-01T00:00:00Z` (`DateTime.MinValue`) |
| `DateTimeOffset` | `Date` (`dates: 'iso'`: text with the offset) | `0001-01-01T00:00:00+00:00` |
| `TimeSpan` | `string`, `"1.02:03:04.5000000"` (as System.Text.Json) | `"00:00:00"` |
| `DateOnly`, `TimeOnly` | `string`, `"2026-10-13"`, `"12:34:56.2500000"` | `"0001-01-01"`, `"00:00:00"` |
| `byte[]`, `Memory<byte>` | `Uint8Array` | `null` |
| classes | objects (instances of the models) | `null` |
| structs | objects | an object with its defaults |
| `Nullable<T>` | as `T` | `null` |
| lists, arrays, sets | arrays | `null` |
| dictionaries | objects (keys as text, like JSON) | `null` |
| `KeyValuePair<K, V>` | `{ key, value }` (`{ Key, Value }` with `propertyNames: 'dotnet'`) | `{ key: default, value: default }` |
| tuples | arrays | `null` |
| `object`, interfaces | what the value is (an object of its class with a type id, its name in `$type`) | `null` |

The rule: what System.Text.Json writes, except dates (`Date`) and bytes (`Uint8Array`). `[DefaultValue(x)]` makes `x` the default. With models the default is what the class sets (the .NET constructor's value).

Untyped values (`object` properties, the elements of `List<object>`) are read as they are: numbers, strings, `Date` for timestamps and LsMsgPack's DateTime (extension type 6), `Uint8Array` for bytes, objects (with `$type`) for values with a type id. The `dates: 'iso'` option does not apply to them.

## The described schema

The indexed schema only has names: `{ "Invoice": ["Id", "InvoiceNumber", ...], ... }`. With the array layout (the default) a value the server left out is `nil`, so a client without the classes cannot know whether that was `0`, `false` or `null`, nor whether a nested array is an object or a list. .NET clients know it from their classes, this client from the generated models or from version 2 of the schema.

The client asks for it: `Accept: application/x-lsmsgpack; schema=described, application/x-lsmsgpack; q=0.9`. The server sends the schema with the type of each property (format: [schema.md, version 2](schema.md#version-2-the-declared-types)). The body is the same bytes, and the schema has its own id, so it is negotiated and referred to like a plain one (see [WebFormatters.md](WebFormatters.md#schema-version-2-for-clients-without-the-classes)). The second media type is for servers that do not know the parameter, and for ASP.NET Core's content negotiation, which only matches a parameter it lists.

A server that does not describe schemas (`DescribeSchemas = false`, `NegotiateSchemas = false`) sends the plain schema. With models the client still reads everything (the models know the types, and the root when it has a type id). Without them values with a type id become objects (left out properties `null`), the root and other objects stay arrays of their values.

Without the described schema the client does not know the type of the root, and it does not guess: the first entry of the schema is the root's class only when the root is an object (for a `List<Invoice>` it is `Invoice`), and an object written as an array of its values looks like a list. So the root of such a payload is an object only when it has a type id. Keep `DescribeSchemas` on (the default) when the client needs it.

The client keeps an inline schema only when its hash equals the `MsgPack-Schema` response header (SHA-256 over the version byte and the bytes, first 16 bytes).

## Security

- **Schemas from the server are data.** A schema is kept only when its hash is the id the server names, ids are compared in full (SHA-256, 128 bits). Lengths in the data are checked against the rest of the bytes before anything is allocated, nesting is limited to `maxDepth` (256), and data after the value is refused.
- **`__proto__`** as a property name or dictionary key becomes an own property (as `JSON.parse` makes it), never the prototype.
- **Generated code.** For speed, the client generates a function per schema entry that creates and fills its objects (an object of the array layout is created once its values are read: a model's class without its constructor, its fields get the values the constructor sets), and one per model class that writes its properties (`new Function`). The property names from the schema and the models are only embedded as JSON string literals, a schema with a property `__proto__` uses the generic path. With a Content-Security-Policy without `'unsafe-eval'` the generic path is used automatically (`new Function` throws once). `generateCode: false` turns it off; reading is then 30-35% slower (below), writing with the models about 15%.
- **Types from the data**: a type id only picks a class of the models or makes a plain object, nothing else is created.

## Performance

100 invoices of the `BenchmarkInvoices` model, written by the ASP.NET Core formatters (`npm run bench` in `clients/typescript`, Node 22, i7-7700K, one run, October 2026):

| | raw | gzip | read |
|---|---|---|---|
| JSON (System.Text.Json, every property) | 316.7 KB | 42.1 KB | 1080 µs `JSON.parse` of the string, 1290-1310 µs with the UTF-8 decoding |
| `application/msgpack` with @msgpack/msgpack (November 2026) | 266.9 KB | 41.5 KB | 2880-2890 µs; this client 1890-1900 µs |
| `application/x-lsmsgpack`, schema reference | 166.9 KB | 38.0 KB | 674-676 µs; with the models 668-680 µs |
| the same without generated code (CSP) | | | 948-967 µs; with the models 959-968 µs |

One invoice: JSON 4022 bytes (1048 gzipped), schema reference 2111 bytes (810 gzipped, -23%). The described schema is about 300 bytes larger than the plain one and is sent once per client and schema (with the generated schema set: never).

Writing the same objects as a request body: `JSON.stringify` 1230-1250 µs (1580 µs with the UTF-8 encoding), this client 1100-1190 µs, with the models 1110-1230 µs, @msgpack/msgpack 2400-2450 µs. In the browser both formats cost about the same; the server reads MsgPack about 3.5x faster than System.Text.Json reads JSON (LsMsgPack, names).

## Generic classes

The models have a class per closed generic type: `Page<Invoice>` is `class Page_Invoice`, as NSwag makes `PageOfInvoice`. Each closed type has its own schema entry, property types and constructor values (a field of type `T` is `0` in `Page<int>` and `null` in `Page<Invoice>`), and `$type` (on the prototype) is the name of the closed type. A generic interface `Page<T>` that the closed classes implement, so the code can say `Page<Invoice>`, is not generated (yet).
