using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

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

      if (_serializer.SlowObjects)
        return Slow(c, start, assignedTo);

      int count = c.R.TryReadMapHeader();
      if (count < 0)
        return Slow(c, start, assignedTo);

      ReadPlan plan = null;
      if (count > 0 && c.R.Peek() == 0xA0) // the type id (key "") is written first
      {
        c.R.Pos++;
        Type type = ReadTypeId(c, assignedTo);
        if (type is null)
          return Slow(c, start, assignedTo);
        plan = type == typeof(T) ? Plan : _serializer.GetReadPlan(type);
        if (plan is null || !typeof(T).IsAssignableFrom(type))
          return Slow(c, start, assignedTo);
        count--;
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

    private T Restart(ReadContext c, int start, int depth, FullPropertyInfo assignedTo)
    {
      c.Depth = depth;
      return Slow(c, start, assignedTo);
    }

    private ReadPlan Plan
    {
      get { return _plan ?? (_plan = _serializer.GetReadPlan(typeof(T))); }
    }

    /// <returns>null when the type id is not a plain index or name, or custom type resolvers decide</returns>
    private Type ReadTypeId(ReadContext c, FullPropertyInfo assignedTo)
    {
      if (c.Schema)
      {
        if (!c.R.TryReadInt64(out long id))
          return null;
        return c.Bound.TypeById(id);
      }

      if (_serializer.CustomTypeResolvers || !c.R.TryReadString(out string name) || string.IsNullOrWhiteSpace(name))
        return null;
      return _serializer.ResolveTypeName(name, typeof(T));
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

    internal ReadPlan(Serializer serializer, Type type, FullPropertyInfo[] infos)
    {
      Type = type;
      Props = new PropReader[infos.Length];
      for (int t = 0; t < infos.Length; t++)
        Props[t] = PropReader.Create(serializer, infos[t]);
      _create = Creator(type);
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

      for (int t = 0; t < props.Length; t++)
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
      foreach (PropReader prop in Props)
        if (prop.Name == name)
          return prop;
      return null;
    }

    private static Func<object> Creator(Type type)
    {
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
      return () => Instances.Create(type);
    }
  }

  internal interface ICreator
  {
    object Create();
  }

  internal sealed class Creator<T> : ICreator where T : new()
  {
    public object Create()
    {
      try
      {
        return new T();
      }
      catch (Exception) // as LsMsgPack: fall back to an uninitialized instance when the constructor throws
      {
        return Instances.Create(typeof(T));
      }
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
      if (name.Length != length)
        return false;
      for (int t = 0; t < length; t++)
        if (buffer[offset + t] != name[t])
          return false;
      return true;
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

    internal BoundSchema(IndexedSchemaTypeResolver schema)
    {
      _schema = schema;
    }

    /// <returns>null when the id is not in the schema or its type is unknown</returns>
    internal Type TypeById(long id)
    {
      if (id < 0 || id >= _schema.ByTypeId.Count)
        return null;
      return _schema.ByTypeId[(int)id].Type;
    }

    /// <returns>null when the type is not in the schema (LsMsgPack would add it, which is left to it)</returns>
    internal PropReader[] Binding(ReadPlan plan)
    {
      if (_bindings.TryGetValue(plan, out PropReader[] bound))
        return ReferenceEquals(bound, NotBound) ? null : bound;

      bound = NotBound;
      if (_schema.ByType.TryGetValue(plan.Type, out ComplexTypeDef def) && !def.IsCollection)
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
