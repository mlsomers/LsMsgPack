# Reading into existing instances

By default the serializers create the objects they read. Sometimes an object already exists and should keep its identity: a member the class owns and won't let you replace, a settings object other code holds, a view model bound to a UI, an object from a pool. The serializers can read into those instead of creating new ones ("populate").

## Members without a setter

A class often creates the parts it owns itself and doesn't let callers swap them:

```csharp
public class Order
{
  public string Number { get; set; }
  public Address ShipTo { get; } = new Address();        // owned: callers fill order.ShipTo.City = ...
  public List<Line> Lines { get; } = new List<Line>();   // an owned collection
  public decimal Total => Lines.Sum(l => l.Amount);      // a calculation
}
```

All three are written (as System.Text.Json and Json.NET write them), so readers without the class get them. When reading, by default:

- `Lines` is cleared, then filled with the items of the data.
- `ShipTo` and `Total` are skipped, as System.Text.Json skips members without a setter.

With `Populate = PopulateMembers.GetOnly` in the options (or `[MsgPackPopulate]` on the member or class, see [attributes](#attributes)), `ShipTo` is read into the `Address` the constructor made: its members get the values of the data, members the data lacks keep theirs. `Total` stays skipped: a getter written by hand is a calculation (or it may return shared state, `=> Settings.Global`), so it's never read into unless an attribute on the member asks for it.

Reading into objects applies to auto-properties without a setter (`{ get; }`) and readonly fields (when they're serialized, see [schema.md](schema.md#fields)) that hold a class or an interface of your own. Not to:

- structs (the getter returns a copy), strings, arrays, `object`, delegates, framework types;
- types with a [converter](Converters.md) (the value is written as its surrogate);
- [immutable classes](schema.md#immutable-classes), created by a constructor taking their values: there's nothing to read into, so a member holding one is skipped;
- members a constructor of the class takes (they're passed to it).

A getter returning null, or a nil in the data, leaves the member as it is.

## The option

`MsgPackOptions.Populate` (both serializers) says which members are filled:

| Value | Members without a setter | Settable members |
|---|---|---|
| `Collections` (default) | collections filled, objects skipped | replaced |
| `GetOnly` | collections filled, objects read into | replaced |
| `All` | collections filled, objects read into | objects read into, collections filled |

With `All`, a settable member keeps the instance it holds: an object is read into (`Order.Billing = new Address { Country = "NL" }` keeps its `Country` when the data has none), a collection is cleared and filled (an `ObservableCollection` a UI is bound to stays the same instance). A member holding null gets a new instance, an array or a read-only collection is replaced, and nil sets the member to null.

System.Text.Json's `JsonObjectCreationHandling.Populate` is the same, except that it appends to collections: here a collection holds what the data holds.

## Attributes

`[MsgPackPopulate]` on a property or field reads into it whatever the option says, `[MsgPackPopulate(false)]` doesn't (a member without a setter is then skipped). On a class, struct or interface it applies to the members it declares. The member's attribute wins over its class's, which wins over the option.

```csharp
public class Window
{
  [MsgPackPopulate]
  public Theme Theme => _theme;            // a getter written by hand: read into only with the attribute
  private readonly Theme _theme = new Theme();

  [MsgPackPopulate]
  public Layout Layout { get; set; } = new Layout();   // settable: kept, read into
}
```

The attributes of other serializers that mean the same are recognized too, so models shared with them need no changes:

| Attribute | Means |
|---|---|
| System.Text.Json `[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]` (member or type) | `[MsgPackPopulate]` |
| System.Text.Json `[JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]` | `[MsgPackPopulate(false)]` |
| Json.NET `[JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Reuse)]` (or `Auto`) | `[MsgPackPopulate]` |
| Json.NET `[JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]` | `[MsgPackPopulate(false)]` |

## Reading into an object you have

`Populate` reads a payload into an existing object or collection:

```csharp
AppSettings current = ...;                         // held by other code
LsMsgPackSerializer serializer = new LsMsgPackSerializer();
serializer.Populate(bytes, current);               // also ReadOnlyMemory<byte>, a Stream, (bytes, offset, count)
serializer.Populate(bytes, current, out ReadDifferences differences);

MsgPackSerializer.Populate(bytes, current, new MsgPackSettings());   // LsMsgPackDebug
```

- The members the data has get their values, the members holding objects and collections as the option says; members the data lacks keep theirs. So a payload with a few members updates only those (a patch).
- The data is read as the target's own class, as `Serialize(target)` writes it. A type id naming a class the target isn't (or derives from) throws a `MsgPackException`.
- A collection with Add is cleared and filled.
- Nil leaves the target as it is.
- `OnDeserializing` and `OnDeserialized` ([callbacks](Callbacks.md)) are called on the target, as on a new object.
- The target can't be a struct, a string or another framework type, an immutable class or a type with a converter (`ArgumentException`), or a read-only collection.

For a stream that must not be read synchronously (a network stream, a file opened for asynchronous I/O), `PopulateAsync` reads the payload with `ReadAsync`, then into the target, as `DeserializeAsync` does; the stream is left after the value. A target that can't be read into throws before anything is read. The differences come through a callback (async methods have no `out` parameters, see [ReadDifferences.md](ReadDifferences.md)):

```csharp
await serializer.PopulateAsync(stream, current);
await serializer.PopulateAsync(stream, current, differences => logger.LogWarning(differences.GenerateReport()));
```

### Streams of values

`MsgPackStreamReader.PopulateObject(target)` and `PopulateObjectAsync(target)` read the next value into a target where `ReadObject` would read a new one ([Streaming.md](Streaming.md)): a root value of the stream, or a value in the body of a head, at any depth:

```csharp
using (MsgPackStreamReader reader = new MsgPackStreamReader(stream, serializer))
{
  await reader.PopulateObjectAsync(settings);            // a root value
  await reader.ReadHeadAsync<List<Station>>();
  await reader.ReadAsync();                               // the start of the array
  for (int t = 0; !await reader.AtEndAsync(); t++)
    await reader.PopulateObjectAsync(stations[t]);        // each element into the station the app shows
}
```

### HTTP

With the Web API package's HttpClient helpers, `PopulateFromMsgPackAsync` reads a response into an object you have, as `ReadFromMsgPackAsync` reads a new one ([WebFormatters.md](WebFormatters.md#httpclient)):

```csharp
using (HttpResponseMessage response = await client.GetAsync("api/settings"))
{
  await response.EnsureSuccessStatusCodeAsync();
  await response.Content.PopulateFromMsgPackAsync(current, formatter);
}
```

An empty body (204 No Content) leaves the target as it is. With `ReportDifferences` the differences are on the content afterwards (`response.GetReadDifferences()`). `LsMsgPackHttpSerializer.PopulateBody` does the same for a body you have in an array. The server side formatters create the objects of a request, as model binding asks: read the body yourself with `PopulateBody` to read it into an object you have.

## Errors and edge cases

- A type id naming another class than the one the member holds (or one it derives from): a `MsgPackException` that follows [`ReadErrors`](ReadDifferences.md) (skipped and reported with the other modes, the member keeps its object).
- With [references](References.md) (`ReferenceHandling.Preserve`), an object read into is registered as any other, so later references resolve to it. A member without a setter whose value is a reference to another object can't hold that object: an error that follows `ReadErrors`. A settable member takes the referenced object.
- When a value is read again (data with references read by options without `Preserve`, a schema that grows, LsMsgPack handing an object to LsMsgPack.Core's conversions), it's read into the same instance again: the result is the same, its callbacks are called again.
- The generated code of the [source generator](SourceGenerator.md) and the readers LsMsgPack compiles read the other members as usual; the populated members go through the runtime's readers.
