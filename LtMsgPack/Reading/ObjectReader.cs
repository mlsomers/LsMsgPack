using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
#if NETSTANDARD2_1_OR_GREATER
using System.Reflection.Emit;
#endif
using System.Text;
using System.Threading;

namespace LtMsgPack.Reading
{
  /// <summary>
  /// Reads a map into an object with properties: the type id (when it is the first key) picks the type, the keys are looked up by name or by schema index.
  /// Anything unexpected (a wrapped value, a type id elsewhere, keys of another kind, custom resolvers) is read as LsMsgPack does.
  /// </summary>
  internal sealed class ObjectReader<T> : ValueReader<T> where T : class
  {
    private readonly Serializer _serializer;
    private ReadPlan _plan;

    internal ObjectReader(Serializer serializer)
    {
      _serializer = serializer;
    }

    internal override T Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadNil())
        return null;

      if (c.Unusual) // objects read as LsMsgPack does, or the differences are collected
        return _serializer.SlowObjects ? Slow(c, start, assignedTo) : ReadReporting(c, start, assignedTo);

      int count = c.R.TryReadMapHeader();
      if (count < 0)
      {
        int items = c.R.TryReadArrayHeader(); // ObjectLayout.Array
        if (items < 0 || Plan is null)
          return Slow(c, start, assignedTo);
        return ReadPositional(c, Plan, items, start, assignedTo);
      }

      ReadPlan plan = null;
      if (count > 0 && c.R.Peek() == 0xA0) // the type id (key "") is written first
      {
        c.R.Pos++;
        Type type = ReadTypeId(c, assignedTo);
        if (type is null)
          return Slow(c, start, assignedTo);
        plan = type == typeof(T) ? Plan : _serializer.GetReadPlan(type);
        if (plan is null || !typeof(T).IsAssignableFrom(type)) // LsMsgPack's way refuses a type that is not assignable
          return Slow(c, start, assignedTo);
        if (type != typeof(T) && _serializer.Options._typeGuard != null) // the type guard, before the instance is created (see docs/security.md)
          TypeResolver.ThrowIfNotAllowed(type, typeof(T), assignedTo, _serializer.Options, type);
        count--;

        if (count == 1 && c.R.Pos + 1 < c.R.End &&c.R.Buf[c.R.Pos] == 0xA1 && c.R.Buf[c.R.Pos + 1] == (byte)'@') // ObjectLayout.Array with a type id: { "": typeId, "@": [values] }
        {
          c.R.Pos += 2;
          int items = c.R.TryReadArrayHeader();
          if (items < 0)
            return Slow(c, start, assignedTo);
          return ReadPositional(c, plan, items, start, assignedTo);
        }
      }
      else
      {
        plan = Plan;
        if (plan is null) // abstract or not an object with properties
          return Slow(c, start, assignedTo);
      }

      PropReader[] bound = null;
      if (c.Schema)
      {
        bound = c.Bound.Binding(plan);
        if (bound is null)
          return Slow(c, start, assignedTo);
      }

      int depth = c.Depth;
      c.Enter();
      object result = plan.Create();
      int next = 0; // names are usually written in the order of the properties
      for (int t = 0; t < count; t++)
      {
        PropReader prop;
        if (c.Schema)
        {
          if (!c.R.TryReadInt64(out long index))
            return Restart(c, start, depth, assignedTo);
          prop = index >= 0 && index < bound.Length ? bound[index] : null;
        }
        else
        {
          int length = c.R.TryReadStringHeader();
          if (length <= 0 || (length == 1 && c.R.Buf[c.R.Pos] == (byte)'@')) // another kind of key, or a reserved one ("" or "@")
            return Restart(c, start, depth, assignedTo);
          prop = plan.FindByName(c.R.Buf, c.R.Pos, length, ref next);
          c.R.Pos += length;
        }

        if (prop is null)
          c.R.Skip(c.Depth); // not a property of this type (removed, or ignored here)
        else
          prop.Read(c, result);
      }
      c.Depth = depth;
      return (T)result;
    }

    /// <summary>
    /// An object written as an array (<see cref="ObjectLayout.Array"/>), the header is read: the values by position, or with the schema by the names of the schema.
    /// A nil value leaves the property as the constructor made it (LsMsgPack: ValueConverter.ConvertPositional).
    /// </summary>
    private T ReadPositional(ReadContext c, ReadPlan plan, int items, int start, FullPropertyInfo assignedTo)
    {
      PropReader[] props = plan.Props;
      if (c.Schema && items > 0)
      {
        props = c.Bound.Binding(plan);
        if (props is null) // not in the schema (LsMsgPack takes the properties in its own order)
          return Slow(c, start, assignedTo);
      }

      int depth = c.Depth;
      c.Enter();
      object result = plan.Create();
      for (int t = 0; t < items; t++)
      {
        if (t >= props.Length || props[t] is null)
          c.R.Skip(c.Depth); // not a property of this type (added by a newer writer, or ignored here)
        else if (!c.R.TryReadNil())
          props[t].Read(c, result);
      }
      c.Depth = depth;
      return (T)result;
    }

    private T Restart(ReadContext c, int start, int depth, FullPropertyInfo assignedTo)
    {
      c.Depth = depth;
      return Slow(c, start, assignedTo);
    }

    #region Collecting the differences

    /// <summary>
    /// <see cref="Read"/> collecting the differences (<see cref="ReadContext.Differences"/>): a copy, so the usual way has no checks for them. Keep the two the same.
    /// </summary>
    private T ReadReporting(ReadContext c, int start, FullPropertyInfo assignedTo)
    {
      int count = c.R.TryReadMapHeader();
      if (count < 0)
      {
        int items = c.R.TryReadArrayHeader(); // ObjectLayout.Array
        if (items < 0 || Plan is null)
          return Slow(c, start, assignedTo);
        return ReadPositionalReporting(c, Plan, items, start, assignedTo);
      }

      ReadPlan plan = null;
      if (count > 0 && c.R.Peek() == 0xA0) // the type id (key "") is written first
      {
        c.R.Pos++;
        Type type = ReadTypeId(c, assignedTo, true);
        if (type is null)
          return Slow(c, start, assignedTo); // also a name that is not found: LsMsgPack.Core reports it
        plan = type == typeof(T) ? Plan : _serializer.GetReadPlan(type);
        if (plan is null || !typeof(T).IsAssignableFrom(type)) // LsMsgPack's way refuses a type that is not assignable
          return Slow(c, start, assignedTo);
        if (type != typeof(T) && _serializer.Options._typeGuard != null) // the type guard, before the instance is created (see docs/security.md)
          TypeResolver.ThrowIfNotAllowed(type, typeof(T), assignedTo, _serializer.Options, type);
        count--;

        if (count == 1 && c.R.Pos + 1 < c.R.End &&c.R.Buf[c.R.Pos] == 0xA1 && c.R.Buf[c.R.Pos + 1] == (byte)'@') // ObjectLayout.Array with a type id: { "": typeId, "@": [values] }
        {
          c.R.Pos += 2;
          int items = c.R.TryReadArrayHeader();
          if (items < 0)
            return Slow(c, start, assignedTo);
          return ReadPositionalReporting(c, plan, items, start, assignedTo);
        }
      }
      else
      {
        plan = Plan;
        if (plan is null) // abstract or not an object with properties
          return Slow(c, start, assignedTo);
      }

      PropReader[] bound = null;
      if (c.Schema)
      {
        bound = c.Bound.Binding(plan);
        if (bound is null)
          return Slow(c, start, assignedTo);
      }

      int depth = c.Depth;
      c.Enter();
      int inner = c.Depth;
      object result = plan.Create();
      c.Differences.Push(result);
      int parents = c.Differences.Depth;
      int next = 0; // names are usually written in the order of the properties
      int t = 0;
      PropReader prop = null;
      int valueStart = 0;
      while (true) // one exception handler per object (one per property measured 2-3% slower): a value that is skipped continues the loop
      {
        try
        {
          for (; t < count; t++)
          {
            prop = null;
            if (c.Schema)
            {
              if (!c.R.TryReadInt64(out long index))
                return RestartReporting(c, start, depth, assignedTo);
              prop = index >= 0 && index < bound.Length ? bound[index] : null;
              if (prop is null)
                c.Differences.UnknownProperty(result, c.Bound.NameOf(plan, index));
            }
            else
            {
              int length = c.R.TryReadStringHeader();
              if (length <= 0 || (length == 1 && c.R.Buf[c.R.Pos] == (byte)'@')) // another kind of key, or a reserved one ("" or "@")
                return RestartReporting(c, start, depth, assignedTo);
              prop = plan.FindByName(c.R.Buf, c.R.Pos, length, ref next);
              if (prop is null)
                c.Differences.UnknownProperty(result, c.R.Buf, c.R.Pos, length);
              c.R.Pos += length;
            }

            if (prop is null)
              c.R.Skip(c.Depth); // not a property of this type (removed, or ignored here)
            else
            {
              valueStart = c.R.Pos;
              prop.Read(c, result);
            }
          }
          break;
        }
        catch (Exception ex) when (prop != null && c.Differences.SkipInvalidValue(result, prop.Info, ex))
        {
          SkipValue(c, valueStart, inner, parents);
          t++;
        }
      }
      c.Differences.Pop();
      c.Depth = depth;
      return (T)result;
    }

    /// <summary>
    /// <see cref="ReadPositional"/> collecting the differences.
    /// </summary>
    private T ReadPositionalReporting(ReadContext c, ReadPlan plan, int items, int start, FullPropertyInfo assignedTo)
    {
      PropReader[] props = plan.Props;
      if (c.Schema && items > 0)
      {
        props = c.Bound.Binding(plan);
        if (props is null) // not in the schema (LsMsgPack takes the properties in its own order)
          return Slow(c, start, assignedTo);
      }

      int depth = c.Depth;
      c.Enter();
      int inner = c.Depth;
      object result = plan.Create();
      c.Differences.Push(result);
      int parents = c.Differences.Depth;
      int t = 0;
      int valueStart = 0;
      while (true) // one exception handler per object, see ReadReporting
      {
        try
        {
          for (; t < items; t++)
          {
            valueStart = -1;
            if (t >= props.Length || props[t] is null)
            {
              if (c.R.Peek() != 0xC0) // a nil is a value left out (as a key that is not in a map)
              {
                if (t >= props.Length)
                  c.Differences.ExtraValue(result, t);
                else
                  c.Differences.UnknownProperty(result, c.Bound.NameOf(plan, t));
              }
              c.R.Skip(c.Depth); // not a property of this type (added by a newer writer, or ignored here)
            }
            else if (!c.R.TryReadNil())
            {
              valueStart = c.R.Pos;
              props[t].Read(c, result);
            }
          }
          break;
        }
        catch (Exception ex) when (valueStart >= 0 && c.Differences.SkipInvalidValue(result, props[t].Info, ex))
        {
          SkipValue(c, valueStart, inner, parents);
          t++;
        }
      }
      c.Differences.Pop();
      c.Depth = depth;
      return (T)result;
    }

    /// <summary>
    /// A value that could not be converted (read completely, see ReadDifferences.NotConvertedKey) and is skipped (MsgPackOptions.ReadErrors): read again from its start without converting it.
    /// </summary>
    /// <param name="depth">The depth of the object's values</param>
    /// <param name="parents">The objects on the stack of the differences while reading the object's values</param>
    private static void SkipValue(ReadContext c, int valueStart, int depth, int parents)
    {
      c.Differences.RestoreDepth(parents);
      c.Depth = depth;
      c.R.Pos = valueStart;
      c.R.Skip(c.Depth);
    }

    private T RestartReporting(ReadContext c, int start, int depth, FullPropertyInfo assignedTo)
    {
      c.Depth = depth;
      c.Differences.Pop(); // the object is read again
      return Slow(c, start, assignedTo);
    }

    #endregion

    private ReadPlan Plan
    {
      get { return _plan ?? (_plan = _serializer.GetReadPlan(typeof(T))); }
    }

    /// <returns>null when the type id is not a plain index or name, or custom type resolvers decide</returns>
    /// <param name="orNull">Null for a name that is not found (otherwise the declared type, as LsMsgPack reads it)</param>
    private Type ReadTypeId(ReadContext c, FullPropertyInfo assignedTo, bool orNull = false)
    {
      if (c.Schema)
      {
        if (!c.R.TryReadInt64(out long id))
          return null;
        return c.Bound.TypeById(id);
      }

      if (_serializer.CustomTypeResolvers || !c.R.TryReadString(out string name) || string.IsNullOrWhiteSpace(name))
        return null;
      return _serializer.ResolveTypeName(name, typeof(T), !orNull);
    }
  }

  /// <summary>
  /// The properties of a type for reading (the ones LsMsgPack sets: FullPropertyInfo.GetSerializedProps).
  /// </summary>
  internal sealed class ReadPlan
  {
    internal readonly Type Type;
    internal readonly PropReader[] Props;
    private readonly Func<object> _create;

    /// <summary>
    /// The binding to the schema read last (see <see cref="BoundSchema.Binding"/>): a payload usually has the same schema as the previous one.
    /// </summary>
    internal volatile BoundSchema.Cached LastBinding;

    internal ReadPlan(Serializer serializer, Type type, FullPropertyInfo[] infos)
    {
      Type = type;
      Props = new PropReader[infos.Length];
      for (int t = 0; t < infos.Length; t++)
        Props[t] = PropReader.Create(serializer, infos[t]);
      _create = Creator(type, serializer.Options._objectCreation);
    }

    internal object Create()
    {
      return _create();
    }

    internal PropReader FindByName(byte[] buffer, int offset, int length, ref int next)
    {
      PropReader[] props = Props;
      if (next < props.Length && props[next].NameIs(buffer, offset, length))
        return props[next++];

      for (int t = props.Length - 1; t >= 0; t--) // the names are unique
      {
        if (props[t].NameIs(buffer, offset, length))
        {
          next = t + 1;
          return props[t];
        }
      }
      return null;
    }

    internal PropReader FindByName(string name)
    {
      for (int t = 0; t < Props.Length; t++)
        if (Props[t].Name == name)
          return Props[t];
      return null;
    }

    /// <summary>
    /// Creates the instances as <see cref="MsgPackOptions.ObjectCreation"/> says (decided once per type, the options of a serializer do not change).
    /// </summary>
    private static Func<object> Creator(Type type, ObjectCreation creation)
    {
      if (creation == ObjectCreation.Uninitialized)
        return () => Instances.Uninitialized(type);

      if (type.GetConstructor(Type.EmptyTypes) != null)
      {
        try
        {
          return ((ICreator)Activator.CreateInstance(typeof(Creator<>).MakeGenericType(type))).Create;
        }
        catch (Exception) // e.g. a runtime that cannot create the generic type
        {
        }
      }
      return () => Instances.Create(type, creation); // a non-public constructor, or none (uninitialized, or refused with ObjectCreation.Constructor)
    }
  }

  internal interface ICreator
  {
    object Create();
  }

  /// <summary>
  /// Creates instances with the parameterless constructor. <c>new T()</c> of a reference type is Activator.CreateInstance (about 18 ns on .NET 8), once a type has been created
  /// <see cref="PropertyAccessor.CompileAfterCalls"/> times a compiled delegate takes over (about 11 ns, the allocation), when the runtime compiles code (see <see cref="PropertyAccessor.CanCompile"/>).
  /// </summary>
  internal sealed class Creator<T> : ICreator where T : new()
  {
    private Func<object> _compiled;
    private int _calls;

    public object Create()
    {
      Func<object> compiled = _compiled;
      if (compiled != null)
        return compiled(); // an exception of the constructor is passed on as it is
      if (Interlocked.Increment(ref _calls) == PropertyAccessor.CompileAfterCalls)
        _compiled = Compile();
      try
      {
        return new T();
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null) // new T() is Activator.CreateInstance<T>(), which wraps the exception of the constructor: pass it on as it is (as LsMsgPack and the compiled delegate)
      {
        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw; // not reached
      }
    }

    private static Func<object> Compile()
    {
#if NETSTANDARD2_1_OR_GREATER
      if (!PropertyAccessor.CanCompile)
        return null;
      try
      {
        DynamicMethod method = new DynamicMethod("Create" + typeof(T).Name, typeof(object), Type.EmptyTypes, typeof(T).Module, true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Newobj, typeof(T).GetConstructor(Type.EmptyTypes));
        il.Emit(OpCodes.Ret);
        return (Func<object>)method.CreateDelegate(typeof(Func<object>));
      }
      catch (Exception) // e.g. a runtime that does not allow it after all, keep new T()
      {
        return null;
      }
#else
      return null; // .NET Standard 2.0 has no DynamicMethod (and compiling an expression costs milliseconds)
#endif
    }
  }

  /// <summary>
  /// Reads the value of one property and sets it.
  /// </summary>
  internal abstract class PropReader
  {
    internal readonly string Name;
    internal readonly FullPropertyInfo Info;
    private readonly byte[] _nameBytes;

    protected PropReader(FullPropertyInfo info)
    {
      Info = info;
      Name = info.PropertyInfo.Name;
      _nameBytes = MsgPackOptions.StringEncoding.GetBytes(Name);
    }

    internal bool NameIs(byte[] buffer, int offset, int length)
    {
      byte[] name = _nameBytes;
      return name.Length == length && new ReadOnlySpan<byte>(buffer, offset, length).SequenceEqual(name);
    }

    internal abstract void Read(ReadContext c, object target);

    internal static PropReader Create(Serializer serializer, FullPropertyInfo info)
    {
      PropertyInfo property = info.PropertyInfo;
      MethodInfo set = property.GetSetMethod(true);
      if (set != null && !set.IsStatic && !property.DeclaringType.IsValueType && !property.DeclaringType.ContainsGenericParameters && serializer.CanBindDelegates)
      {
        try
        {
          Type readerType = typeof(PropReader<,>).MakeGenericType(property.DeclaringType, property.PropertyType);
          return (PropReader)Activator.CreateInstance(readerType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new object[] { serializer, info, set }, null);
        }
        catch (Exception) // use reflection
        {
        }
      }
      return new BoxedPropReader(serializer, info);
    }
  }

  internal sealed class PropReader<TTarget, TValue> : PropReader where TTarget : class
  {
    private readonly Serializer _serializer;
    private readonly Action<TTarget, TValue> _set;
    private ValueReader<TValue> _reader;

    public PropReader(Serializer serializer, FullPropertyInfo info, MethodInfo set) : base(info)
    {
      _serializer = serializer;
      _set = (Action<TTarget, TValue>)Delegate.CreateDelegate(typeof(Action<TTarget, TValue>), set);
    }

    internal override void Read(ReadContext c, object target)
    {
      ValueReader<TValue> reader = _reader ?? (_reader = _serializer.Reader<TValue>());
      _set((TTarget)target, reader.Read(c, Info));
    }
  }

  /// <summary>
  /// Set by reflection (struct targets, static properties).
  /// </summary>
  internal sealed class BoxedPropReader : PropReader
  {
    private readonly Serializer _serializer;
    private ValueReader _reader;

    public BoxedPropReader(Serializer serializer, FullPropertyInfo info) : base(info)
    {
      _serializer = serializer;
    }

    internal override void Read(ReadContext c, object target)
    {
      ValueReader reader = _reader ?? (_reader = _serializer.Reader(Info.PropertyInfo.PropertyType));
      Info.SetValue(target, reader.ReadBoxed(c, Info));
    }
  }

  /// <summary>
  /// The indexed schema of the data, bound to the local types: the types by id, and per type the properties by the index of their name.
  /// </summary>
  internal sealed class BoundSchema
  {
    private readonly IndexedSchemaTypeResolver _schema;
    private readonly ConcurrentDictionary<ReadPlan, PropReader[]> _bindings = new ConcurrentDictionary<ReadPlan, PropReader[]>();
    private static readonly PropReader[] NotBound = new PropReader[0];

    /// <summary>
    /// Used by many calls (a frozen session), the plans remember their binding to it.
    /// </summary>
    private readonly bool _shared;

    internal BoundSchema(IndexedSchemaTypeResolver schema, bool shared)
    {
      _schema = schema;
      _shared = shared;
    }

    /// <returns>The name of the property with this index in the schema entry of the type, otherwise the index</returns>
    internal object NameOf(ReadPlan plan, long index)
    {
      if (_schema.ByType.TryGetValue(plan.Type, out ComplexTypeDef def) && index >= 0 && index < def.Props.Count && def.Props[(int)index] != null)
        return def.Props[(int)index];
      return index;
    }

    /// <returns>null when the id is not in the schema or its type is unknown</returns>
    internal Type TypeById(long id)
    {
      if (id < 0 || id >= _schema.ByTypeId.Count)
        return null;
      return _schema.ByTypeId[(int)id].Type;
    }

    internal sealed class Cached
    {
      internal readonly BoundSchema Schema;
      internal readonly PropReader[] Props;

      internal Cached(BoundSchema schema, PropReader[] props)
      {
        Schema = schema;
        Props = props;
      }
    }

    /// <returns>null when the type is not in the schema (LsMsgPack would add it, which is left to it)</returns>
    internal PropReader[] Binding(ReadPlan plan)
    {
      Cached last = plan.LastBinding;
      if (last != null && ReferenceEquals(last.Schema, this))
        return last.Props;

      PropReader[] props = Bind(plan);
      if (_shared)
        plan.LastBinding = new Cached(this, props);
      return props;
    }

    private PropReader[] Bind(ReadPlan plan)
    {
      if (_bindings.TryGetValue(plan, out PropReader[] bound))
        return ReferenceEquals(bound, NotBound) ? null : bound;

      bound = NotBound;
      if (_schema.TryGetDef(plan.Type, out ComplexTypeDef def) && !def.IsCollection)
      {
        bound = new PropReader[def.Props.Count];
        for (int t = 0; t < bound.Length; t++)
          bound[t] = def.Props[t] is null ? null : plan.FindByName(def.Props[t]);
      }
      _bindings.TryAdd(plan, bound);
      return ReferenceEquals(bound, NotBound) ? null : bound;
    }
  }
}
