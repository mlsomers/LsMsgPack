# Changelog

## Unreleased

- AI agents (MCP): the extension registers the LsMsgPack MCP server with VS Code (chat agent mode) and lets it read MsgPack from the debugger: where the program is paused, the variables that hold bytes, and their bytes decoded into JSON with comments. Other agents (Claude Code...) find the window through a lock file in `~/.lsmsgpack/ide`. **MsgPack: Copy MCP Server Configuration...** copies their configuration. Settings `lsmsgpack.mcp.enabled` and `lsmsgpack.mcp.allowAnyExpression` (only variable and member paths by default).
- The objects pane also opens by itself for data without a schema that looks like objects: type ids, or maps with the same names as keys (a list of records, as Python or JavaScript write them). Setting `lsmsgpack.showObjectsAt` (`certain`, `high` by default, `medium`, `low`), the tooltip of the Objects button tells why.
- The extension now starts when VS Code has started (`onStartupFinished`), so agents can reach the debugger without opening the explorer first.
- Bytes written as text: hex in groups of different lengths (`91 c4 30 000102...`) is read too.
- Without "ignore errors" the reading stops at the first error. The data after it was read as items that follow each other.

## 2026.10.5

- First version: the MsgPack Explorer of the Visual Studio visualizer for VS Code. Item tree, hex view, properties, validation, objects (indexed schema), search, display limit, endianness and ignore errors.
- Reads `byte[]`, streams, `List<byte>` and other `IEnumerable<byte>`, `Memory<byte>`, HTTP content and (base64) strings while debugging .NET, typed arrays and buffers in JavaScript, bytes in Python, and array elements in any debugger.
- Opens `.msgpack` files, and bytes written as text from the clipboard or the selection.
