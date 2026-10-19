using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace LsMsgPack
{
  /// <summary>
  /// Writes version 2 of the indexed schema (<see cref="SchemaVersion.Described"/>, docs/schema.md "Schema version 2"): the kind and default of each property, for readers without the classes (e.g. JavaScript).
  /// <para>The same map as version 1 (the same keys in the same order, so the type ids of the body are the same), each value an array of items: <c>[name, type]</c> or <c>[name, type, default]</c>
  /// (the value of <see cref="DefaultValueAttribute"/> that the dynamic filters leave out, encoded as the body encodes it). An entry that is not an object (a collection or a value with a type id) starts with <c>["@", type]</c>
  /// (not a property: the property ids are the positions of the other items), and a last entry <c>""</c> has <c>[["@", rootType]]</c>.</para>
  /// <para>Types: a kind (<c>string bool int long ulong float decimal char bigint guid bin uri datetime dto dto-clock timespan dateonly timeonly any</c>), an entry index (an object of that entry),
  /// or an array: <c>["?", T]</c> (Nullable), <c>["struct", k]</c> (a struct of entry k: its default is an object, not null), <c>["struct", [items]]</c> (a struct without an entry,
  /// it was only written as its default so far: its properties as an entry has them), <c>["list", T]</c>, <c>["dict", K, V]</c>, <c>["pair", K, V]</c>, <c>["tuple", T...]</c>.</para>
  /// <para>Both serializers write it with this class, so the bytes are the same. Only called when a schema is made (once per schema with a <see cref="SchemaStore"/>), not per value.</para>
  /// </summary>
  internal sealed class DescribedSchema
  {
    /// <summary>
    /// The key of the last entry, which describes the root.
    /// </summary>
    internal const string RootKey = "";

    /// <summary>
    /// The name of the item of an entry (and of the root entry) that describes the content of a wrapped value: <c>{ "": typeId, "@": content }</c>.
    /// </summary>
    internal const string ContentName = "@";

    private readonly List<ComplexTypeDef> _defs;
    private readonly Dictionary<Type, int> _entries;
    private readonly MsgPackOptions _settings;
    private readonly MsgPackOptions _byteOrder;
    private readonly ByteWriter _bytes;
    private readonly bool _leavesOutDefaults;
    private readonly HashSet<Type> _inlineStructs = new HashSet<Type>(); // being written: a struct with a (settable, not stored) property of its own type

    private DescribedSchema(ByteWriter bytes, List<ComplexTypeDef> defs, MsgPackOptions settings, MsgPackOptions byteOrder)
    {
      _bytes = bytes;
      _defs = defs;
      _settings = settings;
      _byteOrder = byteOrder;
      _entries = new Dictionary<Type, int>(defs.Count);
      for (int t = 0; t < defs.Count; t++)
        if (!(defs[t].Type is null) && !_entries.ContainsKey(defs[t].Type))
          _entries.Add(defs[t].Type, t);

      for (int t = 0; t < settings._dynamicFilters.Length; t++)
        _leavesOutDefaults |= settings._dynamicFilters[t] is FilterDefaultValues;
    }

    /// <summary>
    /// Writes the schema of the entries (in the order of their type ids) and the root.
    /// </summary>
    /// <param name="defs">The entries, with their types (an entry without a type gets its names only)</param>
    /// <param name="root">The declared type of the root</param>
    /// <param name="settings">The settings the body is written with (filters, formats, custom extensions)</param>
    /// <param name="byteOrder">The <see cref="MsgPackOptions.EndianAction"/> of the bytes: the settings of the body, or <see cref="SchemaBytes.Canonical"/> for a <see cref="SchemaStore"/></param>
    internal static void Write(ByteWriter bytes, List<ComplexTypeDef> defs, Type root, MsgPackOptions settings, MsgPackOptions byteOrder)
    {
      new DescribedSchema(bytes, defs, settings, byteOrder).Write(root);
    }

    private void Write(Type root)
    {
      _bytes.WriteMapHeader(_defs.Count + 1, _byteOrder);
      for (int t = 0; t < _defs.Count; t++)
      {
        ComplexTypeDef def = _defs[t];
        WriteName(def.TypeName);
        WriteEntry(def);
      }

      _bytes.WriteString(RootKey, _byteOrder);
      _bytes.WriteArrayHeader(1, _byteOrder);
      WriteContent(root);
    }

    private void WriteName(string name)
    {
      if (name is null)
        _bytes.Write((byte)MsgPackTypeId.MpNull);
      else
        _bytes.WriteString(name, _byteOrder);
    }

    private void WriteEntry(ComplexTypeDef def)
    {
      Type type = def.Type;
      List<string> names = def.Props;
      if (type is null) // not resolved (not for a writer): the names only
      {
        _bytes.WriteArrayHeader(names.Count, _byteOrder);
        for (int p = 0; p < names.Count; p++)
          WriteName(names[p]);
        return;
      }

      bool content = def.IsCollection || !IsObject(type);
      _bytes.WriteArrayHeader(names.Count + (content ? 1 : 0), _byteOrder);
      if (content)
        WriteContent(type);

      if (names.Count == 0)
        return;
      FullPropertyInfo[] props = FullPropertyInfo.GetStaticallyIncludedProps(type, _settings); // the shared cache of the static filters, as the session made them
      for (int p = 0; p < names.Count; p++)
        WriteProperty(names[p], Find(props, names[p]));
    }

    /// <summary>
    /// <c>["@", type]</c>: the content of a value wrapped with a type id (<c>{ "": typeId, "@": content }</c>), or the root.
    /// </summary>
    private void WriteContent(Type type)
    {
      _bytes.WriteArrayHeader(2, _byteOrder);
      _bytes.WriteString(ContentName, _byteOrder);
      WriteType(type);
    }

    private static FullPropertyInfo Find(FullPropertyInfo[] props, string name)
    {
      for (int t = 0; t < props.Length; t++)
        if (string.Equals(props[t].PropertyInfo.Name, name, StringComparison.Ordinal))
          return props[t];
      return null;
    }

    private void WriteProperty(string name, FullPropertyInfo prop)
    {
      if (prop is null) // e.g. a property of a collection that is not found by name
      {
        WriteName(name);
        return;
      }

      Type type = prop.PropertyInfo.PropertyType;
      byte[] defaultValue = _leavesOutDefaults ? DefaultOf(prop) : null;
      _bytes.WriteArrayHeader(defaultValue is null ? 2 : 3, _byteOrder);
      WriteName(name);
      WriteType(type);
      if (!(defaultValue is null))
        _bytes.Write(defaultValue);
    }

    /// <summary>
    /// The value of [DefaultValue], encoded as the body encodes it, when the filters leave out values equal to it (see <see cref="FilterDefaultValues"/>: only a value of the same type is equal), otherwise null.
    /// </summary>
    private byte[] DefaultOf(FullPropertyInfo prop)
    {
      Dictionary<string, object> attributes = prop.CustomAttributes;
      if (attributes is null || attributes.Count == 0 || !attributes.TryGetValue(nameof(DefaultValueAttribute), out object attribute))
        return null;
      object value = (attribute as DefaultValueAttribute)?.Value;
      if (value is null)
        return null;
      Type type = prop.PropertyInfo.PropertyType;
      Type valueType = Nullable.GetUnderlyingType(type) ?? type;
      if (value.GetType() != valueType)
        return null;
      return _settings.SerializePlainValue(value, type, _byteOrder._endianAction);
    }

    /// <summary>
    /// An object with properties (written as an array or map of its values), not a value the serializers write otherwise. In the order of LtMsgPack's TypeKinds.Classify (LsMsgPack's MsgPackItem.Pack).
    /// </summary>
    private bool IsObject(Type type)
    {
      type = Nullable.GetUnderlyingType(type) ?? type;
      if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(byte[]) || type == typeof(sbyte[]) || type == typeof(Guid)
        || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type.IsArray || type.IsEnum)
        return false;
      if (_settings.HasCustomExtension(type) || FrameworkTypeInfo.GetPlainForm(type) != PlainForm.None || FrameworkTypeInfo.IsKeyValuePair(type) || FrameworkTypeInfo.IsTuple(type))
        return false;
      if (typeof(IEnumerable).IsAssignableFrom(type))
        return false;
      return !(type == typeof(TimeSpan) || typeof(Uri).IsAssignableFrom(type) || type == FrameworkTypeInfo.DateOnlyType || type == FrameworkTypeInfo.TimeOnlyType);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = TrimReasons.ModelTypes)]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
    private void WriteType(Type type)
    {
      Type nullable = Nullable.GetUnderlyingType(type);
      if (!(nullable is null))
      {
        _bytes.WriteArrayHeader(2, _byteOrder);
        _bytes.WriteString("?", _byteOrder);
        WriteType(nullable);
        return;
      }

      if (type == typeof(decimal)) // a custom extension (the built-in decimal extension) or a string (LtMsgPack's DecimalFormat.String)
      {
        Kind("decimal");
        return;
      }

      if (type.IsArray && type.GetArrayRank() > 1) { Kind("any"); return; } // multidimensional: [length0, ..., [items]]
      if (type == typeof(bool)) { Kind("bool"); return; }
      if (type == typeof(sbyte) || type == typeof(short) || type == typeof(int) || type == typeof(byte) || type == typeof(ushort) || type == typeof(uint)) { Kind("int"); return; }
      if (type == typeof(long)) { Kind("long"); return; }
      if (type == typeof(ulong)) { Kind("ulong"); return; }
      if (type == typeof(float) || type == typeof(double)) { Kind("float"); return; }
      if (type == typeof(string)) { Kind("string"); return; }
      if (type == typeof(byte[]) || type == typeof(sbyte[])) { Kind("bin"); return; }
      if (type == typeof(Guid)) { Kind("guid"); return; }
      if (type.IsArray && !type.GetElementType().IsValueType) { Composite("list", type.GetElementType()); return; }
      if (type == typeof(DateTime)) { Kind("datetime"); return; }
      if (type == typeof(DateTimeOffset)) { Kind(_settings._dateTimeOffsetFormat == DateTimeOffsetFormat.ClockTimeAndOffset ? "dto-clock" : "dto"); return; }
      if (_settings.HasCustomExtension(type)) { Kind("any"); return; } // written as the extension writes it

      PlainForm plain = FrameworkTypeInfo.GetPlainForm(type);
      if (plain != PlainForm.None) { WritePlain(plain); return; }

      if (type.IsEnum) { WriteType(Enum.GetUnderlyingType(type)); return; }
      if (FrameworkTypeInfo.IsKeyValuePair(type)) { Composite("pair", type.GetGenericArguments()); return; }
      if (FrameworkTypeInfo.IsTuple(type)) { Composite("tuple", type.GetGenericArguments()); return; } // the 8th item (Rest) is a tuple: a nested array
      if (typeof(IEnumerable).IsAssignableFrom(type))
      {
        CollectionInfo info = CollectionInfo.Get(type);
        if (info.IsDictionary || !(info.PairKey is null) || typeof(IDictionary).IsAssignableFrom(type))
        {
          Type key = info.PairKey?.PropertyType ?? info.KeyType ?? typeof(object);
          Type value = info.PairValue?.PropertyType ?? info.ValueType ?? typeof(object);
          Composite("dict", key, value);
          return;
        }
        Composite("list", (type.IsArray ? type.GetElementType() : info.ElementType) ?? typeof(object));
        return;
      }
      if (type == typeof(char)) { Kind("char"); return; }
      if (type == typeof(TimeSpan)) { Kind("timespan"); return; }
      if (typeof(Uri).IsAssignableFrom(type)) { Kind("uri"); return; }
      if (type == FrameworkTypeInfo.DateOnlyType) { Kind("dateonly"); return; }
      if (type == FrameworkTypeInfo.TimeOnlyType) { Kind("timeonly"); return; }

      if (type.IsValueType && !_entries.ContainsKey(type) && _inlineStructs.Add(type))
      {
        // Never written other than as its default (left out), so it has no entry: its properties inline, for the default (an object, as the readers of .NET make it)
        FullPropertyInfo[] props = FullPropertyInfo.GetStaticallyIncludedProps(type, _settings);
        _bytes.WriteArrayHeader(2, _byteOrder);
        _bytes.WriteString("struct", _byteOrder);
        _bytes.WriteArrayHeader(props.Length, _byteOrder);
        for (int p = 0; p < props.Length; p++)
          WriteProperty(props[p].PropertyInfo.Name, props[p]);
        _inlineStructs.Remove(type);
        return;
      }
      if (!type.IsAbstract && !type.IsInterface && type != typeof(object) && _entries.TryGetValue(type, out int entry))
      {
        if (type.IsValueType)
        {
          _bytes.WriteArrayHeader(2, _byteOrder);
          _bytes.WriteString("struct", _byteOrder);
        }
        WriteIndex(entry);
        return;
      }
      Kind("any"); // declared as object, an interface or abstract class (the value has a type id), or a class this schema never wrote (the value is nil or has a type id)
    }

    private void WritePlain(PlainForm plain)
    {
      switch (plain)
      {
        case PlainForm.Half: Kind("float"); return;
        case PlainForm.Text: Kind("string"); return;
        case PlainForm.Rune: Kind("int"); return;
        case PlainForm.NInt: Kind("long"); return;
        case PlainForm.NUInt: Kind("ulong"); return;
        case PlainForm.Binary: Kind("bin"); return;
        case PlainForm.Complex: Composite("tuple", typeof(double), typeof(double)); return;
        case PlainForm.BigInteger: Kind("bigint"); return;
        default: Kind("any"); return;
      }
    }

    private void Kind(string kind)
    {
      _bytes.WriteString(kind, _byteOrder);
    }

    private void Composite(string kind, params Type[] items)
    {
      _bytes.WriteArrayHeader(items.Length + 1, _byteOrder);
      _bytes.WriteString(kind, _byteOrder);
      for (int t = 0; t < items.Length; t++)
        WriteType(items[t]);
    }

    /// <summary>
    /// A positive integer in the smallest format (as MpInt writes it with DynamicallyCompact).
    /// </summary>
    private void WriteIndex(int value)
    {
      if (value <= 0x7f)
        _bytes.Write((byte)value);
      else if (value <= 0xff)
      {
        _bytes.Write((byte)MsgPackTypeId.MpUByte);
        _bytes.Write((byte)value);
      }
      else if (value <= 0xffff)
      {
        _bytes.Write((byte)MsgPackTypeId.MpUShort);
        _bytes.WriteEndian((ulong)value, 2, _byteOrder);
      }
      else
      {
        _bytes.Write((byte)MsgPackTypeId.MpUInt);
        _bytes.WriteEndian((ulong)value, 4, _byteOrder);
      }
    }
  }
}
