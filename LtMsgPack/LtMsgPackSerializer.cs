using LsMsgPack;
using LsMsgPack.Meta;
using LtMsgPack.IO;
using System;
using System.IO;
using System.Reflection;

namespace LtMsgPack
{
  /// <summary>
  /// Serializes .NET objects to MsgPack and back, without an intermediate item tree: the same data as LsMsgPack's MsgPackSerializer with the same settings (see <see cref="MsgPackOptions"/>).
  /// <para>Thread-safe. Create one per set of options and keep it: the plans per type are built once.</para>
  /// </summary>
  public sealed class LtMsgPackSerializer
  {
    private readonly Serializer _serializer;

    public LtMsgPackSerializer() : this(new LtMsgPackOptions()) { }

    /// <param name="options">Copied, later changes do not apply</param>
    public LtMsgPackSerializer(LtMsgPackOptions options)
    {
      if (options is null)
        throw new ArgumentNullException(nameof(options));
      _serializer = new Serializer(options.Clone());
    }

    /// <summary>
    /// Makes the types of the assembly known by their names, for type ids the reader cannot find by itself (see docs/schema.md: e.g. the implementations of an interface in another assembly, assigned to an object property).
    /// <para>The same as <see cref="MsgPackTypes.CacheAssemblyTypes(Assembly)"/> of LsMsgPack.Core (the type caches are shared with LsMsgPack).</para>
    /// </summary>
    public static void CacheAssemblyTypes(Assembly assembly)
    {
      MsgPackTypes.CacheAssemblyTypes(assembly);
    }

    /// <summary>
    /// Makes the types of the assembly of <paramref name="type"/> known by their names (see <see cref="CacheAssemblyTypes(Assembly)"/>).
    /// </summary>
    public static void CacheAssemblyTypes(Type type)
    {
      MsgPackTypes.CacheAssemblyTypes(type);
    }

    private static LtMsgPackSerializer _default;

    /// <summary>
    /// A serializer with the default options.
    /// </summary>
    public static LtMsgPackSerializer Default
    {
      get { return _default ?? (_default = new LtMsgPackSerializer()); }
    }

    /// <summary>
    /// A copy of the options of this serializer.
    /// </summary>
    public LtMsgPackOptions Options
    {
      get { return _serializer.Options.Clone(); }
    }

    #region Serialize

    public byte[] Serialize<T>(T value)
    {
      _serializer.Serialize(typeof(T), value, TypedWriter<T>.Write, null, out byte[] result);
      return result;
    }

    public void Serialize<T>(T value, Stream target)
    {
      if (target is null)
        throw new ArgumentNullException(nameof(target));
      _serializer.Serialize(typeof(T), value, TypedWriter<T>.Write, target, out byte[] result);
    }

    /// <summary>
    /// Provided for generic flexibility, use the strongly typed <see cref="Serialize{T}(T)"/> where possible.
    /// </summary>
    /// <param name="assignedTo">The declared type the value is assigned to, a type id is only added when the value's type differs (depending on <see cref="MsgPackOptions.AddTypeIdOptions"/>). Null: the type of the value.</param>
    public byte[] Serialize(object value, Type assignedTo)
    {
      _serializer.Serialize(assignedTo ?? value?.GetType() ?? typeof(object), value, BoxedWriter, null, out byte[] result);
      return result;
    }

    public void Serialize(object value, Type assignedTo, Stream target)
    {
      if (target is null)
        throw new ArgumentNullException(nameof(target));
      _serializer.Serialize(assignedTo ?? value?.GetType() ?? typeof(object), value, BoxedWriter, target, out byte[] result);
    }

    // The writers of the root value (in WriteContext.Root), without a closure per call

    private static readonly Action<Writing.WriteContext, LsMsgPack.Meta.FullPropertyInfo> BoxedWriter = (c, root) => c.Serializer.WriteBoxed(c, c.Root, root);

    private static class TypedWriter<T>
    {
      internal static readonly Action<Writing.WriteContext, LsMsgPack.Meta.FullPropertyInfo> Write = (c, root) =>
      {
        object value = c.Root;
        if (value == null)
          c.W.Nil();
        else
          c.Serializer.Handler<T>(null).Write(c, (T)value, root);
      };
    }

    #endregion

    #region Deserialize

    public T Deserialize<T>(byte[] data)
    {
      object result = Deserialize(typeof(T), data);
      return result is null ? default(T) : (T)result;
    }

    /// <param name="offset">Where the data starts</param>
    /// <param name="count">The number of bytes of the data</param>
    public T Deserialize<T>(byte[] data, int offset, int count)
    {
      if (data is null)
        throw new ArgumentNullException(nameof(data));
      if (offset < 0 || count < 0 || data.Length - offset < count)
        throw new ArgumentOutOfRangeException(nameof(count));
      object result = _serializer.Deserialize(typeof(T), data, offset, offset + count, out int consumed);
      return result is null ? default(T) : (T)result;
    }

    /// <summary>
    /// Reads one value (with its schema) from the stream, the stream is left after it.
    /// </summary>
    public T Deserialize<T>(Stream source)
    {
      object result = Deserialize(typeof(T), source);
      return result is null ? default(T) : (T)result;
    }

    /// <summary>
    /// Provided for generic flexibility, use the strongly typed <see cref="Deserialize{T}(byte[])"/> where possible.
    /// </summary>
    public object Deserialize(Type type, byte[] data)
    {
      if (data is null)
        throw new ArgumentNullException(nameof(data));
      return _serializer.Deserialize(type, data, 0, data.Length, out int consumed);
    }

    /// <param name="offset">Where the data starts</param>
    /// <param name="count">The number of bytes of the data</param>
    public object Deserialize(Type type, byte[] data, int offset, int count)
    {
      if (data is null)
        throw new ArgumentNullException(nameof(data));
      if (offset < 0 || count < 0 || data.Length - offset < count)
        throw new ArgumentOutOfRangeException(nameof(count));
      return _serializer.Deserialize(type, data, offset, offset + count, out int consumed);
    }

    public object Deserialize(Type type, Stream source)
    {
      return Deserialize(type, source, null);
    }

    /// <summary>
    /// Reads the data like <see cref="Deserialize{T}(byte[])"/>, and reports what did not match between the data and the classes: unknown properties, extra values,
    /// objects of classes without a schema entry (skipped instead of throwing), counted per class and name.
    /// </summary>
    /// <param name="differences">Null when the data matched the classes. See <see cref="ReadDifferences.GenerateReport"/>.</param>
    public T Deserialize<T>(byte[] data, out ReadDifferences differences)
    {
      object result = Deserialize(typeof(T), data, out differences);
      return result is null ? default(T) : (T)result;
    }

    /// <inheritdoc cref="Deserialize{T}(byte[], out ReadDifferences)"/>
    public T Deserialize<T>(Stream source, out ReadDifferences differences)
    {
      object result = Deserialize(typeof(T), source, out differences);
      return result is null ? default(T) : (T)result;
    }

    /// <inheritdoc cref="Deserialize{T}(byte[], out ReadDifferences)"/>
    public object Deserialize(Type type, byte[] data, out ReadDifferences differences)
    {
      if (data is null)
        throw new ArgumentNullException(nameof(data));
      return Deserialize(type, data, 0, data.Length, out differences);
    }

    /// <inheritdoc cref="Deserialize{T}(byte[], out ReadDifferences)"/>
    /// <param name="offset">Where the data starts</param>
    /// <param name="count">The number of bytes of the data</param>
    public object Deserialize(Type type, byte[] data, int offset, int count, out ReadDifferences differences)
    {
      if (data is null)
        throw new ArgumentNullException(nameof(data));
      if (offset < 0 || count < 0 || data.Length - offset < count)
        throw new ArgumentOutOfRangeException(nameof(count));
      ReadDifferences found = new ReadDifferences(_serializer.Options);
      object result = Collecting(found, () => _serializer.Deserialize(type, data, offset, offset + count, out int consumed, found));
      differences = found.IsEmpty ? null : found;
      return result;
    }

    /// <inheritdoc cref="Deserialize{T}(byte[], out ReadDifferences)"/>
    public object Deserialize(Type type, Stream source, out ReadDifferences differences)
    {
      ReadDifferences found = new ReadDifferences(_serializer.Options);
      object result = Collecting(found, () => Deserialize(type, source, found));
      differences = found.IsEmpty ? null : found;
      return result;
    }

    private static object Collecting(ReadDifferences found, Func<object> read)
    {
      try
      {
        object result = read();
        found.Root = result;
        return result;
      }
      catch (Exception ex) when (found.AttachTo(ex))
      {
        throw; // not reached, the filter attaches the differences and passes it on as it is
      }
    }

    private object Deserialize(Type type, Stream source, ReadDifferences differences)
    {
      if (source is null)
        throw new ArgumentNullException(nameof(source));

      if (source is MemoryStream memory && memory.TryGetBuffer(out ArraySegment<byte> segment)) // read in place
      {
        int offset = segment.Offset + (int)memory.Position;
        object result = _serializer.Deserialize(type, segment.Array, offset, segment.Offset + (int)memory.Length, out int consumed, differences);
        memory.Position += consumed - offset;
        return result;
      }

      byte[] payload = StreamPayload.Read(source, _serializer.Options);
      return _serializer.Deserialize(type, payload, 0, payload.Length, out int end, differences);
    }

    #endregion
  }
}
