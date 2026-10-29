[Setup wizard](README.md) › [Sent](Network.md) › [A browser or TypeScript client](NetworkBrowser.md) › Generated models

# Setup: TypeScript front end with generated models

A TypeScript front end (React, Angular, Vue, Svelte, or Node.js) calling your ASP.NET Core API, with TypeScript classes generated from the server's .NET classes.

## In short

| | |
|---|---|
| Server | `LsMsgPack.AspNetCore`: `AddLsMsgPackSerializerFormatters()` (and CORS when the page is on another origin) |
| Generator | `lsmsgpack-ts` (.NET tool, NuGet `LsMsgPack.TypeScript`): writes `models.ts` and `models.schemas.ts` |
| Client | `@lsmsgpack/client` (npm): `new LsMsgPackClient({ baseUrl, models, schemas })` |
| Keep in mind | regenerate the models when the classes change; CORS must allow the `MsgPack-Schemas` header |

```
dotnet add Shop.Api package LsMsgPack.AspNetCore
dotnet tool install -g LsMsgPack.TypeScript
npm install @lsmsgpack/client
```

## 1. The server

```csharp
using LsMsgPackFormatters;

builder.Services.AddControllers()
  .AddLsMsgPackSerializerFormatters(); // minimal APIs: builder.Services.AddLsMsgPack(), see WebFormatters.md
```

Controllers don't change, and JSON keeps working for everyone who doesn't ask for MsgPack.

When the page is served from another origin than the API (`https://app.example.com` calling `https://api.example.com`), CORS must allow the header with which the client lists its schemas, and expose the one the server answers with:

```csharp
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
  .WithOrigins("https://app.example.com")
  .WithHeaders("Content-Type", "MsgPack-Schemas", "Authorization")
  .WithExposedHeaders("MsgPack-Schema")
  .SetPreflightMaxAge(TimeSpan.FromHours(2))));
// ...
app.UseCors();
```

**Don't skip this:** with the generated schema set, the client sends `MsgPack-Schemas` on every request, and a CORS policy that doesn't allow it makes the browser refuse *every* request (`fetch` throws a `TypeError`).

## 2. Generate the models

Build the API, then point the generator at its output:

```bash
dotnet build Shop.Api -c Release
lsmsgpack-ts --assembly Shop.Api/bin/Release/net8.0/Shop.Api.dll --solution Shop.sln --out web/src/api/models.ts
```

- `--solution` finds the classes the way the analyzers do: the results and parameters of your controller actions (and minimal APIs). Instead, or as well, name them: `--root "List<Shop.Invoice>"` for a response type, `--request Shop.Order` for a request body.
- It writes `models.ts` (the classes) and `models.schemas.ts` (the schemas the server will send, so the client has them before its first request).
- Server with custom formatter options? Pass them with `--options Shop.Api.MsgPackSetup.Options` (a static member returning your `LtMsgPackHttpOptions`), so the models have the same properties.

What you get (abridged):

```ts
/** .NET Shop.Invoice */
export class Invoice {
  declare readonly $type: 'Invoice';
  id: string = '00000000-0000-0000-0000-000000000000';
  invoiceDate: Date = new Date(-62135596800000);
  retries: number = 3;                 // public int Retries { get; set; } = 3;
  lines: InvoiceLine[] | null = null;
}

export abstract class Animal { declare readonly $type: 'Cat' | 'Dog'; name: string | null = null; }
export class Dog extends Animal { declare readonly $type: 'Dog'; barks: number = 0; }

export const models = defineModels({ /* the .NET names and types of every property */ });
```

Make it part of the front-end build, so the models never fall behind:

```json
{
  "scripts": {
    "models": "dotnet build ../Shop.Api -c Release && lsmsgpack-ts --assembly ../Shop.Api/bin/Release/net8.0/Shop.Api.dll --solution ../Shop.sln --out src/api/models.ts",
    "prebuild": "npm run models"
  }
}
```

For a team or CI, install the tool in a [local tool manifest](https://learn.microsoft.com/dotnet/core/tools/local-tools-how-to-use) (`dotnet new tool-manifest`, `dotnet tool install LsMsgPack.TypeScript`) and call `dotnet tool run lsmsgpack-ts`.

## 3. The client

```ts
import { LsMsgPackClient, HttpError } from '@lsmsgpack/client';
import { models, Invoice, Order, Dog } from './api/models';
import { schemas } from './api/models.schemas';

// One for the lifetime of the app: it holds the schemas
export const api = new LsMsgPackClient({
  baseUrl: 'https://api.example.com/',
  models,
  schemas,
  headers: { Authorization: `Bearer ${token}` },
});

const invoice = await api.get<Invoice>('api/invoices/42'); // invoice instanceof Invoice

const order = new Order();                                 // the initial values of the .NET constructor
order.pet = Object.assign(new Dog(), { name: 'Rex' });     // declared as Animal: the server creates a Dog
await api.post('api/orders', order);

try {
  await api.get<Invoice>('api/invoices/0');
} catch (e) {
  if (e instanceof HttpError) console.log(e.status, e.body);
}
```

## Why these settings

- **Generated models** give typed objects that are real class instances (`instanceof`), with the values your .NET constructors set. A value the server left out to save space (a `0`, `false` or `null`) comes back as exactly what a .NET client would get. Class hierarchies work both ways: `$type` tells you the class of a response object, and an instance of `Dog` is sent so the server creates a `Dog`.
- **The schema set** means the server never has to send a schema: from the first request, responses refer to the schemas the client already has (18 bytes). This works because the server builds a schema from the classes alone, the same on every server instance and after restarts.
- **camelCase property names** (`invoiceDate`), as ASP.NET Core's JSON has them, so code written for the JSON API keeps working. `--property-names dotnet` keeps the .NET names.

## Changing your classes later

- **The server changes first** (it usually does): the client keeps working with its old models. A new property comes in as a plain value, a removed property keeps the model's initial value, a new class becomes a plain object. The server sends its new schema once, and the client learns it.
- **Regenerate** to get the new types: with the `prebuild` script above that happens on every build.
- **Renaming** a property or a class that's sent with its name (`$type`) breaks older clients, as with any API. Add the new one first, remove the old one later.
- Options of the generator (`--int64`, `--dates`, `--decimal`, `--property-names`) are part of the models; the client checks that it reads with the same ones.

## Not HTTP: a WebSocket

`Decoder` and `encode` work on any bytes:

```ts
import { Decoder, encode } from '@lsmsgpack/client';

const decoder = new Decoder({ models });
socket.binaryType = 'arraybuffer';
socket.onmessage = e => handle(decoder.decode(new Uint8Array(e.data)).value);
socket.send(encode(order, { models }));
```

On the server, write the messages with `new LtMsgPackSerializer(new LtMsgPackOptions { SchemaVersion = SchemaVersion.Described })` (each message has its schema), and read what the browser sends with a plain reader (`encode` writes plain MsgPack): `MsgPackMediaTypes.ToPlain(new LtMsgPackOptions())` with `PropertyNameCaseInsensitive = true`.

## Watch out

- **CORS**: see step 1. Without the exposed `MsgPack-Schema` header the client still works, but schemas outside the generated set are sent inline every time.
- **Every request is untrusted on the server**: the browser is not your code once it runs on someone's machine. See [Add-ons](Extras.md#reading-data-you-dont-trust).
- **`long` values** above 2^53 lose precision as numbers: generate with `--int64 bigint`.
- A Content-Security-Policy without `'unsafe-eval'` is detected; the client then uses a slower generic path (about 30% slower reading).

## Next

- All client options, values and performance: [TypeScriptClient.md](../TypeScriptClient.md). The generator: [TypeScriptModels.md](../TypeScriptModels.md).
- A .NET client for the same API: [.NET to .NET over HTTP](Setup-DotNetHttp.md).
