[Setup wizard](README.md) › [Saved](Files.md) › [Only my own app](FilesOwnApp.md) › Kept for years

# Setup: long-lived documents

Files that your .NET app saves and opens again, also after updates: documents, projects, save games, archives.

## In short

| | |
|---|---|
| Package | `LtMsgPack` |
| Serializer | `LtMsgPackSerializer` with the default options |
| Format | the indexed schema (property names once per file), type ids where needed |
| Keep in mind | keep the schema in the file, give polymorphic classes stable names, keep a test file per release |

```
dotnet add package LtMsgPack
```

## The code

```csharp
using LtMsgPack;

public static class DocumentFiles
{
  // One serializer for the whole app: it's thread-safe and learns your classes once.
  private static readonly LtMsgPackSerializer Serializer = new LtMsgPackSerializer();

  public static async Task SaveAsync(Drawing drawing, string path)
  {
    byte[] data = Serializer.Serialize(drawing);

    // Write to a temporary file first, then replace the old one:
    // a crash halfway through never leaves a broken document behind.
    string temp = path + ".tmp";
    await File.WriteAllBytesAsync(temp, data);
    File.Move(temp, path, overwrite: true);
  }

  public static async Task<Drawing> OpenAsync(string path)
  {
    byte[] data = await File.ReadAllBytesAsync(path);
    return Serializer.Deserialize<Drawing>(data);
  }
}
```

On .NET Framework use `File.WriteAllBytes` / `File.ReadAllBytes` (or a `FileStream`), and `File.Delete` + `File.Move`. There are also overloads that write to and read from a `Stream` (`Serializer.Serialize(drawing, stream)`); they're synchronous, so in a UI app run them in `Task.Run` for large files.

Your classes need nothing special: public properties with a getter and a setter (or a constructor that takes them) are saved.

```csharp
public class Drawing
{
  public string Title { get; set; }
  public DateTime Created { get; set; }
  public List<IShape> Shapes { get; set; } = new List<IShape>();
}

public interface IShape { double X { get; set; } double Y { get; set; } }
public class Circle : IShape { public double X { get; set; } public double Y { get; set; } public double Radius { get; set; } }
public class Square : IShape { public double X { get; set; } public double Y { get; set; } public double Side { get; set; } }
```

## Why these settings

The defaults were chosen for exactly this case, so there's nothing to set:

- **The indexed schema.** Each file starts with a small table: for each class, its property names (`Drawing: Title, Created, Shapes`). The objects after it are just the values. A drawing with 1000 circles stores the word `Radius` once, not 1000 times. When the file is read, the values are matched to your properties *by name*, through that table. That's what makes old files readable by new code.
- **Type ids where needed.** `Shapes` is declared as `List<IShape>`, so the reader can't know whether an element is a `Circle` or a `Square`. The writer adds the class name to those elements only (where the actual class differs from the declared type). You don't need `[XmlInclude]`-style attributes.
- **Default values are left out.** A `Radius` of `0` or a `Title` of `null` costs one byte.

## Changing your classes later

| Change | Old files |
|---|---|
| Add a property | Open fine. The new property keeps the value your constructor gives it (or `0`/`null`). |
| Remove a property | Open fine. Its saved value is skipped. |
| Reorder properties | Open fine. Values are matched by name. |
| Change a property's type to a compatible one (`int` → `long`, a number → `string`) | Open fine, the value is converted. Incompatible values throw, or are skipped with `ReadErrors` (see [Add-ons](Extras.md#finding-out-what-didnt-match)). |
| Rename a property | The old value is **lost** (the file has the old name). Keep the old name, or see below. |
| Rename or move a class | Fine for classes that never get a type id. For polymorphic classes (a `Circle` in a `List<IShape>`) the name **is** stored: see below. |

**Renaming a property** while keeping old files: give it its old name for serialization with `[JsonPropertyName("OldName")]` (System.Text.Json's attribute; `[DataMember(Name = ...)]` in a `[DataContract]` class works too) and turn on the attribute names:

```csharp
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Names;

LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions
{
  PropertyNameResolvers = new IMsgPackPropertyIdResolver[] { new AttributePropertyNameResolver() }
});

public class Drawing
{
  [JsonPropertyName("Name")] // it was called Name in version 1
  public string Title { get; set; }
}
```

LtMsgPack reads and writes classes with custom names on a somewhat slower path, so only do this when you need it.

**Stable names for polymorphic classes.** By default the type id is the short class name (`Circle`). Rename `Circle` to `Ellipse` and old files name a class that no longer exists. Give such classes a name of their own with `[XmlRoot]` and the `XmlRootAttributeTypeResolver`. Use the current class name when you add it to a released app, and you can rename the class freely afterwards:

```csharp
using System.Xml.Serialization;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Types;

XmlRootAttributeTypeResolver shapeNames = new XmlRootAttributeTypeResolver();
shapeNames.RegisterAssembly(typeof(IShape).Assembly);

LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions
{
  TypeResolvers = new IMsgPackTypeResolver[] { shapeNames }
});

[XmlRoot("Circle")] // the name in the files, whatever the class is called
public class Ellipse : IShape { ... }
```

The implementations are found automatically when they're in the same assembly as `IShape` (or as the class you open). When they live elsewhere (plugins), register them once at startup with `MsgPackTypes.CacheAssemblyTypes(typeof(SomePluginShape))` (namespace `LsMsgPack`). See [schema.md](../schema.md#polymorphic-class-hierarchy-support).

**Room for big changes.** If you expect to restructure the format one day, wrap the content in a small envelope with a version number from the start. Version 3 of your app can then read a version 1 file into the old classes and convert it:

```csharp
public class DrawingFile
{
  public int FormatVersion { get; set; } = 1;
  public Drawing Drawing { get; set; }
}
```

## Test it

Keep one file saved by every released version in your test project, and a test that opens each of them. Pass an `out ReadDifferences` to see what didn't match (a property the file has but your classes don't):

```csharp
[TestMethod]
public void OpensDrawingsOfVersion1()
{
  byte[] data = File.ReadAllBytes("TestFiles/v1.drawing");
  Drawing drawing = Serializer.Deserialize<Drawing>(data, out ReadDifferences differences);

  Assert.AreEqual(3, drawing.Shapes.Count);
  Console.WriteLine(differences?.GenerateReport() ?? "the file matches the classes");
}
```

## Watch out

- **Don't turn on `WriteSchemaReference` for files.** It replaces the schema in the file with an 18-byte reference to a schema kept in memory (meant for network traffic). The file can then only be opened with that memory.
- **Files from other people are untrusted data.** If users email documents to each other, a file can be crafted by someone else. Type ids let the file choose which classes are created, within the declared types: avoid properties declared as `object` (`List<object>`, `Dictionary<string, object>`) and read [Add-ons: untrusted data](Extras.md#reading-data-you-dont-trust).
- **Dates are stored as a moment in UTC.** A `DateTime` comes back with `Kind = Utc`. Call `ToLocalTime()` to show it.
- A property with an initial value (`public int Retries { get; set; } = 3;`) that is saved as `0` opens as `0`, as JSON does. The [analyzers](../Analyzers.md) that come with the package point out the cases where a value could change.

## Next

- Want to look inside a saved file? Open it in MsgPack Explorer or VS Code: [Add-ons](Extras.md#looking-inside-the-bytes).
- Mobile app, trimmed or NativeAOT? Use the source generator: [Add-ons](Extras.md#trimmed-and-aot-apps-and-more-speed).
- More about files: [ClientApps.md](../ClientApps.md). The format in detail: [schema.md](../schema.md).
