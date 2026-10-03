using LsMsgPack;
using System;
using System.Globalization;
using System.Text;

namespace ObjectDebugger
{
  /// <summary>
  /// A value of the reconstructed object tree: a property, an element of a collection or an entry of a dictionary.
  /// <para>This is a leaf (a primitive value), <see cref="ComplexObject"/> holds the objects and collections.</para>
  /// </summary>
  public class PrimitiveObject
  {
    /// <summary>
    /// The property name, "[index]" for an element of a collection, the key for an entry of a dictionary.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The .NET type name when the data tells it (a type id or the indexed schema), otherwise null (see <see cref="Kind"/>).
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// The <see cref="Type"/> was inferred from the shape of the data (e.g. the length of an array matches the properties of a type in the schema), not read from a type id.
    /// </summary>
    public bool TypeIsGuess { get; set; }

    /// <summary>
    /// What the data holds here (for a leaf).
    /// </summary>
    public ValueKind ValueKind { get; set; }

    /// <summary>
    /// The unpacked value of a leaf: integers have the smallest type that holds them (not the type they were written from), a timestamp is local time, an extension without a registered type is the <see cref="MpExt"/> itself.
    /// </summary>
    public object Value { get; set; }

    /// <summary>
    /// The first item of this value, the key when it is an entry of a map. Null when the item tree was not kept (only KEEPTRACK keeps the items of arrays and maps).
    /// </summary>
    public MsgPackItem FirstItemRef { get; set; }

    /// <summary>
    /// The item holding the value. Null when the item tree was not kept.
    /// </summary>
    public MsgPackItem LastItemRef { get; set; }

    public ComplexObject Parent { get; internal set; }

    /// <summary>
    /// What went wrong reading or interpreting this value (null when nothing did).
    /// </summary>
    public string Error { get; set; }

    /// <summary>
    /// A place for the user interface to refer to its own element (e.g. the tree node).
    /// </summary>
    public object Tag { get; set; }

    /// <summary>
    /// Names from the root, like a C# expression (e.g. "Lines[2].Product"), empty for the root.
    /// </summary>
    public string Path
    {
      get
      {
        if (Parent is null)
          return string.Empty;
        string parentPath = Parent.Path;
        if (Name != null && Name.StartsWith("[", StringComparison.Ordinal))
          return parentPath + Name;
        return string.IsNullOrEmpty(parentPath) ? Name : string.Concat(parentPath, ".", Name);
      }
    }

    public void AddError(string message)
    {
      Error = Error is null ? message : string.Concat(Error, Environment.NewLine, message);
    }

    /// <summary>
    /// The value as text, for displaying it.
    /// </summary>
    public virtual string ValueText
    {
      get { return FormatValue(Value); }
    }

    /// <summary>
    /// The type name when known, otherwise what the data holds (e.g. "int").
    /// </summary>
    public virtual string TypeText
    {
      get
      {
        if (Type != null)
          return Type;
        if (Value is MpExt ext)
          return string.Concat("ext ", ext.TypeSpecifier.ToString(CultureInfo.InvariantCulture));
        return KindName(ValueKind);
      }
    }

    public override string ToString()
    {
      return string.Concat(Name, " = ", ValueText);
    }

    internal static string KindName(ValueKind kind)
    {
      switch (kind)
      {
        case ValueKind.Nil: return "nil";
        case ValueKind.Bool: return "bool";
        case ValueKind.Int: return "int";
        case ValueKind.Float: return "float";
        case ValueKind.String: return "string";
        case ValueKind.Bin: return "bin";
        case ValueKind.Timestamp: return "timestamp";
        case ValueKind.Decimal: return "decimal";
        case ValueKind.Extension: return "ext";
        case ValueKind.Array: return "array";
        case ValueKind.Map: return "map";
        case ValueKind.Error: return "error";
        default: return "custom extension";
      }
    }

    public static string FormatValue(object value)
    {
      if (value is null)
        return "null";
      if (value is string text)
        return text;
      if (value is byte[] bytes)
        return FormatBytes(bytes);
      if (value is MpExt ext)
        return string.Concat("ext ", ext.TypeSpecifier.ToString(CultureInfo.InvariantCulture), ": ", FormatBytes((byte[])ext.Value));
      if (value is DateTime time)
        return time.ToString("o", CultureInfo.InvariantCulture);
      if (value is IFormattable formattable)
        return formattable.ToString(null, CultureInfo.InvariantCulture);
      return value.ToString();
    }

    private static string FormatBytes(byte[] bytes)
    {
      const int max = 64;
      StringBuilder sb = new StringBuilder(bytes.Length * 3 + 16);
      for (int t = 0; t < bytes.Length && t < max; t++)
      {
        if (t > 0)
          sb.Append(' ');
        sb.Append(bytes[t].ToString("X2", CultureInfo.InvariantCulture));
      }
      if (bytes.Length > max)
        sb.Append(" ...");
      sb.Append(" (").Append(bytes.Length.ToString(CultureInfo.InvariantCulture)).Append(" bytes)");
      return sb.ToString();
    }
  }
}
