# Reporting differences while reading

The data and your classes don't always match exactly: an older or newer version of a class, a peer's DTOs read into your own classes, or a misspelled property name. Reading goes on anyway, like the JSON serializers do: a property your class doesn't have is skipped, and a property the data doesn't have keeps the value the constructor gave it. That's what you want for rolling upgrades. But when something fails later on (a value that is null where it shouldn't be), the cause is hard to find.

Pass an `out ReadDifferences` to `Deserialize` to find out what didn't match:

```csharp
using LsMsgPack;

Order order = MsgPackSerializer.Deserialize<Order>(data, settings, out ReadDifferences differences);
if (differences != null)
  logger.LogWarning(differences.GenerateReport());
```

LtMsgPack has the same overloads on the serializer: `serializer.Deserialize<Order>(data, out ReadDifferences differences)`. The web formatters collect them with `LtMsgPackHttpOptions.ReportDifferences` (see [WebFormatters.md](WebFormatters.md#reporting-differences)).

`differences` is `null` when the data matched the classes. Otherwise it counts each difference per class and name, over the whole payload:

```
3 differences between the data and the classes:
- OrderEntity.Discount: not a property of the class, skipped, 2 times
    at $[0], $[1]
    left at their default (first object): Adress
- OrderEntity.Address: not a property of the class, skipped, 2 times
    at $[0], $[1]
    did you mean Adress? (left at its default)
- LineEntity.Colour: not a property of the class, skipped, 1 time
    at $[1].Lines[1]
```

## What is reported

| `DifferenceKind` | What happened | `Name` / `Position` |
|---|---|---|
| `UnknownProperty` | The data has a property the class doesn't have (or that its static filters leave out, e.g. `[IgnoreDataMember]`). The value is skipped. | The name in the data |
| `ExtraValue` | An object written as an array (`ObjectLayout.Array`) without the indexed schema has more values than the class has properties. The values are skipped. | The position in the array |
| `UnmatchedClass` | **Error.** With the indexed schema: an object of a class that has no schema entry and couldn't be paired with one of the writer's classes (see [schema.md](schema.md)). Skipped (the property is left `null`) unless `ReadErrors` is `FailFast` (below). When the class was found for more than one of the writer's classes (e.g. one `AddressEntity` where the writer had an `AddressDto` and a `LineDto`), neither is used and `WriterClasses` names them. | The property it was assigned to (null for the root and elements) |
| `UnresolvedType` | A type id in the data (a type name, or with the indexed schema a writer's class) that doesn't resolve to a type here, e.g. a subclass the reader doesn't have. With the indexed schema, or when the declared type is abstract or an interface, it's an **error**: the object is skipped (left `null`) unless `ReadErrors` is `FailFast`. Without the schema and with a declared type that can be created, the object is read as the declared type (as it always was) and only reported. A type that is found but doesn't fit always throws (see [security.md](security.md)). | The type name in the data (`Class` is the declared type) |
| `InvalidValue` | **Error.** A value that was read but doesn't convert into its property: a string that isn't a number where an `int` is declared, 300 for a `byte`, a name that isn't a value of the enum, an array where an object is declared. The property keeps what the constructor gave it, unless `ReadErrors` is `FailFast`. A value of a collection (an element, a dictionary value) fails the property that holds the collection. `Error` is the exception. | The property |

- A value that is nil in an array (`ObjectLayout.Array`) isn't counted: nil is how that layout writes a value it leaves out, like a key that is missing from a map.
- **Missing properties** (in the class, not in the data) aren't reported as such. Without the schema they can't be told apart from default values, which aren't written (`FilterDefaultValues`). Instead, the report lists the properties of the first object with an unknown property that were left at their default. A misspelled name shows up as an unknown property with a suggestion ("did you mean ..."), for names that differ in case or by up to two characters.
- With a type id, the class is the type that was read, not the declared type.
- `Difference.IsError` tells the errors (values that couldn't be read) from the other differences, `ReadDifferences.ErrorCount` counts them.

## Values that can't be read: `ReadErrors`

What happens to an error is up to `MsgPackOptions.ReadErrors` (both serializers, also in the web formatters' options):

| `ReadErrorHandling` | What happens |
|---|---|
| `FailFast` (default) | The error throws where it's found, the exception the conversion threw (e.g. a `FormatException`). With an `out ReadDifferences`, the exception has the differences found until then, this error included (`ex.Data[ReadDifferences.ExceptionDataKey]`). |
| `FailDeferred` | The value is skipped and reading goes on. Once all data is read, a `ReadErrorsException` is thrown with all errors and differences, and with the paths (`Differences.Root` is the object that was read). Its `InnerException` is the first error. |
| `ReportAndContinue` | The value is skipped, and you get what could be read. The errors are in the `out ReadDifferences`. |

`FailDeferred` and `ReportAndContinue` collect the differences also when you call `Deserialize` without an `out ReadDifferences`: `FailDeferred` still gives you the complete report in its exception. With `ReportAndContinue` and no `out ReadDifferences` the errors are only skipped, so use that only when something else reports them (the web formatters with `ReportDifferences`).

These always throw, in every mode:
- Data that can't be parsed: truncated, nested deeper than `MaxDepth`, a length that claims more than there is. Reading can't go on after those.
- The security checks: a type that doesn't fit where it goes, one the `TypeGuard` refuses, a type name that names an assembly (see [security.md](security.md)).
- Exceptions of your classes: constructors and property setters (e.g. a setter that validates its value). They're passed on as they are.
- A value at the root that can't be converted (there is no property to skip), a type name that fits two classes ("Type assignment dilamma", a setup problem), a missing schema (`MissingSchemaException`).

```csharp
MsgPackSettings settings = new MsgPackSettings { ReadErrors = ReadErrorHandling.FailDeferred };
try
{
  Order order = MsgPackSerializer.Deserialize<Order>(data, settings);
}
catch (ReadErrorsException ex)
{
  logger.LogError(ex.Message); // all errors and differences, with paths
}
```

## The model

- `Differences`: one `Difference` per class, kind and name, in the order they were found. Each has a `Count` and up to `ReadDifferences.MaxSamples` (10) `Samples`: the objects it was found on (or, for `UnmatchedClass`, the objects holding the one that was skipped).
- `Root`: the value that was read. The paths of the samples (`$`, `.Name`, `[0]`, `["key"]`) are only looked up when you ask for them: `GenerateReport()` (also `ToString()`) and `PathOf(object)` walk the graph from the root, breadth first, over the properties the serializer reads, the elements of collections and the values of dictionaries.
- `Omitted`: the names come from the data, so at most `ReadDifferences.MaxDifferences` (100) different ones are kept, the rest are only counted. Names longer than 256 characters are cut.

Collecting costs little while the data matches: the serializers only take note where they skip something. Reading with an `out ReadDifferences` (or with `ReadErrors` other than `FailFast`) is still slower than without: about 10-15% for LtMsgPack on larger payloads and 0.15 µs per call, because it takes a separate path for the objects. So use it where you want the report, not by default everywhere.

## Things to keep in mind

- **The paths are looked up when you ask for them.** If you change the objects in between (move them, replace a list), a sample may be found at another path, or not at all ("not found"). A sample that is a struct is a copy and isn't found either.
- **When reading throws** (`FailFast`, or one of the errors that always throw), the differences found until then are on the exception: `ex.Data[ReadDifferences.ExceptionDataKey]` (no `Root`, so no paths). That's often the cause: a property that wasn't set because its name was misspelled.
- **LsMsgPack and LtMsgPack report the same differences** with the same counts. The order of the differences and of their samples can differ (LsMsgPack converts the values of a collection from the last to the first). With more than 10 occurrences they may keep other samples. The report lists the samples in the order of the graph.
- The report is per call. Settings and serializers are shared by threads, the differences belong to the call that read them.
