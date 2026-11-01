# LsMsgPack

MsgPack serializer for .NET classes (like the xml and json serializers) with an optional indexed schema and type ids for polymorphic object models, plus MsgPack explorer/debugging tools. Published on nuget.org (the libraries, the formatters, `LsMsgPack.Mcp`, `LsMsgPack.TypeScript`) and npm (`@lsmsgpack/client`) without known users yet, so **breaking changes are allowed** until there are signs of real use (the maintainer's decision): better defaults, behavior and format changes may go in, stated explicitly, the maintainer decides on them. Compatibility code for data of released versions stays only as a last resort in a slow path; remove it when it costs anything on a main path. Docs and these notes describe the current behavior only, without the history of earlier versions.

Project-specific notes live in a `CLAUDE.md` next to each project (loaded when you work in it). Before changing serializer behavior or the format, read `LsMsgPackCore/CLAUDE.md`: it has the rules both serializers share.

## Projects

| Folder | What |
|---|---|
| `LsMsgPackCore/` | `LsMsgPack.Core`: options, type resolving, schema, conversions, everything without `MsgPackItem`s; shared by both serializers |
| `LsMsgPackNetStandard/` | `LsMsgPack`: the item-tree serializer (`MsgPackItem`, `Types/`), used by the explorers |
| `LtMsgPack/` | `LtMsgPack`: the fast serializer, same bytes without the item tree; streaming; `Http/` shared by the web formatters |
| `LsMsgPackFormatters/`, `LsMsgPackWebApiFormatters/`, `LsMsgPackMvc/` | ASP.NET Core, Web API 2 + HttpClient, MVC 5 formatters (on LtMsgPack) |
| `LtMsgPack.Generator/` | source generator (contexts, generated readers/writers) |
| `LsMsgPack.Analyzers/` (+ `.CodeFixes`) | analyzers LSMP001-009, packed into LsMsgPack.Core |
| `ObjectDebugger/` | rebuilds object trees from payloads without the types; validation; search |
| `MsgPackExplorer/`, `LsMsgPackFiddlerInspector/`, `LsMsgPackVisualStudioPlugin/` | Windows-only tools (old-style .NET Framework projects) |
| `LsMsgPackVsCode/` | VS Code extension "MsgPack Explorer" + `Server/` (LsMsgPackInspector) |
| `LsMsgPackMcpServer/` | MCP server / CLI `lsmsgpack-mcp` |
| `LsMsgPackTypeScript/` | `lsmsgpack-ts`, TypeScript model generator |
| `clients/typescript/` | npm `@lsmsgpack/client` |
| `MicroFramework/` | own old copies of the sources, does not link the library files |

## Build and test

```bash
dotnet build LsMsgPack.slnf -c DEBUG                  # what CI does (.github/workflows/dotnet.yml), cross-platform projects only
dotnet test LsMsgPack.slnf --no-build -c DEBUG -m:1   # one project at a time (7 GB RAM: parallel runs were OOM-killed)
dotnet test LsMsgPackNetStandardUnitTests/LsMsgPackUnitTests.csproj -c Release --filter "TestCategory!=Benchmark"
dotnet test LsMsgPackNetStandardUnitTests/LsMsgPackUnitTests.csproj -c DebugKeepTrack --filter "TestCategory!=Benchmark"
dotnet test LsMsgPackInteropTests/LsMsgPackInteropTests.csproj -c Release
cd LsMsgPackVsCode && npm install && npm test         # after dotnet build of LsMsgPackVsCode/Server
dotnet test LtMsgPack.GeneratorTests
dotnet test LsMsgPack.AnalyzerTests
dotnet test LsMsgPackTypeScriptTests
cd clients/typescript && npm install && npm test        # live tests need dotnet build clients/typescript/test-server and LsMsgPackTypeScript
bash LtMsgPackAotSmokeTest/run.sh                       # NativeAOT with the generated code (clang, or gcc when clang is missing)
```

- `LsMsgPack.sln` also has the Windows-only projects: they reference the library projects but cannot be built on Linux, mention it when a change could affect them (target frameworks, public API). They do not get transitive project references (they reference both `LsMsgPack.csproj` and `LsMsgPack.Core.csproj`), and the `App.config` of MsgPackExplorer and LsMsgPackFiddlerInspector has hand-written binding redirects for System.Memory, System.Buffers, System.Numerics.Vectors and System.Runtime.CompilerServices.Unsafe: update them to the packages' assembly versions when those change (stale ones give a FileLoadException). LsMsgPack and Core only load System.Memory where they need it (the schema cache; `FrameworkTypeInfo`'s `Memory<byte>` methods are `NoInlining` and only called for those types), so hosts without the redirects keep working.
- Configurations: `Debug`, `Release`, `DebugKeepTrack`, `ReleaseKeepTrack`. KeepTrack defines `KEEPTRACK`: items remember offsets and lengths, errors become `MpError` items instead of exceptions (`ContinueProcessingOnBreakingError`), `PreservePackages` keeps the item trees. The explorer tools use it. `#if KEEPTRACK` code must keep compiling: build a KeepTrack configuration after touching `MsgPackItem` or `Types/`. The `.sln` builds the MCP projects only in the KeepTrack configurations.
- **Configuration mappings in project references** (`AdditionalProperties="Configuration=..."`): Visual Studio does not build references (`BuildProjectReferences=false`), so a reference to a configuration the solution build does not produce compiles against whatever is in that bin folder (an old build, members missing). Only map to a configuration the same solution build produces (`KeepTrackConfiguration`); the formatters and their test projects reference LtMsgPack and LsMsgPack without a mapping. To check: delete the `bin/Debug` folders, build `-c DebugKeepTrack`, then again with `-p:BuildProjectReferences=false --no-restore`; or emulate the IDE with `dotnet msbuild <project> -t:Rebuild -p:BuildProjectReferences=false -p:BuildingInsideVisualStudio=true -p:CurrentSolutionConfigurationContents=<SolutionConfiguration>...`.

## Language and targets

- LsMsgPack targets `netstandard2.0;netstandard2.1`, LsMsgPack.Core and LtMsgPack also `net8.0` (trim/AOT analyzers, `IsAotCompatible` on that target only). 2.1 exists for `RuntimeFeature.IsDynamicCodeSupported` (`PropertyAccessor.CanCompile`: not `IsDynamicCodeCompiled`, which is false on WebAssembly, where typed delegates are still 3-4.7x faster than reflection) and span APIs: write `#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER` (net8.0 does not define the NETSTANDARD symbols). .NET Framework picks 2.0, .NET 6/7 2.1, .NET 8+ net8.0, so the test projects (net8.0) run the net8.0 builds; the .NET Standard builds are only compiled. System.Memory and Unsafe are package references of the .NET Standard builds only.
- netstandard2.0 compiles as **C# 7.3**: no `??=`, switch expressions, `is not`, ranges, `using` declarations or nullable annotations. `Dictionary.TryAdd` comes from `Meta/MissingExtensions.cs`.
- Style: 2-space indentation, `_camelCase` fields, explicit types rather than `var`, comments explain why. `for` loops rather than `foreach` (except over an IEnumerable of unknown length, a Dictionary/HashSet, an iterator), backwards when the order cannot change the result, but never over span copies (slower backwards). Some files use a different brace style, match the file. Most files are UTF-8 with BOM, keep it when editing with scripts (Python `encoding='utf-8-sig'`).
- **Culture-sensitive string APIs are slow on Linux** (ICU): always pass `StringComparison.Ordinal` (`EndsWith`, `StartsWith`, `IndexOf(string)`, `Compare`).

## Packaging and docs

- The version comes from `CommonAssemblyInfo.cs` (read by `Packaging.props`). The npm client uses it as `yyyy.M.(d*100+build)`.
- Each packable project has a `README.md` next to its project file, packed as the package's page on nuget.org: absolute links, no mermaid. Keep their examples in sync with `docs/` when the public API changes (see `docs/CLAUDE.md`).

## Performance

- **Keep the output stable.** Performance changes must not change the bytes or the round trip results: verify with the `lsmsgpack-perf` skill (A/B equivalence and benchmark against master), which also covers profiling with `perf`.
- The baseline is **System.Text.Json** (default options, as ASP.NET Core uses it). Numbers per library are in their CLAUDE.md.

## Working with the maintainer

- Create branches from `origin/master` with `--no-track` (`git switch --no-track -c <branch> origin/master`), then check `git status -sb` shows no upstream. With a tracking branch the maintainer's push landed on master.
- Commit, push and open pull requests only when asked. Commit messages: a short subject, a wrapped body explaining what and why, then the attribution line the harness provides.
- Report measured numbers (A/B, same session) and state behavior changes explicitly, the maintainer decides on format and behavior changes.
