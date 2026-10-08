# MsgPack Explorer for VS Code

Inspect MsgPack data while debugging, and in `.msgpack` files. At the top low level MsgPack items tree with HEX viewer and exact type information. The bottom half (may be toggled on or off) shows higher level objects with their properties.

**Requires the .Net 8 (or higher) runtime**.

![Screenshot of the debugging user interface](https://github.com/mlsomers/LsMsgPack/raw/HEAD/LsMsgPackVsCode/Screenshot.png "MsgPack explorer")

## While debugging

Pause the program, then:

- right-click a variable in the **Variables** view and choose **View as MsgPack**, or
- run **MsgPack: Inspect Expression...** from the Command Palette and type an expression (evaluated in the stack frame selected in the Call Stack view).

For .Net:

| Value | How it is read |
|---|---|
| `byte[]` |  |
| `MemoryStream` | `ToArray()`: the whole stream, its position does not change |
| other `Stream`s | seekable: from the start, then the position is put back. Not seekable: from the current position, after you confirm (the program cannot read those bytes any more) |
| `List<byte>`, `ArraySegment<byte>` and other `IEnumerable<byte>`, `Memory<byte>`, `ReadOnlyMemory<byte>`, `ReadOnlySequence<byte>` | converted to an array |
| `HttpResponseMessage`, `ByteArrayContent` and other `HttpContent` | `ReadAsByteArrayAsync().Result` |
| `string` | base64, hex or delimited values |

Other debuggers:

- **JavaScript** (Node.js, Chrome, Edge): `Uint8Array`, `Buffer`, `ArrayBuffer`, `DataView` and other typed arrays, arrays of numbers, strings.
- **Python** (debugpy): `bytes`, `bytearray`, `memoryview`, `io.BytesIO`, lists of ints, strings.
- **Any debugger**: the elements the Variables view shows for an array (also in groups like `[0..99]`), or a string holding the bytes.

Large values are read up to `lsmsgpack.maxBytes` (16 MB by default).

## AI agents (MCP)

AI agents can read MsgPack while you debug, through the MCP server that comes with the extension ([LsMsgPack MCP server](https://github.com/mlsomers/LsMsgPack/blob/HEAD/LsMsgPackMcpServer/README.md)). It decodes the data into JSON with comments (types from the indexed schema and type ids, timestamps, decimals, errors with their offsets), so the agent doesn't have to decode hex.

- **VS Code chat (agent mode)**: the server "MsgPack (LsMsgPack)" is listed in the MCP servers, nothing to configure.
- **Other agents** (Claude Code, Cursor, Claude Desktop...): **MsgPack: Copy MCP Server Configuration...** copies the command or JSON for them. Their server finds this window through a lock file in `~/.lsmsgpack/ide`, and uses the window whose folder holds its working directory.

The agent asks where the program is paused (`msgpack_debug_status`), which variables hold bytes (`msgpack_debug_locals`), and reads one (`msgpack_debug_read`), the same values as **View as MsgPack**. It only reads variable and member paths (`buffer`, `this._payload`, `items[2].Data`), unless `lsmsgpack.mcp.allowAnyExpression` allows any expression. A stream that cannot seek is only read after you confirm. A notification shows what is being read, and you can cancel it.

## Files and text

- `.msgpack`, `.MsgPack` and `.mpk` files open in the explorer (**Reopen Editor With...** switches to the text or hex editor). Other files: **MsgPack: Open File...**. The view follows changes of the file.
- **MsgPack: Explore Bytes from Clipboard** and **Explore Selected Text as MsgPack** (editor context menu) read bytes written as text: hex (`0x81A3...`, `81 A3 ...`, `81-A3-...`), base64, delimited decimal values (`[129, 163, ...]`, e.g. copied from a debugger) or a Python bytes literal (`b'\x81\xa3'`).

## Requirements

The data is read by the LsMsgPack library itself (the same code as the Visual Studio visualizer), in small .NET programs that come with the extension (the inspector, and the MCP server for AI agents). It needs the **.NET runtime 8 or later** (`dotnet` on the PATH, or set `lsmsgpack.dotnetPath`).

## Settings

| Setting | Default | |
|---|---|---|
| `lsmsgpack.dotnetPath` | `dotnet` | The `dotnet` executable that runs the inspector. |
| `lsmsgpack.displayLimit` | 1000 | The number of items shown at first (0: no limit). |
| `lsmsgpack.showObjectsAt` | `high` | When the objects pane opens by itself: `certain` (an indexed schema or type ids of LsMsgPack), `high` (also maps with the same names as keys, e.g. a list of objects from Python or JavaScript), `medium` (also one map with names as keys and values of different kinds), `low` (any map with text keys). |
| `lsmsgpack.maxBytes` | 16777216 | The most bytes read from the debugged program. |
| `lsmsgpack.chunkSize` | 49152 | Bytes per evaluation in the debugger (made smaller automatically when the debugger shortens long strings). |
| `lsmsgpack.mcp.enabled` | `true` | Lets AI agents read from the debugger through the MCP server (the bridge on 127.0.0.1 with a random token, and its lock file). |
| `lsmsgpack.mcp.allowAnyExpression` | `false` | Lets AI agents read any expression, including method calls that can change the program. User settings only. |

## Views:

- **MsgPack items**: the tree of the data, every item with its MsgPack type and value. Keys and values of maps are marked, items of the indexed schema are shown in blue, items read after an error in gray.
- **Bytes**: the hex view. The type byte of each item is red, the bytes holding a length are blue, bytes after the data are gray. The selected item is highlighted, clicking a byte selects its item.
- **Properties**: what the explorer's property grid shows for the selected item (type, offset, length, count, value...), with the description of the selected property.
- **Validation**: issues of the data (a smaller encoding would have saved bytes, keys of different types, duplicate keys, where reading stopped). Clicking one selects the item.
- **Objects**: the objects the data was written from, reconstructed without the types: type names and property names from the indexed schema of [LsMsgPack](https://github.com/mlsomers/LsMsgPack), names from the keys of maps (data without a schema, e.g. from other languages), or the values by position. Shown automatically when the data looks like objects (`lsmsgpack.showObjectsAt`, the tooltip of the Objects button tells why). Selecting an object selects its bytes.
- **Search**: strings containing the text, and values the text converts to (numbers, true/false, null, Guids, dates and times). Enter for the next, Shift+Enter for the previous.
- **Limit**, **Endian** and **Ignore errors** like the Visual Studio visualizer: show more items, read numbers in another byte order, keep reading after a breaking error (best effort).
- **Save**, **Copy hex**, **Copy base64** and **Refresh** (read the value again while the program is paused, or the file again).

## License

Apache License 2.0, © Matheu Louis Somers.
