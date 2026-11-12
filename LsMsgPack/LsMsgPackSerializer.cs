using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.IO;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Reflection;

namespace LsMsgPack
{
  /// <summary>
  /// Serializes .NET objects to MsgPack and back, without an intermediate item tree: the same data as LsMsgPack's MsgPackSerializer with the same settings (see <see cref="MsgPackOptions"/>).
  /// <para>Thread-safe. Create one per set of options and keep it: the plans per type are built once.</para>
  /// <para>Trimmed and NativeAOT apps: create it with the code of the source generator (<see cref="LsMsgPackSerializer(LsMsgPackContext, LsMsgPackOptions)"/>),
  /// the constructors without a context require unreferenced and dynamic code (the serializer then finds the members of the types by reflection and instantiates its generic code at runtime).</para>
  /// </summary>
  public sealed class LsMsgPackSerializer
  {
    internal const string RequiresUnreferencedCodeMessage = "Without generated code the serializer reads the properties and constructors of the types by reflection, trimming may remove them. Create it with the context of LsMsgPack's source generator (LsMsgPackSerializer(LsMsgPackContext, LsMsgPackOptions)).";
    internal const string RequiresDynamicCodeMessage = "Without generated code the serializer instantiates its generic code for the types at runtime, NativeAOT may not have it. Create it with the context of LsMsgPack's source generator (LsMsgPackSerializer(LsMsgPackContext, LsMsgPackOptions)).";

    private readonly Serializer _serializer;

    [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(RequiresDynamicCodeMessage)]
    public LsMsgPackSerializer() : this(new LsMsgPackOptions(), null) { }

    /// <param name="options">Copied, later changes do not apply</param>
    [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(RequiresDynamicCodeMessage)]
    public LsMsgPackSerializer(LsMsgPackOptions options) : this(options, null) { }

    /// <summary>
    /// A serializer with the default options and the code of LsMsgPack's source generator (see <see cref="LsMsgPackSerializer(LsMsgPackContext, LsMsgPackOptions)"/>).
    /// </summary>
    /// <param name="context">E.g. <c>AppMsgPackContext.Default</c></param>
    public LsMsgPackSerializer(LsMsgPackContext context) : this(new LsMsgPackOptions(), NotNull(context)) { }

    /// <summary>
    /// A serializer with the code of LsMsgPack's source generator: <paramref name="context"/> is asked first, then <see cref="LsMsgPackOptions.Contexts"/> (and the registered contexts, see <see cref="LsMsgPackOptions.UseRegisteredContexts"/>).
    /// <para>For trimmed and NativeAOT apps: the generated code keeps the classes of the context and creates the serializer's generic code for them.
    /// Classes it does not describe are still serialized by reflection (the same data), the generator names the ones to list (LSMP110, see docs/SourceGenerator.md).</para>
    /// </summary>
    /// <param name="context">E.g. <c>AppMsgPackContext.Default</c></param>
    /// <param name="options">Copied, later changes do not apply</param>
    public LsMsgPackSerializer(LsMsgPackContext context, LsMsgPackOptions options) : this(options, NotNull(context)) { }

    /// <param name="options">Copied</param>
    /// <param name="context">null for none: the public constructors without a context require unreferenced and dynamic code, callers of this one (the HTTP serializer) do the same</param>
    internal LsMsgPackSerializer(LsMsgPackOptions options, LsMsgPackContext context)
    {
      if (options is null)
        throw new ArgumentNullException(nameof(options));
      LsMsgPackOptions copy = options.Clone();
      if (context != null)
      {
        LsMsgPackContext[] contexts = new LsMsgPackContext[copy._contexts.Length + 1];
        contexts[0] = context;
        Array.Copy(copy._contexts, 0, contexts, 1, copy._contexts.Length);
        copy._contexts = contexts;
      }
      _serializer = new Serializer(copy);
    }

    /// <summary>
    /// The implementation, for the stream writer and reader.
    /// </summary>
    internal Serializer Implementation { get { return _serializer; } }

    private static LsMsgPackContext NotNull(LsMsgPackContext context)
    {
      if (context is null)
        throw new ArgumentNullException(nameof(context));
      return context;
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

    private static LsMsgPackSerializer _default;

    /// <summary>
    /// A serializer with the default options (without a context: it uses the registered ones, see <see cref="LsMsgPackOptions.UseRegisteredContexts"/>).
    /// </summary>
    public static LsMsgPackSerializer Default
    {
      [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
      [RequiresDynamicCode(RequiresDynamicCodeMessage)]
      get { return _default ?? (_default = new LsMsgPackSerializer()); }
    }

    /// <summary>
    /// A copy of the options of this serializer.
    /// </summary>
    public LsMsgPackOptions Options
    {
      get { return _serializer.Options.Clone(); }
    }

    /// <summary>
    /// Makes the schema that values of the root type will refer to before the first one is written, and returns its id (e.g. to compare it with the schemas a client holds).
    /// With <see cref="MsgPackOptions.CompleteSchemas"/> it has every class the values can hold, so its id is known ahead; otherwise it starts without classes (the values add them).
    /// Does nothing when values of the root type were written already (their schema is kept).
    /// </summary>
    /// <param name="root">The declared type of the values (as the type argument of <see cref="Serialize{T}(T)"/>)</param>
    /// <exception cref="InvalidOperationException">Without <see cref="MsgPackOptions.WriteSchemaReference"/>, a <see cref="MsgPackOptions.SchemaStore"/> and the indexed schema: there is no schema to refer to</exception>
    public SchemaId PrepareSchema(Type root)
    {
      if (root is null)
        throw new ArgumentNullException(nameof(root));
      LsMsgPackOptions options = _serializer.Options;
      if (!options._useInexedSchema || !options._writeSchemaReference || options._schemaStore is null)
        throw new InvalidOperationException($"Only schemas that are referred to are kept: set {nameof(MsgPackOptions.UseInexedSchema)}, {nameof(MsgPackOptions.WriteSchemaReference)} and a {nameof(MsgPackOptions.SchemaStore)}.");
      return options._schemaStore.PrepareWriter(root, options);
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

    internal static readonly Action<Writing.WriteContext, LsMsgPack.Meta.FullPropertyInfo> BoxedWriter = (c, root) => c.Serializer.WriteRoot(c, root);

    internal static class TypedWriter<T>
    {
      internal static readonly Action<Writing.WriteContext, LsMsgPack.Meta.FullPropertyInfo> Write = (c, root) =>
      {
        object value = c.Root;
        if (value == null)
          c.W.Nil();
        else
        {
          Writing.ValueHandler<T> handler;
          if (ReferenceEquals(c.RootHandlerOf, root)) // the previous call on this thread had this root type (usually the case): no casts, in shared generic code each is a lookup and a walk over the base classes
            handler = Unsafe.As<Writing.ValueHandler<T>>(c.RootHandler);
          else
          {
            c.RootHandler = handler = c.Serializer.Handler<T>(null);
            c.RootHandlerOf = root;
          }
          handler.Write(c, typeof(T).IsValueType ? (T)value : Unsafe.As<object, T>(ref value), root); // value is the T of the public Serialize<T>
        }
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
    /// type ids that are not found and values that could not be read (errors, see <see cref="MsgPackOptions.ReadErrors"/>), counted per class and name.
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
      object result;
      try // ReadDifferences.Collect without the delegate (a closure per call)
      {
        result = found.Collected(_serializer.Deserialize(type, data, offset, offset + count, out int consumed, found));
      }
      catch (Exception ex) when (found.AttachTo(ex))
      {
        throw; // not reached, the filter attaches the differences and passes it on as it is
      }
      differences = found.IsEmpty ? null : found;
      return result;
    }

    /// <inheritdoc cref="Deserialize{T}(byte[], out ReadDifferences)"/>
    public object Deserialize(Type type, Stream source, out ReadDifferences differences)
    {
      ReadDifferences found = new ReadDifferences(_serializer.Options);
      object result;
      try // ReadDifferences.Collect without the delegate (a closure per call)
      {
        result = found.Collected(Deserialize(type, source, found));
      }
      catch (Exception ex) when (found.AttachTo(ex))
      {
        throw; // not reached, the filter attaches the differences and passes it on as it is
      }
      differences = found.IsEmpty ? null : found;
      return result;
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
