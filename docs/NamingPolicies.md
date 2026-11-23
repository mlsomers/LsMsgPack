# Naming policies

By default a property is written by its .NET name (`InvoiceDate`). Readers in JavaScript or Python often expect other names: `invoiceDate` (as ASP.NET Core's JSON has them), or `invoice_date`. A naming policy converts the names when they're written, as System.Text.Json's `JsonNamingPolicy` does. You don't need one for the [TypeScript client](TypeScriptClient.md), which converts the names itself.

There are three options in `MsgPackOptions` (both serializers), all null (off) by default. Each takes its own policy:

| Option | Converts | System.Text.Json |
|---|---|---|
| `PropertyNamingPolicy` | the names of properties and fields | `PropertyNamingPolicy` |
| `DictionaryKeyPolicy` | the string keys of dictionaries (writing only) | `DictionaryKeyPolicy` |
| `EnumNamingPolicy` | the names of enums written by name ([Enums.md](Enums.md)) | the policy of `JsonStringEnumConverter` |

```csharp
LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions
{
  UseIndexedSchema = false,                          // maps keyed by names, readable by any MsgPack library
  ObjectLayout = ObjectLayout.Map,
  PropertyNamingPolicy = MsgPackNamingPolicy.SnakeCaseLower,
});
// new Invoice { InvoiceDate = ..., CustomerName = "Ann" } is written as { "invoice_date": ..., "customer_name": "Ann" }
```

## The policies

The built-in policies give the same names as System.Text.Json's (the tests compare them):

| Policy | `InvoiceDate` | `URLValue` | `SHA512Hash` |
|---|---|---|---|
| `MsgPackNamingPolicy.CamelCase` | `invoiceDate` | `urlValue` | `shA512Hash` |
| `MsgPackNamingPolicy.SnakeCaseLower` | `invoice_date` | `url_value` | `sha512_hash` |
| `MsgPackNamingPolicy.SnakeCaseUpper` | `INVOICE_DATE` | `URL_VALUE` | `SHA512_HASH` |
| `MsgPackNamingPolicy.KebabCaseLower` | `invoice-date` | `url-value` | `sha512-hash` |
| `MsgPackNamingPolicy.KebabCaseUpper` | `INVOICE-DATE` | `URL-VALUE` | `SHA512-HASH` |

For other names, derive from `MsgPackNamingPolicy` and override `ConvertName`. It must return the same name for the same input every time (property and enum names are converted once per type and kept) and be safe to call from several threads. A policy that returns null or an empty name gets a `MsgPackException`.

```csharp
public sealed class PrefixedNames : MsgPackNamingPolicy
{
  public override string ConvertName(string name) => "x_" + name;
}
```

## Property names

- **Names given by attributes are written as they are.** With an `AttributePropertyNameResolver` ([schema.md](schema.md#property-names-and-filters)) a `[JsonPropertyName("Given_Name")]` stays `Given_Name`. The policy only converts .NET names, as in System.Text.Json.
- **Everywhere a name is written**: map keys, the names in the [indexed schema](schema.md) (both versions), objects of anonymous types, objects below `object` properties. Type ids (class names) are never converted.
- **Two properties with the same converted name** (`Id` and `ID` with camelCase) throw a `MsgPackException` when the class is first written or read. System.Text.Json refuses them too.
- **Reading**: a key is looked up by the name the policy gives, then by the .NET name (data from a writer without the policy, or an older version), then ignoring case when `PropertyNameCaseInsensitive` is on (the converted names, then the .NET names). So a reader with the policy reads data written without one, and a reader without it reads camelCase data with `PropertyNameCaseInsensitive` (on in the [web formatters](WebFormatters.md)). A reader without the policy doesn't read snake_case or kebab-case names: give it the policy too.
- When a map has both names for one property (`customer_name` and `CustomerName`), LsMsgPackDebug sets the converted name's value, LsMsgPack the last key's (as it does for duplicate keys).
- Names the class doesn't have are reported in [ReadDifferences](ReadDifferences.md) as they were written, and [extension data](ExtensionData.md) keeps and writes them as they came.

The cost: the names are converted once per class, so writing and reading with a policy is as fast as without one. Keys that aren't the converted names cost a second lookup, by the .NET name. With the [source generator](SourceGenerator.md), the generated code reads maps keyed by names with constants of the .NET names. Under a policy that changes the names it reads them with its loop over the keys instead. With a JIT, the readers compiled at runtime take the converted names straight-line again after `CompileReadersAfter` objects.

## Dictionary keys

`DictionaryKeyPolicy` converts the keys of dictionaries that are strings (declared as `string` or `object`). Keys of other types are written as they are, and so are the keys of [extension data](ExtensionData.md) (written as they were read). As in System.Text.Json, it applies to writing only: keys are read as they were written, and a round trip gives the converted keys. The built-in policies remember the names of the last 256 keys they converted, so keys that repeat (as the keys of most dictionaries do) are converted once. A custom policy is asked each time a key is written.

## Enum names

With `EnumFormat.Name`, `EnumNamingPolicy` converts the names of enum values (`InProgress` → `in_progress`). Flags combine the converted names (`can_read, is_owner`). Custom names (`[EnumMember(Value = ...)]`, `[JsonStringEnumMemberName]`) are written as they are. Two values with the same converted name throw a `MsgPackException`.

Readers take the converted names (with the policy), the .NET names and numbers. A reader without the policy reads camelCase names (the .NET names ignoring case), not snake_case or kebab-case ones. More in [Enums.md](Enums.md#naming-policy).

## Other tools

- [lsmsgpack-ts](TypeScriptModels.md) follows the policies of the options you pass with `--options`. The models list the names the server writes, and the fields are named from them.
- [Nerdbank.MessagePack](Compatibility.md)'s `PropertyNamingPolicy = MessagePackNamingPolicy.CamelCase` writes the same names as `MsgPackNamingPolicy.CamelCase` (tested on its invoices).
