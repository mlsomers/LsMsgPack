# Enums

An enum value is written as its number by default: compact, fast, and what MessagePack-CSharp and Nerdbank.MessagePack write. It can also be written as its name, as the JSON serializers do with their string enum converters. A name still means the same value after the enum is reordered, and readers in other languages see `"Shipped"` instead of `3`.

Both serializers (LsMsgPackDebug's `MsgPackSerializer` and LsMsgPack's `LsMsgPackSerializer`) write the same bytes, with or without the source generator.

**Readers take both forms, whatever the settings say.** A number and a name both read into the enum, so a writer can switch from numbers to names (or back) while older data and older clients keep working.

## Choosing names

For every enum:

```csharp
LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions() { EnumFormat = EnumFormat.Name });
```

`MsgPackOptions.Default_EnumFormat` sets it for new options (LsMsgPackDebug's `MsgPackSettings` too).

For one enum, or one property or field, with `[MsgPackEnumFormat]` (namespace `LsMsgPack`):

```csharp
[MsgPackEnumFormat(EnumFormat.Name)]
public enum OrderStatus { Open, Paid, Shipped }

public class Order
{
  public OrderStatus Status { get; set; }                                  // "Shipped": the enum's attribute

  [MsgPackEnumFormat(EnumFormat.Number)] public OrderStatus Legacy { get; set; } // 2: the member's attribute
  [MsgPackEnumFormat(EnumFormat.Name)] public DayOfWeek Day { get; set; }        // "Monday": also for enums you don't own
}
```

The most specific one wins: the member's attribute, then the enum's, then `EnumFormat` of the options. An attribute on a member applies to the member's own value, so its type is the enum or a nullable of it. On a `List<OrderStatus>` it has no effect (the analyzers warn, [LSMP012](Analyzers.md#lsmp012-msgpackenumformat-on-a-member-that-is-no-enum)): put it on the enum, or use the options. The elements of collections, dictionary keys and values, and the root value use the enum's format.

## Attributes of other serializers

Code written for another serializer keeps its choice without changes. These attributes are recognized by their full names, so LsMsgPack doesn't reference those libraries:

| Library | By name | By number |
|---|---|---|
| System.Text.Json | `[JsonConverter(typeof(JsonStringEnumConverter))]`, `JsonStringEnumConverter<T>` | `[JsonConverter(typeof(JsonNumberEnumConverter<T>))]` |
| Json.NET | `[JsonConverter(typeof(StringEnumConverter))]` (or a class deriving from it) | |
| MessagePack-CSharp | `[MessagePackFormatter(typeof(EnumAsStringFormatter<T>))]` | `[MessagePackFormatter(typeof(GenericEnumFormatter<T>))]` |
| MsgPack.Cli | `[MessagePackEnum]` (its default is by name), `SerializationMethod = EnumSerializationMethod.ByName`; on a member `[MessagePackEnumMember(SerializationMethod = EnumMemberSerializationMethod.ByName)]` | `ByUnderlyingValue` |

They work on the enum and on a member, as `[MsgPackEnumFormat]` does. `[MsgPackEnumFormat]` wins when there are several. The arguments of Json.NET's converter (camelCase, naming strategies) aren't followed: names are written as declared, or as their custom names, or as `EnumNamingPolicy` converts them (below). Options that set this for every enum in another library (System.Text.Json's `JsonSerializerOptions.Converters`, MessagePack-CSharp's `DynamicEnumAsStringResolver`, Nerdbank.MessagePack's `SerializeEnumValuesByName`) are the `EnumFormat` option here.

## Names

- **Custom names**: `[JsonStringEnumMemberName("...")]` (System.Text.Json), `[EnumMember(Value = "...")]` (data contracts, used by Json.NET and MessagePack-CSharp) and `[EnumMemberShape(Name = "...")]` (PolyType, Nerdbank.MessagePack) on an enum value are written instead of its .NET name, in that order when a value has several.
- **Aliases** (two names for one value) are written by the first one declared. Both names read.
- **`[Flags]`** combinations are written as their names joined by `", "` (`"Read, Write"`), as `Enum.ToString` writes them. A combination that has a name of its own is written by that name.
- **A value without a name**, like `(OrderStatus)7`, or bits no flag covers, is written as its number, as System.Text.Json and Json.NET write it.
- **Two values written with the same name** are an error (a `MsgPackException` naming both), for example a custom name that equals another value's name. Writing them as numbers still works.

Reading a name takes, in this order: a written name (custom or .NET name), the .NET name, the same ignoring case, names joined by commas (combined), and an integer as text (`"2"`). A name the enum doesn't have is an error that follows `ReadErrors` (see [ReadDifferences.md](ReadDifferences.md)).

LsMsgPack compares a written or .NET name in place as UTF-8 bytes, so reading one allocates no string. A name in another case, a combination or a number in text goes through the slower path, which converts the string.

## Naming policy

`EnumNamingPolicy` in the options converts the .NET names of the values that are written by name, as the policy of System.Text.Json's `JsonStringEnumConverter` does: with `MsgPackNamingPolicy.SnakeCaseLower`, `OrderStatus.InProgress` is written as `"in_progress"`, a combination as `"can_read, is_owner"`. Custom names are written as they are. Two values with the same converted name are an error, as above. It's an option of its own: `PropertyNamingPolicy` doesn't convert enum names (see [NamingPolicies.md](NamingPolicies.md)).

A reader with the policy takes the converted names first, then the .NET names and the rest as above. A reader without it takes the .NET names ignoring case, which covers camelCase (`"inProgress"`) but not snake_case or kebab-case: give it the same policy.

## Schema and other clients

- **The indexed schema** has the names of properties, not of enum values: nothing changes there.
- **Version 2 of the schema** (`SchemaVersion.Described`) describes an enum written by name as `string`, otherwise as its underlying integer kind.
- **lsmsgpack-ts** follows the attributes and the options of `--options`. The const object of an enum written by name has the names (`Large: 'large'`), and a member by name of an enum written by number gets the union of its names (`'Open' | 'Closed'`). The `defineModels` kind is `string`. See [TypeScriptModels.md](TypeScriptModels.md).
- **The TypeScript client** reads the strings as they are and writes them back as strings, which the server reads.
- **MessagePack-CSharp** with `EnumAsStringFormatter<T>` or `DynamicEnumAsStringResolver` writes and reads the same bytes for named values (tested). It writes a value without a name as its number in text (`"7"`), which LsMsgPack reads. It doesn't read LsMsgPack's number for such a value. **Nerdbank.MessagePack** with `SerializeEnumValuesByName` writes the same bytes, custom names of `[EnumMemberShape]` included (tested).
