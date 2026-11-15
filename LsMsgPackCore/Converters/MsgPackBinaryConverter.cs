using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LsMsgPack.Converters
{
  /// <summary>
  /// A converter that writes values as bytes it makes itself: MsgPack binary data (bin), or an extension when <see cref="ExtensionTypeCode"/> is set. Derive from <see cref="MsgPackBinaryConverter{T}"/>,
  /// or use the blit converters (<see cref="BlitArrayConverter{T}"/>...). Registered as any converter (<see cref="MsgPackOptions.Converters"/>, <see cref="MsgPackConverterAttribute"/>).
  /// </summary>
  public abstract class MsgPackBinaryConverter : MsgPackConverter
  {
    internal MsgPackBinaryConverter(sbyte? extensionTypeCode)
    {
      if (extensionTypeCode < 0)
        throw new ArgumentOutOfRangeException(nameof(extensionTypeCode), extensionTypeCode, "An extension type code of an application is 0 to 127: the negative ones are reserved by the MsgPack specification.");
      ExtensionTypeCode = extensionTypeCode;
    }

    /// <summary>
    /// The extension type code the bytes are written with, null to write them as binary data (bin). Readers take both: bin, or an extension of this type code.
    /// <para>Pick a code no other extension of the reader uses (LsMsgPackDebug reads the decimal extension's code 1 as a decimal).</para>
    /// </summary>
    public sbyte? ExtensionTypeCode { get; }

    /// <summary>
    /// The bytes as a value: binary data (the described schema says <c>bin</c>, or <c>any</c> for an extension).
    /// </summary>
    public sealed override Type SurrogateType
    {
      get { return typeof(byte[]); }
    }

    /// <param name="value">Not null, of exactly <see cref="MsgPackConverter.ConvertedType"/></param>
    internal abstract byte[] ToBytes(object value);

    internal abstract object FromBytes(byte[] data);

    internal sealed override object ToSurrogateBoxed(object value)
    {
      return ToBytes(value);
    }

    internal sealed override object FromSurrogateBoxed(object surrogate)
    {
      return FromBytes((byte[])surrogate);
    }
  }

  /// <summary>
  /// Writes values of <typeparamref name="T"/> as bytes it makes itself, and reads them back from those bytes (LsMsgPack writes them straight into its buffer and reads them in place).
  /// Neither method is called for null: a null value is nil, nil is read as null (the default of a struct).
  /// </summary>
  public abstract class MsgPackBinaryConverter<T> : MsgPackBinaryConverter
  {
    /// <param name="extensionTypeCode">null (the default): binary data (bin); 0 to 127: an extension of that type code</param>
    protected MsgPackBinaryConverter(sbyte? extensionTypeCode = null) : base(extensionTypeCode)
    {
    }

    public sealed override Type ConvertedType
    {
      get { return typeof(T); }
    }

    /// <summary>
    /// The number of bytes <see cref="Write"/> writes for the value.
    /// </summary>
    /// <param name="value">Not null</param>
    public abstract int GetLength(T value);

    /// <summary>
    /// Writes exactly <see cref="GetLength"/> bytes.
    /// </summary>
    /// <param name="value">Not null</param>
    public abstract void Write(T value, Span<byte> destination);

    /// <summary>
    /// The value of the bytes (as many as were written, also none). The bytes are only valid during the call: copy what you keep.
    /// </summary>
    public abstract T Read(ReadOnlySpan<byte> data);

    internal sealed override byte[] ToBytes(object value)
    {
      T typed = (T)value;
      byte[] bytes = new byte[GetLength(typed)];
      Write(typed, bytes);
      return bytes;
    }

    internal sealed override object FromBytes(byte[] data)
    {
      return Read(data);
    }

    internal sealed override TResult Accept<TResult>(IMsgPackConverterVisitor<TResult> visitor)
    {
      return visitor.Binary(this);
    }
  }

  /// <summary>
  /// What the blit converters share.
  /// </summary>
  internal static class Blit
  {
    internal static void ThrowIfBigEndian()
    {
      if (!BitConverter.IsLittleEndian)
        throw new PlatformNotSupportedException("The blit converters write the memory of the values as it is, which is little-endian in the data: this machine is big-endian.");
    }

    /// <returns>The number of values of the data</returns>
    /// <exception cref="MsgPackException">The length is not a whole number of values</exception>
    internal static int CountOf<T>(ReadOnlySpan<byte> data) where T : unmanaged
    {
      int size = Unsafe.SizeOf<T>();
      if (data.Length % size != 0)
        throw new MsgPackException($"{data.Length} bytes are not a whole number of {typeof(T).Name} values of {size} bytes: written with another layout of the struct?");
      return data.Length / size;
    }

    internal static int LengthOf<T>(int count) where T : unmanaged
    {
      return checked(count * Unsafe.SizeOf<T>());
    }
  }

  /// <summary>
  /// Writes a struct as the bytes of its memory (binary data, or an extension): no per-field encoding, no compaction, and no compatibility across versions of the struct.
  /// <para>For structs without references (<c>unmanaged</c>), on little-endian machines (every current .NET platform). Reading checks the length only: a struct whose layout changed
  /// with the same size reads other values, and fields that are not valid for any bit pattern (bool, enums) read what the bytes say. Padding bytes are written as they are in memory.</para>
  /// </summary>
  public sealed class BlitConverter<T> : MsgPackBinaryConverter<T> where T : unmanaged
  {
    /// <summary>
    /// Writes binary data (bin): also the constructor <see cref="MsgPackConverterAttribute"/> calls.
    /// </summary>
    public BlitConverter() : this(null)
    {
    }

    /// <param name="extensionTypeCode">null: binary data (bin); 0 to 127: an extension of that type code</param>
    public BlitConverter(sbyte? extensionTypeCode) : base(extensionTypeCode)
    {
      Blit.ThrowIfBigEndian();
    }

    public override int GetLength(T value)
    {
      return Unsafe.SizeOf<T>();
    }

    public override void Write(T value, Span<byte> destination)
    {
      if (destination.Length < Unsafe.SizeOf<T>())
        throw new ArgumentException($"{destination.Length} bytes, {typeof(T).Name} needs {Unsafe.SizeOf<T>()}.", nameof(destination));
      Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
    }

    public override T Read(ReadOnlySpan<byte> data)
    {
      if (Blit.CountOf<T>(data) != 1)
        throw new MsgPackException($"{data.Length} bytes are not one {typeof(T).Name} of {Unsafe.SizeOf<T>()} bytes: written with another layout of the struct?");
      return MemoryMarshal.Read<T>(data);
    }
  }

  /// <summary>
  /// Writes an array of structs as the bytes of its memory: one copy, instead of encoding each field of each element (see <see cref="BlitConverter{T}"/> for the conditions).
  /// Register it for the array type: <c>new BlitArrayConverter&lt;Vector3&gt;()</c> converts <c>Vector3[]</c>.
  /// </summary>
  public sealed class BlitArrayConverter<T> : MsgPackBinaryConverter<T[]> where T : unmanaged
  {
    /// <summary>
    /// Writes binary data (bin): also the constructor <see cref="MsgPackConverterAttribute"/> calls.
    /// </summary>
    public BlitArrayConverter() : this(null)
    {
    }

    /// <param name="extensionTypeCode">null: binary data (bin); 0 to 127: an extension of that type code</param>
    public BlitArrayConverter(sbyte? extensionTypeCode) : base(extensionTypeCode)
    {
      Blit.ThrowIfBigEndian();
    }

    public override int GetLength(T[] value)
    {
      return Blit.LengthOf<T>(value.Length);
    }

    public override void Write(T[] value, Span<byte> destination)
    {
      MemoryMarshal.AsBytes(new ReadOnlySpan<T>(value)).CopyTo(destination);
    }

    public override T[] Read(ReadOnlySpan<byte> data)
    {
      T[] values = new T[Blit.CountOf<T>(data)];
      data.CopyTo(MemoryMarshal.AsBytes(new Span<T>(values)));
      return values;
    }
  }

  /// <summary>
  /// Writes a list of structs as the bytes of its memory (see <see cref="BlitConverter{T}"/> for the conditions). Register it for the list type: <c>new BlitListConverter&lt;Vector3&gt;()</c> converts <c>List&lt;Vector3&gt;</c>.
  /// </summary>
  public sealed class BlitListConverter<T> : MsgPackBinaryConverter<List<T>> where T : unmanaged
  {
    /// <summary>
    /// Writes binary data (bin): also the constructor <see cref="MsgPackConverterAttribute"/> calls.
    /// </summary>
    public BlitListConverter() : this(null)
    {
    }

    /// <param name="extensionTypeCode">null: binary data (bin); 0 to 127: an extension of that type code</param>
    public BlitListConverter(sbyte? extensionTypeCode) : base(extensionTypeCode)
    {
      Blit.ThrowIfBigEndian();
    }

    public override int GetLength(List<T> value)
    {
      return Blit.LengthOf<T>(value.Count);
    }

    public override void Write(List<T> value, Span<byte> destination)
    {
#if NET8_0_OR_GREATER
      MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(value)).CopyTo(destination);
#else
      Span<T> target = MemoryMarshal.Cast<byte, T>(destination);
      for (int t = 0; t < value.Count; t++)
        target[t] = value[t];
#endif
    }

    public override List<T> Read(ReadOnlySpan<byte> data)
    {
      int count = Blit.CountOf<T>(data);
#if NET8_0_OR_GREATER
      List<T> values = new List<T>(count);
      CollectionsMarshal.SetCount(values, count);
      data.CopyTo(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(values)));
      return values;
#else
      T[] items = new T[count];
      data.CopyTo(MemoryMarshal.AsBytes(new Span<T>(items)));
      return new List<T>(items);
#endif
    }
  }

  /// <summary>
  /// Writes the memory of a <see cref="Memory{T}"/> of structs as its bytes (see <see cref="BlitConverter{T}"/> for the conditions); read into a new array.
  /// </summary>
  public sealed class BlitMemoryConverter<T> : MsgPackBinaryConverter<Memory<T>> where T : unmanaged
  {
    /// <summary>
    /// Writes binary data (bin): also the constructor <see cref="MsgPackConverterAttribute"/> calls.
    /// </summary>
    public BlitMemoryConverter() : this(null)
    {
    }

    /// <param name="extensionTypeCode">null: binary data (bin); 0 to 127: an extension of that type code</param>
    public BlitMemoryConverter(sbyte? extensionTypeCode) : base(extensionTypeCode)
    {
      Blit.ThrowIfBigEndian();
    }

    public override int GetLength(Memory<T> value)
    {
      return Blit.LengthOf<T>(value.Length);
    }

    public override void Write(Memory<T> value, Span<byte> destination)
    {
      MemoryMarshal.AsBytes(value.Span).CopyTo(destination);
    }

    public override Memory<T> Read(ReadOnlySpan<byte> data)
    {
      T[] values = new T[Blit.CountOf<T>(data)];
      data.CopyTo(MemoryMarshal.AsBytes(new Span<T>(values)));
      return values;
    }
  }

  /// <summary>
  /// Writes the memory of a <see cref="ReadOnlyMemory{T}"/> of structs as its bytes (see <see cref="BlitConverter{T}"/> for the conditions); read into a new array.
  /// </summary>
  public sealed class BlitReadOnlyMemoryConverter<T> : MsgPackBinaryConverter<ReadOnlyMemory<T>> where T : unmanaged
  {
    /// <summary>
    /// Writes binary data (bin): also the constructor <see cref="MsgPackConverterAttribute"/> calls.
    /// </summary>
    public BlitReadOnlyMemoryConverter() : this(null)
    {
    }

    /// <param name="extensionTypeCode">null: binary data (bin); 0 to 127: an extension of that type code</param>
    public BlitReadOnlyMemoryConverter(sbyte? extensionTypeCode) : base(extensionTypeCode)
    {
      Blit.ThrowIfBigEndian();
    }

    public override int GetLength(ReadOnlyMemory<T> value)
    {
      return Blit.LengthOf<T>(value.Length);
    }

    public override void Write(ReadOnlyMemory<T> value, Span<byte> destination)
    {
      MemoryMarshal.AsBytes(value.Span).CopyTo(destination);
    }

    public override ReadOnlyMemory<T> Read(ReadOnlySpan<byte> data)
    {
      T[] values = new T[Blit.CountOf<T>(data)];
      data.CopyTo(MemoryMarshal.AsBytes(new Span<T>(values)));
      return values;
    }
  }
}
