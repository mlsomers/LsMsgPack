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
    /// Makes the types of the assembly known by their names, for type ids the reader cannot find by itself (see the README: e.g. the implementations of an interface in another assembly, assigned to an object property).
    /// <para>The type caches are shared with LsMsgPack (MsgPackSerializer.CacheAssemblyTypes does the same).</para>
    /// </summary>
    public static void CacheAssemblyTypes(Assembly assembly)
    {
      if (assembly is null)
        throw new ArgumentNullException(nameof(assembly));
      TypeResolver.CacheAssembly(assembly, null);
    }

    /// <summary>
    /// Makes the types of the assembly of <paramref name="type"/> known by their names (see <see cref="CacheAssemblyTypes(Assembly)"/>).
    /// </summary>
    public static void CacheAssemblyTypes(Type type)
    {
      if (type is null)
        throw new ArgumentNullException(nameof(type));
      TypeResolver.CacheAssembly(type.Assembly, type.Name);
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
      if (source is null)
        throw new ArgumentNullException(nameof(source));

      if (source is MemoryStream memory && memory.TryGetBuffer(out ArraySegment<byte> segment)) // read in place
      {
        int offset = segment.Offset + (int)memory.Position;
        object result = _serializer.Deserialize(type, segment.Array, offset, segment.Offset + (int)memory.Length, out int consumed);
        memory.Position += consumed - offset;
        return result;
      }

      byte[] payload = StreamPayload.Read(source, _serializer.Options);
      return _serializer.Deserialize(type, payload, 0, payload.Length, out int end);
    }

    #endregion
  }
}
