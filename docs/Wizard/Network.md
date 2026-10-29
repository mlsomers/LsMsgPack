[Setup wizard](README.md) › Sent

# Question 2: who is on the other end?

When data is sent, the question is what the *receiver* understands, and how much both sides can share.

LsMsgPack's own format (`application/x-lsmsgpack`) is the most compact: it sends the property names once (a schema), and between these packages even that only once per client. It also carries class hierarchies (a `List<Animal>` with cats and dogs) without attributes. It needs these packages, or the TypeScript client, on both ends. Everyone else gets *plain* MsgPack (`application/msgpack`), which any MsgPack library in any language reads. A web server can offer both at the same time: each client asks for the one it understands, just like it asks for JSON.

### My own .NET programs, on both ends

You write the sender and the receiver in .NET: an ASP.NET Core API and the WPF, MAUI, Avalonia, Blazor or console app that calls it; two of your microservices; a worker that reads what your API puts on a queue.

**→ [Between my .NET programs](NetworkDotNet.md)**

### A browser or Node.js app (JavaScript / TypeScript) and my ASP.NET backend

A front end in React, Angular, Vue, Svelte (or plain JavaScript) calls your ASP.NET API, as a faster replacement for JSON. Or a Node.js service calls it.

**→ [A browser or TypeScript client](NetworkBrowser.md)**

### Programs I don't control, or in other languages

Third parties call your API, or you call theirs; the other side uses Python, Go, Java, MessagePack-CSharp, or you simply don't know.

**→ [Other parties and languages](NetworkOthers.md)**

### Several of these?

Common: one API serves your own .NET app, your web front end and some partners. The web formatters handle all of them with one registration, so walk through the wizard for each kind of client and combine the client-side parts; the server-side part is the same in each.
