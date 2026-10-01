using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LtMsgPack.IO;
using System;
using System.ComponentModel;
using System.Reflection;

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

    internal ObjectPlan(Serializer serializer, Type type, bool names)
    {
      Type = type;
      LtMsgPackOptions options = serializer.Options;
      Infos = names ? FullPropertyInfo.GetSerializedProps(type, options) : FullPropertyInfo.GetStaticallyIncludedProps(type, options);

      Props = new PropWriter[Infos.Length];
      for (int t = 0; t < Infos.Length; t++)
        Props[t] = PropWriter.Create(serializer, Infos[t]);

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
    }

    /// <summary>
    /// Writes the object as a map (LsMsgPack: SerializeObject for an object with properties).
    /// </summary>
    internal void Write(WriteContext c, object value, bool typeId, FullPropertyInfo assignedTo)
    {
      byte[][] keys = c.BeginObject(this, out FullPropertyInfo[] infos); // before the type id, as LsMsgPack resolves the ids first
      if (c.Serializer.Options._objectLayout == ObjectLayout.Array)
      {
        WriteArray(c, value, typeId, assignedTo, infos);
        return;
      }

      int max = Props.Length + 1;
      int at = c.W.ReserveMapHeader(max);
      int count = 0;
      if (typeId)
      {
        c.W.String(string.Empty);
        c.WriteTypeId(Type, assignedTo);
        count++;
      }
      else
        SerializationRules.ThrowIfUnresolvableWithSchema(Type, assignedTo, c.UsesSchema);

      c.EnterContainer();
      PropWriter[] props = Props;
      for (int t = 0; t < props.Length; t++)
        if (props[t].Write(c, value, keys[t], infos[t]))
          count++;
      c.LeaveContainer();

      c.W.PatchMapHeader(at, max, count);
    }

    /// <summary>
    /// <see cref="ObjectLayout.Array"/> (LsMsgPack: SerializeAsArray): the values in the order of the properties, nil when the filters leave one out, wrapped in a map with the type id when needed.
    /// </summary>
    private void WriteArray(WriteContext c, object value, bool typeId, FullPropertyInfo assignedTo, FullPropertyInfo[] infos)
    {
      if (typeId)
      {
        c.W.MapHeader(2);
        c.W.String(string.Empty);
        c.WriteTypeId(Type, assignedTo);
        c.W.String(MsgPackOptions.ContentKey);
      }
      else
        SerializationRules.ThrowIfUnresolvableAsArray(Type, assignedTo);

      c.EnterContainer();
      PropWriter[] props = Props;
      if (c.Serializer.Options._trimTrailingNulls)
        WriteTrimmed(c, value, infos);
      else
      {
        c.W.ArrayHeader(props.Length);
        for (int t = 0; t < props.Length; t++)
          if (!props[t].Write(c, value, null, infos[t]))
            c.W.Nil();
      }
      c.LeaveContainer();
    }

    /// <summary>
    /// <see cref="MsgPackOptions.TrimTrailingNulls"/>: the nils after the last other value are taken back, the header is written for the values that are left.
    /// </summary>
    private void WriteTrimmed(WriteContext c, object value, FullPropertyInfo[] infos)
    {
      MsgPackWriter w = c.W;
      PropWriter[] props = Props;
      int at = w.ReserveArrayHeader(props.Length);
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
    /// Writes the properties into a map that is being written (a collection with <c>SerializeProperties</c>).
    /// </summary>
    /// <returns>The number of entries written</returns>
    internal int WriteProperties(WriteContext c, object value, byte[][] keys, FullPropertyInfo[] infos)
    {
      int count = 0;
      PropWriter[] props = Props;
      for (int t = 0; t < props.Length; t++)
        if (props[t].Write(c, value, keys[t], infos[t]))
          count++;
      return count;
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
    protected readonly bool BoxedFilters;
    protected readonly bool NoFilters;

    protected PropWriter(Serializer serializer, FullPropertyInfo info)
    {
      Serializer = serializer;
      NoFilters = serializer.Filters == FilterMode.None;
      BoxedFilters = serializer.Filters == FilterMode.Custom || info.CustomAttributes.ContainsKey(nameof(DefaultValueAttribute));
    }

    /// <param name="key">The encoded property id, null for the array layout (only the value)</param>
    /// <returns>false when the filters left the value out (nothing was written)</returns>
    internal abstract bool Write(WriteContext c, object target, byte[] key, FullPropertyInfo info);

    protected bool IncludeBoxed(object value, FullPropertyInfo info)
    {
      IMsgPackPropertyIncludeDynamically[] filters = Serializer.Options._dynamicFilters;
      for (int t = filters.Length - 1; t >= 0; t--)
        if (!filters[t].IncludeProperty(info, value))
          return false;
      return true;
    }

    internal static PropWriter Create(Serializer serializer, FullPropertyInfo info)
    {
      PropertyInfo property = info.PropertyInfo;
      MethodInfo get = property.GetGetMethod(true);
      if (get != null && !get.IsStatic && !property.DeclaringType.IsValueType && !property.DeclaringType.ContainsGenericParameters && serializer.CanBindDelegates)
      {
        try
        {
          Type writerType = typeof(PropWriter<,>).MakeGenericType(property.DeclaringType, property.PropertyType);
          return (PropWriter)Activator.CreateInstance(writerType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new object[] { serializer, info, get }, null);
        }
        catch (Exception) // e.g. a runtime that cannot create the generic type, use reflection
        {
        }
      }
      return new BoxedPropWriter(serializer, info);
    }
  }

  /// <summary>
  /// A property read through a typed delegate, the value is not boxed.
  /// </summary>
  internal sealed class PropWriter<TTarget, TValue> : PropWriter where TTarget : class
  {
    private readonly Func<TTarget, TValue> _get;
    private readonly FullPropertyInfo _declared;
    private ValueHandler<TValue> _handler;

    public PropWriter(Serializer serializer, FullPropertyInfo info, MethodInfo get) : base(serializer, info)
    {
      _declared = info;
      _get = (Func<TTarget, TValue>)Delegate.CreateDelegate(typeof(Func<TTarget, TValue>), get);
    }

    internal override bool Write(WriteContext c, object target, byte[] key, FullPropertyInfo info)
    {
      TValue value;
      try
      {
        value = _get((TTarget)target);
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null)
      {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw;
      }

      ValueHandler<TValue> handler = _handler ?? (_handler = Serializer.Handler<TValue>(_declared));
      if (!NoFilters)
      {
        if (BoxedFilters)
        {
          if (!IncludeBoxed(value, info))
            return false;
        }
        else if (!handler.IncludeByDefault(value, info))
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
  /// A property read by reflection (struct targets, static properties, or when delegates cannot be bound), the value is written like any boxed value.
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
