[Setup wizard](README.md) › [Sent](Network.md) › Other parties and languages

# Question 3: is it your server, or theirs?

When the other side isn't yours, you can't choose its library or settings. What you *can* choose depends on who defines the API.

### My server: others call my API

You publish an API (for partners, customers, mobile apps of other teams, scripts), and you want to offer MsgPack next to JSON. Clients in any language must be able to use it, and you can't update them when you change something.

The web formatters offer plain MsgPack (`application/msgpack`) to everyone and the compact LsMsgPack format to clients that ask for it, side by side with JSON. Because requests come from outside, this page also covers reading untrusted data.

Example: a logistics company offers `POST /api/shipments` and `GET /api/shipments/{id}`; a partner calls it from Python, another from Go, your own app with the compact format.

**→ [A public API](Setup-PublicApi.md)**

### Their server: I call someone else's service

Someone else's server (or message producer) speaks MsgPack, and your .NET app is the client. Their format is fixed: you adapt to it.

Example: a market data feed that publishes MsgPack messages, an IoT gateway, a Python service written by another team, a game server.

**→ [Calling another service](Setup-ForeignService.md)**

### Not sure?

If the other side is another team in your company that also uses .NET and these packages, you're really in the [.NET to .NET](NetworkDotNet.md) case: agree on the packages and use the compact format.
