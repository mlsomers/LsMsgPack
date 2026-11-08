using LsMsgPack;
using LsMsgPack.Generated;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LtMsgPack.Generated;
using LtMsgPack.IO;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LtMsgPack.Writing
{
  /// <summary>
  /// How the properties of a type are written: in the order of LsMsgPack (FullPropertyInfo), each through a typed getter.
  /// <para>Two plans per type: one with the property ids of the names mode, one for the indexed schema (where the id of a property is its position).</para>
  /// </summary>
  internal sealed class ObjectPlan
  {
    internal readonly Type Type;
    internal readonly PropWriter[] Props;

    /// <summary>
    /// The properties as LsMsgPack sees them without a session (names mode), or the statically included ones (schema, their ids are resolved per call).
    /// </summary>
    internal readonly FullPropertyInfo[] Infos;

    /// <summary>
    /// Names mode: the encoded property ids.
    /// </summary>
    internal readonly byte[][] NameKeys;

    /// <summary>
    /// Inline schema: the encoded positions.
    /// </summary>
    internal readonly byte[][] IndexKeys;

    internal readonly SchemaTypeInfo Schema;

    /// <summary>
    /// The writer of the source generator (see LtMsgPackContext), or one compiled at runtime (<see cref="CompiledWriter"/>), and how it fits these options (with the code: <see cref="GeneratedWritePlan.Code"/>);
    /// null when there is none or it is not used: the <see cref="Props"/> write the values. A compiled writer is set once (<see cref="Compile"/>): read it once per object.
    /// </summary>
    internal GeneratedWritePlan GeneratedPlan;

    /// <summary>
    /// The objects still to write before the writer is compiled (<see cref="Serializer.CompileWritersAfter"/>), 0 when it is not (any more). Not exact with several threads, which is all right
    /// (two threads reaching 0 at once both compile, the last writer is kept).
    /// </summary>
    private int _untilCompiled;

    /// <summary>
    /// A struct whose properties are all read by reference (<see cref="RefPropWriter{T}"/>): <see cref="WriteStruct{T}"/> writes it without a box (with its generated code, <see cref="GeneratedStructCode{T}"/>, when there is a <see cref="GeneratedPlan"/>).
    /// </summary>
    internal readonly bool ByRef;

    /// <summary>
    /// <see cref="IdMode.Session"/>: the ids of the frozen session used last (see <see cref="WriteContext.BeginObject"/>).
    /// </summary>
    internal volatile SessionIds LastSession;

    internal sealed class SessionIds
    {
      internal readonly MsgPackOptions Settings;
      internal readonly FullPropertyInfo[] Infos;
      internal readonly byte[][] Keys;

      internal SessionIds(MsgPackOptions settings, FullPropertyInfo[] infos, byte[][] keys)
      {
        Settings = settings;
        Infos = infos;
        Keys = keys;
      }
    }

    internal ObjectPlan(Serializer serializer, Type type, bool names)
    {
      Type = type;
      SerializationRules.ThrowIfUnsupportedFrameworkType(type);
      LtMsgPackOptions options = serializer.Options;
      Infos = names ? FullPropertyInfo.GetSerializedProps(type, options) : FullPropertyInfo.GetStaticallyIncludedProps(type, options);

      Props = new PropWriter[Infos.Length];
      for (int t = 0; t < Infos.Length; t++)
        Props[t] = PropWriter.Create(serializer, Infos[t]);

      GeneratedPlan = serializer.GeneratedWritePlan(serializer.GeneratedCode(type), Infos, Props);
      if (type.IsValueType)
      {
        bool byRef = true;
        for (int t = Props.Length - 1; t >= 0; t--)
          byRef &= Props[t].ByRef; // a struct declares all its properties: each is a RefPropWriter of this type
        ByRef = byRef;
      }

      if (names)
      {
        NameKeys = new byte[Infos.Length][];
        for (int t = 0; t < Infos.Length; t++)
          NameKeys[t] = serializer.EncodeKey(Infos[t].PropertyId);
      }
      else
      {
        IndexKeys = new byte[Infos.Length][];
        for (int t = 0; t < Infos.Length; t++)
          IndexKeys[t] = serializer.EncodeKey(t);
        Schema = serializer.GetSchemaInfo(type);
      }

      if (GeneratedPlan is null && serializer.CompileWritersAfter >= 0)
      {
        if (serializer.CompileWritersAfter == 0)
          Compile(serializer);
        else
          _untilCompiled = serializer.CompileWritersAfter;
      }
    }

    /// <summary>
    /// Counts an object written by the runtime's plan, and compiles the writer after <see cref="Serializer.CompileWritersAfter"/> of them (in the background).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CountWrite(Serializer serializer)
    {
      if (_untilCompiled > 0 && --_untilCompiled == 0)
        CompileInBackground(serializer);
    }

    /// <summary>
    /// Compiles on the thread pool, the runtime's plan writes until the compiled writer is set: compiling costs 1-7 ms per type (90% of it the JIT, which optimizes a dynamic method
    /// at once and inlines the writer's methods for every property), which the call that reaches the count should not wait for. The generated code is optimized in the background too (tiered JIT).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CompileInBackground(Serializer serializer)
    {
      ThreadPool.UnsafeQueueUserWorkItem(plan => ((ObjectPlan)plan).Compile(serializer), this); // no ExecutionContext: nothing of the caller's (AsyncLocal values) is needed or kept
    }

    /// <summary>
    /// Compiles the writer of this plan (<see cref="CompiledWriter"/>) and sets it (one store: the plan holds its code); stays with the runtime's plan when it cannot.
    /// Two threads may both compile, the last one is kept.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Compile(Serializer serializer)
    {
      _untilCompiled = 0;
      try
      {
        GeneratedObjectCode code = CompiledWriter.Create(serializer, Type, Infos);
        GeneratedWritePlan plan = code is null ? null : serializer.GeneratedWritePlan(code, Infos, Props);
        if (plan is null)
          return;
        ((ICompiledCode)code).Compile(plan, serializer.Options._objectLayout != ObjectLayout.Array);
        Volatile.Write(ref GeneratedPlan, plan);
      }
      catch (Exception) // a type or runtime the expressions do not handle: the runtime's plan writes it, as before (CompiledWriterTests check that the writers are used)
      {
      }
    }

    /// <summary>
    /// Writes the object as a map (LsMsgPack: SerializeObject for an object with properties).
    /// </summary>
    internal void Write(WriteContext c, object value, bool typeId, FullPropertyInfo assignedTo)
    {
      byte[][] keys = c.BeginObject(this, out FullPropertyInfo[] infos); // before the type id, as LsMsgPack resolves the ids first
      GeneratedWritePlan generated = GeneratedPlan;
      if (generated is null)
        CountWrite(c.Serializer);
      if (c.Serializer.Options._objectLayout == ObjectLayout.Array)
      {
        WriteArray(c, value, typeId, assignedTo, infos, generated);
        return;
      }

      int max = Props.Length + 1;
      int at = c.W.ReserveMapHeader(max);
      int count = 0;
      if (typeId)
      {
        c.W.String(string.Empty);
        c.WriteTypeId(this, assignedTo);
        count++;
      }
      else
        SerializationRules.ThrowIfUnresolvableWithSchema(Type, assignedTo, c.UsesSchema);

      c.EnterContainer();
      if (generated != null)
        count += generated.Code.WriteBoxed(c, value, keys, infos, generated, out int end); // WriteUnrolledMap or Write: decided there
      else
      {
        PropWriter[] props = Props;
        for (int t = 0; t < props.Length; t++)
          if (props[t].Write(c, value, keys[t], infos[t]))
            count++;
      }
      c.LeaveContainer();

      c.W.PatchMapHeader(at, max, count);
    }

    /// <summary>
    /// <see cref="ObjectLayout.Array"/> (LsMsgPack: SerializeAsArray): the values in the order of the properties, nil when the filters leave one out, wrapped in a map with the type id when needed.
    /// </summary>
    private void WriteArray(WriteContext c, object value, bool typeId, FullPropertyInfo assignedTo, FullPropertyInfo[] infos, GeneratedWritePlan generated)
    {
      if (typeId)
      {
        c.W.MapHeader(2);
        c.W.String(string.Empty);
        c.WriteTypeId(this, assignedTo);
        c.W.String(MsgPackOptions.ContentKey);
      }
      else
        SerializationRules.ThrowIfUnresolvableAsArray(Type, assignedTo);

      c.EnterContainer();
      PropWriter[] props = Props;
      if (c.Serializer.Options._trimTrailingNulls)
        WriteTrimmed(c, value, infos, generated);
      else
      {
        c.W.ArrayHeader(props.Length);
        if (generated != null)
        {
          if (generated.Unrolled)
            generated.Code.WriteUnrolledBoxed(c, value, infos, generated);
          else
            generated.Code.WriteBoxed(c, value, null, infos, generated, out int end);
        }
        else
        {
          for (int t = 0; t < props.Length; t++)
            if (!props[t].Write(c, value, null, infos[t]))
              c.W.Nil();
        }
      }
      c.LeaveContainer();
    }

    /// <summary>
    /// <see cref="MsgPackOptions.TrimTrailingNulls"/>: the nils after the last other value are taken back, the header is written for the values that are left.
    /// </summary>
    private void WriteTrimmed(WriteContext c, object value, FullPropertyInfo[] infos, GeneratedWritePlan generated)
    {
      MsgPackWriter w = c.W;
      PropWriter[] props = Props;
      int at = w.ReserveArrayHeader(props.Length);
      if (generated != null)
      {
        int last = generated.Code.WriteBoxed(c, value, null, infos, generated, out int after);
        w.Pos = after;
        w.PatchArrayHeader(at, props.Length, last);
        return;
      }

      int count = 0;
      int end = w.Pos; // after the last value that is not nil
      for (int t = 0; t < props.Length; t++)
      {
        int start = w.Pos;
        if (!props[t].Write(c, value, null, infos[t]))
          w.Nil();
        if (w.Pos != start + 1 || w.Buf[start] != 0xC0) // nil is the only value of one byte 0xC0
        {
          count = t + 1;
          end = w.Pos;
        }
      }
      w.Pos = end;
      w.PatchArrayHeader(at, props.Length, count);
    }

    /// <summary>
    /// <see cref="Write"/>, <see cref="WriteArray"/> and <see cref="WriteTrimmed"/> for a struct by reference (<see cref="ByRef"/>, see ObjectHandler): no box, no type id (a struct is its own type),
    /// the generated code by reference (<see cref="GeneratedStructCode{T}"/>). A copy so the class path stays as it is: keep the two the same.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void WriteStruct<T>(WriteContext c, ref T value, FullPropertyInfo assignedTo)
    {
      byte[][] keys = c.BeginObject(this, out FullPropertyInfo[] infos); // before anything else, as LsMsgPack resolves the ids first
      PropWriter[] props = Props;
      GeneratedWritePlan generated = GeneratedPlan;
      if (generated is null)
        CountWrite(c.Serializer);
      if (c.Serializer.Options._objectLayout == ObjectLayout.Array)
      {
        SerializationRules.ThrowIfUnresolvableAsArray(Type, assignedTo);
        c.EnterContainer();
        if (c.Serializer.Options._trimTrailingNulls)
          WriteStructTrimmed(c, ref value, infos, generated);
        else
        {
          c.W.ArrayHeader(props.Length);
          if (generated != null)
          {
            if (generated.Unrolled)
              Unsafe.As<GeneratedStructCode<T>>(generated.Code).WriteUnrolled(c, ref value, infos, generated); // the code of the plan's type
            else
              Unsafe.As<GeneratedStructCode<T>>(generated.Code).Write(c, ref value, null, infos, generated, out int end);
          }
          else
          {
            for (int t = 0; t < props.Length; t++)
              if (!Unsafe.As<RefPropWriter<T>>(props[t]).Write(c, ref value, null, infos[t])) // ByRef: each is one
                c.W.Nil();
          }
        }
        c.LeaveContainer();
        return;
      }

      int max = props.Length + 1; // as Write reserves it (the header's size)
      int at = c.W.ReserveMapHeader(max);
      int count = 0;
      SerializationRules.ThrowIfUnresolvableWithSchema(Type, assignedTo, c.UsesSchema);
      c.EnterContainer();
      if (generated != null)
      {
        if (generated.UnrolledMap)
          count += Unsafe.As<GeneratedStructCode<T>>(generated.Code).WriteUnrolledMap(c, ref value, keys, infos, generated);
        else
          count += Unsafe.As<GeneratedStructCode<T>>(generated.Code).Write(c, ref value, keys, infos, generated, out int end);
      }
      else
      {
        for (int t = 0; t < props.Length; t++)
          if (Unsafe.As<RefPropWriter<T>>(props[t]).Write(c, ref value, keys[t], infos[t]))
            count++;
      }
      c.LeaveContainer();
      c.W.PatchMapHeader(at, max, count);
    }

    /// <summary>
    /// <see cref="WriteTrimmed"/> for a struct by reference, see <see cref="WriteStruct{T}"/>.
    /// </summary>
    private void WriteStructTrimmed<T>(WriteContext c, ref T value, FullPropertyInfo[] infos, GeneratedWritePlan generated)
    {
      MsgPackWriter w = c.W;
      PropWriter[] props = Props;
      int at = w.ReserveArrayHeader(props.Length);
      if (generated != null)
      {
        int last = Unsafe.As<GeneratedStructCode<T>>(generated.Code).Write(c, ref value, null, infos, generated, out int after);
        w.Pos = after;
        w.PatchArrayHeader(at, props.Length, last);
        return;
      }

      int count = 0;
      int end = w.Pos; // after the last value that is not nil
      for (int t = 0; t < props.Length; t++)
      {
        int start = w.Pos;
        if (!Unsafe.As<RefPropWriter<T>>(props[t]).Write(c, ref value, null, infos[t]))
          w.Nil();
        if (w.Pos != start + 1 || w.Buf[start] != 0xC0) // nil is the only value of one byte 0xC0
        {
          count = t + 1;
          end = w.Pos;
        }
      }
      w.Pos = end;
      w.PatchArrayHeader(at, props.Length, count);
    }

    /// <summary>
    /// Writes the properties into a map that is being written (a collection with <c>SerializeProperties</c>).
    /// </summary>
    /// <returns>The number of entries written</returns>
    internal int WriteProperties(WriteContext c, object value, byte[][] keys, FullPropertyInfo[] infos)
    {
      GeneratedWritePlan generated = GeneratedPlan;
      if (generated != null)
        return generated.Code.WriteBoxed(c, value, keys, infos, generated, out int end);
      CountWrite(c.Serializer);

      int count = 0;
      PropWriter[] props = Props;
      for (int t = 0; t < props.Length; t++)
        if (props[t].Write(c, value, keys[t], infos[t]))
          count++;
      return count;
    }
  }

  /// <summary>
  /// An object with an anonymous type written as a map of its property names and values, without a type id and not in the schema, whatever the layout (LsMsgPack: SerializeByNames, see SerializationRules.WritesByNames).
  /// <para>Custom property id resolvers are asked once per type (FullPropertyInfo.GetNamedProps), as in names mode.</para>
  /// </summary>
  internal sealed class NamesPlan
  {
    private readonly FullPropertyInfo[] _infos;
    private readonly PropWriter[] _props;
    private readonly byte[][] _keys;

    internal NamesPlan(Serializer serializer, Type type)
    {
      _infos = FullPropertyInfo.GetNamedProps(type, serializer.Options);
      _props = new PropWriter[_infos.Length];
      _keys = new byte[_infos.Length][];
      for (int t = 0; t < _infos.Length; t++)
      {
        _props[t] = PropWriter.Create(serializer, _infos[t]);
        _keys[t] = serializer.EncodeKey(_infos[t].PropertyId);
      }
    }

    internal void Write(WriteContext c, object value)
    {
      bool byNames = c.ByNames;
      c.ByNames = true; // the values below it are written by names as far as they have anonymous types
      PropWriter[] props = _props;
      int at = c.W.ReserveMapHeader(props.Length);
      int count = 0;
      c.EnterContainer();
      for (int t = 0; t < props.Length; t++)
        if (props[t].Write(c, value, _keys[t], _infos[t]))
          count++;
      c.LeaveContainer();
      c.W.PatchMapHeader(at, props.Length, count);
      c.ByNames = byNames;
    }
  }

  /// <summary>
  /// Which dynamic filters apply (LsMsgPack: MsgPackSettings.DynamicFilters).
  /// </summary>
  internal enum FilterMode
  {
    None,

    /// <summary>
    /// Only <see cref="FilterDefaultValues"/>: evaluated per type without boxing.
    /// </summary>
    DefaultValues,
    Custom
  }

  /// <summary>
  /// Writes one property: applies the dynamic filters, then writes the key and the value (LsMsgPack: AddProperties).
  /// </summary>
  internal abstract class PropWriter
  {
    protected readonly Serializer Serializer;

    /// <summary>
    /// The filters are asked with the value boxed: other filters than FilterDefaultValues, or a [DefaultValue] on the property.
    /// </summary>
    internal readonly bool BoxedFilters;
    protected readonly bool NoFilters;

    protected PropWriter(Serializer serializer, FullPropertyInfo info)
    {
      Serializer = serializer;
      NoFilters = serializer.Filters == FilterMode.None;
      BoxedFilters = IsBoxed(serializer, info);
    }

    /// <param name="key">The encoded property id, null for the array layout (only the value)</param>
    /// <returns>false when the filters left the value out (nothing was written)</returns>
    internal abstract bool Write(WriteContext c, object target, byte[] key, FullPropertyInfo info);

    /// <summary>
    /// A <see cref="RefPropWriter{T}"/>: writes the property of a struct by reference.
    /// </summary>
    internal virtual bool ByRef
    {
      get { return false; }
    }

    /// <summary>
    /// The filter mode of a property as the type argument of <see cref="PropWriter{TTarget, TValue, TFilter}"/> and <see cref="StructPropWriter{TTarget, TValue, TFilter}"/>:
    /// <see cref="NoFilter"/>, <see cref="DefaultValuesFilter"/> or <see cref="BoxedFilter"/> (decided once per property, not tested per value).
    /// </summary>
    internal static Type FilterType(Serializer serializer, FullPropertyInfo info)
    {
      if (serializer.Filters == FilterMode.None)
        return typeof(NoFilter);
      return IsBoxed(serializer, info) ? typeof(BoxedFilter) : typeof(DefaultValuesFilter);
    }

    private static bool IsBoxed(Serializer serializer, FullPropertyInfo info)
    {
      return serializer.Filters == FilterMode.Custom || info.CustomAttributes.ContainsKey(nameof(DefaultValueAttribute));
    }

    internal bool IncludeBoxed(object value, FullPropertyInfo info)
    {
      IMsgPackPropertyIncludeDynamically[] filters = Serializer.Options._dynamicFilters;
      for (int t = filters.Length - 1; t >= 0; t--)
        if (!filters[t].IncludeProperty(info, value))
          return false;
      return true;
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only when the runtime supports dynamic code (CanBindDelegates), a failure falls back to reflection.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.Fallback)] // StructPropWriter's struct constraint (for Unsafe.Unbox) asks for a parameterless constructor, none is called
    [UnconditionalSuppressMessage("Trimming", "IL2076", Justification = TrimReasons.Fallback)] // the same, as the .NET 10 tools report it
    internal static PropWriter Create(Serializer serializer, FullPropertyInfo info)
    {
      PropertyInfo property = info.PropertyInfo;
      if (property is null)
        return CreateForField(serializer, info, info.FieldInfo);
      PropWriter generated = serializer.GeneratedProperty(property)?.CreateWriter(serializer, info); // the getter of the source generator
      if (generated != null)
        return generated;

      MethodInfo get = property.GetGetMethod(true);
      if (get != null && !get.IsStatic && !property.DeclaringType.ContainsGenericParameters && serializer.CanBindDelegates)
      {
        try
        {
          Type writerType = (property.DeclaringType.IsValueType ? typeof(StructPropWriter<,,>) : typeof(PropWriter<,,>)).MakeGenericType(property.DeclaringType, property.PropertyType, FilterType(serializer, info));
          return (PropWriter)Activator.CreateInstance(writerType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new object[] { serializer, info, get }, null);
        }
        catch (Exception) // e.g. a runtime that cannot create the generic type, use reflection
        {
        }
      }
      return new BoxedPropWriter(serializer, info);
    }

    /// <summary>
    /// A field: read by the getter of the source generator, or a typed delegate compiled from an expression when the plan is built (not after some uses: PGO then profiles the final code), as a property's getter is bound.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only when the runtime supports dynamic code (CanBindDelegates), a failure falls back to reflection.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.Fallback)] // as Create
    [UnconditionalSuppressMessage("Trimming", "IL2076", Justification = TrimReasons.Fallback)]
    private static PropWriter CreateForField(Serializer serializer, FullPropertyInfo info, FieldInfo field)
    {
      PropWriter generated = serializer.GeneratedProperty(field)?.CreateWriter(serializer, info);
      if (generated != null)
        return generated;
      Type declaring = field.DeclaringType;
      if (!field.IsStatic && !declaring.ContainsGenericParameters && serializer.CanBindDelegates)
      {
        try
        {
          Type writerType = (declaring.IsValueType ? typeof(StructPropWriter<,,>) : typeof(PropWriter<,,>)).MakeGenericType(declaring, field.FieldType, FilterType(serializer, info));
          return (PropWriter)Activator.CreateInstance(writerType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new object[] { serializer, info, field }, null);
        }
        catch (Exception) // e.g. a runtime that cannot create the generic type or compile the expression, use reflection
        {
        }
      }
      return new BoxedPropWriter(serializer, info);
    }
  }

  /// <summary>
  /// The filter modes of the typed property writers (a type argument: the JIT drops the tests of the other modes, see <see cref="PropWriter.FilterType"/>).
  /// </summary>
  internal struct NoFilter
  {
  }

  /// <summary>
  /// Only FilterDefaultValues, typed (<see cref="ValueHandler{T}.IncludeByDefault"/>).
  /// </summary>
  internal struct DefaultValuesFilter
  {
  }

  /// <summary>
  /// The dynamic filters with the value boxed (<see cref="PropWriter.BoxedFilters"/>).
  /// </summary>
  internal struct BoxedFilter
  {
  }

  /// <summary>
  /// A property (or field) read through a typed delegate, the value is not boxed. <typeparamref name="TFilter"/>: the filter mode (<see cref="PropWriter.FilterType"/>).
  /// </summary>
  internal sealed class PropWriter<TTarget, TValue, TFilter> : PropWriter where TTarget : class where TFilter : struct
  {
    private readonly Func<TTarget, TValue> _get;
    private readonly FullPropertyInfo _declared;
    private ValueHandler<TValue> _handler;

    public PropWriter(Serializer serializer, FullPropertyInfo info, MethodInfo get) : base(serializer, info)
    {
      _declared = info;
      _get = (Func<TTarget, TValue>)Delegate.CreateDelegate(typeof(Func<TTarget, TValue>), get);
    }

    /// <param name="field">Read by a delegate compiled from an expression</param>
    public PropWriter(Serializer serializer, FullPropertyInfo info, FieldInfo field) : this(serializer, info, FieldDelegates.Getter<TTarget, TValue>(field))
    {
    }

    /// <param name="get">The getter of the source generator</param>
    internal PropWriter(Serializer serializer, FullPropertyInfo info, Func<TTarget, TValue> get) : base(serializer, info)
    {
      _declared = info;
      _get = get;
    }

    internal override bool Write(WriteContext c, object target, byte[] key, FullPropertyInfo info)
    {
      TValue value;
      try
      {
        value = _get(Unsafe.As<TTarget>(target)); // the plan of the target's type (or a type deriving from TTarget): no cast, which in shared generic code is a lookup per property
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null)
      {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw;
      }

      ValueHandler<TValue> handler = _handler ?? (_handler = Serializer.Handler<TValue>(_declared));
      if (typeof(TFilter) == typeof(BoxedFilter))
      {
        if (!IncludeBoxed(value, info))
          return false;
      }
      else if (typeof(TFilter) == typeof(DefaultValuesFilter))
      {
        if (!handler.IncludeByDefault(value, info))
          return false;
      }

      if (key != null)
        c.W.Raw(key);
      if (value == null)
        c.W.Nil();
      else
        handler.Write(c, value, info);
      return true;
    }
  }

  /// <summary>
  /// Writes a property of a struct of type <typeparamref name="T"/> by reference (see <see cref="ObjectPlan.WriteStruct{T}"/>). Without a struct constraint, so ObjectHandler can call it.
  /// </summary>
  internal abstract class RefPropWriter<T> : PropWriter
  {
    protected RefPropWriter(Serializer serializer, FullPropertyInfo info) : base(serializer, info)
    {
    }

    internal sealed override bool ByRef
    {
      get { return true; }
    }

    /// <inheritdoc cref="PropWriter.Write"/>
    internal abstract bool Write(WriteContext c, ref T target, byte[] key, FullPropertyInfo info);
  }

  /// <summary>
  /// A property of a struct read through a typed delegate on the struct by reference (in the caller's value, see ObjectPlan.WriteStruct, or in a box: a struct written as an object), the value is not boxed.
  /// The same as <see cref="PropWriter{TTarget, TValue, TFilter}.Write"/> after the getter: keep the two the same (the class writer is not changed for this, it is the usual path).
  /// </summary>
  internal sealed class StructPropWriter<TTarget, TValue, TFilter> : RefPropWriter<TTarget> where TTarget : struct where TFilter : struct
  {
    private readonly StructGetter<TTarget, TValue> _get;
    private readonly FullPropertyInfo _declared;
    private ValueHandler<TValue> _handler;

    /// <param name="get">Bound as an open instance delegate: its first parameter is the struct by reference</param>
    public StructPropWriter(Serializer serializer, FullPropertyInfo info, MethodInfo get) : base(serializer, info)
    {
      _declared = info;
      _get = (StructGetter<TTarget, TValue>)Delegate.CreateDelegate(typeof(StructGetter<TTarget, TValue>), get);
    }

    /// <param name="field">Read by reference by a delegate compiled from an expression</param>
    public StructPropWriter(Serializer serializer, FullPropertyInfo info, FieldInfo field) : this(serializer, info, FieldDelegates.StructGetter<TTarget, TValue>(field))
    {
    }

    /// <param name="get">The getter of the source generator</param>
    internal StructPropWriter(Serializer serializer, FullPropertyInfo info, StructGetter<TTarget, TValue> get) : base(serializer, info)
    {
      _declared = info;
      _get = get;
    }

    internal override bool Write(WriteContext c, object target, byte[] key, FullPropertyInfo info)
    {
      return Write(c, ref Unsafe.Unbox<TTarget>(target), key, info); // in the box, without copying the struct
    }

    internal override bool Write(WriteContext c, ref TTarget target, byte[] key, FullPropertyInfo info)
    {
      TValue value = _get(ref target);

      ValueHandler<TValue> handler = _handler ?? (_handler = Serializer.Handler<TValue>(_declared));
      if (typeof(TFilter) == typeof(BoxedFilter))
      {
        if (!IncludeBoxed(value, info))
          return false;
      }
      else if (typeof(TFilter) == typeof(DefaultValuesFilter))
      {
        if (!handler.IncludeByDefault(value, info))
          return false;
      }

      if (key != null)
        c.W.Raw(key);
      if (value == null)
        c.W.Nil();
      else
        handler.Write(c, value, info);
      return true;
    }
  }

  /// <summary>
  /// A property read by reflection (static properties, or when delegates cannot be bound), the value is written like any boxed value.
  /// </summary>
  internal sealed class BoxedPropWriter : PropWriter
  {
    private readonly FullPropertyInfo _declared;

    public BoxedPropWriter(Serializer serializer, FullPropertyInfo info) : base(serializer, info)
    {
      _declared = info;
    }

    internal override bool Write(WriteContext c, object target, byte[] key, FullPropertyInfo info)
    {
      object value = _declared.GetValue(target);
      if (!NoFilters && !IncludeBoxed(value, info))
        return false;

      if (key != null)
        c.W.Raw(key);
      Serializer.WriteBoxed(c, value, info);
      return true;
    }
  }
}
