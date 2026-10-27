# Analyzers

The `LsMsgPack.Core` package includes analyzers, so every project that references `LsMsgPack`, `LtMsgPack` or one of the web formatter packages gets them. They warn about these things in the classes you serialize:

- **Values that are lost** because a left-out default doesn't match what the constructor sets (LSMP001 to LSMP003), or because null can't be told apart from a left-out value (LSMP006). Code fixes add or correct the `[DefaultValue]`.
- **Places where the data picks the type** that a reader creates (LSMP004, LSMP005), see [security.md](security.md).
- **Reads without property names**: `object` and `Dictionary<string, object>` (LSMP007).
- **Classes of other projects** that no analyzer checked (LSMP008).

The warnings show while you type in Visual Studio, VS Code (C# Dev Kit) and Rider, and in the build output.

## Which classes are checked

Only the classes your project serializes. Options classes, view models and anything else with initialized properties are left alone. A class is serialized when the project hands it to a serializer, or a serialized class reaches it:

- **Calls**: the type argument or `typeof` of `MsgPackSerializer.Serialize`/`Deserialize`, `LtMsgPackSerializer.Serialize`/`Deserialize` and `LtMsgPackHttpSerializer`. Without one, the type of the value passed to `Serialize`.
- **Your own generic methods** that pass their type parameter on to one of these (`Save<T>(T item) => MsgPackSerializer.Serialize(item)`), also through the interface methods they implement: the type arguments of their callers.
- **Contexts of the source generator**: `[LtMsgPackSerializable(typeof(Invoice))]`.
- **Controller actions**, when the project references one of the web formatters: the result type (`ActionResult<Invoice>`, `Task<List<Invoice>>`) and the parameters of the public methods of ASP.NET Core controllers (`ControllerBase`), Web API controllers (`ApiController`) and MVC 5 controllers (`Controller`). `[NonAction]` methods and `[FromServices]` parameters are skipped.
- **Values of ASP.NET Core results**, for actions that return `IActionResult`: the type of the value passed to `Ok(value)`, `Created...(..., value)`, `Accepted...(..., value)`, `StatusCode(code, value)`, `NotFound(value)`, `BadRequest(error)`, `Conflict(error)`, `UnprocessableEntity(error)`, `Unauthorized(value)` and to the constructors of `ObjectResult` and its subclasses (`new OkObjectResult(value)`). As with `Serialize`, a value declared as `object` tells nothing.
- **HttpClient with the Web API formatter**: `ReadAsAsync<T>`, `PostAsync<T>`, `PutAsync<T>`, `new ObjectContent<T>`. MVC 5: `new LsMsgPackResult(value)`.
- **What those reach**: the types of public properties, elements of collections, generic arguments, base classes, and the classes of the project that derive from any of them (a type id can name those).

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

Only the properties the serializers write are checked: public, not static, with a public getter and setter, and not ignored (`[IgnoreDataMember]`, `[JsonIgnore]`, `[XmlIgnore]`, MessagePack's `[IgnoreMember]`...).

Change the severity of a rule or turn it off in `.editorconfig`, as for any analyzer:

```ini
[*.cs]
dotnet_diagnostic.LSMP004.severity = none
```

## Default values

The serializers leave out properties that hold their default value (`FilterDefaultValues`): the type's default (`0`, `false`, `null`), or the value of `[DefaultValue]`. How a reader fills them in depends on the layout:

- **Arrays** (`ObjectLayout.Array`, the default, and `application/x-lsmsgpack`): a left-out value is written as nil, and a reader reads a nil as the value that was left out: the `[DefaultValue]`, otherwise the type's default (`null`, `0`, `false`), as JSON reads them. Only values missing after the end of the array (a writer with fewer properties, or `TrimTrailingNulls`) keep what the constructor set.
- **Maps** (`ObjectLayout.Map`, `application/msgpack`): a left-out value has no key, and a reader keeps what the constructor set.

So in a map, when the constructor sets something other than what was left out, the value is lost:

```csharp
public int Retries { get; set; } = 3;   // a map leaves out 0, a reader gets 3
```

`[DefaultValue(3)]` makes the writer leave out 3 instead, and write 0. Until October 2026 a nil also kept what the constructor set, so this happened in the array layout too. Version 2 of the schema (`SchemaVersion.Described`) also tells the default to readers without the classes, e.g. JavaScript.

What the constructor sets is the property's initializer, the initializer of the field its getter returns, or the last assignment to either in the body of the parameterless constructor. Only constant values count: there is nothing to check for `= DateTime.Now`, `= Guid.NewGuid()` or `= new List<int>()`. An empty string isn't flagged (`= ""` is the usual way to avoid `null`, and reading `null` back as `""` is what such code wants).

A class without a parameterless constructor (or a struct without an explicit one) is created without running a constructor (`ObjectCreation.ConstructorOrUninitialized`). A left-out property then keeps the type's default, as the writer assumes, so these rules don't apply to it.

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
