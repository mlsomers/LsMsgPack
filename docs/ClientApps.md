# Client applications (WinForms, WPF, MAUI, Avalonia...)

The serializers target .NET Standard 2.0 and 2.1, so they run in any .NET client: WinForms and WPF (.NET Framework 4.6.2+ or .NET), MAUI, Avalonia, Uno, console apps and services. This page shows how to save and load files and how to talk to a web service.

Which package
-------------

| Package | Use it when |
|---|---|
| `LtMsgPack` | Normal use: saving and loading files, calling web services, caches, queues. It's several times faster than LsMsgPack and System.Text.Json (see the [README](../README.md#which-package)). |
| `LsMsgPack` | You want to look inside MsgPack data in your app: `MsgPackItem.Unpack` gives you the tree of MsgPack items (maps, arrays, strings...) of any MsgPack payload, also data you don't have classes for. |

Both write and read exactly the same data with the same settings, so a file written by one can be read by the other.

Basic usage
-----------

```csharp
public class MyClass
{
  public string Name { get; set; }
  public int Quantity { get; set; }
  public List<object> Anything { get; set; }
}

MyClass message = new MyClass()
{
  Name = "Test message",
  Quantity = 100,
  Anything = new List<object>(new object[] { "first", 2, false, null, 4.2d, "last" })
};
```

With **LtMsgPack** (create one serializer per set of options and keep it, it's thread-safe and builds its plans per type once):

```csharp
using LtMsgPack;

LtMsgPackSerializer serializer = new LtMsgPackSerializer(); // or LtMsgPackSerializer.Default

byte[] buffer = serializer.Serialize(message);
MyClass returnMsg = serializer.Deserialize<MyClass>(buffer);
```

With **LsMsgPack**:

```csharp
using LsMsgPack;

byte[] buffer = MsgPackSerializer.Serialize(message);
MyClass returnMsg = MsgPackSerializer.Deserialize<MyClass>(buffer);

// Looking inside: the items of the payload (with the indexed schema, the schema followed by the body)
MpRoot items = MsgPackItem.UnpackMultiple(buffer);
```

The settings are in `LtMsgPackOptions` (LtMsgPack) and `MsgPackSettings` (LsMsgPack). Both derive from `MsgPackOptions`, so the format settings have the same names. The defaults are a good choice when only your own application reads the data. See [schema.md](schema.md) for what they do.

Saving and loading files
------------------------

```csharp
LtMsgPackSerializer serializer = new LtMsgPackSerializer();

// Save
byte[] data = serializer.Serialize(document);
await File.WriteAllBytesAsync(path, data);   // .NET Framework: File.WriteAllBytes, or a FileStream with WriteAsync

// Load
byte[] data = await File.ReadAllBytesAsync(path);
MyDocument document = serializer.Deserialize<MyDocument>(data);
```

There are also overloads that write to and read from a `Stream` (`serializer.Serialize(document, stream)`, `serializer.Deserialize<MyDocument>(stream)`). They work synchronously, so in a UI app read the file into memory asynchronously first (as above), or run the whole thing in `Task.Run` for large files.

Things to keep in mind for files:
- **Versions of your app**: with the indexed schema (the default) every file starts with the names of its properties, and the reader looks properties up by name. Files written by an older or newer version of your classes still load: added properties keep the value the constructor gave them, removed ones are skipped. Without the schema (`UseInexedSchema = false`) set `ObjectLayout = ObjectLayout.Map`, or the reader's property order must match the writer's.
- **Keep the schema in the file.** `WriteSchemaReference` replaces the schema by an 18 byte reference to a schema held in a `SchemaStore`. That makes files unreadable unless you also keep the store (`SchemaStore.Export` / `Import`), so leave it off for files.
- **Polymorphic documents** (a `List<IShape>` holding circles and squares) work out of the box: a type id (the short class name) is written where the type differs from the declared type. Renaming such a class breaks old files, so give them stable ids with `[XmlRoot("...")]` and the `XmlRootAttributeTypeResolver`, or your own type resolver. See [schema.md](schema.md#polymorphic-class-hierarchy-support), also for when the reader needs `CacheAssemblyTypes`.
- **Exchanging files with other programs** (Python, JavaScript, another MsgPack library): use a preset, e.g. `new LtMsgPackSerializer(LtMsgPackPresets.Generic())`, see [Compatibility.md](Compatibility.md).
- **Looking inside a file** while developing: open it in MsgPack Explorer (see the [README](../README.md)).

Calling a web service
---------------------

**When the server uses the [web formatters](WebFormatters.md)** (ASP.NET Core, Web API 2 or MVC 5), add the `LsMsgPack.AspNet.WebApi` package to the client. It has a `MediaTypeFormatter` for `HttpClient` that works on any platform:

```csharp
using LsMsgPack;
using LsMsgPackWebApiFormatters;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;

// Once, for the lifetime of the app
LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
HttpClient client = new HttpClient(formatter.CreateHandler()) { BaseAddress = new Uri("https://example.com/") };
client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack));

// Per call
HttpResponseMessage response = await client.PostAsync("api/orders", new ObjectContent<Order>(order, formatter, MsgPackMediaTypes.XLsMsgPack));
response.EnsureSuccessStatusCode();
Order saved = await response.Content.ReadAsAsync<Order>(new MediaTypeFormatter[] { formatter });
```

`application/x-lsmsgpack` sends the indexed schema and type ids, and with `formatter.CreateHandler()` the server stops repeating schemas the client already has. See [WebFormatters.md](WebFormatters.md#httpclient) for the details.

**Any other server** that speaks MsgPack (or when you'd rather not depend on `System.Net.Http.Formatting`): serialize yourself with the settings the server expects:

```csharp
LtMsgPackSerializer serializer = new LtMsgPackSerializer(LtMsgPackPresets.Generic()); // plain maps, see Compatibility.md

ByteArrayContent content = new ByteArrayContent(serializer.Serialize(order));
content.Headers.ContentType = new MediaTypeHeaderValue(MsgPackMediaTypes.MsgPack);
HttpResponseMessage response = await client.PostAsync("api/orders", content);
response.EnsureSuccessStatusCode();
Order saved = serializer.Deserialize<Order>(await response.Content.ReadAsByteArrayAsync());
```

Trimming and AOT
----------------

Both serializers find the properties of your classes through reflection. When the app is trimmed (iOS, Android or desktop apps published with full trimming, Native AOT), make sure the trimmer keeps the model classes and their properties, for example by marking the assembly with your models as a root: `<TrimmerRootAssembly Include="MyApp.Models" />`.
