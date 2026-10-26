using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Collections.Generic;
using System.IO;

namespace LsMsgPack
{
  /// <summary>
  /// Reads the indexed schema directly from the bytes (without <see cref="MsgPackItem"/>s): a map of type names with an array of property names each (see <see cref="IndexedSchemaTypeResolver.Pack()"/>).
  /// <para>Reading is split in two steps, so a schema that was seen before is recognized by its bytes before any name is decoded.</para>
  /// </summary>
  internal static class SchemaBytes
  {
    /// <summary>
    /// Longer names are refused, so a corrupt or hostile length cannot make the reader allocate a huge buffer.
    /// </summary>
    private const int MaxNameLength = 0xFFFF;

    /// <summary>
    /// The largest schema <see cref="ReadExport"/> accepts (16 MB).
    /// </summary>
    private const int MaxExportedLength = 16 * 1024 * 1024;

    /// <summary>
    /// Settings with the byte order of the MsgPack specification (whatever <see cref="MsgPackSettings.Default_EndianAction"/> is): the schemas in a <see cref="SchemaStore"/> are identified by these bytes.
    /// </summary>
    internal static readonly MsgPackOptions Canonical = new DefaultOptions() { EndianAction = EndianAction.SwapIfCurrentSystemIsLittleEndian };

    /// <summary>
    /// Copies the schema from the stream (of either <see cref="SchemaVersion"/>).
    /// </summary>
    /// <param name="firstByte">The first byte of the schema, already read from the stream</param>
    /// <param name="settings">Only the <see cref="MsgPackSettings.EndianAction"/> is used (for the lengths of long names and large schemas)</param>
    /// <exception cref="MsgPackException">When the stream does not contain a schema</exception>
    internal static byte[] ReadRaw(Stream stream, int firstByte, MsgPackOptions settings)
    {
      ByteWriter raw = new ByteWriter(512);
      raw.Write((byte)firstByte);

      long types = ReadMapLength(stream, firstByte, raw, settings);
      for (long t = 0; t < types; t++)
      {
        CopyName(stream, ReadByte(stream, raw), raw, settings);
        long props = ReadArrayLength(stream, ReadByte(stream, raw), raw, settings);
        for (long p = 0; p < props; p++)
        {
          int first = ReadByte(stream, raw);
          if (IsArray(first)) // version 2: [name, type] or [name, type, default], ["@", type]
            CopyRest(stream, first, raw, settings, settings, 1);
          else
            CopyName(stream, first, raw, settings);
        }
      }

      return raw.ToArray();
    }

    /// <summary>
    /// Decodes the type and property names of a schema read by <see cref="ReadRaw"/> (or packed by <see cref="IndexedSchemaTypeResolver.Pack()"/>).
    /// </summary>
    /// <returns>The types in the order of their ids, without a <see cref="ComplexTypeDef.Type"/></returns>
    /// <param name="settings">The <see cref="MsgPackSettings.EndianAction"/> of the lengths, null for <see cref="Canonical"/></param>
    internal static List<ComplexTypeDef> Parse(byte[] raw, MsgPackOptions settings)
    {
      return Parse(raw, settings, out SchemaVersion _, out int _);
    }

    internal static List<ComplexTypeDef> Parse(byte[] raw, MsgPackOptions settings, out SchemaVersion version)
    {
      return Parse(raw, settings, out version, out int _);
    }

    /// <summary>
    /// Version 2 (<see cref="DescribedSchema"/>) has a last entry <c>""</c> (no type has an empty name), its properties are <c>[name, type]</c> or <c>[name, type, default]</c>,
    /// items <c>["@", type]</c> describe the content of an entry and are not properties (the property ids are the positions of the other items). The names are kept, and of the types
    /// only the entries of the objects they hold (<see cref="ComplexTypeDef.ValueEntries"/>, <see cref="ComplexTypeDef.ContentEntry"/>), to pair the classes of a reader without the writer's classes (IndexedSchemaTypeResolver.BindReaderTypes).
    /// </summary>
    /// <param name="version">The version of the schema</param>
    /// <param name="rootEntry">Version 2: the entry of the objects the root holds (see <see cref="ReadValueEntry"/>), otherwise -1</param>
    internal static List<ComplexTypeDef> Parse(byte[] raw, MsgPackOptions settings, out SchemaVersion version, out int rootEntry)
    {
      rootEntry = -1;
      if (settings is null)
        settings = Canonical;
      MemoryStream stream = new MemoryStream(raw, false);
      int first = stream.ReadByte();
      long types = ReadMapLength(stream, first, null, settings);
      List<ComplexTypeDef> defs = new List<ComplexTypeDef>((int)Math.Min(types, 1024));
      bool described = false;
      version = SchemaVersion.Names;
      for (int t = 0; t < types; t++)
      {
        string typeName = ReadName(stream, settings);
        long props = ReadArrayLength(stream, ReadByte(stream, null), null, settings);
        if (typeName.Length == 0) // the root of version 2: [["@", type]], the last entry
        {
          if (t != types - 1 || props != 1 || ReadItem(stream, settings, out bool content, out rootEntry) != DescribedSchema.ContentName || !content)
            throw InvalidSchema();
          version = SchemaVersion.Described;
          break;
        }

        ComplexTypeDef def = new ComplexTypeDef() { TypeId = t, TypeName = typeName, Props = new List<string>((int)Math.Min(props, 1024)) };
        List<int> valueEntries = null;
        for (long p = 0; p < props; p++)
        {
          string name = ReadItem(stream, settings, out bool isDescribed, out int valueEntry);
          described |= isDescribed;
          if (isDescribed && name == DescribedSchema.ContentName)
          {
            if (p != 0) // first, so the positions of the properties do not depend on it
              throw InvalidSchema();
            def.ContentEntry = valueEntry;
            continue;
          }
          if (isDescribed && valueEntries is null)
            valueEntries = new List<int>(def.Props.Count + 1);
          if (valueEntries != null)
          {
            while (valueEntries.Count < def.Props.Count)
              valueEntries.Add(-1);
            valueEntries.Add(valueEntry);
          }
          def.Props.Add(name);
        }
        def.ValueEntries = valueEntries?.ToArray();
        defs.Add(def);
      }

      if (stream.Position != raw.Length || (described && version != SchemaVersion.Described))
        throw InvalidSchema();
      return defs;
    }

    /// <summary>
    /// A property name (version 1), or an item of version 2: <c>[name, type]</c>, <c>[name, type, default]</c> or <c>["@", type]</c>.
    /// </summary>
    /// <param name="described">An item of version 2</param>
    /// <param name="valueEntry">The entry of the objects its type holds (see <see cref="ReadValueEntry"/>), -1 for none</param>
    /// <returns>The name</returns>
    private static string ReadItem(Stream stream, MsgPackOptions settings, out bool described, out int valueEntry)
    {
      valueEntry = -1;
      int first = ReadByte(stream, null);
      described = IsArray(first);
      if (!described)
        return ReadName(stream, first, settings);

      long count = ReadArrayLength(stream, first, null, settings);
      if (count < 2 || count > 3)
        throw InvalidSchema();
      string name = ReadName(stream, ReadByte(stream, null), settings);
      if (count == 3 && name == DescribedSchema.ContentName)
        throw InvalidSchema();
      valueEntry = ReadValueEntry(stream, settings, 1);
      if (count == 3) // the default: any value, for other readers
        CopyValue(stream, null, settings, settings, 1);
      return name;
    }

    /// <summary>
    /// A type of version 2 (see <see cref="DescribedSchema"/>): the entry of the objects a value of it holds, as IndexedSchemaTypeResolver.ObjectTypeOf finds the class of a reader's type:
    /// an entry index itself, or one in <c>["?", T]</c>, <c>["struct", k]</c>, <c>["list", T]</c> or the value of <c>["dict", K, V]</c>. -1 for anything else (kinds, pairs, tuples, other readers' additions).
    /// </summary>
    private static int ReadValueEntry(Stream stream, MsgPackOptions settings, int depth)
    {
      if (depth > MaxValueDepth)
        throw InvalidSchema();
      int first = ReadByte(stream, null);
      if (first <= 0x7F)
        return first;
      if (first == (int)MsgPackTypeId.MpUByte)
        return (int)ReadNumber(stream, 1, settings);
      if (first == (int)MsgPackTypeId.MpUShort)
        return (int)ReadNumber(stream, 2, settings);
      if (first == (int)MsgPackTypeId.MpUInt)
      {
        ulong index = ReadNumber(stream, 4, settings);
        return index > int.MaxValue ? -1 : (int)index;
      }
      if (!IsArray(first))
      {
        CopyRest(stream, first, null, settings, settings, depth);
        return -1;
      }

      long count = ReadArrayLength(stream, first, null, settings);
      if (count == 0)
        return -1;
      int kindFirst = ReadByte(stream, null);
      string kind = null;
      if ((kindFirst & 0xE0) == 0xA0 || kindFirst == (int)MsgPackTypeId.MpStr8)
        kind = ReadName(stream, kindFirst, settings);
      else
        CopyRest(stream, kindFirst, null, settings, settings, depth + 1);

      int entry = -1;
      int follow = kind == "?" || kind == "struct" || kind == "list" ? 1 : kind == "dict" ? 2 : -1; // the item of the type of the values
      for (long t = 1; t < count; t++)
      {
        if (t == follow)
          entry = ReadValueEntry(stream, settings, depth + 1);
        else
          CopyValue(stream, null, settings, settings, depth + 1);
      }
      return entry;
    }

    /// <summary>
    /// The schema in another byte order (the lengths of long names and large schemas, and in version 2 also the numbers of the default values).
    /// </summary>
    internal static byte[] Repack(byte[] schema, MsgPackOptions from, MsgPackOptions to)
    {
      MemoryStream stream = new MemoryStream(schema, false);
      ByteWriter bytes = new ByteWriter(schema.Length);
      CopyValue(stream, bytes, from, to, 0);
      if (stream.Position != schema.Length)
        throw InvalidSchema();
      return bytes.ToArray();
    }

    /// <summary>
    /// Binary data in the smallest format (as MpBin writes it), the lengths in the byte order of the specification.
    /// </summary>
    internal static void WriteBin(ByteWriter bytes, byte[] value)
    {
      if (value.Length < 256) { bytes.Write((byte)MsgPackTypeId.MpBin8); bytes.Write((byte)value.Length); }
      else if (value.Length <= ushort.MaxValue) { bytes.Write((byte)MsgPackTypeId.MpBin16); bytes.WriteEndian((ulong)value.Length, 2, Canonical); }
      else { bytes.Write((byte)MsgPackTypeId.MpBin32); bytes.WriteEndian((ulong)value.Length, 4, Canonical); }
      bytes.Write(value);
    }

    /// <summary>
    /// Reads what <see cref="SchemaStore.Export"/> writes: a map of ids (bin) to schemas (bin).
    /// </summary>
    /// <returns>The schemas (the ids are not needed, they are computed again)</returns>
    internal static List<byte[]> ReadExport(Stream stream)
    {
      int first = stream.ReadByte();
      if (first < 0 || first == (int)MsgPackTypeId.MpNull)
        return new List<byte[]>(0);
      if (!IsMap(first))
        throw InvalidExport();

      long count = ReadMapLength(stream, first, null, Canonical);
      List<byte[]> schemas = new List<byte[]>((int)Math.Min(count, 1024));
      for (long t = 0; t < count; t++)
      {
        ReadBin(stream);
        schemas.Add(ReadBin(stream));
      }
      return schemas;
    }

    private static byte[] ReadBin(Stream stream)
    {
      int first = ReadByte(stream, null);
      long length;
      if (first == (int)MsgPackTypeId.MpBin8) length = ReadLength(stream, 1, null, Canonical);
      else if (first == (int)MsgPackTypeId.MpBin16) length = ReadLength(stream, 2, null, Canonical);
      else if (first == (int)MsgPackTypeId.MpBin32) length = ReadLength(stream, 4, null, Canonical);
      else throw InvalidExport();

      if (length > MaxExportedLength) // a corrupt or hostile length should not make the reader allocate a huge buffer
        throw InvalidExport();
      return ReadBytes(stream, (int)length);
    }

    private static MsgPackException InvalidExport()
    {
      return new MsgPackException("Invalid schema export, expected a map of schema ids (bin) to schemas (bin).");
    }

    /// <summary>
    /// The exception when the data does not start with a schema.
    /// </summary>
    internal static MsgPackException NotASchema(int firstByte)
    {
      MsgPackTypeId typeId = ToTypeId(firstByte);
      return new MsgPackException($"Expected the data to start with an indexed schema (a map) but found {typeId}. Was it serialized with MsgPackSettings.{nameof(MsgPackOptions.UseInexedSchema)} = false?", 0, typeId);
    }

    internal static MsgPackException InvalidSchema()
    {
      return new MsgPackException($"Invalid indexed schema, expected a map of type names with an array of property names. Was the data serialized with MsgPackSettings.{nameof(MsgPackOptions.UseInexedSchema)} = false?");
    }

    internal static bool IsMap(int firstByte)
    {
      return (firstByte & 0xF0) == 0x80 || firstByte == (int)MsgPackTypeId.MpMap16 || firstByte == (int)MsgPackTypeId.MpMap32;
    }

    /// <summary>
    /// The type id as <see cref="MsgPackItem.TypeId"/> would report it (the fixed formats that hold a value or length in the first byte are masked).
    /// </summary>
    private static MsgPackTypeId ToTypeId(int b)
    {
      if (b < 0) return MsgPackTypeId.NeverUsed;
      if (b <= 0x7F) return MsgPackTypeId.MpBytePart;
      if (b >= 0xE0) return MsgPackTypeId.MpSBytePart;
      if (b <= 0x8F) return MsgPackTypeId.MpMap4;
      if (b <= 0x9F) return MsgPackTypeId.MpArray4;
      if (b <= 0xBF) return MsgPackTypeId.MpStr5;
      return (MsgPackTypeId)b;
    }

    private static long ReadMapLength(Stream stream, int first, ByteWriter raw, MsgPackOptions settings)
    {
      if ((first & 0xF0) == 0x80) return first & 0x0F;
      if (first == (int)MsgPackTypeId.MpMap16) return ReadLength(stream, 2, raw, settings);
      if (first == (int)MsgPackTypeId.MpMap32) return ReadLength(stream, 4, raw, settings);
      throw NotASchema(first);
    }

    private static long ReadArrayLength(Stream stream, int first, ByteWriter raw, MsgPackOptions settings)
    {
      if ((first & 0xF0) == 0x90) return first & 0x0F;
      if (first == (int)MsgPackTypeId.MpArray16) return ReadLength(stream, 2, raw, settings);
      if (first == (int)MsgPackTypeId.MpArray32) return ReadLength(stream, 4, raw, settings);
      throw InvalidSchema();
    }

    /// <returns>The length of the name, -1 for nil</returns>
    private static long ReadNameLength(Stream stream, int first, ByteWriter raw, MsgPackOptions settings)
    {
      long length;
      if ((first & 0xE0) == 0xA0) length = first & 0x1F;
      else if (first == (int)MsgPackTypeId.MpStr8) length = ReadLength(stream, 1, raw, settings);
      else if (first == (int)MsgPackTypeId.MpStr16) length = ReadLength(stream, 2, raw, settings);
      else if (first == (int)MsgPackTypeId.MpStr32) length = ReadLength(stream, 4, raw, settings);
      else if (first == (int)MsgPackTypeId.MpNull) return -1;
      else throw InvalidSchema();

      if (length > MaxNameLength)
        throw InvalidSchema();
      return length;
    }

    private static void CopyName(Stream stream, int first, ByteWriter raw, MsgPackOptions settings)
    {
      long length = ReadNameLength(stream, first, raw, settings);
      if (length <= 0)
        return;
      byte[] name = ReadBytes(stream, (int)length);
      raw.Write(name);
    }

    /// <summary>
    /// Names are strings, nil (written for a missing name by <see cref="IndexedSchemaTypeResolver.Pack()"/>) is refused like it always was when reading.
    /// </summary>
    private static string ReadName(Stream stream, MsgPackOptions settings)
    {
      return ReadName(stream, ReadByte(stream, null), settings);
    }

    private static string ReadName(Stream stream, int first, MsgPackOptions settings)
    {
      long length = ReadNameLength(stream, first, null, settings);
      if (length < 0)
        throw InvalidSchema();
      if (length == 0)
        return string.Empty;
      return MsgPackOptions.StringEncoding.GetString(ReadBytes(stream, (int)length));
    }

    private static bool IsArray(int firstByte)
    {
      return (firstByte & 0xF0) == 0x90 || firstByte == (int)MsgPackTypeId.MpArray16 || firstByte == (int)MsgPackTypeId.MpArray32;
    }

    /// <summary>
    /// The deepest nesting of a value in a schema (the types of version 2, default values).
    /// </summary>
    private const int MaxValueDepth = 64;

    /// <summary>
    /// Copies (or skips, <paramref name="target"/> null) one MsgPack value, converting its numbers and lengths from the byte order of <paramref name="from"/> to the one of <paramref name="to"/>
    /// (as the serializers write them: integers, floats, lengths and timestamps; the data of other extensions as it is).
    /// </summary>
    private static void CopyValue(Stream stream, ByteWriter target, MsgPackOptions from, MsgPackOptions to, int depth)
    {
      int first = ReadByte(stream, null);
      target?.Write((byte)first);
      CopyRest(stream, first, target, from, to, depth);
    }

    /// <summary>
    /// <see cref="CopyValue"/> after the first byte (already copied).
    /// </summary>
    private static void CopyRest(Stream stream, int first, ByteWriter target, MsgPackOptions from, MsgPackOptions to, int depth)
    {
      if (depth > MaxValueDepth)
        throw InvalidSchema();

      if (first <= 0x7F || first >= 0xE0 || first == 0xC0 || first == 0xC2 || first == 0xC3) // fixint, nil, false, true
        return;
      if ((first & 0xF0) == 0x80) { CopyItems(stream, (first & 0x0F) * 2L, target, from, to, depth); return; }
      if ((first & 0xF0) == 0x90) { CopyItems(stream, first & 0x0F, target, from, to, depth); return; }
      if ((first & 0xE0) == 0xA0) { CopyBytes(stream, first & 0x1F, target); return; }

      switch ((MsgPackTypeId)first)
      {
        case MsgPackTypeId.MpStr8:
        case MsgPackTypeId.MpBin8: CopyBytes(stream, CopyLength(stream, 1, target, from, to), target); return;
        case MsgPackTypeId.MpStr16:
        case MsgPackTypeId.MpBin16: CopyBytes(stream, CopyLength(stream, 2, target, from, to), target); return;
        case MsgPackTypeId.MpStr32:
        case MsgPackTypeId.MpBin32: CopyBytes(stream, CopyLength(stream, 4, target, from, to), target); return;
        case MsgPackTypeId.MpUByte:
        case MsgPackTypeId.MpSByte: CopyLength(stream, 1, target, from, to); return;
        case MsgPackTypeId.MpUShort:
        case MsgPackTypeId.MpShort: CopyLength(stream, 2, target, from, to); return;
        case MsgPackTypeId.MpUInt:
        case MsgPackTypeId.MpInt:
        case MsgPackTypeId.MpFloat: CopyLength(stream, 4, target, from, to); return;
        case MsgPackTypeId.MpULong:
        case MsgPackTypeId.MpLong:
        case MsgPackTypeId.MpDouble: CopyLength(stream, 8, target, from, to); return;
        case MsgPackTypeId.MpArray16: CopyItems(stream, CopyLength(stream, 2, target, from, to), target, from, to, depth); return;
        case MsgPackTypeId.MpArray32: CopyItems(stream, CopyLength(stream, 4, target, from, to), target, from, to, depth); return;
        case MsgPackTypeId.MpMap16: CopyItems(stream, CopyLength(stream, 2, target, from, to) * 2, target, from, to, depth); return;
        case MsgPackTypeId.MpMap32: CopyItems(stream, CopyLength(stream, 4, target, from, to) * 2, target, from, to, depth); return;
        case MsgPackTypeId.MpFExt1: CopyExtension(stream, 1, target, from, to); return;
        case MsgPackTypeId.MpFExt2: CopyExtension(stream, 2, target, from, to); return;
        case MsgPackTypeId.MpFExt4: CopyExtension(stream, 4, target, from, to); return;
        case MsgPackTypeId.MpFExt8: CopyExtension(stream, 8, target, from, to); return;
        case MsgPackTypeId.MpFExt16: CopyExtension(stream, 16, target, from, to); return;
        case MsgPackTypeId.MpExt8: CopyExtension(stream, CopyLength(stream, 1, target, from, to), target, from, to); return;
        case MsgPackTypeId.MpExt16: CopyExtension(stream, CopyLength(stream, 2, target, from, to), target, from, to); return;
        case MsgPackTypeId.MpExt32: CopyExtension(stream, CopyLength(stream, 4, target, from, to), target, from, to); return;
        default: throw InvalidSchema(); // never used (0xC1)
      }
    }

    private static void CopyItems(Stream stream, long count, ByteWriter target, MsgPackOptions from, MsgPackOptions to, int depth)
    {
      for (long t = 0; t < count; t++) // each item has at least one byte: data that ends early throws
        CopyValue(stream, target, from, to, depth + 1);
    }

    /// <summary>
    /// A number of <paramref name="size"/> bytes (a length, an integer or the bits of a float), in the byte order of <paramref name="to"/>.
    /// </summary>
    /// <returns>The value as an unsigned number</returns>
    private static long CopyLength(Stream stream, int size, ByteWriter target, MsgPackOptions from, MsgPackOptions to)
    {
      ulong value = ReadNumber(stream, size, from);
      target?.WriteEndian(value, size, to);
      return (long)value;
    }

    private static ulong ReadNumber(Stream stream, int size, MsgPackOptions settings)
    {
      byte[] bytes = ReadBytes(stream, size);
      bool littleEndian = BitConverter.IsLittleEndian != MsgPackOptions.SwapEndianChoice(settings, size);
      ulong value = 0;
      for (int t = 0; t < size; t++)
        value = (value << 8) | bytes[littleEndian ? size - 1 - t : t];
      return value;
    }

    /// <summary>
    /// The type and data of an extension. A timestamp (type -1) holds numbers in the byte order of the settings (see MpDateTime), any other extension is copied as it is.
    /// </summary>
    private static void CopyExtension(Stream stream, long length, ByteWriter target, MsgPackOptions from, MsgPackOptions to)
    {
      int type = ReadByte(stream, null);
      target?.Write((byte)type);
      if (unchecked((sbyte)type) == -1 && (length == 4 || length == 8))
        CopyLength(stream, (int)length, target, from, to);
      else if (unchecked((sbyte)type) == -1 && length == 12)
      {
        CopyLength(stream, 4, target, from, to); // nanoseconds
        CopyLength(stream, 8, target, from, to); // seconds
      }
      else
        CopyBytes(stream, length, target);
    }

    private static void CopyBytes(Stream stream, long length, ByteWriter target)
    {
      if (length > MaxExportedLength || (stream.CanSeek && length > stream.Length - stream.Position)) // a corrupt or hostile length should not make the reader allocate a huge buffer
        throw EndOfData();
      byte[] bytes = ReadBytes(stream, (int)length);
      target?.Write(bytes);
    }

    private static int ReadByte(Stream stream, ByteWriter raw)
    {
      int b = stream.ReadByte();
      if (b < 0)
        throw EndOfData();
      raw?.Write((byte)b);
      return b;
    }

    /// <summary>
    /// A length of 2 or 4 bytes, in the byte order of <see cref="ByteWriter.WriteEndian"/>.
    /// </summary>
    private static long ReadLength(Stream stream, int size, ByteWriter raw, MsgPackOptions settings)
    {
      byte[] bytes = ReadBytes(stream, size);
      raw?.Write(bytes);

      bool littleEndian = BitConverter.IsLittleEndian != MsgPackOptions.SwapEndianChoice(settings, size);
      ulong value = 0;
      for (int t = 0; t < size; t++)
        value = (value << 8) | bytes[littleEndian ? size - 1 - t : t];
      return (long)value;
    }

    private static byte[] ReadBytes(Stream stream, int count)
    {
      byte[] buffer = new byte[count];
      int offset = 0;
      while (offset < count) // Stream.Read may return fewer bytes than asked (network streams)
      {
        int read = stream.Read(buffer, offset, count - offset);
        if (read <= 0)
          throw EndOfData();
        offset += read;
      }
      return buffer;
    }

    private static MsgPackException EndOfData()
    {
      return new MsgPackException("Unexpected end of data while reading the indexed schema.");
    }
  }
}
