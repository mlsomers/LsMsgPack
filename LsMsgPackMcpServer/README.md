# LsMsgPack MCP server

<!-- mcp-name: io.github.mlsomers/lsmsgpack -->

MsgPack for AI agents. An [MCP](https://modelcontextprotocol.io) server and a command line tool that decode MsgPack data into text an AI (or a person in a terminal) can read, and that read the bytes of a variable of a program paused in the debugger of **VS Code** or **Visual Studio**.

Language models read text, not bytes. Hex or base64 costs several tokens per byte, and decoding it by hand is slow and error-prone. Some data can't be decoded from the bytes at all: LsMsgPack's indexed schema writes property names as indexes, and a schema reference holds only the 16-byte id of the schema. This tool decodes the data with the LsMsgPack library itself (the code of MsgPack Explorer) and writes it as JSON with comments:

```text
MsgPack "doc1" 247 bytes from payload (byte[], Launch Api).
Structure: indexed schema (inline, 3 types) followed by the body
No errors, 0 warnings or comments.
Schema (inline): type ids and property ids in the body are indexes into these lists.
  #0 Invoice: Id, Customer, Billing, Lines, Date, Reference, Tags
  #1 Address: Street, Number, City
  #2 Line: Product, Quantity, Price

Objects (JSON with // comments: a type name comes from a type id, a name with ~ is inferred from the schema; dictionary keys that are not strings are not quoted):
{ // Invoice~
  "Id": 1234,
  "Customer": "Alice",
  "Billing": { "Street": "Main street", "Number": 12, "City": "Amsterdam" }, // Address~
  "Lines": [ // 2 items
    { "Product": "Apples", "Quantity": 3, "Price": 1.25 }, // Line~, Price: decimal
    { "Product": "Pears", "Quantity": 2, "Price": 0.99 } // Line~, Price: decimal
  ],
  "Date": "2026-10-05T12:30:00Z", // timestamp
  "Reference": "5bad8f0fcbd99f46a16570867728950e", // bin, 16 bytes, as Guid 0f8fad5b-d9cb-469f-a165-70867728950e
  "Tags": ["fruit", "fresh"] // 2 items
}
```

It reads any MsgPack, not only data written by LsMsgPack. Errors come with their offset, and the values read after an error are marked.

## Install

Requires the **.NET runtime 8 or later**.

```bash
dotnet tool install -g LsMsgPack.Mcp      # the command is lsmsgpack-mcp
```

From source: `dotnet publish LsMsgPackMcpServer -c Release -o <folder>`, then run `dotnet <folder>/LsMsgPackMcp.dll` in place of `lsmsgpack-mcp`.

## Add it to your agent

| Client | How |
|---|---|
| VS Code (Copilot agent mode) | Nothing to do with the [MsgPack Explorer extension](https://github.com/mlsomers/LsMsgPack/blob/HEAD/LsMsgPackVsCode/README.md): it registers the server "MsgPack (LsMsgPack)". |
| Claude Code | `claude mcp add lsmsgpack -- lsmsgpack-mcp` |
| Visual Studio 2022 (17.14+) | `%USERPROFILE%\.mcp.json` (or `.mcp.json` next to the solution): `{ "servers": { "lsmsgpack": { "type": "stdio", "command": "lsmsgpack-mcp" } } }` |
| Claude Desktop, Cursor, Windsurf... | `{ "mcpServers": { "lsmsgpack": { "command": "lsmsgpack-mcp" } } }` |

In VS Code, **MsgPack: Copy MCP Server Configuration...** copies these for the server that comes with the extension.

## Tools

| Tool | What it does |
|---|---|
| `msgpack_debug_status` | The IDEs with the MsgPack extension, their debug sessions, and where the program is paused. |
| `msgpack_debug_locals` | The variables of the paused stack frame with their types. The ones that can hold bytes are marked. |
| `msgpack_debug_read` | Reads the bytes of an expression in the paused program and decodes them. Reads `byte[]`, streams (seekable ones go back to their position), `Memory<byte>`, `List<byte>`, `HttpContent` and base64 or hex strings. In VS Code also JavaScript `Uint8Array`/`Buffer` and Python `bytes`. `schemaStore` reads the schema from the program's `SchemaStore` when the payload refers to one. `saveTo` also writes the bytes to a file. |
| `msgpack_decode` | Decodes `data` (hex, base64 or byte values), a `file`, or a `doc` decoded before. `schemas` takes a `SchemaStore.Export` (or one schema) for schema references. |
| `msgpack_explain_offset` | What the byte at an offset is: the items holding it, the object member, the header and content bytes, and the bytes around it. |
| `msgpack_search` | The items holding a text or a value, with offsets and object paths. |

Every decoded payload gets a document id (`doc1`, `doc2`...) for follow-up calls. The output can be narrowed and switched with these options:

- `view`: `objects` (default), `items` (the MsgPack encodings with offsets) or `both`.
- `path` (e.g. `Lines[2]`), `from`/`to` (a byte range), `maxNodes`, `maxString`.
- `offsets` adds the offset of every value. `issues` takes `errors`, `all` or `none`.
- `endian` and `continueOnError` (on by default).

## Debugging

The MsgPack extensions of VS Code ([MsgPack Explorer](https://github.com/mlsomers/LsMsgPack/blob/HEAD/LsMsgPackVsCode/README.md)) and Visual Studio (MsgPack Debugger Extension) listen on 127.0.0.1. They write a lock file with their port and a random token to `~/.lsmsgpack/ide` (`%USERPROFILE%\.lsmsgpack\ide`). The MCP server uses the IDE that started it, otherwise the one whose workspace holds its working directory (`ide` picks one when several run).

- By default only variable and member paths are read (`buffer`, `this._payload`, `response.Content`, `items[2].Data`), no method calls. The IDE setting can allow any expression: `lsmsgpack.mcp.allowAnyExpression` in VS Code (user settings only), Tools > Options > MsgPack Explorer in Visual Studio.
- A stream that can't seek is consumed by reading it, so the IDE asks the user first.
- The IDE settings can turn the bridge off: `lsmsgpack.mcp.enabled` in VS Code, and the same options page in Visual Studio.

## Command line

```bash
lsmsgpack-mcp decode payload.msgpack                # the objects
lsmsgpack-mcp decode payload.msgpack --view items   # the items with offsets and encodings
lsmsgpack-mcp decode --data "82 a4 4e 61 6d 65 ..." # bytes as text
lsmsgpack-mcp explain payload.msgpack 0x7b          # what the byte at 0x7b is
lsmsgpack-mcp search payload.msgpack Amsterdam
lsmsgpack-mcp debug status | debug locals | debug read <expression>
```

`lsmsgpack-mcp --help` lists the options. Without arguments it runs the MCP server on stdin/stdout.

More: [docs/Mcp.md](https://github.com/mlsomers/LsMsgPack/blob/HEAD/docs/Mcp.md).
