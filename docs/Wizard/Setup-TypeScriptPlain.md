[Setup wizard](README.md) › [Sent](Network.md) › [A browser or TypeScript client](NetworkBrowser.md) › Plain objects

# Setup: JavaScript or TypeScript front end without generated models

A front end (or Node.js service) calling your ASP.NET Core API, getting plain JavaScript objects back, like `JSON.parse` gives them. No build step.

## In short

| | |
|---|---|
| Server | `LsMsgPack.AspNetCore`: `AddLsMsgPackSerializerFormatters()` (and CORS when the page is on another origin) |
| Client | `@lsmsgpack/client` (npm): `new LsMsgPackClient({ baseUrl })` |
| Types | your own interfaces, or none |
| Keep in mind | set `$type` on new objects where the server expects a subclass |

```
dotnet add Shop.Api package LsMsgPack.AspNetCore
npm install @lsmsgpack/client
```

## The server

```csharp
using LsMsgPackFormatters;

builder.Services.AddControllers()
  .AddLsMsgPackSerializerFormatters(); // minimal APIs: builder.Services.AddLsMsgPack(), see WebFormatters.md
```

Controllers don't change, and JSON keeps working for everyone who doesn't ask for MsgPack.

Page on another origin than the API? CORS must allow the `Content-Type` header (a MsgPack body isn't a "simple" request) and, for schemas to be sent only once, the `MsgPack-Schemas` request header and the `MsgPack-Schema` response header:

```csharp
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
  .WithOrigins("https://app.example.com")
  .WithHeaders("Content-Type", "MsgPack-Schemas", "Authorization")
  .WithExposedHeaders("MsgPack-Schema")
  .SetPreflightMaxAge(TimeSpan.FromHours(2))));
// ...
app.UseCors();
```

## The client

```ts
import { LsMsgPackClient, HttpError } from '@lsmsgpack/client';

// One for the lifetime of the app: it remembers the schemas the server sent
const api = new LsMsgPackClient({ baseUrl: 'https://api.example.com/' });

const invoice = await api.get<Invoice>('api/invoices/42');
console.log(invoice.invoiceDate.getFullYear(), invoice.lines[0].quantity);

const saved = await api.post<Invoice>('api/invoices', invoice);

// Describe what you get, if you like (the client doesn't check it)
interface Invoice { id: string; invoiceDate: Date; total: number; lines: InvoiceLine[]; }
interface InvoiceLine { description: string | null; quantity: number; }
```

Replacing `fetch`: `api.get(url)` instead of `fetch(url).then(r => r.json())`. Errors throw an `HttpError` with `status` and the decoded `body`. Responses that are JSON or text (an error page) are decoded as such.

## Why these settings

- **The server sends a *described* schema** to this client: the property names *and* their types, once per response type. The client needs the types because the server leaves out `0`, `false` and `null` to save space; with the schema it knows which of those to fill in. So every property is there: `invoice.lines[3].discount` is `0`, not `undefined`.
- **After the first response**, the client tells the server which schemas it has, and the server refers to them with 18 bytes instead of resending them.
- **camelCase names** (`invoiceDate`), as ASP.NET Core's JSON has them, so code written for the JSON API keeps working. `propertyNames: 'dotnet'` keeps the .NET names (`InvoiceDate`).
- Values are what System.Text.Json would give, except that dates are `Date` objects and bytes `Uint8Array`: [the full list](../TypeScriptClient.md#values).

## Class hierarchies

When a property is declared as a base class or interface on the server (a `List<Animal>` holding cats and dogs), each object has its class name in `$type`:

```ts
const zoo = await api.get<Zoo>('api/zoo');
for (const animal of zoo.animals) {
  if (animal.$type === 'Cat') console.log(animal.name, 'is a cat');
}

zoo.animals.push({ $type: 'Cat', name: 'Kit', indoor: true }); // a new object: say which class
await api.post('api/zoo', zoo);                                // the server creates a Cat
```

Objects that came from the server keep their `$type` when you send them back. Only set it on new objects where the server can't tell the class from the declared type. The server only accepts classes that fit the declared type.

## Changing your classes later

Values are matched by name, through the schema of each response, so the server can add, remove and reorder properties without breaking this client: the new schema arrives with the next response. Renaming a property or class breaks code that uses the old name, as with JSON.

When you'd like typed classes after all, add the generated models: only the client's constructor changes, see [TypeScript with models](Setup-TypeScriptModels.md).

## Not HTTP: a WebSocket

```ts
import { Decoder, encode } from '@lsmsgpack/client';

const decoder = new Decoder();
socket.binaryType = 'arraybuffer';
socket.onmessage = e => handle(decoder.decode(new Uint8Array(e.data)).value);
socket.send(encode({ type: 'subscribe', symbol: 'MSFT' }));
```

On the server, write with `new LtMsgPackSerializer(new LtMsgPackOptions { SchemaVersion = SchemaVersion.Described })` and read what the browser sends with `MsgPackMediaTypes.ToPlain(new LtMsgPackOptions())` plus `PropertyNameCaseInsensitive = true` (`encode` writes plain MsgPack with the camelCase keys of your objects).

## Watch out

- **Every request is untrusted on the server**, see [Add-ons](Extras.md#reading-data-you-dont-trust).
- **`long` values** above 2^53 lose precision as numbers: `new LsMsgPackClient({ baseUrl, int64: 'bigint' })`.
- **`decimal`** is a `number` by default; for money with every digit use `decimal: 'string'`.
- Objects you build yourself are written as they are: their keys (camelCase) are matched by the server ignoring case, which costs it about 10% more than the .NET names.

## Next

- All client options: [TypeScriptClient.md](../TypeScriptClient.md#options).
- Also a .NET client for the same API: [.NET to .NET over HTTP](Setup-DotNetHttp.md).
