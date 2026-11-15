using LsMsgPack.TypeResolving.Types;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// The values of the members a class does not have, kept by its extension data member (see <see cref="ExtensionDataMember"/>, docs/ExtensionData.md).
  /// <para>Both serializers keep them through the plain values (object[] for arrays, KeyValuePair&lt;object, object&gt;[] for maps, the primitives, extensions and timestamps their readers unpack),
  /// so they keep the same bytes: <see cref="Encode"/> writes them in the byte order of the specification, the numbers in the width they were read with.</para>
  /// <para>With the indexed schema the keys and type ids in a value are indexes into the writer's schema, which the next writer does not have: <see cref="WithoutSchema"/> makes its objects maps keyed by names
  /// and its type ids type names, with the types of version 2 (<see cref="ComplexTypeDef.PropType"/>). Version 1 has only the names, which do not say what a value holds, so an array or a map is refused there.</para>
  /// </summary>
  internal static class KeptValues
  {
    private static readonly DateTime Zero = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
    private const long ZeroSeconds = 62135596800; // of Zero
    private static readonly DateTime MaxFExt4 = Zero.AddSeconds(uint.MaxValue);
    private static readonly DateTime MaxFExt8 = new DateTime(2514, 5, 30, 1, 53, 03, DateTimeKind.Utc).Add(TimeSpan.FromTicks(9999999));

    /// <summary>
    /// A plain value read from the data in a form without the schema of the data (see the class): as it is without the schema (<paramref name="def"/> null).
    /// </summary>
    /// <param name="def">The writer's entry of the object (null without the indexed schema)</param>
    /// <param name="position">The position of the name in <paramref name="def"/></param>
    /// <exception cref="MsgPackException">Version 1 of the schema and an array or map (marked as not converted, see <see cref="ReadDifferences.NotConverted"/>)</exception>
    internal static object WithoutSchema(object value, string name, ComplexTypeDef def, int position, IndexedSchemaTypeResolver schema, MsgPackOptions settings)
    {
      if (def is null || schema is null || value is null)
        return value;
      if (!def.IsDescribed)
      {
        if (value is object[] || value is KeyValuePair<object, object>[])
          throw ReadDifferences.NotConverted(settings, new MsgPackException($"{def.TypeName}.{name} is not a member of the class and its value is an array or a map: with version 1 of the indexed schema (SchemaVersion.Names, the default) "
            + "the data does not say which classes the objects in it are, so it cannot be kept. Write it with SchemaVersion.Described (version 2), or without the indexed schema."));
        return value;
      }
      return Retype(value, def.PropType(position), schema.ByTypeId);
    }

    /// <summary>
    /// A value of a type of version 2 (an entry index, a kind, or <c>[kind, ...]</c>, see SchemaBytes.ReadTypeTree), its objects as maps keyed by names.
    /// </summary>
    private static object Retype(object value, object type, List<ComplexTypeDef> defs)
    {
      if (value is null)
        return null;
      if (type is long entry)
        return entry < defs.Count ? Object(value, defs[(int)entry], defs) : Any(value, defs);
      if (type is string kind)
        return kind == "any" ? Any(value, defs) : value;
      if (!(type is object[] composite) || composite.Length == 0 || !(composite[0] is string compositeKind))
        return Any(value, defs);

      switch (compositeKind)
      {
        case "?":
        case "struct":
          return composite.Length > 1 ? Retype(value, composite[1], defs) : Any(value, defs);
        case "list":
          if (value is object[] items && composite.Length > 1)
          {
            object[] result = new object[items.Length];
            for (int t = 0; t < items.Length; t++)
              result[t] = Retype(items[t], composite[1], defs);
            return result;
          }
          return Any(value, defs); // a collection wrapped with a type id or properties
        case "dict":
          if (value is KeyValuePair<object, object>[] pairs && composite.Length > 2 && !IsWrapped(pairs))
          {
            KeyValuePair<object, object>[] result = new KeyValuePair<object, object>[pairs.Length];
            for (int t = 0; t < pairs.Length; t++)
              result[t] = new KeyValuePair<object, object>(Retype(pairs[t].Key, composite[1], defs), Retype(pairs[t].Value, composite[2], defs));
            return result;
          }
          return Any(value, defs);
        case "pair":
        case "tuple":
          if (value is object[] values)
          {
            object[] result = new object[values.Length];
            for (int t = 0; t < values.Length; t++)
              result[t] = Retype(values[t], t + 1 < composite.Length ? composite[t + 1] : null, defs);
            return result;
          }
          return Any(value, defs);
        default:
          return Any(value, defs);
      }
    }

    /// <summary>
    /// A map with a type id (an object of another class than declared, or a wrapped value).
    /// </summary>
    private static bool IsWrapped(KeyValuePair<object, object>[] map)
    {
      for (int t = 0; t < map.Length; t++)
        if (map[t].Key is string key && (key.Length == 0 || key == MsgPackOptions.ContentKey))
          return true;
      return false;
    }

    /// <summary>
    /// A value declared as object (or of a type this reader does not know): only a map with a type id says which entry it is, other maps and arrays are collections.
    /// </summary>
    private static object Any(object value, List<ComplexTypeDef> defs)
    {
      if (value is KeyValuePair<object, object>[] map)
      {
        for (int t = 0; t < map.Length; t++)
          if (map[t].Key is string key && key.Length == 0)
            return Object(value, null, defs);
        return AnyEntries(map, defs);
      }
      if (value is object[] items)
      {
        object[] result = new object[items.Length];
        for (int t = 0; t < items.Length; t++)
          result[t] = Any(items[t], defs);
        return result;
      }
      return value;
    }

    /// <summary>
    /// The entries of a map that is not an object of an entry (a dictionary, an object written by names): the keys as they are (types of their own), the values declared as object.
    /// </summary>
    private static KeyValuePair<object, object>[] AnyEntries(KeyValuePair<object, object>[] map, List<ComplexTypeDef> defs)
    {
      KeyValuePair<object, object>[] result = new KeyValuePair<object, object>[map.Length];
      for (int t = 0; t < map.Length; t++)
        result[t] = new KeyValuePair<object, object>(map[t].Key is string ? map[t].Key : Any(map[t].Key, defs), Any(map[t].Value, defs));
      return result;
    }

    /// <summary>
    /// An object of the entry (or of the entry its type id names): a map keyed by its names, the type id as the type name.
    /// <para>Written as a map of indexes, as an array of values (<see cref="ObjectLayout.Array"/>), or wrapped with a type id: <c>{ "": typeId, "@": [values] }</c>,
    /// or a collection's map <c>{ "": typeId, "@": elements, ...properties }</c> (an entry with a content type).</para>
    /// </summary>
    private static object Object(object value, ComplexTypeDef def, List<ComplexTypeDef> defs)
    {
      if (value is object[] positional)
        return def is null ? Any(value, defs) : ByNames(positional, def, defs, null);
      if (!(value is KeyValuePair<object, object>[] map))
        return value;

      string typeName = null;
      object content = null;
      bool hasContent = false;
      for (int t = 0; t < map.Length; t++)
      {
        if (!(map[t].Key is string key))
          continue;
        if (key.Length == 0)
        {
          object id = map[t].Value;
          if (id is string name)
            typeName = name;
          else if (id != null && id.GetType().IsPrimitive && !(id is bool) && !(id is float) && !(id is double))
          {
            long index = Convert.ToInt64(id, System.Globalization.CultureInfo.InvariantCulture);
            if (index >= 0 && index < defs.Count)
            {
              def = defs[(int)index];
              typeName = def.TypeName;
            }
          }
        }
        else if (key == MsgPackOptions.ContentKey)
        {
          content = map[t].Value;
          hasContent = true;
        }
      }
      if (def is null) // a type id that is no entry of this schema (a name already: an object written by names), or none
        return AnyEntries(map, defs);

      object contentType = def.ContentType;
      if (hasContent && contentType is null && content is object[] values) // an object written as an array, wrapped with its type id
        return ByNames(values, def, defs, typeName);

      List<KeyValuePair<object, object>> result = new List<KeyValuePair<object, object>>(map.Length);
      if (typeName != null)
        result.Add(new KeyValuePair<object, object>(MsgPackOptions.TypeIdKey, typeName));
      for (int t = 0; t < map.Length; t++)
      {
        object key = map[t].Key;
        if (key is string text)
        {
          if (text.Length == 0)
            continue;
          if (text == MsgPackOptions.ContentKey)
            result.Add(new KeyValuePair<object, object>(text, contentType is null ? Any(content, defs) : Retype(content, contentType, defs)));
          else
            result.Add(new KeyValuePair<object, object>(text, Any(map[t].Value, defs))); // a name already (an object written by names)
          continue;
        }
        int position = PositionOf(key, def);
        if (position < 0)
          result.Add(new KeyValuePair<object, object>(key, Any(map[t].Value, defs))); // not a name of the entry: nothing to translate it to
        else
          result.Add(new KeyValuePair<object, object>(def.Props[position], Retype(map[t].Value, def.PropType(position), defs)));
      }
      return result.ToArray();
    }

    private static int PositionOf(object key, ComplexTypeDef def)
    {
      if (key is null || !key.GetType().IsPrimitive || key is bool || key is char || key is float || key is double)
        return -1;
      long index = Convert.ToInt64(key, System.Globalization.CultureInfo.InvariantCulture);
      return index >= 0 && index < def.Props.Count && def.Props[(int)index] != null ? (int)index : -1;
    }

    /// <summary>
    /// The values of an object written as an array, as a map of the names of the entry (a nil is a value left out, as a key that is not in a map).
    /// </summary>
    private static object ByNames(object[] values, ComplexTypeDef def, List<ComplexTypeDef> defs, string typeName)
    {
      List<KeyValuePair<object, object>> result = new List<KeyValuePair<object, object>>(values.Length + 1);
      if (typeName != null)
        result.Add(new KeyValuePair<object, object>(MsgPackOptions.TypeIdKey, typeName));
      for (int t = 0; t < values.Length; t++)
      {
        if (values[t] is null || t >= def.Props.Count || def.Props[t] is null)
          continue;
        result.Add(new KeyValuePair<object, object>(def.Props[t], Retype(values[t], def.PropType(t), defs)));
      }
      return result.ToArray();
    }

    /// <summary>
    /// The value as one MsgPack value in the byte order of the specification.
    /// </summary>
    /// <param name="compact">Integers in the smallest format (values of a dictionary, whose numbers do not say how they were read); otherwise in the width of their .NET type, as they were read
    /// (only bytes up to 127 and sbytes from -32 as fixints: the readers unpack those as bytes and sbytes)</param>
    /// <param name="settings">The settings of the serializer: what it reads as a DateTime (<see cref="MsgPackOptions.ReadDateTimeKind"/>), how it writes the values it has extensions for</param>
    internal static byte[] Encode(object value, MsgPackOptions settings, bool compact)
    {
      ByteWriter bytes = new ByteWriter(64);
      Write(bytes, value, settings, compact, 0);
      return bytes.ToArray();
    }

    private static void Write(ByteWriter bytes, object value, MsgPackOptions settings, bool compact, int depth)
    {
      if (depth > settings._maxDepth)
        throw new MsgPackException($"The value is nested deeper than MaxDepth ({settings._maxDepth}).");
      MsgPackOptions order = SchemaBytes.Canonical;
      switch (value)
      {
        case null: bytes.Write((byte)MsgPackTypeId.MpNull); return;
        case bool b: bytes.Write((byte)(b ? MsgPackTypeId.MpBoolTrue : MsgPackTypeId.MpBoolFalse)); return;
        case string s: bytes.WriteString(s, order); return;
        case byte[] bin: SchemaBytes.WriteBin(bytes, bin); return;
        case float f: bytes.Write((byte)MsgPackTypeId.MpFloat); bytes.WriteEndian((uint)BitConverter.ToInt32(BitConverter.GetBytes(f), 0), 4, order); return;
        case double d: bytes.Write((byte)MsgPackTypeId.MpDouble); bytes.WriteEndian((ulong)BitConverter.DoubleToInt64Bits(d), 8, order); return;
        case DateTime dateTime: WriteTimestamp(bytes, compact ? settings.ToTimestamp(dateTime) : (dateTime.Kind == DateTimeKind.Local ? dateTime.ToUniversalTime() : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc))); return;
        case IMsgPackExtension extension: WriteExtension(bytes, extension.TypeSpecifier, extension.Data); return;
        case object[] items:
          bytes.WriteArrayHeader(items.Length, order);
          for (int t = 0; t < items.Length; t++)
            Write(bytes, items[t], settings, compact, depth + 1);
          return;
        case KeyValuePair<object, object>[] map:
          bytes.WriteMapHeader(map.Length, order);
          for (int t = 0; t < map.Length; t++)
          {
            Write(bytes, map[t].Key, settings, compact, depth + 1);
            Write(bytes, map[t].Value, settings, compact, depth + 1);
          }
          return;
      }

      if (WriteInteger(bytes, value, compact))
        return;
      if (compact) // a value of a dictionary
      {
        if (value is IDictionary dictionary)
        {
          bytes.WriteMapHeader(dictionary.Count, order);
          foreach (DictionaryEntry entry in dictionary)
          {
            Write(bytes, entry.Key, settings, compact, depth + 1);
            Write(bytes, entry.Value, settings, compact, depth + 1);
          }
          return;
        }
        if (value is IList list && !(value is Array array && array.Rank > 1))
        {
          bytes.WriteArrayHeader(list.Count, order);
          for (int t = 0; t < list.Count; t++)
            Write(bytes, list[t], settings, compact, depth + 1);
          return;
        }
      }

      // A value the serializer has an extension or a format for (decimal, a custom extension's type), or an object put into a dictionary: as it writes it without the schema, by names
      byte[] plain = settings.SerializePlainValue(value, value.GetType(), order._endianAction, true);
      if (plain is null)
        throw new MsgPackException($"A value of {value.GetType().Name} cannot be kept as extension data.");
      bytes.Write(plain);
    }

    private static bool WriteInteger(ByteWriter bytes, object value, bool compact)
    {
      MsgPackOptions order = SchemaBytes.Canonical;
      long signed;
      switch (value)
      {
        case byte b:
          if (b <= 0x7F) bytes.Write(b);
          else { bytes.Write((byte)MsgPackTypeId.MpUByte); bytes.Write(b); }
          return true;
        case sbyte sb:
          if (sb >= -32) bytes.Write((byte)sb);
          else { bytes.Write((byte)MsgPackTypeId.MpSByte); bytes.Write((byte)sb); }
          return true;
        case ushort us: if (compact) { WriteCompact(bytes, us); return true; } bytes.Write((byte)MsgPackTypeId.MpUShort); bytes.WriteEndian(us, 2, order); return true;
        case uint ui: if (compact) { WriteCompact(bytes, ui); return true; } bytes.Write((byte)MsgPackTypeId.MpUInt); bytes.WriteEndian(ui, 4, order); return true;
        case ulong ul:
          if (compact && ul <= long.MaxValue) { WriteCompact(bytes, (long)ul); return true; }
          bytes.Write((byte)MsgPackTypeId.MpULong); bytes.WriteEndian(ul, 8, order); return true;
        case short s: signed = s; if (compact) break; bytes.Write((byte)MsgPackTypeId.MpShort); bytes.WriteEndian((ulong)(ushort)s, 2, order); return true;
        case int i: signed = i; if (compact) break; bytes.Write((byte)MsgPackTypeId.MpInt); bytes.WriteEndian((uint)i, 4, order); return true;
        case long l: signed = l; if (compact) break; bytes.Write((byte)MsgPackTypeId.MpLong); bytes.WriteEndian((ulong)l, 8, order); return true;
        default: return false;
      }
      WriteCompact(bytes, signed);
      return true;
    }

    /// <summary>
    /// The smallest format of the value (as MpInt writes it with DynamicallyCompact): unsigned formats for positive values.
    /// </summary>
    private static void WriteCompact(ByteWriter bytes, long value)
    {
      MsgPackOptions order = SchemaBytes.Canonical;
      if (value >= 0)
      {
        if (value <= 0x7F) bytes.Write((byte)value);
        else if (value <= byte.MaxValue) { bytes.Write((byte)MsgPackTypeId.MpUByte); bytes.Write((byte)value); }
        else if (value <= ushort.MaxValue) { bytes.Write((byte)MsgPackTypeId.MpUShort); bytes.WriteEndian((ulong)value, 2, order); }
        else if (value <= uint.MaxValue) { bytes.Write((byte)MsgPackTypeId.MpUInt); bytes.WriteEndian((ulong)value, 4, order); }
        else { bytes.Write((byte)MsgPackTypeId.MpULong); bytes.WriteEndian((ulong)value, 8, order); }
        return;
      }
      if (value >= -32) bytes.Write((byte)value);
      else if (value >= sbyte.MinValue) { bytes.Write((byte)MsgPackTypeId.MpSByte); bytes.Write((byte)value); }
      else if (value >= short.MinValue) { bytes.Write((byte)MsgPackTypeId.MpShort); bytes.WriteEndian((ulong)value, 2, order); }
      else if (value >= int.MinValue) { bytes.Write((byte)MsgPackTypeId.MpInt); bytes.WriteEndian((ulong)value, 4, order); }
      else { bytes.Write((byte)MsgPackTypeId.MpLong); bytes.WriteEndian((ulong)value, 8, order); }
    }

    /// <summary>
    /// A timestamp (extension -1) in the smallest of its formats, as the serializers write it (LsMsgPack's MsgPackWriter.DateTime): the seconds rounded down, also before 1970.
    /// </summary>
    private static void WriteTimestamp(ByteWriter bytes, DateTime utc)
    {
      MsgPackOptions order = SchemaBytes.Canonical;
      long ticks = utc.Ticks;
      long whole = ticks / TimeSpan.TicksPerSecond;
      long seconds = whole - ZeroSeconds;
      long fraction = ticks - whole * TimeSpan.TicksPerSecond;
      if (utc < Zero || utc > MaxFExt8)
      {
        bytes.Write((byte)MsgPackTypeId.MpExt8);
        bytes.Write((byte)12);
        bytes.Write((byte)0xFF);
        bytes.WriteEndian((ulong)fraction * 100, 4, order);
        bytes.WriteEndian((ulong)seconds, 8, order);
      }
      else if (utc > MaxFExt4 || fraction != 0)
      {
        bytes.Write((byte)MsgPackTypeId.MpFExt8);
        bytes.Write((byte)0xFF);
        bytes.WriteEndian(((ulong)fraction * 100) << 34 | (ulong)seconds, 8, order);
      }
      else
      {
        bytes.Write((byte)MsgPackTypeId.MpFExt4);
        bytes.Write((byte)0xFF);
        bytes.WriteEndian((ulong)seconds, 4, order);
      }
    }

    private static void WriteExtension(ByteWriter bytes, sbyte typeCode, byte[] data)
    {
      MsgPackOptions order = SchemaBytes.Canonical;
      switch (data.Length)
      {
        case 1: bytes.Write((byte)MsgPackTypeId.MpFExt1); break;
        case 2: bytes.Write((byte)MsgPackTypeId.MpFExt2); break;
        case 4: bytes.Write((byte)MsgPackTypeId.MpFExt4); break;
        case 8: bytes.Write((byte)MsgPackTypeId.MpFExt8); break;
        case 16: bytes.Write((byte)MsgPackTypeId.MpFExt16); break;
        default:
          if (data.Length <= byte.MaxValue) { bytes.Write((byte)MsgPackTypeId.MpExt8); bytes.Write((byte)data.Length); }
          else if (data.Length <= ushort.MaxValue) { bytes.Write((byte)MsgPackTypeId.MpExt16); bytes.WriteEndian((ulong)data.Length, 2, order); }
          else { bytes.Write((byte)MsgPackTypeId.MpExt32); bytes.WriteEndian((ulong)data.Length, 4, order); }
          break;
      }
      bytes.Write((byte)typeCode);
      bytes.Write(data);
    }

    /// <summary>
    /// A plain value as the dictionary of a <c>[MsgPackExtensionData]</c> member holds it: maps as a Dictionary&lt;string, object&gt; (Dictionary&lt;object, object&gt; when a key is not a string), arrays as object[].
    /// </summary>
    internal static object ToDictionaryValue(object value)
    {
      if (value is object[] items)
      {
        object[] result = new object[items.Length];
        for (int t = 0; t < items.Length; t++)
          result[t] = ToDictionaryValue(items[t]);
        return result;
      }
      if (value is KeyValuePair<object, object>[] map)
      {
        bool names = true;
        for (int t = 0; t < map.Length && names; t++)
          names = map[t].Key is string;
        if (names)
        {
          Dictionary<string, object> byName = new Dictionary<string, object>(map.Length, StringComparer.Ordinal);
          for (int t = 0; t < map.Length; t++)
            byName[(string)map[t].Key] = ToDictionaryValue(map[t].Value);
          return byName;
        }
        Dictionary<object, object> byKey = new Dictionary<object, object>(map.Length);
        for (int t = 0; t < map.Length; t++)
          if (map[t].Key != null)
            byKey[map[t].Key] = ToDictionaryValue(map[t].Value);
        return byKey;
      }
      return value;
    }

    /// <summary>
    /// A value given to <see cref="MsgPackExtensionData.SetRaw(string, byte[])"/>: exactly one MsgPack value.
    /// </summary>
    internal static void CheckOneValue(byte[] value, string parameter)
    {
      if (value is null)
        throw new ArgumentNullException(parameter);
      try
      {
        if (SchemaBytes.SkipValue(value) == value.Length)
          return;
      }
      catch (Exception ex) when (ex is MsgPackException || ex is EndOfStreamException)
      {
        throw new ArgumentException("Not one complete MsgPack value (or nested deeper than 64).", parameter, ex);
      }
      throw new ArgumentException("More than one MsgPack value: a kept value is exactly one.", parameter);
    }

    /// <summary>
    /// A kept value in the byte order of the settings (the kept bytes are in the order of the specification).
    /// </summary>
    internal static byte[] InByteOrder(byte[] value, MsgPackOptions settings)
    {
      if (MsgPackOptions.SwapEndianChoice(settings, 2) == MsgPackOptions.SwapEndianChoice(SchemaBytes.Canonical, 2)) // the same for every size above one byte
        return value;
      return SchemaBytes.CopyValue(value, SchemaBytes.Canonical, settings);
    }
  }
}
