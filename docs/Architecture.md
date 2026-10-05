# Source documentation and architecture

## Modules

| Project (folder) | Assembly / package | What it is |
|---|---|---|
| `LsMsgPackCore` | `LsMsgPack.Core` | What both serializers share and what doesn't depend on MsgPack items: the format settings (`MsgPackOptions`), media types, the indexed schema and its `SchemaStore`, property metadata and accessors, type resolvers, attributes and filters. |
| `LsMsgPackNetStandard` | `LsMsgPack` | The original serializer and parser. It turns objects into a tree of MsgPack items (`MsgPackItem`, `Types/`) and that tree into bytes, and back. Built with `KEEPTRACK` (the `DebugKeepTrack` / `ReleaseKeepTrack` configurations), the items remember their offsets and lengths and errors become items instead of exceptions, which is what the explorer tools use. |
| `LtMsgPack` | `LtMsgPack` | The fast serializer. It writes the same bytes and reads the same values as LsMsgPack with the same settings, without the item tree (typed writers and readers per type). Also has the options for other libraries (`LtMsgPackPresets`) and what the web formatters share (`Http/`). |
| `LsMsgPackFormatters` | `LsMsgPack.AspNetCore` | Input and output formatters for ASP.NET Core, see [WebFormatters.md](WebFormatters.md). |
| `LsMsgPackWebApiFormatters` | `LsMsgPack.AspNet.WebApi` | `MediaTypeFormatter` for ASP.NET Web API 2 and `HttpClient`, and the client side of the schema negotiation (`LsMsgPackSchemaHandler`). |
| `LsMsgPackMvc` | `LsMsgPack.AspNet.Mvc` | Model binder and action result for ASP.NET MVC 5 (.NET Framework 4.5 through 4.8). |
| `MsgPackExplorer` | `MsgPackExplorer.exe` | The WinForms debugging tool (Windows, .NET Framework). The `MsgPackExplorer` UserControl can be used in other tools, like the two below. |
| `LsMsgPackFiddlerInspector` | `LsMsgPackFiddlerInspector.dll` | A tiny wrapper that makes MsgPack Explorer a Fiddler Inspector. |
| `LsMsgPackVisualStudioPlugin` | Visual Studio extension | MsgPack Explorer as a debugger visualizer for `byte[]`, `List<byte>`, streams and more. |
| `ObjectDebugger` | `ObjectDebugger.dll` | Rebuilds the objects of a payload without their types (type and property names from the indexed schema), and the search of the explorers. Used by MsgPack Explorer and the VS Code extension. |
| `LsMsgPackVsCode` | VS Code extension | MsgPack Explorer for VS Code (TypeScript webview), for values of the debugged program and `.msgpack` files. `Server/` (`LsMsgPackInspector`, net8.0) reads the data with LsMsgPack (KEEPTRACK) and ObjectDebugger for it. See its [README](../LsMsgPackVsCode/README.md). |
| `MicroFramework` | | An old copy of the sources for the .NET Micro Framework. It doesn't link the current library files. |

Tests:
- `LsMsgPackNetStandardUnitTests` (`LsMsgPackUnitTests`): most test classes run against both serializers (an `Ls...` and an `Lt...` subclass each), `CrossLibraryTests` checks that they write the same bytes in all settings, and `BenchmarkInvoices` compares them with System.Text.Json.
- `LsMsgPackInteropTests`: what MessagePack-CSharp and Nerdbank.MessagePack read of our output and the other way around, the source of [Compatibility.md](Compatibility.md).
- `LsMsgPackFormattersTests`, `LsMsgPackWebApiFormattersTests`, `LsMsgPackMvcTests`: the web formatters, in process.
- `LsMsgPackVsCode/test`: the VS Code extension (`npm test`): reading bytes from debuggers, text formats, and the inspector process.

`LsMsgPack.slnf` contains the projects that build on any platform (what CI builds). `LsMsgPack.sln` also has the Windows-only projects (the explorer, the Fiddler inspector and the Visual Studio plugin).

## How it works

**LsMsgPack** works in two phases. Writing: object → tree of `MsgPackItem`s → bytes (each item writes itself). Reading: bytes → tree of `MsgPackItem`s → plain values (`object[]` for arrays, key/value pairs for maps) → objects. The item tree is what makes it useful for inspecting data, and also what makes it slower.

**LtMsgPack** skips the tree. Per type it builds a plan once (which properties, in which order, with typed getters and setters) and writes the values straight into a buffer, or reads them straight into the objects. When a value isn't what it expects, it falls back to reading it the way LsMsgPack does, so both return the same values.

Both use the decisions in `LsMsgPack.Core`: which properties are written (filters, property order), when a type id is needed and how it's resolved, and the indexed schema. See [schema.md](schema.md) for the wire formats.

## Object model (LsMsgPack)

![Hierarchy](https://github.com/mlsomers/LsMsgPack/blob/master/Hierarchy.png)

Each class can serialize and deserialize the associated MsgPack type. Types that have a variable length inherit from `MsgPackVarLen`.

## Worker classes (or services)

![Services](https://github.com/mlsomers/LsMsgPack/blob/master/Services.png)

`MsgPackSerializer` and `MsgPackSettings` are the classes end users are supposed to use (the entry points). For LtMsgPack these are `LtMsgPackSerializer` and `LtMsgPackOptions`.

The diagrams are the class diagrams in `LsMsgPackNetStandard/Architecture/`.
