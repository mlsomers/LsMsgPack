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
    /// Copies the schema from the stream.
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
        CopyName(stream, raw, settings);
        long props = ReadArrayLength(stream, ReadByte(stream, raw), raw, settings);
        for (long p = 0; p < props; p++)
          CopyName(stream, raw, settings);
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
      if (settings is null)
        settings = Canonical;
      MemoryStream stream = new MemoryStream(raw, false);
      int first = stream.ReadByte();
      long types = ReadMapLength(stream, first, null, settings);
      List<ComplexTypeDef> defs = new List<ComplexTypeDef>((int)Math.Min(types, 1024));
      for (int t = 0; t < types; t++)
      {
        string typeName = ReadName(stream, settings);
        long props = ReadArrayLength(stream, ReadByte(stream, null), null, settings);
        ComplexTypeDef def = new ComplexTypeDef() { TypeId = t, TypeName = typeName, Props = new List<string>((int)Math.Min(props, 1024)) };
        for (long p = 0; p < props; p++)
          def.Props.Add(ReadName(stream, settings));
        defs.Add(def);
      }

      if (stream.Position != raw.Length)
        throw InvalidSchema();
      return defs;
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

    private static void CopyName(Stream stream, ByteWriter raw, MsgPackOptions settings)
    {
      long length = ReadNameLength(stream, ReadByte(stream, raw), raw, settings);
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
      long length = ReadNameLength(stream, ReadByte(stream, null), null, settings);
      if (length < 0)
        throw InvalidSchema();
      if (length == 0)
        return string.Empty;
      return MsgPackOptions.StringEncoding.GetString(ReadBytes(stream, (int)length));
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
