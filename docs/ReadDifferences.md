# Reporting differences while reading

The data and your classes don't always match exactly: an older or newer version of a class, a peer's DTOs read into your own classes, or a misspelled property name. Reading goes on anyway, like the JSON serializers do: a property your class doesn't have is skipped, and a property the data doesn't have keeps the value the constructor gave it. That's what you want for rolling upgrades. But when something fails later on (a value that is null where it shouldn't be), the cause is hard to find.

Pass an `out ReadDifferences` to `Deserialize` to find out what didn't match:

```csharp
using LsMsgPack;

Order order = MsgPackSerializer.Deserialize<Order>(data, settings, out ReadDifferences differences);
if (differences != null)
  logger.LogWarning(differences.GenerateReport());
```

LtMsgPack has the same overloads on the serializer: `serializer.Deserialize<Order>(data, out ReadDifferences differences)`.

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
| `UnmatchedClass` | With the indexed schema: an object of a class that has no schema entry and couldn't be paired with one of the writer's classes (see [schema.md](schema.md)). Without an `out ReadDifferences` this throws. With one, the object is skipped (the property is left `null`) and reported. | The property it was assigned to (null for the root and elements) |

- A value that is nil in an array (`ObjectLayout.Array`) isn't counted: nil is how that layout writes a value it leaves out, like a key that is missing from a map.
- **Missing properties** (in the class, not in the data) aren't reported as such. Without the schema they can't be told apart from default values, which aren't written (`FilterDefaultValues`). Instead, the report lists the properties of the first object with an unknown property that were left at their default. A misspelled name shows up as an unknown property with a suggestion ("did you mean ..."), for names that differ in case or by up to two characters.
- With a type id, the class is the type that was read, not the declared type.
- Values that don't convert (a string where an int is declared) and type ids that don't resolve still throw, see below.

## The model

- `Differences`: one `Difference` per class, kind and name, in the order they were found. Each has a `Count` and up to `ReadDifferences.MaxSamples` (10) `Samples`: the objects it was found on (or, for `UnmatchedClass`, the objects holding the one that was skipped).
- `Root`: the value that was read. The paths of the samples (`$`, `.Name`, `[0]`, `["key"]`) are only looked up when you ask for them: `GenerateReport()` (also `ToString()`) and `PathOf(object)` walk the graph from the root, breadth first, over the properties the serializer reads, the elements of collections and the values of dictionaries.
- `Omitted`: the names come from the data, so at most `ReadDifferences.MaxDifferences` (100) different ones are kept, the rest are only counted. Names longer than 256 characters are cut.

Collecting costs nothing while the data matches: the serializers only take note where they skip something. Reading with an `out ReadDifferences` is a bit slower than without (LtMsgPack takes a separate path for the objects), so use it where you want the report, not by default everywhere.

## Things to keep in mind

- **The paths are looked up when you ask for them.** If you change the objects in between (move them, replace a list), a sample may be found at another path, or not at all ("not found"). A sample that is a struct is a copy and isn't found either.
- **When reading throws**, the differences found until then are on the exception: `ex.Data[ReadDifferences.ExceptionDataKey]` (no `Root`, so no paths). That's often the cause: a property that wasn't set because its name was misspelled.
- **LsMsgPack and LtMsgPack report the same differences** with the same counts. The order of the differences and of their samples can differ (LsMsgPack converts the values of a collection from the last to the first). With more than 10 occurrences they may keep other samples. The report lists the samples in the order of the graph.
- The report is per call. Settings and serializers are shared by threads, the differences belong to the call that read them.
