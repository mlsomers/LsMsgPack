# @lsmsgpack/client

Reads the `application/x-lsmsgpack` responses of [LsMsgPack's ASP.NET formatters](https://github.com/mlsomers/LsMsgPack/blob/master/docs/WebFormatters.md) into complete JavaScript objects, in browsers and Node 18+:

- every property of the .NET class is there, also the ones the server left out (`null`, `0`, `false`, `[DefaultValue]`...), with the default of its type;
- polymorphic values get the properties of their actual class;
- the schema (the property names) is sent once, then the server refers to it.

No dependencies. Request bodies are sent as plain MsgPack (`application/msgpack`).

```ts
import { LsMsgPackClient } from '@lsmsgpack/client';

const api = new LsMsgPackClient({ baseUrl: 'https://example.com/' });
const invoice = await api.get<Invoice>('api/invoices/42');
await api.post('api/invoices', invoice);
```

Documentation: [docs/TypeScriptClient.md](https://github.com/mlsomers/LsMsgPack/blob/master/docs/TypeScriptClient.md) (options, value conversions, CORS, the described schema).

## Development

```bash
npm install
npm test                                # builds, then runs test/*.test.mjs (node:test)
dotnet build test-server                # enables the tests against the ASP.NET Core formatters (skipped without it)
npm run bench                           # reading 100 invoices: JSON.parse, @msgpack/msgpack, this client (needs the test server)
```
