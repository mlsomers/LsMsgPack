[Setup wizard](README.md) › [Sent](Network.md) › My .NET programs

# Question 3: over HTTP, or something else?

How the bytes travel decides which package does the work for you.

### HTTP with ASP.NET (a web API)

The server is an ASP.NET Core app with controllers or minimal APIs (or ASP.NET Web API 2 / MVC 5 on .NET Framework), and the client uses `HttpClient`.

Then you don't call the serializer yourself: the *web formatters* plug MsgPack into ASP.NET next to JSON. Controllers stay unchanged, and a client asks for MsgPack with an HTTP header. Between these formatters, the schema of a response type is sent only once per client, after that an 18-byte reference to it.

Example: an ASP.NET Core API at `https://api.example.com` and a MAUI app calling `GET /api/orders/42`.

**→ [.NET to .NET over HTTP](Setup-DotNetHttp.md)**

### Something else: a queue, a socket, a stream, gRPC bytes

The bytes go through a message broker (RabbitMQ, Azure Service Bus, Kafka, Redis pub/sub), a WebSocket or TCP connection, a named pipe, or a `bytes` field of another protocol. You call the serializer yourself and hand the bytes to the transport.

Example: an order service publishes `OrderPlaced` messages on RabbitMQ, and a shipping worker consumes them.

**→ [.NET messaging](Setup-DotNetMessaging.md)**

### Not sure?

SignalR, gRPC and similar frameworks have their own serializers; use *something else* only when you put LtMsgPack's bytes into them yourself. If you're starting from scratch and both ends are yours, HTTP with the formatters is the least code.
