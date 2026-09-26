# LsMsgPack
MsgPack debugging and validation tool also usable as Fiddler plugin

More info about this application (and screenshots) can be found at:
http://www.infotopie.nl/open-source/msgpack-explorer

[!["Buy Me A Coffee"](https://www.buymeacoffee.com/assets/img/custom_images/yellow_img.png)](https://www.buymeacoffee.com/mlsomers)

[![.NET](https://github.com/mlsomers/LsMsgPack/actions/workflows/dotnet.yml/badge.svg)](https://github.com/mlsomers/LsMsgPack/actions/workflows/dotnet.yml)

Library Usage Example
---------------------
Although the original was optimised for debugging and analysing, some compiler directives have been added to exclude keeping track of all offsets and other overhead needed for debugging. It has been expanded to support serialization and deserialization of .Net classes (using the properties) similar to other xml and json serializers.

Install the `LsMsgPack` NuGet package (or add LsMsgPack.dll as a reference).

```csharp
public class MyClass
{
    public string Name { get; set; }
    public int Quantity { get; set; }
    public List<object> Anything { get; set; }
}

public void Test()
{
    MyClass message = new MyClass()
    {
        Name = "Test message",
        Quantity = 100,
        Anything = new List<object>(new object[] { "first", 2, false, null, 4.2d, "last" })
    };
    
    // Serialize
    byte[] buffer = MsgPackSerializer.Serialize(message);
    
    // Deserialize
    MyClass returnMsg = MsgPackSerializer.Deserialize<MyClass>(buffer);
}
```

Compatibility with other implementations
----------------------------------------
Serializing classes by creating name-value dictionaries of their properties is not an official standard, and to my surprise I found than a majority of MsgPack implementations do not, instead they simply string up a list of values. This is indeed efficient and will work well for the first version, however migrating to a new version may pose some compatibility challenges when introducing new properties over time.

For this reason I have submitted a [pull request]( https://github.com/msgpack/msgpack/pull/334/commits/c6a4935b9e0e38818cc1ef878db72621143bfcd7) to the official MsgPack specification, including a more standardized choice of solutions and in addition a standard way to support polymorphic class-hierarchies.

While using dictionaries diminishes the small size of a MsgPack message, it does help bring up the compatibility level with other serializers (XML / JSON) so that it can be used as a drop-in replacement.

To win back most of that size, LsMsgPack has a few options in `MsgPackSettings`:

- **Indexed schema** (`UseInexedSchema`, on by default): each message starts with a small schema listing every type and its property names once. The body then refers to them by index. This roughly halves the size of messages with repeating objects, and it stays tolerant to adding or removing properties. Other MsgPack implementations don't understand it, so turn it off when they need to read your data.
- **Type ids** (`AddTypeIdOptions`, `IfAmbiguious` by default): adds the type name (or a custom id) when a property holds a derived type. This gives polymorphic class hierarchies (a `List<IPet>` holding cats and dogs) out of the box, without `XmlInclude`-style attributes.
- **Type resolvers** (`TypeResolvers`): control how type ids are written and resolved, for example with short `[XmlRoot]` names, numeric ids, or by recognizing a type from its properties.
- **Plain MsgPack** (`UseInexedSchema = false`, `AddTypeIdOptions = Never`): plain maps of property names and values that any MsgPack implementation can read.

See [docs/schema.md](docs/schema.md) for examples of each wire format, their pros and cons, and how polymorphic class-hierarchy support works.

ASP.NET Integration
-------------------

Three NuGet packages add MsgPack support to ASP.NET controllers and to HttpClient, so they receive and return MsgPack the same way they handle JSON. Clients choose the format with the `Content-Type` and `Accept` headers: `application/msgpack` (or `application/x-msgpack`) for plain MsgPack that any implementation understands, or `application/x-lsmsgpack` for the configured settings (by default the indexed schema with type ids).

| Package | For | How to use |
|---|---|---|
| `LsMsgPack.AspNetCore` | ASP.NET Core 2.x and up | `services.AddControllers().AddLsMsgPackSerializerFormatters();` |
| `LsMsgPack.AspNet.WebApi` | ASP.NET Web API 2 | `config.Formatters.Add(new LsMsgPackMediaTypeFormatter());` |
| `LsMsgPack.AspNet.WebApi` | HttpClient (any platform) | `new ObjectContent<T>(value, formatter, MsgPackMediaTypes.XLsMsgPack)` and `response.Content.ReadAsAsync<T>(new[] { formatter })` |
| `LsMsgPack.AspNet.Mvc` | ASP.NET MVC 5 | `LsMsgPackMvc.LsMsgPackMvc.Register();` in `Application_Start`, and `return this.MsgPack(data);` in actions |

ASP.NET Core example:

```csharp
builder.Services.AddControllers()
  .AddLsMsgPackSerializerFormatters(); // optionally: (settings => settings.UseInexedSchema = false) for application/x-lsmsgpack

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
  [HttpPost]
  public ActionResult<Order> Post(Order order) => order; // MsgPack in and out when the client asks for it, JSON otherwise
}
```

See [docs/aspnet.md](docs/aspnet.md) for installation, configuration, error handling and complete examples for each package.


Fiddler Integration
-------------------

In order to use this tool as a Fiddler plugin, copy the following files to the Fiddler Inspectors directory (usually C:\Program Files\Fiddler2\Inspectors):

- MsgPackExplorer.exe
- LsMsgPackFiddlerInspector.dll
- LsMsgPack.dll

Restart fiddler and you should see a MsgPack option in the Inspectors list.

Visual Studio Integration
-------------------------

The tool can also be used as a debugging Visualizer in Visual Studio. It can be installed via the [Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=mlsomers.V2025102900).

Source documentation
--------------------

### Modules

#### LsMsgPack.dll
This module contains the "parser" and generator of MsgPack Packages. It breaks down the binary file into a hirarchical structure, keeping track of offsets and errors. And it can also be used to generate MsgPack files.

#### MsgPackExplorer.exe
The main winforms executable, containing a MsgPackExplorer UserControl (so it can easily be integrated into other tools such as Fiddler).

#### LsMsgPackFiddlerInspector.dll
A tiny wrapper enabling the use of MsgPack Explorer as a Fiddler Inspector.

#### LsMsgPackUnitTests.dll
Some unit tests on the core LsMsgPack.dll. No full coverage yet, but at least it's a start.

#### LsMsgPackNetStandard.dll & LsMsgPackNetStandardUnitTests.dll

A light version of the serializer. The parsing and generating methods are almost identical to the LsMsgPack lib, but with allot of overhead removed that comes with keeping track of offsets, original types and other debugging info. I'm planning to use this version in my projects that use the MsgPack format. It is published as the `LsMsgPack` NuGet package.

#### LsMsgPackFormatters.dll, LsMsgPackWebApiFormatters.dll & LsMsgPackMvc.dll

The ASP.NET integration packages (`LsMsgPack.AspNetCore`, `LsMsgPack.AspNet.WebApi` and `LsMsgPack.AspNet.Mvc`), see [docs/aspnet.md](docs/aspnet.md). Each has its own test project.

### Architecture

#### Object-model

![Hierarchy](https://github.com/mlsomers/LsMsgPack/blob/master/Hierarchy.png)

Each class can serialize/deserialize the associated MsgPack type. Types that have a variable length inherit from MsgPackVarLen.

#### Worker classes (or services)

![Hierarchy](https://github.com/mlsomers/LsMsgPack/blob/master/Services.png)

The MsgPackSerializer and MsgPackSettings are the ones that end-users are supposed to use (entry points).
