# Schema, type ids and polymorphic class hierarchies

LsMsgPack serializes .NET objects the same way the XML and JSON serializers do: as maps of property names and values. How those maps look on the wire is controlled by a couple of settings in `MsgPackSettings`. This document explains each option, what it costs and when to use it.

- [The options at a glance](#the-options-at-a-glance)
- [Plain maps](#plain-maps)
- [Type ids](#type-ids)
- [The indexed schema](#the-indexed-schema)
- [Polymorphic class-hierarchy support](#polymorphic-class-hierarchy-support)
- [Type resolvers](#type-resolvers)
- [Property names and filters](#property-names-and-filters)
- [Choosing your settings](#choosing-your-settings)

The examples below all serialize this small object model:

```csharp
public interface IPet { string Name { get; set; } }
public class Cat : IPet { public string Name { get; set; } public int Lives { get; set; } }
public class Dog : IPet { public string Name { get; set; } public bool GoodBoy { get; set; } }
public class Owner { public string Name { get; set; } public List<IPet> Pets { get; set; } }

var owner = new Owner
{
  Name = "Ann",
  Pets = new List<IPet> { new Cat { Name = "Mia", Lives = 9 }, new Dog { Name = "Rex", GoodBoy = true } }
};
```

The MsgPack data is shown in a JSON-like notation. `""` is the reserved key for a type id, and `"@"` holds the elements of a collection when the collection needs to be wrapped in a map.

The options at a glance
-----------------------

| Settings | Output for `owner` | Size | Polymorphism | Readable by other MsgPack implementations |
|---|---|---|---|---|
| `UseInexedSchema = false`, `AddTypeIdOptions = Never` | [Plain maps](#plain-maps) | 52 bytes | No (only via a [signature resolver](#resolving-by-signature)) | Yes, it is plain MsgPack |
| `UseInexedSchema = false`, `AddTypeIdOptions = IfAmbiguious` | [Maps with type names](#type-ids) | 62 bytes | Yes | Yes, the type name is just an extra `""` key |
| `UseInexedSchema = false`, `AddTypeIdOptions = Always` | Maps with type names on every object | 84 bytes | Yes | Yes |
| `UseInexedSchema = true`, `AddTypeIdOptions = IfAmbiguious` (**default**) | [Schema followed by the body](#the-indexed-schema) | 80 bytes | Yes | Not really, they would need to understand the schema |
| `UseInexedSchema = true`, `AddTypeIdOptions = Always` | Schema followed by the body | 108 bytes | Yes | Not really |

For a single small object the schema is overhead. It pays off once objects repeat: the same owner with 100 pets is **2508 bytes** as plain maps with type names and **1252 bytes** with the indexed schema.

Both sides must use the same `UseInexedSchema` setting. Data written with the schema starts with the schema map, and data written without it does not. Reading data with the wrong setting throws a `MsgPackException`.

### Why not just use `List<object>`?

Every setting above that writes type ids lets the *data* decide which class is created, wherever the declared type allows it. With `List<IPet>` (or `IPet[]`), only implementations of `IPet` fit, and a type id naming anything else is refused before it's created. With `List<object>`, every element can carry a type id for any type the reader can find, because everything is assignable to `object`. Whoever writes the data then chooses which constructors and property setters run on your side. The same goes for `object[]`, `Dictionary<string, object>`, non-generic collections and `Deserialize<object>()`.

It also costs more. The writer can't leave out a type id for an element whose type it can't tell from the declaration, and the reader looks up a type for every element instead of using the declared one.

Declare the most specific type you can. When you need `object` and read data you don't trust, limit the allowed types with a `TypeGuard`. See [Security: type injection](security.md).

Plain maps
----------

```csharp
var settings = new MsgPackSettings { UseInexedSchema = false, AddTypeIdOptions = AddTypeIdOption.Never };
```

```
{"Name": "Ann", "Pets": [{"Name": "Mia", "Lives": 9}, {"Name": "Rex", "GoodBoy": true}]}
```

Pros:
- Any MsgPack implementation can read and write it. It maps one-to-one onto JSON.
- Tolerant to versioning. Properties are matched by name, so adding, removing or reordering properties doesn't break older readers or writers.

Cons:
- Property names are repeated for every object, which makes large collections big.
- No type information is included. A property declared as an interface, an abstract class or `object` can't be deserialized unless a custom [type resolver](#resolving-by-signature) recognizes the type from its properties. For the example above, reading it back throws *"Cannot create an instance of an interface or abstract type"*.

This is what the web formatters send for `application/msgpack` and `application/x-msgpack` (see [Web formatters](WebFormatters.md)).

Type ids
--------

```csharp
var settings = new MsgPackSettings { UseInexedSchema = false, AddTypeIdOptions = AddTypeIdOption.IfAmbiguious };
```

```
{"Name": "Ann", "Pets": [{"": "Cat", "Name": "Mia", "Lives": 9}, {"": "Dog", "Name": "Rex", "GoodBoy": true}]}
```

When the runtime type of a value differs from the declared type of the property (or collection element) it's assigned to, the type id is stored under the `""` key. When deserializing, the type id tells LsMsgPack which class to create.

`AddTypeIdOption` is a flags enum:

| Flag | Meaning |
|---|---|
| `Never` | Never add a type id. Only safe when there are no interfaces, abstract classes or base classes with derived instances in your model, or when a [signature resolver](#resolving-by-signature) is used. |
| `IfAmbiguious` | Add a type id only when the runtime type differs from the declared type (default). This is usually the best choice. |
| `Always` | Add a type id to every object, even when the declared type already says what it is. It rarely adds information but makes the data self-describing. |
| `FullName` | Combine with one of the above to use `Namespace.TypeName` instead of `TypeName`. Only needed when the same class name exists in more than one namespace. The payload gets larger (except with the schema, where each name is stored once). |
| `NoDefaultFallBack` | Combine with one of the above to use only the ids returned by your own [type resolvers](#custom-type-resolvers). If none of them returns an id, nothing is written instead of the type name. |

Values that aren't maps (such as a collection or a primitive assigned to `object`) are wrapped when they need a type id: `{"": "List<IPet>", "@": [...]}`.

Pros:
- Polymorphism works out of the box, with no attributes needed.
- The data is still plain MsgPack. Other implementations see an extra `""` entry that they can ignore.

Cons:
- Type names are repeated on every polymorphic object.
- Type names are part of your contract. Renaming a class (or moving it to another namespace with `FullName`) breaks existing data unless a [type resolver](#type-resolvers) maps the old name.

The indexed schema
------------------

```csharp
var settings = new MsgPackSettings(); // UseInexedSchema = true and AddTypeIdOptions = IfAmbiguious are the defaults
```

```
{"Owner": ["Name", "Pets"], "Cat": ["Name", "Lives"], "Dog": ["Name", "GoodBoy"]}
{0: "Ann", 1: [{"": 1, 0: "Mia", 1: 9}, {"": 2, 0: "Rex", 1: true}]}
```

The output is two MsgPack items. First comes a small schema, which maps each type name to its property names. Then comes the body. In the body, property names are replaced by their index in the type's property list, and type ids are replaced by the type's index in the schema. Every name is written only once per message, no matter how many objects use it.

Pros:
- Much smaller for anything with repeating objects (about half the size in the 100-pet example). Type ids cost about 1 byte, so `IfAmbiguious` is almost free.
- Still tolerant to versioning. The schema carries the property names, so properties are matched by name when reading even though the body uses indexes. Each message carries its own schema, so the writer and reader don't need to share a schema file.
- `FullName` costs little because each full name appears once.

Cons:
- Other MsgPack implementations can parse the data but won't understand it without extra work: they see a schema map followed by a map with integer keys. Use it between LsMsgPack endpoints, or switch it off for public APIs (the web formatters do this for `application/msgpack`).
- For a single small object, the schema is larger than what it saves.
- Resolving by signature isn't possible. The property keys are schema indexes, so the reader can't guess the type from its property names. Serializing a polymorphic value without a type id (`AddTypeIdOption.Never`) with the schema switched on throws a `MsgPackException` right away rather than producing data that would be read as the wrong type.
- The types named in the schema must be resolvable when reading where they are needed (see [type resolvers](#type-resolvers)): for a type id, or to read into another class (below). Otherwise, deserializing throws with a hint to pre-register the type. A type that is read into a class of the same name is found by that name.
- Reading into **another class** than the writer used (e.g. a DTO into an entity) needs the writer's classes: the values of an object only say which property of the writer's class they are. The root is the first class of the schema, and the other classes follow from the properties both classes have (the writer's class of `Address` is the declared type of the writer's `Address`, also for elements of collections and values of dictionaries). The values are then matched by name, like for the same class. An object whose class can't be paired with one of the writer's (e.g. one class of the reader for two classes of the writer) throws a `MsgPackException` rather than being read by position.

`null` is written without a schema (a single MsgPack `nil`), and deserializing it returns `null`.

Polymorphic class-hierarchy support
-----------------------------------

One of my frustrations with other serializers is that they don't handle class hierarchies very well. For example, the `System.Xml.Serialization` classes needed an `XmlInclude` attribute on a base class, or `XmlArrayItem` attributes on a property holding a list of derived classes. You had to add an attribute for every possible derived type, and not forget one when adding new types. Other serializers had other solutions, but they almost always needed extra code. LsMsgPack supports class hierarchies out of the box.

Suppose you have an interface `IPet` with classes `Cat`, `Dog` and `Fish` that implement it. A class containing an array (or another collection) of pets serializes and deserializes correctly without any extra code, as long as [type ids](#type-ids) are enabled (they are by default).

To deserialize, the type id has to become a `Type` again. A type id is the short name of the type by default (`Cat`), and it's looked up in this order:

1. Names resolved before (the fastest), and types that `Type.GetType` finds by itself.
2. The assembly of the declared type (and of its generic arguments): all its types are cached by name the first time. The assemblies of the type you deserialize are cached as well, and of the types it reaches: generic arguments, element types, base classes and the types of public properties (not the framework's), once per type.
3. The names of all types in the assemblies cached so far.

So the implementations of `IPet` are found without any registration when they're in the assembly of `IPet` (declared as `IPet`, `List<IPet>`, `IPet[]`...), or in the assembly of the class you deserialize:

```csharp
public List<IPet> Pets { get; set; }   // Cat and Dog are found in the assembly that defines IPet
```

Register an assembly yourself when that isn't the case, once, before deserializing:

```csharp
// The property is declared as object, so the reader has no assembly to look in:
public IEnumerable<object> Pets { get; set; } = new HashSet<IPet> { new Cat(), new Dog() };

MsgPackTypes.CacheAssemblyTypes(typeof(IPet));  // LsMsgPack.Core, for both serializers
```

`MsgPackSerializer.CacheAssemblyTypes` (LsMsgPack) and `LtMsgPackSerializer.CacheAssemblyTypes` (LtMsgPack) do the same: there is one cache. A [type guard](security.md) registers what it allows: `new AllowedTypesGuard().AllowAssemblyOf(typeof(IPet))` makes the types of that assembly known by name as well, `Allow(typeof(Cat))` only `Cat`.

The same applies when implementations of `IPet` live in other assemblies than `IPet` itself (e.g. plugins), and nothing you deserialize reaches them: register each of them. `Deserialize<List<Cat>>()` finds `Cat` (it's reachable from the root type).

- **Short names should be unique** among the cached assemblies. When several cached classes have the name, the one that fits where the value goes is used: a type id names the type of an instance, so abstract classes and interfaces don't count (your own `Attribute` class is found, not `System.Attribute`), nor do classes that can't be assigned to the declared type. When more than one fits (e.g. two classes called `Cat` in a property of type `object`), reading throws "Type assignment dilamma". Use `AddTypeIdOptions = AddTypeIdOption.FullName` (bigger payloads) or your own `IMsgPackTypeResolver` in `TypeResolvers`.
- **Let it search**: the included [`WildGooseChaseResolver`](#wildgoosechaseresolver) searches all assemblies loaded in the AppDomain for a name it can't find otherwise (and caches the assemblies it searched). Convenient, but slower the first time, and it keeps more names in memory.
- **Your own mapping**: implement `IMsgPackTypeResolver` to choose the ids and the types (e.g. a fixed table of names, or [`XmlRootAttributeTypeResolver`](#xmlrootattributetyperesolver) to use the names of `[XmlRoot]`).

Type resolvers
--------------

Type resolvers (`IMsgPackTypeResolver`) decide which id is written for a type and which type an id is read back as. Configure them in `MsgPackSettings.TypeResolvers` (or `MsgPackSettings.Default_TypeResolvers` for all new settings). They're consulted from last to first. If none of them returns a result, the built-in name resolver described above is used.

Whichever resolver picks the type, the result must be assignable to the declared type, and names that would load an assembly are refused. To limit the types the data may pick further (e.g. for properties declared as `object`), set a type guard (`MsgPackOptions.TypeGuard`, such as `AllowedTypesGuard`). A resolver is the wrong place for that: resolvers are asked in turn until one returns a type, so one of them can't veto what another one returns. See [Security: type injection](security.md).

### WildGooseChaseResolver

```csharp
settings.TypeResolvers = new IMsgPackTypeResolver[] { new WildGooseChaseResolver() };
```

When a name can't be found, this resolver searches `AppDomain.CurrentDomain.GetAssemblies()` and caches each assembly it searches until it finds the type.

- Pro: no pre-registering needed.
- Con: slow the first time a type is missed, and it caches lots of types you may never need. Handy during development, but not recommended for production.
- Con: the data can then name types in every loaded assembly. Don't use it for data you don't trust, or limit the types with a [type guard](security.md#the-type-guard).

### XmlRootAttributeTypeResolver

```csharp
var resolver = new XmlRootAttributeTypeResolver();
resolver.RegisterAssembly(typeof(IPet).Assembly); // or resolver.RegisterType(typeof(Cat));
settings.TypeResolvers = new IMsgPackTypeResolver[] { resolver };

[XmlRoot("cat")]
public class Cat : IPet { ... }
```

This resolver uses the name from the `[XmlRoot("...")]` attribute as the type id.

- Pro: short, stable names that don't change when you rename or move a class, and you may already have these attributes for the XML serializer.
- Pro or con: using different names than the class names obfuscates the type (intentionally or not) making debugging harder, but may also be used for intentionally obscuring information. E.g. one may call the `Dog` class "cat" and the `Cat` class "dog", or just a single character like "%".
- Con: types must be registered before deserializing, and only types with the attribute are handled (the others fall back to the default name).

### Custom type resolvers

To get full control over the ids (for example to keep the payload minimal with numbers), implement `IMsgPackTypeResolver` yourself:

```csharp
public class PetResolver : IMsgPackTypeResolver
{
  public object IdForType(Type type, FullPropertyInfo assignedTo, MsgPackSettings settings)
  {
    if (type == typeof(Dog)) return 1;
    if (type == typeof(Cat)) return 2;
    return null; // leave it to the next resolver (ultimately the type name)
  }

  public Type Resolve(object typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<object, object> properties, MsgPackSettings settings)
  {
    switch (typeId)
    {
      case null: return null;
      case string _: return null; // a name written by another resolver
    }
    switch (Convert.ToInt32(typeId))
    {
      case 1: return typeof(Dog);
      case 2: return typeof(Cat);
    }
    return null;
  }
}
```

- Pro: the smallest and fastest option without a schema, and fully under your control (including mapping old names after a rename).
- Con: you need to maintain the mapping.

With the indexed schema, the id in the body is always the schema index. A custom resolver can still supply the *name* stored in the schema (when `IdForType` returns a string), which is how `XmlRootAttributeTypeResolver` works together with the schema. Numeric ids from a custom resolver aren't used there, because the schema already makes type ids about 1 byte.

### Resolving by signature

`Resolve` receives the properties of the object being deserialized, so a resolver can recognize a type from its signature even when no type id was written:

```csharp
public Type Resolve(object typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<object, object> properties, MsgPackSettings settings)
{
  if (properties.ContainsKey("Lives")) return typeof(Cat);
  if (properties.ContainsKey("GoodBoy")) return typeof(Dog);
  return null;
}
```

Use this with `UseInexedSchema = false` and `AddTypeIdOptions = AddTypeIdOption.Never` to read polymorphic data written by other implementations that don't add type ids.

This is the way typical serialization libraries often solve polymorphism (ISerializationBinder). The extra code needed and trouble of maintaining this code is the reason for our [indexed schemas](#the-indexed-schema) and `AddTypeIdOption.IfAmbiguious` features. However the freedom to write custom resolvers is fully supported.

Property names and filters
--------------------------

These settings also affect the size and the contract of the data:

- **Property names**: by default, the .NET property name is used. Add an `AttributePropertyNameResolver` to `MsgPackSettings.PropertyNameResolvers` to use the name from `[JsonPropertyName]` (System.Text.Json), `[XmlAttribute]` or `[XmlElement]`, or from a custom attribute of your own (`new AttributePropertyNameResolver(typeof(MyNameAttribute), nameof(MyNameAttribute.Name))`). With the indexed schema, these names end up in the schema.
- **Static filters** (`StaticFilters`) decide once per type which properties are serialized. By default, properties without a public setter (`FilterNonSettable`) are skipped. So are properties with an "ignore" attribute of another serializer (`FilterIgnoredAttribute`): `[XmlIgnore]`, `[JsonIgnore]` (System.Text.Json and Json.NET), `[IgnoreDataMember]`, `[IgnoreMember]` (MessagePack-CSharp), `[PropertyShape(Ignore = true)]` (Nerdbank.MessagePack) and any other attribute with "Ignore" in its name. And static properties are skipped (`FilterStatic`, see [Security](security.md#what-else-to-keep-in-mind)). Keep `FilterStatic` in the list if you set your own static filters.
- **Dynamic filters** (`DynamicFilters`) decide per value. By default, `FilterDefaultValues` omits properties that are `null`, their type's default value, or the value of their `[DefaultValue]` attribute. An empty string is written, as the JSON serializers do (`new FilterDefaultValues(omitEmptyStrings: true)` omits it too, it is then read back as the value the constructor sets, often `null`). Use `FilterNullValues` instead to omit only `null` values, or use no dynamic filters to write every property.
- **Collections**: `[SerializeEnumerable]` on a collection class or property sets the element type, and controls whether the collection's own properties and/or its elements are serialized.

### Ignoring the same properties as another serializer

To leave out the same properties as the serializer your classes were made for, replace `FilterIgnoredAttribute` by one of its presets: `LikeNewtonsoft` (`[Newtonsoft.Json.JsonIgnore]` and `[IgnoreDataMember]`), `LikeSystemTextJson`, `LikeXmlSerializer`, `LikeDataContract`, `LikeMessagePackCSharp` or `LikeNerdbank` (tested against those libraries). Or pick the attributes yourself with `IgnoreAttributes` flags, and add the names of your own attributes:

```csharp
MsgPackSettings settings = new MsgPackSettings()
{
  // A new array: the serializers cache the result per filter array, do not change the default array in place
  StaticFilters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(), FilterIgnoredAttribute.LikeNewtonsoft, new FilterStatic() }
};

// [XmlIgnore] and [JsonIgnore] of both JSON serializers, and the project's own [SkipInMsgPack]
FilterIgnoredAttribute custom = new FilterIgnoredAttribute(IgnoreAttributes.XmlIgnore | IgnoreAttributes.SystemTextJson | IgnoreAttributes.Newtonsoft, "SkipInMsgPack");
```

The libraries also differ in where they look for the attributes. Json.NET also sees them on the property a property overrides (for inherited attributes, so not `[IgnoreDataMember]`) and on an interface property of the same name, Nerdbank.MessagePack on the overridden property, DataContractSerializer on the overridden property (also `[IgnoreDataMember]`), and System.Text.Json, XmlSerializer and MessagePack-CSharp only on the property itself. The presets look where their library does. The default filter and the constructors without `IgnoreAttributeLookup` look everywhere (`IgnoreAttributeLookup.All`), so the override of an ignored property is ignored too. To look only at the property itself:

```csharp
FilterIgnoredAttribute declaredOnly = new FilterIgnoredAttribute(IgnoreAttributes.All, IgnoreAttributeLookup.Declared);
```

The attributes are recognized by their names, LsMsgPack does not reference the libraries that define them. System.Text.Json's `[JsonIgnore]` only leaves a property out with `Condition = JsonIgnoreCondition.Always` (its default): `Never`, `WhenWritingNull` and `WhenWritingDefault` keep it (the latter two leave out values, which is up to the dynamic filters). The filters are the same for reading and writing. `MsgPackSettings.Default_StaticFilters` holds the defaults of new settings.

Choosing your settings
----------------------

- **Only LsMsgPack on both sides** (your own client and server, files, caches, queues): keep the defaults. The indexed schema with `IfAmbiguious` type ids gives the smallest data and full polymorphism.
- **Other MsgPack implementations need to read the data**: set `UseInexedSchema = false`. Keep `IfAmbiguious` if you have polymorphic properties (the others can ignore the `""` key), or use `Never` for fully plain maps.
- **Reading data from other implementations**: `UseInexedSchema = false`. If the data contains polymorphic values without type ids, add a [signature resolver](#resolving-by-signature).
- **Class names may change, or you want shorter ids without a schema**: use `XmlRootAttributeTypeResolver` or a [custom type resolver](#custom-type-resolvers).
- **Web APIs**: the [web formatters](WebFormatters.md) handle this per request. `application/msgpack` is plain MsgPack for everyone (or a preset for another library), and `application/x-lsmsgpack` uses the schema between LsMsgPack / LtMsgPack endpoints.
