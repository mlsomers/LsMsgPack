using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ObjectDebugger
{
  public enum SchemaSource
  {
    /// <summary>The schema precedes the body (<c>UseInexedSchema</c>)</summary>
    Inline,
    /// <summary>The body refers to a schema by its id (<c>WriteSchemaReference</c>), the schema is only known when a <c>SchemaStore</c> holding it was given</summary>
    Reference
  }

  /// <summary>
  /// The indexed schema of a payload: the type names, each with the names of its properties. In the body, type ids are indexes of the types and property ids indexes of the property names.
  /// <para>Version 2 (<c>SchemaVersion.Described</c>) also has the declared type of each property (<see cref="SchemaType.PropertyTypes"/>), the defaults that are left out and the type of the root.</para>
  /// </summary>
  public class SchemaInfo
  {
    public SchemaSource Source { get; set; }

    /// <summary>
    /// 1: the names, 2: also the declared types (<c>SchemaVersion</c>). 0 when the schema is not available.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Version 2: the declared type of the root, null otherwise.
    /// </summary>
    public DeclaredType RootType { get; set; }

    /// <summary>
    /// The id the body refers to (hexadecimal), null for an inline schema.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// False when the body refers to a schema that was not available, <see cref="Types"/> is empty then.
    /// </summary>
    public bool IsAvailable { get; set; }

    public List<SchemaType> Types { get; } = new List<SchemaType>();

    /// <summary>
    /// The item holding the schema (a map) or the reference to it (an extension). Null when the item tree was not kept.
    /// </summary>
    public LsMsgPack.MsgPackItem ItemRef { get; set; }

    public override string ToString()
    {
      if (Source == SchemaSource.Inline)
        return Version == 2 ? $"Inline schema of {Types.Count} types (described)" : $"Inline schema of {Types.Count} types";
      return IsAvailable ? $"Schema {Id} ({Types.Count} types)" : $"Schema {Id} (not available)";
    }
  }

  public class SchemaType
  {
    /// <summary>
    /// The type id in the body.
    /// </summary>
    public int Index { get; set; }

    public string Name { get; set; }

    /// <summary>
    /// The property names, the property id in the body is the index.
    /// </summary>
    public List<string> Properties { get; } = new List<string>();

    /// <summary>
    /// Version 2: the declared types of the properties, in the order of <see cref="Properties"/> (null for a property without a description). Empty in version 1.
    /// </summary>
    public List<DeclaredType> PropertyTypes { get; } = new List<DeclaredType>();

    /// <summary>
    /// Version 2: the values the writer leaves out (<c>[DefaultValue]</c>), in the order of <see cref="Properties"/> (null for none). Empty in version 1.
    /// </summary>
    public List<PrimitiveObject> PropertyDefaults { get; } = new List<PrimitiveObject>();

    /// <summary>
    /// Version 2: the type of the content of a value of this type that is not an object (<c>{ "": typeId, "@": content }</c>, e.g. a collection), null otherwise.
    /// </summary>
    public DeclaredType ContentType { get; set; }

    /// <summary>
    /// The declared type of a property (version 2), null when the schema does not tell it.
    /// </summary>
    public DeclaredType PropertyType(int index)
    {
      return index >= 0 && index < PropertyTypes.Count ? PropertyTypes[index] : null;
    }

    /// <summary>
    /// The default of a property that the writer leaves out (version 2: <c>[DefaultValue]</c>), null when there is none.
    /// </summary>
    public PrimitiveObject PropertyDefault(int index)
    {
      return index >= 0 && index < PropertyDefaults.Count ? PropertyDefaults[index] : null;
    }

    /// <summary>
    /// The properties as text: their names, in version 2 with their types and defaults (e.g. "Id: guid, Lines: list&lt;Line&gt;, Country: string = NL").
    /// </summary>
    /// <param name="types">The types of the schema (for the names of the types the properties refer to)</param>
    public string Describe(List<SchemaType> types)
    {
      StringBuilder sb = new StringBuilder();
      if (ContentType != null)
        sb.Append("@: ").Append(ContentType.Format(types));
      for (int t = 0; t < Properties.Count; t++)
      {
        if (sb.Length > 0)
          sb.Append(", ");
        sb.Append(Properties[t] ?? "(null)");
        DeclaredType type = PropertyType(t);
        if (type != null)
          sb.Append(": ").Append(type.Format(types));
        PrimitiveObject value = PropertyDefault(t);
        if (value != null)
          sb.Append(" = ").Append(value.ValueText);
      }
      return sb.ToString();
    }

    /// <summary>
    /// The values of an object of this type whose properties were all left out (a struct that was only written as its default): e.g. "{ X: 0, Y: 0 }".
    /// </summary>
    public string DefaultsText(List<SchemaType> types)
    {
      StringBuilder sb = new StringBuilder("{ ");
      for (int t = 0; t < Properties.Count; t++)
      {
        if (t > 0)
          sb.Append(", ");
        string value = PropertyDefault(t)?.ValueText ?? PropertyType(t)?.DefaultText(types) ?? "null";
        sb.Append(Properties[t] ?? "(null)").Append(": ").Append(value);
      }
      return sb.Append(" }").ToString();
    }

    /// <summary>
    /// The item holding the type name (a key of the schema). Null when the item tree was not kept, or for a schema read from a store.
    /// </summary>
    public LsMsgPack.MsgPackItem NameItemRef { get; set; }

    /// <summary>
    /// The items holding the property names (version 2: the items <c>[name, type, default]</c>), in the order of <see cref="Properties"/> (null items when the item tree was not kept).
    /// </summary>
    public List<LsMsgPack.MsgPackItem> PropertyItemRefs { get; } = new List<LsMsgPack.MsgPackItem>();

    /// <summary>
    /// The body has a value of this type (read from a type id or inferred).
    /// </summary>
    public bool IsUsed { get; set; }

    public override string ToString()
    {
      return $"#{Index} {Name} ({Describe(null)})";
    }
  }

  /// <summary>
  /// A declared type of version 2 of the schema (<c>SchemaVersion.Described</c>, docs/schema.md): a kind (<c>int</c>, <c>string</c>, <c>datetime</c>, <c>any</c>...),
  /// an object of a type of the schema (<see cref="Entry"/>), or a composite (<c>?</c>, <c>struct</c>, <c>list</c>, <c>dict</c>, <c>pair</c>, <c>tuple</c>) of <see cref="Items"/>.
  /// </summary>
  public class DeclaredType
  {
    /// <summary>A kind or composite as the schema names it, <see cref="EntryKind"/> for an object of a type of the schema.</summary>
    public string Kind { get; set; }

    /// <summary>The index of the type in the schema (<see cref="EntryKind"/>, and a struct of a type of the schema), -1 otherwise.</summary>
    public int Entry { get; set; } = -1;

    /// <summary>The types a composite is made of (the type of a nullable, the elements of a list, key and value...).</summary>
    public List<DeclaredType> Items { get; } = new List<DeclaredType>();

    /// <summary>A struct without a type in the schema (only written as its default so far): its properties, as a type of the schema has them. Null otherwise.</summary>
    public SchemaType InlineStruct { get; set; }

    public const string EntryKind = "entry";

    /// <summary>
    /// Any value, the data tells what it is (declared as object, an interface or an abstract class, or a kind this version does not know).
    /// </summary>
    public bool IsAny
    {
      get { return Kind == "any" || Kind is null; }
    }

    /// <summary>
    /// The kind of a nullable is the kind of its type.
    /// </summary>
    public DeclaredType WithoutNullable
    {
      get { return Kind == "?" && Items.Count == 1 ? Items[0].WithoutNullable : this; }
    }

    /// <summary>
    /// What a reader without the classes gives a value that was left out (nil): the default of a value type, null for references and nullables.
    /// </summary>
    /// <returns>Null when the default is null</returns>
    public string DefaultText(List<SchemaType> types)
    {
      switch (Kind)
      {
        case "int": case "long": case "ulong": case "float": case "decimal": case "bigint": return "0";
        case "bool": return "false";
        case "char": return "'\\0'";
        case "guid": return "00000000-0000-0000-0000-000000000000";
        case "datetime": return "0001-01-01T00:00:00";
        case "dto": case "dto-clock": return "0001-01-01T00:00:00+00:00";
        case "timespan": case "timeonly": return "00:00:00";
        case "dateonly": return "0001-01-01";
        case "struct": return InlineStruct is null ? string.Concat("default(", Format(types), ")") : InlineStruct.DefaultsText(types);
        default: return null;
      }
    }

    /// <param name="types">The types of the schema, for the names of <see cref="Entry"/></param>
    public string Format(List<SchemaType> types)
    {
      switch (Kind)
      {
        case EntryKind:
          return types != null && Entry >= 0 && Entry < types.Count ? types[Entry].Name : string.Concat("#", Entry.ToString(CultureInfo.InvariantCulture));
        case "?":
          return string.Concat(ItemText(0, types), "?");
        case "struct":
          if (InlineStruct != null)
            return string.Concat("struct { ", InlineStruct.Describe(types), " }");
          return string.Concat("struct ", types != null && Entry >= 0 && Entry < types.Count ? types[Entry].Name : string.Concat("#", Entry.ToString(CultureInfo.InvariantCulture)));
        case "list":
        case "dict":
        case "pair":
        case "tuple":
          StringBuilder sb = new StringBuilder(Kind).Append('<');
          for (int t = 0; t < Items.Count; t++)
          {
            if (t > 0)
              sb.Append(", ");
            sb.Append(ItemText(t, types));
          }
          return sb.Append('>').ToString();
        default:
          return Kind ?? "any";
      }
    }

    private string ItemText(int index, List<SchemaType> types)
    {
      return index < Items.Count ? Items[index].Format(types) : "any";
    }

    public override string ToString()
    {
      return Format(null);
    }
  }
}
