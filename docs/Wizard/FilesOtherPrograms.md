[Setup wizard](README.md) › [Saved](Files.md) › Other programs

# Question 3: which other programs read the files?

MsgPack itself only defines basic values: numbers, strings, bytes, lists (arrays) and dictionaries (maps), plus timestamps. How a .NET *class*, a `Guid` or a `decimal` is stored is up to each library. To make sure the other program gets your objects back, the files have to be written in the way that program expects.

### JavaScript or TypeScript (a web page, Node.js, Electron)

The files are opened by JavaScript code: a web page where the user drops an exported file, a Node.js report generator, an Electron tool.

There's a client package for JavaScript and TypeScript, `@lsmsgpack/client`, that understands the default (schema) format. It can even turn the data into instances of TypeScript classes generated from your .NET classes. So the .NET side keeps its compact format, and JavaScript still gets complete objects.

**→ [Files for JavaScript](Setup-FilesJavaScript.md)**

If JavaScript also *writes* files that your .NET app reads back, that works too: the setup page explains how.

### Python, Go, Rust, other .NET libraries, or "anyone"

The files are read with a general-purpose MsgPack library: `msgpack` in Python, `vmihailenco/msgpack` in Go, `rmp-serde` in Rust, MessagePack-CSharp or Nerdbank.MessagePack in .NET, or you publish the format for whoever wants it.

These libraries read *plain* MsgPack: each object is a map of property names and values, as in JSON. LtMsgPack has ready-made settings ("presets") for this.

**→ [Plain MsgPack files](Setup-FilesPlain.md)**

### Both?

Choose *plain MsgPack files*: JavaScript reads plain MsgPack as well, with any MsgPack library (including `@lsmsgpack/client`).
