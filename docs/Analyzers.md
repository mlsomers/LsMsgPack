# Analyzers

The `LsMsgPack.Core` package includes analyzers, so every project that references `LsMsgPackDebug`, `LsMsgPack` or one of the web formatter packages gets them. They warn about these things in the classes you serialize:

- **Values that are lost** because a left-out default doesn't match what the constructor sets (LSMP001 to LSMP003), or because null can't be told apart from a left-out value (LSMP006). Code fixes add or correct the `[DefaultValue]`.
- **Places where the data picks the type** that a reader creates (LSMP004, LSMP005), see [security.md](security.md).
- **Reads without property names**: `object` and `Dictionary<string, object>` (LSMP007).
- **Classes of other projects** that no analyzer checked (LSMP008).
- **JSON document types** (`JsonElement`, `JsonNode`, Json.NET's `JObject`...), which the serializers don't write or read (LSMP009).
- **Public fields that aren't serialized** (LSMP010): by default the serializers only write fields that opt in with an attribute.

The warnings show while you type in Visual Studio, VS Code (C# Dev Kit) and Rider, and in the build output.

## Which classes are checked

Only the classes your project serializes. Options classes, view models and anything else with initialized properties are left alone. A class is serialized when the project hands it to a serializer, or a serialized class reaches it:

- **Calls**: the type argument or `typeof` of `MsgPackSerializer.Serialize`/`Deserialize`, `LsMsgPackSerializer.Serialize`/`Deserialize` and `LsMsgPackHttpSerializer`. Without one, the type of the value passed to `Serialize`.
- **Your own generic methods** that pass their type parameter on to one of these (`Save<T>(T item) => MsgPackSerializer.Serialize(item)`), also through the interface methods they implement: the type arguments of their callers.
- **Contexts of the source generator**: `[LsMsgPackSerializable(typeof(Invoice))]`.
- **Controller actions**, when the project references one of the web formatters: the result type (`ActionResult<Invoice>`, `Task<List<Invoice>>`) and the parameters of the public methods of ASP.NET Core controllers (`ControllerBase`), Web API controllers (`ApiController`) and MVC 5 controllers (`Controller`). `[NonAction]` methods and `[FromServices]` parameters are skipped.
- **Values of ASP.NET Core results**, for actions that return `IActionResult`: the type of the value passed to `Ok(value)`, `Created...(..., value)`, `Accepted...(..., value)`, `StatusCode(code, value)`, `NotFound(value)`, `BadRequest(error)`, `Conflict(error)`, `UnprocessableEntity(error)`, `Unauthorized(value)` and to the constructors of `ObjectResult` and its subclasses (`new OkObjectResult(value)`). As with `Serialize`, a value declared as `object` tells nothing.
- **Minimal APIs** with `LsMsgPack.AspNetCore`: the type argument of `MsgPackBody<T>` (read) and `MsgPackResult<T>` (written) where they're named (handler parameters, `Results<MsgPackResult<Invoice>, NotFound>`), and the value of `MsgPackResults.MsgPack(value)` / `Results.Extensions.MsgPack(value)`, as for `Ok(value)`. `MsgPackBody<object>` reads into object (LSMP005, LSMP007).
- **HttpClient with the Web API formatter**: `GetFromMsgPackAsync<T>`, `ReadFromMsgPackAsync<T>`, `GetFromMsgPackAsAsyncEnumerable<T>`, `ReadFromMsgPackAsAsyncEnumerable<T>`, `PostAsMsgPackAsync<T>`, `PutAsMsgPackAsync<T>`, `PatchAsMsgPackAsync<T>`, `ReadAsAsync<T>`, `PostAsync<T>`, `PutAsync<T>`, `new ObjectContent<T>`. MVC 5: `new LsMsgPackResult(value)`.
- **What those reach**: the types of public properties and of the fields the serializers write, elements of collections, generic arguments, base classes, and the classes of the project that derive from any of them (a type id can name those).

Classes declared in another project are checked in that project. A project of models that never calls the serializers itself can ask for all its public classes and structs to be checked, with a reference to `LsMsgPack.Core` (the smallest package) or a package that depends on it:

```xml
<PropertyGroup>
  <LsMsgPackAnalyze>AllTypes</LsMsgPackAnalyze>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="LsMsgPack.Core" Version="..." />
</ItemGroup>
```

This also marks the assembly as checked (`[assembly: AssemblyMetadata("LsMsgPackAnalyze", "AllTypes")]`, written by the SDK's assembly info), so the projects using its classes don't report them (LSMP008). With `GenerateAssemblyInfo` set to `false`, add that attribute yourself.

Only the properties and fields the serializers write are checked: properties that are public, not static, with a public getter and setter, and fields the serializers select (see [Which fields are written](#which-fields-are-written)), neither of them ignored (`[IgnoreDataMember]`, `[JsonIgnore]`, `[XmlIgnore]`, MessagePack's `[IgnoreMember]`, `[NonSerialized]` on a field...). A property without a setter and a readonly field are written but never set from the data, so only LSMP009 applies to them.

### Which fields are written

Which fields the serializers write is a setting of their static filters (`FilterNonSettable`, `SerializedFields`, see [schema.md](schema.md#fields)), which the analyzers can't see. By default (`Attributed`) only fields with an attribute that opts them in are written: `[DataMember]`, `[JsonInclude]`, Json.NET's `[JsonProperty]`, `[Key]`... When your serializers write other fields, tell the analyzers (and LsMsgPack's source generator) with the same mode:

```xml
<PropertyGroup>
  <LsMsgPackFields>Public</LsMsgPackFields>   <!-- None, Attributed (the default), Public or All -->
</PropertyGroup>
```

`[assembly: LsMsgPackGenerate(Fields = SerializedFields.Public)]` sets it too and wins over the property. The presets `LsMsgPackPresets.MessagePackCSharp()` and `Nerdbank()` write public fields: set `Public` with them.

Change the severity of a rule or turn it off in `.editorconfig`, as for any analyzer:

```ini
[*.cs]
dotnet_diagnostic.LSMP004.severity = none
```

## Default values

The serializers leave out properties that hold their default value (`FilterDefaultValues`): the type's default (`0`, `false`, `null`), or the value of `[DefaultValue]`. How a reader fills them in depends on the layout:

- **Arrays** (`ObjectLayout.Array`, the default, and `application/x-lsmsgpack`): a left-out value is written as nil, and a reader reads a nil as the value that was left out: the `[DefaultValue]`, otherwise the type's default (`null`, `0`, `false`), as JSON reads them. Only values missing after the end of the array (a writer with fewer properties, or `TrimTrailingNulls`) keep what the constructor set.
- **Maps** (`ObjectLayout.Map`): a left-out value has no key, and a reader keeps what the constructor set. The web formatters' `application/msgpack` writes every value by default (`LsMsgPackPresets.Generic()`), so nothing is left out there.

So in a map, when the constructor sets something other than what was left out, the value is lost:

```csharp
public int Retries { get; set; } = 3;   // a map leaves out 0, a reader gets 3
```

`[DefaultValue(3)]` makes the writer leave out 3 instead, and write 0. Version 2 of the schema (`SchemaVersion.Described`) also tells the default to readers without the classes, e.g. JavaScript.

What the constructor sets is the property's initializer, the initializer of the field its getter returns, or the last assignment to either in the body of the constructor a reader calls: the parameterless one, or the one that takes the values of properties ([immutable classes and constructors with parameters](schema.md#immutable-classes)). Only constant values count: there is nothing to check for `= DateTime.Now`, `= Guid.NewGuid()` or `= new List<int>()`. An empty string isn't flagged (`= ""` is the usual way to avoid `null`, and reading `null` back as `""` is what such code wants).

A property that a parameter of that constructor takes isn't checked: a reader passes the value the writer left out. A class without a parameterless constructor and without one that takes the values of its properties (or a struct without an explicit parameterless one) is created without running a constructor (`ObjectCreation.ConstructorOrUninitialized`). A left-out property then keeps the type's default, as the writer assumes, so these rules don't apply to it.

### LSMP001: Initialized property without [DefaultValue]

```csharp
public int Retries { get; set; } = 3;            // LSMP001
[DefaultValue(3)]
public int Retries { get; set; } = 3;            // fixed
```

The code fix adds `[DefaultValue(...)]` with the initial value in the property's type: `3L` for a `long`, `(byte)4` for a `byte`, `typeof(decimal), "1.5"` for a `decimal` (an attribute can't take a decimal), `Color.Green` for an enum. It also adds `using System.ComponentModel;`.

### LSMP002: [DefaultValue] differs from the initial value

```csharp
[DefaultValue(5)] public int Level { get; set; } = 3;   // in a map 5 is left out and read back as 3
[DefaultValue(5)] public int Limit { get; set; }        // in a map 5 is left out and read back as 0
```

The code fixes write the initial value into the attribute, or (for an auto-property) the attribute's value into the initializer.

### LSMP003: [DefaultValue] of another type than the property

```csharp
[DefaultValue(1)] public long Count { get; set; } = 1;  // the int 1 never equals a long: the value is always written
```

`FilterDefaultValues` compares the value with the attribute's value using `object.Equals`, and a value of another type is never equal. The code fix writes the value in the property's type (`1L`). An enum property needs the enum value (`Color.Red`, not `1`).

### LSMP006: Null is read back as the initial value

```csharp
[DefaultValue("auto")] public string Mode { get; set; } = "auto";   // LSMP006: null is read back as "auto"
[DefaultValue(3)] public int? Retries { get; set; } = 3;             // LSMP006: null is read back as 3
```

`[DefaultValue]` (the fix of LSMP001) makes the writer leave out the initial value and keep null. In a map, a reader then gets null. In the array layout (`ObjectLayout.Array`, the default), both the left-out `[DefaultValue]` and null are written as nil, so a reader can't tell them apart and reads the `[DefaultValue]`. Only nullable properties (reference types and `Nullable<T>`) with a non-null initial value are reported.

If null must arrive, remove the initializer and the `[DefaultValue]`, or write objects as maps. If your writers use maps (e.g. only `application/msgpack`), turn the rule off.

## The data picks the type

A type id in the data names the type that a reader creates. The serializers only accept types that fit the declared type, so a property of your own interface can only get your own classes. A property declared as `object` can get anything the type resolver finds, including framework types that run code when they are created or filled (see [security.md](security.md)). The same goes for an interface or abstract class of the framework (`IDisposable`, `IEnumerable`, `Stream`...), which the dangerous framework types implement, and for collections without an element type (`ArrayList`, `Hashtable`).

Declare the type you expect. Where a property must hold values of several types, set `MsgPackOptions.TypeGuard` (e.g. an `AllowedTypesGuard`) and suppress the warning for that property.

### LSMP004: Property holds any type the data picks

```csharp
public object Value { get; set; }                       // LSMP004
public List<object> Items { get; set; }                 // LSMP004: the elements
public Dictionary<string, object> Extra { get; set; }   // LSMP004: the values
public IDisposable Resource { get; set; }               // LSMP004
public IShape Shape { get; set; }                       // fine: only your own classes implement IShape
```

`Type` and `MemberInfo` are refused by the serializers anyway, and aren't flagged.

### LSMP005: Data is read into any type it picks

```csharp
MsgPackSerializer.Deserialize<object>(bytes);                       // LSMP005
serializer.Deserialize<Dictionary<string, object>>(bytes);         // LSMP005
public IActionResult Post([FromBody] object body) => Ok();         // LSMP005, with a web formatter
```

Only reading is flagged: writing an `object` writes what it holds.

## Reads without property names

### LSMP007: Objects are read without their property names

```csharp
MsgPackSerializer.Deserialize<object>(bytes);                                         // LSMP007: an object[] of the values
public IActionResult Patch([FromBody] Dictionary<string, object> changes) => ...;     // LSMP007: 400, the dictionary can't be filled
```

By default (and for `application/x-lsmsgpack`) an object is written as an array of its values with the indexed schema: the property names are in the schema, or nowhere, and only the class it is read into gives them back. Read into `object`, such an object becomes an `object[]` of its values. Read into a dictionary with string keys and values of any type (`Dictionary<string, object>`, `IDictionary<string, object>`, `ExpandoObject`), it throws a `MsgPackException`, and a web formatter answers 400.

Read into a class instead. For a partial update, give the class nullable properties, so that a property the client didn't send stays null. Only maps without the schema carry the names (`ObjectLayout.Map` and `UseInexedSchema = false`, as for `application/msgpack`). If all the writers do that, suppress the warning.

A dictionary that the client also writes as a dictionary (not from a class) arrives as a map with its keys, so it works.

## Classes of other projects

### LSMP008: Serialized class of another project is not checked

The rules need the source of a class (its initializers and constructor), so a class is checked in the project that declares it, and only when that project serializes it or sets `LsMsgPackAnalyze` to `AllTypes` (see [Which classes are checked](#which-classes-are-checked)). Where a serialized type uses a class of another project that wasn't checked, LSMP008 says so: at the property, the base class, the action or the call. It isn't reported for:

- classes of NuGet packages (a reference in the NuGet package folder: `NuGetPackageRoot`, `.nuget/packages`), which the project can't change;
- the framework's classes (namespaces `System` and `Microsoft`);
- classes without public settable properties;
- assemblies marked as checked (`LsMsgPackAnalyze` = `AllTypes`, see above).

## JSON document types

### LSMP009: JSON document type is not serialized

```csharp
public JsonElement Payload { get; set; }                                     // LSMP009: a 500 when the response is written
public List<JsonNode> Items { get; set; }                                    // LSMP009
public JObject Body { get; set; }                                            // LSMP009 (Json.NET)
public IActionResult Receive([FromBody] JsonElement body) => ...;            // LSMP009: 400
MsgPackSerializer.Deserialize<JsonElement>(bytes);                           // LSMP009
```

MsgPack has no form for a JSON document. System.Text.Json's `JsonElement`, `JsonDocument` and `JsonNode` (`JsonObject`, `JsonArray`, `JsonValue`) have no settable state, so the serializers don't write or read their content. Writing one throws a `MsgPackException`, also from a web formatter (a `JsonElement` in a response answers 500). Reading one throws too, not always a `MsgPackException` (a `[FromBody] JsonElement` answers 400). The exceptions are the values without content: a `JsonObject` or `JsonArray` that is empty or only holds nulls is written as a map or array, an empty map or array is read into an empty one, and a nil is read as `default(JsonElement)`. Json.NET's `JToken` types (`JObject`, `JArray`, `JValue`...) aren't refused, but they are written as their own properties and read back empty or with an exception.

Hold the JSON text in a `string` (`element.GetRawText()`, `JsonDocument.Parse(text)`), or the values in a class of your own. An endpoint that passes JSON through (a webhook) can also stay on JSON: the formatters only answer MsgPack to clients that ask for it.

It is reported for the properties and fields of serialized classes (also get-only properties and readonly fields, which are written), and for the types that calls and actions read or write, also as an element or generic argument (`List<JsonElement>`, `ActionResult<JsonObject>`). `JsonNode` isn't reported as LSMP004.

## Fields

### LSMP010: Public field is not serialized

```csharp
public class Point
{
  public int X;                         // LSMP010: not written, a reader leaves it 0
  [DataMember] public int Y;            // written
  [JsonIgnore] public int Cache;        // left out on purpose: not reported
}
```

The serializers write properties, and fields only when they opt in (the default `SerializedFields.Attributed`, as System.Text.Json without `IncludeFields`). A public field without such an attribute is lost both ways, without an error. It isn't reported when:

- the project tells the analyzers that its serializers write other fields (`LsMsgPackFields` set to `Public` or `All`: then it is written and checked like the others) or none (`None`), see [Which fields are written](#which-fields-are-written);
- it is ignored (`[JsonIgnore]`, `[IgnoreDataMember]`, `[NonSerialized]`...);
- the class marks the members it serializes (`[DataContract]`, MessagePack's `[MessagePackObject]` with keys): a field without `[DataMember]` or `[Key]` is left out there on purpose;
- writing it changes nothing or fails: a readonly field that neither a reader fills (a collection) nor the constructor a reader calls takes, a field of a type that is never written (`Type`, `MemberInfo`, delegates) or of a JSON document type.

The code fixes add `[DataMember]` (from `System.Runtime.Serialization`; outside a `[DataContract]` class it only opts the field in), or make the field an auto-property: `{ get; set; }`, `{ get; }` for a readonly field, with its initializer and attributes (an attribute that can't be on a property gets the `field:` target). A property breaks code that passes the field by `ref` or changes a struct it holds in place (`model.Position.X = 1`), so check the uses first.
