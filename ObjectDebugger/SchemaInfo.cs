using System.Collections.Generic;

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
  /// </summary>
  public class SchemaInfo
  {
    public SchemaSource Source { get; set; }

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
        return $"Inline schema of {Types.Count} types";
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
    /// The item holding the type name (a key of the schema). Null when the item tree was not kept, or for a schema read from a store.
    /// </summary>
    public LsMsgPack.MsgPackItem NameItemRef { get; set; }

    /// <summary>
    /// The items holding the property names, in the order of <see cref="Properties"/> (null items when the item tree was not kept).
    /// </summary>
    public List<LsMsgPack.MsgPackItem> PropertyItemRefs { get; } = new List<LsMsgPack.MsgPackItem>();

    /// <summary>
    /// The body has a value of this type (read from a type id or inferred).
    /// </summary>
    public bool IsUsed { get; set; }

    public override string ToString()
    {
      return $"#{Index} {Name} ({string.Join(", ", Properties)})";
    }
  }
}
