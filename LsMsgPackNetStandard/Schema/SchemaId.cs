using System;
using System.Globalization;
using System.Security.Cryptography;

namespace LsMsgPack
{
  /// <summary>
  /// Identifies an indexed schema by its content: the first 16 bytes of the SHA-256 hash of the schema bytes (see <see cref="SchemaStore"/>).
  /// <para>A cryptographic hash, because a reader may cache schemas it received from others: with a weak hash a schema could be crafted to collide with another one and take its place.</para>
  /// </summary>
  public struct SchemaId : IEquatable<SchemaId>
  {
    /// <summary>
    /// The number of bytes of an id.
    /// </summary>
    public const int Length = 16;

    /// <summary>
    /// Hashed before the schema bytes, so a future change of the schema layout gets new ids.
    /// </summary>
    private static readonly byte[] FormatVersion = { 1 };

    private readonly ulong _high;
    private readonly ulong _low;

    /// <param name="bytes">The 16 bytes of the id (as returned by <see cref="ToByteArray"/>)</param>
    public SchemaId(byte[] bytes) : this(bytes, 0) { }

    internal SchemaId(byte[] bytes, int offset)
    {
      if (bytes is null)
        throw new ArgumentNullException(nameof(bytes));
      if (offset < 0 || bytes.Length - offset < Length)
        throw new ArgumentException($"A schema id has {Length} bytes.", nameof(bytes));

      _high = ReadUInt64(bytes, offset);
      _low = ReadUInt64(bytes, offset + 8);
    }

    [ThreadStatic]
    private static SHA256 _sha256;

    /// <summary>
    /// The id of the given schema bytes (as written by <see cref="TypeResolving.Types.IndexedSchemaTypeResolver.Pack()"/>).
    /// </summary>
    public static SchemaId Compute(byte[] schema)
    {
      if (schema is null)
        throw new ArgumentNullException(nameof(schema));

      SHA256 sha = _sha256 ?? (_sha256 = SHA256.Create());
      sha.TransformBlock(FormatVersion, 0, 1, null, 0);
      sha.TransformFinalBlock(schema, 0, schema.Length);
      return new SchemaId(sha.Hash, 0);
    }

    public byte[] ToByteArray()
    {
      byte[] bytes = new byte[Length];
      WriteTo(bytes, 0);
      return bytes;
    }

    internal void WriteTo(byte[] target, int offset)
    {
      WriteUInt64(target, offset, _high);
      WriteUInt64(target, offset + 8, _low);
    }

    /// <summary>
    /// Reads the hexadecimal notation of <see cref="ToString"/>.
    /// </summary>
    public static SchemaId Parse(string hex)
    {
      if (hex is null || hex.Length != Length * 2)
        throw new FormatException($"A schema id is written as {Length * 2} hexadecimal digits.");

      byte[] bytes = new byte[Length];
      for (int t = 0; t < Length; t++)
        bytes[t] = byte.Parse(hex.Substring(t * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
      return new SchemaId(bytes);
    }

    /// <summary>
    /// 32 hexadecimal digits (lower case).
    /// </summary>
    public override string ToString()
    {
      return _high.ToString("x16", CultureInfo.InvariantCulture) + _low.ToString("x16", CultureInfo.InvariantCulture);
    }

    public bool Equals(SchemaId other)
    {
      return _high == other._high && _low == other._low;
    }

    public override bool Equals(object obj)
    {
      return obj is SchemaId other && Equals(other);
    }

    public override int GetHashCode()
    {
      return (int)_low ^ (int)(_low >> 32); // the bytes of a hash are already evenly distributed
    }

    public static bool operator ==(SchemaId a, SchemaId b) { return a.Equals(b); }
    public static bool operator !=(SchemaId a, SchemaId b) { return !a.Equals(b); }

    private static ulong ReadUInt64(byte[] bytes, int offset)
    {
      ulong value = 0;
      for (int t = 0; t < 8; t++)
        value = (value << 8) | bytes[offset + t];
      return value;
    }

    private static void WriteUInt64(byte[] bytes, int offset, ulong value)
    {
      for (int t = 7; t >= 0; t--)
      {
        bytes[offset + t] = (byte)value;
        value >>= 8;
      }
    }
  }
}
