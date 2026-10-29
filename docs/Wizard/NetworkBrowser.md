[Setup wizard](README.md) › [Sent](Network.md) › A browser or TypeScript client

# Question 3: do you want TypeScript classes generated from your .NET classes?

The server side is always the same: the ASP.NET Core web formatters. The client is `@lsmsgpack/client`, a small npm package without dependencies that reads the responses into JavaScript objects and writes request bodies. It reads as fast as `JSON.parse` and writes about as fast as `JSON.stringify`, with smaller payloads. The question is what kind of objects you want in your front end.

### Yes: generated models

A tool, `lsmsgpack-ts`, reads the server's compiled assemblies (the `.dll` files of a build) and writes a TypeScript file with a class per .NET class: the same properties, the same types, and the same initial values the .NET constructors set. It also writes the schemas the server will use, so even the very first response is small.

Choose this when:
- your front end is TypeScript, and the backend is in the same repository or you can get its build output;
- you want `instanceof`, typed objects, and class hierarchies (`$type`) checked by the compiler;
- you already generate TypeScript for your API (NSwag, OpenAPI generators) and are used to a build step.

Example: a monorepo with `Shop.Api` (ASP.NET Core) and `web/` (React + TypeScript); `npm run models` regenerates `web/src/api/models.ts` after the API changes.

**→ [TypeScript with generated models](Setup-TypeScriptModels.md)**

### No: plain objects

No build step, nothing generated: install the package and call the API. The objects are plain JavaScript objects, typed by interfaces you write yourself (or not at all), just like the result of `JSON.parse`. The client learns the property types at runtime from the schema the server sends.

Choose this when:
- you write JavaScript, or want to try MsgPack before adding a build step;
- the front end lives elsewhere and has no access to the server's build output;
- you're replacing `fetch(...).then(r => r.json())` and want the same objects back.

**→ [TypeScript without generated models](Setup-TypeScriptPlain.md)**

### Not sure?

Start without models: it's one `npm install`. Adding the models later changes only the client's constructor call, the server stays the same.

Not HTTP but a WebSocket? Both pages have a section on that.
