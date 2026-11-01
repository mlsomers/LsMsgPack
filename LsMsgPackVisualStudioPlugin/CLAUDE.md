# Visual Studio plugin

Windows-only, old-style .NET Framework project (cannot be built on Linux). `Package/McpBridge.cs`, `DebuggerBridgeHandler.cs` (EnvDTE, C# 7.3) and `McpOptionsPage.cs` are the Visual Studio side of the MCP server's IDE bridge: keep them in sync with `LsMsgPackMcpServer` (`IdeBridge`) and `LsMsgPackVsCode/src/mcpBridge.ts` (see `../LsMsgPackMcpServer/CLAUDE.md`).
