# LsMsgPack.TypeScript (lsmsgpack-ts)

Writes TypeScript for the .NET model classes a server sends and receives with LsMsgPack's web formatters (`application/x-lsmsgpack`):

- **Models**: a class per .NET class and struct (fields start with the values the .NET constructors set, `$type` with the type name, `extends` for base classes), interfaces, and enums as const objects.
- **A schema set**: the schemas the server will write for its response types, made by the server's own code, so the TypeScript client holds them before its first request and the server refers to them (18 bytes) from the first response on.

```bash
dotnet tool install -g LsMsgPack.TypeScript

lsmsgpack-ts --assembly Shop.Api/bin/Release/net8.0/Shop.Api.dll --solution Shop.sln --out web/src/api/models.ts
```

The roots (the types the server responds with) come from `--root <type>`, `[LtMsgPackSerializable(typeof(T))]` in the assemblies, or `--solution`, which finds them as LsMsgPack's analyzers do (controller actions, calls of the serializers). `--options <Type.Member>` takes the server's `LtMsgPackHttpOptions` from a static member, so the models and schemas follow its filters, property order and type names.

Documentation: [docs/TypeScriptModels.md](https://github.com/mlsomers/LsMsgPack/blob/master/docs/TypeScriptModels.md). The server's side: complete schemas in [docs/WebFormatters.md](https://github.com/mlsomers/LsMsgPack/blob/master/docs/WebFormatters.md#complete-schemas).
