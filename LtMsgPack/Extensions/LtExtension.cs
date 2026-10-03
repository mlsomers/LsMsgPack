using System;

namespace LtMsgPack.Extensions
{
  /// <summary>
  /// A custom extension: writes the values of a type as a MsgPack extension with a type code (see <see cref="LtMsgPackOptions.Extensions"/>). Derive from <see cref="LtExtension{T}"/>.
  /// </summary>
  public abstract class LtExtension
  {
    /// <summary>
    /// The extension type code (0 to 127 for applications, negative codes are reserved by the MsgPack specification; -1 is the timestamp).
    /// </summary>
    public abstract sbyte TypeCode { get; }

    /// <summary>
    /// Whether values of this type are written by this extension.
    /// </summary>
    public abstract bool SupportsType(Type type);

    internal abstract int GetMaxLength(object value);

    internal abstract int WriteBoxed(object value, Span<byte> destination);

    internal abstract object ReadBoxed(ReadOnlySpan<byte> data);
  }

  /// <summary>
  /// A custom extension for values of <typeparamref name="T"/>.
  /// </summary>
  public abstract class LtExtension<T> : LtExtension
  {
    public override bool SupportsType(Type type)
    {
      return type == typeof(T);
    }

    /// <summary>
    /// The largest number of bytes <see cref="Write"/> may write for the value.
    /// </summary>
    public abstract int GetMaxLength(T value);

    /// <summary>
    /// Writes the data of the extension (without the MsgPack header).
    /// </summary>
    /// <returns>The number of bytes written</returns>
    public abstract int Write(T value, Span<byte> destination);

    /// <summary>
    /// Reads the data of the extension (without the MsgPack header).
    /// </summary>
    public abstract T Read(ReadOnlySpan<byte> data);

    internal override int GetMaxLength(object value)
    {
      return GetMaxLength((T)value);
    }

    internal override int WriteBoxed(object value, Span<byte> destination)
    {
      return Write((T)value, destination);
    }

    internal override object ReadBoxed(ReadOnlySpan<byte> data)
    {
      return Read(data);
    }
  }
}
