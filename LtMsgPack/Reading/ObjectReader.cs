using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Types;
using LtMsgPack.Generated;
using System;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
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
        else if (prop.GeneratedId < 0 || !plan.Code.ReadValueBoxed(c, result, prop.GeneratedId, plan.GeneratedRead)) // the reader of the source generator, the runtime's when it does not read the value
          prop.Read(c, result);
      }
      c.Depth = depth;
      if (plan.WithValues != null) // the values were collected for the constructor
        result = plan.WithValues.Create(result);
      return (T)result;
    }

    /// <summary>
    /// An object written as an array (<see cref="ObjectLayout.Array"/>), the header is read: the values by position, or with the schema by the names of the schema.
    /// A nil sets the value the writer leaves out (PropReader.ReadNil), values after the end of the array are left as the constructor made them (LsMsgPack: ValueConverter.ConvertPositional).
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
      if (plan.Positional) // the reader of the source generator: the same loop
        plan.Code.ReadPositionalBoxed(c, result, props, items, plan.GeneratedRead);
      else
      {
        for (int t = 0; t < items; t++)
        {
          if (t >= props.Length || props[t] is null)
            c.R.Skip(c.Depth); // not a property of this type (added by a newer writer, or ignored here)
          else if (c.R.TryReadNil())
            props[t].ReadNil(result);
          else
            props[t].Read(c, result);
        }
      }
      c.Depth = depth;
      if (plan.WithValues != null) // the values were collected for the constructor
        result = plan.WithValues.Create(result);
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
    /// Objects created by the constructor that takes their values (<see cref="ReadPlan.WithValues"/>) are read by LsMsgPack.Core, which reports their class (ObjectConstructor.Arguments).
    /// </summary>
    private T ReadReporting(ReadContext c, int start, FullPropertyInfo assignedTo)
    {
      int count = c.R.TryReadMapHeader();
      if (count < 0)
      {
        int items = c.R.TryReadArrayHeader(); // ObjectLayout.Array
        if (items < 0 || Plan is null || Plan.WithValues != null)
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
        if (plan is null || plan.WithValues != null || !typeof(T).IsAssignableFrom(type)) // LsMsgPack's way refuses a type that is not assignable
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
        if (plan is null || plan.WithValues != null) // abstract or not an object with properties
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
            else if (c.R.TryReadNil())
              props[t].ReadNil(result);
            else
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
    /// The type is created by the constructor that takes its values (see <see cref="ObjectConstructor"/>), null otherwise: <see cref="Create"/> returns the values it collects
    /// (the <see cref="Props"/> read into them), <see cref="ConstructorPlan.Create"/> the object.
    /// </summary>
    internal readonly ConstructorPlan WithValues;

    /// <summary>
    /// The binding to the schema read last (see <see cref="BoundSchema.Binding"/>): a payload usually has the same schema as the previous one.
    /// </summary>
    internal volatile BoundSchema.Cached LastBinding;

    /// <param name="generated">The constructor call of the source generator, null when there is none</param>
    /// <summary>
    /// The reader of the source generator (see LtMsgPackContext), null when there is none: the <see cref="Props"/> with a <see cref="PropReader.GeneratedId"/> are read by it.
    /// </summary>
    internal readonly GeneratedObjectCode Code;
    internal readonly GeneratedReadPlan GeneratedRead;

    /// <summary>
    /// Arrays are read by the generated <see cref="GeneratedObjectCode{T}.ReadPositional"/>.
    /// </summary>
    internal readonly bool Positional;

    private readonly bool _caseInsensitive;

    internal bool CaseInsensitive { get { return _caseInsensitive; } }

    /// <summary>
    /// <see cref="MsgPackOptions.PropertyNameCaseInsensitive"/>: the properties whose name equals another one's ignoring case (a key matching them sets none), null without the option.
    /// </summary>
    private readonly bool[] _ambiguous;

    /// <summary>
    /// Every property name is ASCII: keys in ASCII are compared ignoring ASCII case as bytes (the same as <see cref="StringComparison.OrdinalIgnoreCase"/> for ASCII).
    /// </summary>
    private readonly bool _asciiNames;

    internal ReadPlan(Serializer serializer, Type type, FullPropertyInfo[] infos, Func<object> generated)
    {
      Type = type;
      _caseInsensitive = serializer.Options._propertyNameCaseInsensitive;
      SerializationRules.ThrowIfUnsupportedFrameworkType(type);
      Props = new PropReader[infos.Length];
      ObjectConstructor constructor = ObjectConstructor.Of(type);
      if (constructor.CreatesWith(serializer.Options._objectCreation))
        WithValues = new ConstructorPlan(serializer, constructor, infos, Props);
      else
      {
        for (int t = 0; t < infos.Length; t++)
          Props[t] = PropReader.Create(serializer, infos[t]);
      }
      if (_caseInsensitive)
      {
        _ambiguous = new bool[Props.Length];
        _asciiNames = true;
        for (int t = Props.Length - 1; t >= 0; t--)
        {
          _asciiNames &= IsAscii(Props[t].Name);
          for (int u = Props.Length - 1; u >= 0; u--)
            _ambiguous[t] |= u != t && string.Equals(Props[t].Name, Props[u].Name, StringComparison.OrdinalIgnoreCase);
        }
      }
      if (WithValues != null)
      {
        _create = WithValues.Collect;
        return; // the code of the source generator sets the properties of an object, which is created after them
      }
      _create = Creator(type, serializer.Options._objectCreation, generated);

      GeneratedObjectCode code = serializer.GeneratedCode(type);
      if (code != null && (code.Capabilities & GeneratedCapabilities.ReadValue) != 0)
      {
        Code = code;
        GeneratedRead = serializer.GeneratedReadPlan;
        // Version 1 of the generated code left a nil as the constructor made it: the runtime reads the array layout then
        GeneratedCapabilities positional = GeneratedCapabilities.ReadPositional | GeneratedCapabilities.ReadPositionalNil;
        Positional = (code.Capabilities & positional) == positional;
        for (int t = 0; t < infos.Length; t++)
          Props[t].GeneratedId = serializer.ReadMember(code, infos[t]);
      }
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
      if (_caseInsensitive)
        return FindOrMatchIgnoringCase(buffer, offset, length, ref next);

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

    /// <summary>
    /// FindByName with <see cref="MsgPackOptions.PropertyNameCaseInsensitive"/> (apart, so FindByName stays as small as without it), after the next property did not have the name:
    /// the next property ignoring case (camelCase keys in the order of the properties), the exact names, then ignoring case: ASCII keys and names as bytes, anything else as strings.
    /// </summary>
    private PropReader FindOrMatchIgnoringCase(byte[] buffer, int offset, int length, ref int next)
    {
      PropReader[] props = Props;
      if (next < props.Length && length > 0 && props[next].FirstDiffersInCase(buffer[offset]) && _asciiNames && !_ambiguous[next] && props[next].NameIsIgnoringAsciiCase(buffer, offset, length))
        return props[next++]; // no other property equals the key ignoring case, so none has it as its name

      for (int t = props.Length - 1; t >= 0; t--)
      {
        if (props[t].NameIs(buffer, offset, length))
        {
          next = t + 1;
          return props[t];
        }
      }

      if (!_asciiNames || !IsAscii(buffer, offset, length))
        return FindIgnoringCase(MsgPackOptions.StringEncoding.GetString(buffer, offset, length));
      for (int t = props.Length - 1; t >= 0; t--)
      {
        if (props[t].NameIsIgnoringAsciiCase(buffer, offset, length))
        {
          if (_ambiguous[t])
            return null;
          next = t + 1;
          return props[t];
        }
      }
      return null;
    }

    private static bool IsAscii(string name)
    {
      for (int t = name.Length - 1; t >= 0; t--)
        if (name[t] >= 0x80)
          return false;
      return true;
    }

    private static bool IsAscii(byte[] buffer, int offset, int length)
    {
      for (int t = offset + length - 1; t >= offset; t--)
        if (buffer[t] >= 0x80)
          return false;
      return true;
    }

    /// <summary>
    /// <see cref="MsgPackOptions.PropertyNameCaseInsensitive"/>, for a name that is not a property's: the one property whose name equals it ignoring case, null when none or several do.
    /// </summary>
    private PropReader FindIgnoringCase(string name)
    {
      PropReader found = null;
      for (int t = Props.Length - 1; t >= 0; t--)
      {
        if (!string.Equals(Props[t].Name, name, StringComparison.OrdinalIgnoreCase))
          continue;
        if (found != null)
          return null;
        found = Props[t];
      }
      return found;
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
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.Fallback)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = TrimReasons.Fallback)]
    private static Func<object> Creator(Type type, ObjectCreation creation, Func<object> generated)
    {
      if (creation == ObjectCreation.Uninitialized)
        return () => Instances.Uninitialized(type);

      if (generated != null) // the parameterless constructor (an exception of the constructor is passed on as it is)
        return generated;

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
  /// <see cref="PropertyAccessor.CompileAfterCalls"/> times a compiled delegate takes over (about 11 ns, the allocation), when the runtime supports dynamic code (see <see cref="PropertyAccessor.CanCompile"/>).
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

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only when the runtime supports dynamic code (PropertyAccessor.CanCompile), a failure keeps new T().")]
    private static Func<object> Compile()
    {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
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
  /// <para>Public for the code of the source generator (see LtMsgPack.LtMsgPackContext): may change between releases.</para>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public abstract class PropReader
  {
    internal readonly string Name;
    internal readonly FullPropertyInfo Info;
    private readonly byte[] _nameBytes;

    /// <summary>
    /// The member of the generated reader of the type (<see cref="GeneratedObjectCode{T}.ReadValue"/>), -1 when the runtime reads it.
    /// </summary>
    public int GeneratedId { get; internal set; } = -1;

    internal PropReader(FullPropertyInfo info)
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

    /// <summary>
    /// The first byte of the key is the first letter of the name in the other case (one check before NameIsIgnoringAsciiCase, keys that are left out make the next property another one).
    /// </summary>
    internal bool FirstDiffersInCase(byte first)
    {
      byte[] name = _nameBytes;
      return name.Length > 0 && (first ^ name[0]) == 0x20 && (uint)((first | 0x20) - 'a') <= 'z' - 'a';
    }

    /// <summary>
    /// The name (ASCII) equals the key ignoring ASCII case, false for a key that is not ASCII (see ReadPlan.FindIgnoringCase).
    /// </summary>
    internal bool NameIsIgnoringAsciiCase(byte[] buffer, int offset, int length)
    {
      byte[] name = _nameBytes;
      if (name.Length != length)
        return false;
      for (int t = 0; t < length; t++)
      {
        int a = buffer[offset + t], b = name[t];
        if (a != b && ((a | 0x20) != (b | 0x20) || (uint)((a | 0x20) - 'a') > 'z' - 'a')) // the same letter in the other case
          return false;
      }
      return true;
    }

    /// <summary>
    /// Reads the value at the reader's position as the runtime does (converted when its format is not the expected one) and sets it.
    /// </summary>
    public abstract void Read(ReadContext c, object target);

    /// <summary>
    /// Sets the value of a nil in the array layout (the nil is read): what the writer leaves out (FullPropertyInfo.NilValue: null, the default of the type or the value of [DefaultValue]).
    /// Called by ObjectReader.ReadPositional and the generated code (version 2).
    /// </summary>
    public abstract void ReadNil(object target);

    /// <summary>
    /// Sets a value that was read before the object was created (see <see cref="ConstructorPlan"/>).
    /// </summary>
    internal virtual void SetBoxed(object target, object value)
    {
      Info.SetValue(target, value);
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only when the runtime supports dynamic code (CanBindDelegates), a failure falls back to reflection.")]
    internal static PropReader Create(Serializer serializer, FullPropertyInfo info)
    {
      if (info.IsComputed) // written only (a collection without a setter is filled by its accessor, see BoxedPropReader)
        return new ComputedPropReader(info);

      PropertyInfo property = info.PropertyInfo;
      PropReader generated = serializer.GeneratedProperty(property)?.CreateReader(serializer, info); // the setter of the source generator
      if (generated != null)
        return generated;

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
    private readonly TValue _nil;
    private ValueReader<TValue> _reader;

    public PropReader(Serializer serializer, FullPropertyInfo info, MethodInfo set) : base(info)
    {
      _serializer = serializer;
      _set = (Action<TTarget, TValue>)Delegate.CreateDelegate(typeof(Action<TTarget, TValue>), set);
      _nil = NilOf(serializer, info);
    }

    /// <param name="set">The setter of the source generator</param>
    internal PropReader(Serializer serializer, FullPropertyInfo info, Action<TTarget, TValue> set) : base(info)
    {
      _serializer = serializer;
      _set = set;
      _nil = NilOf(serializer, info);
    }

    private static TValue NilOf(Serializer serializer, FullPropertyInfo info)
    {
      object nil = info.NilValue(serializer.Options);
      return nil is null ? default(TValue) : (TValue)nil;
    }

    public override void Read(ReadContext c, object target)
    {
      ValueReader<TValue> reader = _reader ?? (_reader = _serializer.Reader<TValue>());
      _set((TTarget)target, reader.Read(c, Info));
    }

    public override void ReadNil(object target)
    {
      _set((TTarget)target, _nil);
    }

    internal override void SetBoxed(object target, object value)
    {
      _set((TTarget)target, value is null ? default(TValue) : (TValue)value);
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

    public override void Read(ReadContext c, object target)
    {
      ValueReader reader = _reader ?? (_reader = _serializer.Reader(Info.PropertyInfo.PropertyType));
      Info.SetValue(target, reader.ReadBoxed(c, Info));
    }

    public override void ReadNil(object target)
    {
      Info.SetValue(target, Info.NilValue(_serializer.Options));
    }
  }

  /// <summary>
  /// A property without a setter whose value is skipped (FullPropertyInfo.IsComputed, see GetOnlyProperties.Computed).
  /// </summary>
  internal sealed class ComputedPropReader : PropReader
  {
    public ComputedPropReader(FullPropertyInfo info) : base(info)
    {
    }

    public override void Read(ReadContext c, object target)
    {
      c.R.Skip(c.Depth);
    }

    public override void ReadNil(object target)
    {
    }
  }

  /// <summary>
  /// An object created by the constructor that takes its values (<see cref="ObjectConstructor.WithValues"/>, as LsMsgPack.Core's ObjectConstructor.Arguments): the properties are read
  /// by <see cref="ArgumentReader"/>s into <see cref="ConstructorValues"/>. The constructor is called as soon as the values of all parameters whose properties are read are there
  /// (the properties the data has after them are set directly, without boxing), otherwise when the object ends (<see cref="Create"/>): parameters the data does not have get
  /// the value the writer leaves out. Then the properties no parameter takes that were read before are set.
  /// </summary>
  internal sealed class ConstructorPlan
  {
    /// <summary>
    /// A property no parameter takes that was not read: left as the constructor made it.
    /// </summary>
    internal static readonly object NotRead = new object();

    private readonly ObjectConstructor _constructor;

    /// <summary>
    /// The values before any is read: a parameter whose property is read gets the value the writer leaves out (FullPropertyInfo.NilValue, as a nil sets it), the others their default value
    /// (see ObjectConstructor.Arguments.Create); the other properties <see cref="NotRead"/>.
    /// </summary>
    private readonly object[] _initial;

    /// <summary>
    /// The setters of the properties no parameter takes, by their position in the values after the parameters.
    /// </summary>
    private readonly PropReader[] _setters;

    /// <summary>
    /// A bit per parameter whose property is read (see <see cref="ArgumentReader.Bit"/>): the constructor is called when they are all cleared.
    /// </summary>
    private readonly ulong _awaited;

    /// <param name="props">Gets the reader of each property</param>
    internal ConstructorPlan(Serializer serializer, ObjectConstructor constructor, FullPropertyInfo[] infos, PropReader[] props)
    {
      _constructor = constructor;
      int parameters = constructor.ParameterCount;
      List<object> initial = new List<object>(parameters + infos.Length);
      for (int t = 0; t < parameters; t++)
        initial.Add(constructor.DefaultOf(t));
      List<PropReader> setters = new List<PropReader>();
      for (int t = 0; t < infos.Length; t++)
      {
        FullPropertyInfo info = infos[t];
        if (info.IsComputed) // written only
        {
          props[t] = new ComputedPropReader(info);
          continue;
        }
        object nil = info.NilValue(serializer.Options);
        if (constructor.TryGetParameter(info.PropertyInfo.Name, out int index))
        {
          initial[index] = nil;
          ArgumentReader parameter = new ArgumentReader(serializer, this, info, index, nil, null);
          _awaited |= parameter.Bit;
          props[t] = parameter;
        }
        else
        {
          PropReader setter = PropReader.Create(serializer, info);
          props[t] = new ArgumentReader(serializer, this, info, initial.Count, nil, setter);
          initial.Add(NotRead);
          setters.Add(setter);
        }
      }
      _initial = initial.ToArray();
      _setters = setters.ToArray();
    }

    /// <summary>
    /// What the values of an object are read into.
    /// </summary>
    internal object Collect()
    {
      object[] values = new object[_initial.Length];
      Array.Copy(_initial, values, values.Length); // a third of Clone's time (52 ns for 5 values on .NET 8)
      return new ConstructorValues(values, _awaited);
    }

    /// <summary>
    /// The object, once its values are read: created now when the values of some parameters were not there.
    /// </summary>
    /// <param name="collected">From <see cref="Collect"/>, with the values read</param>
    internal object Create(object collected)
    {
      ConstructorValues values = (ConstructorValues)collected;
      return values.Created ?? Construct(values);
    }

    /// <summary>
    /// Calls the constructor (an exception of it is passed on as it is), then sets the other properties that were read.
    /// </summary>
    internal object Construct(ConstructorValues collected)
    {
      object[] values = collected.Values;
      object result = _constructor.Invoke(values);
      int parameters = values.Length - _setters.Length;
      for (int t = parameters; t < values.Length; t++)
        if (!ReferenceEquals(values[t], NotRead))
          _setters[t - parameters].SetBoxed(result, values[t]);
      collected.Created = result;
      return result;
    }
  }

  /// <summary>
  /// The values of an object read for its constructor (see <see cref="ConstructorPlan"/>), and the object once it is created.
  /// </summary>
  internal sealed class ConstructorValues
  {
    /// <summary>
    /// The parameters, then the properties no parameter takes (<see cref="ConstructorPlan.NotRead"/> until read).
    /// </summary>
    internal readonly object[] Values;

    /// <summary>
    /// The bits of the parameters whose values are still to be read.
    /// </summary>
    internal ulong Awaited;

    /// <summary>
    /// The object, null until the constructor is called.
    /// </summary>
    internal object Created;

    internal ConstructorValues(object[] values, ulong awaited)
    {
      Values = values;
      Awaited = awaited;
    }
  }

  /// <summary>
  /// Reads the value of a property for an object created by its constructor (see <see cref="ConstructorPlan"/>): into the collected values, or, for a property no parameter takes,
  /// into the object once it is created.
  /// </summary>
  internal sealed class ArgumentReader : PropReader
  {
    private readonly Serializer _serializer;
    private readonly ConstructorPlan _plan;
    private readonly int _index;
    private readonly object _nil;

    /// <summary>
    /// The reader setting the property, null for a parameter.
    /// </summary>
    private readonly PropReader _setter;

    /// <summary>
    /// The bit of a parameter in <see cref="ConstructorValues.Awaited"/>: parameters after the 63rd share the last one, which is never cleared (the constructor is called at the end).
    /// </summary>
    internal readonly ulong Bit;

    private ValueReader _reader;

    /// <param name="index">The position of the value in the collected values</param>
    /// <param name="nil">The value of a nil (FullPropertyInfo.NilValue)</param>
    /// <param name="setter">The reader setting the property when no parameter takes it, otherwise null</param>
    internal ArgumentReader(Serializer serializer, ConstructorPlan plan, FullPropertyInfo info, int index, object nil, PropReader setter) : base(info)
    {
      _serializer = serializer;
      _plan = plan;
      _index = index;
      _nil = nil;
      _setter = setter;
      if (setter is null)
        Bit = 1UL << Math.Min(index, 63);
    }

    public override void Read(ReadContext c, object target)
    {
      ConstructorValues values = (ConstructorValues)target;
      if (values.Created != null)
      {
        if (_setter is null)
          c.R.Skip(c.Depth); // a parameter again (a duplicate key): the first value is kept
        else
          _setter.Read(c, values.Created);
        return;
      }
      ValueReader reader = _reader ?? (_reader = _serializer.Reader(Info.PropertyInfo.PropertyType));
      values.Values[_index] = reader.ReadBoxed(c, Info);
      Received(values);
    }

    public override void ReadNil(object target)
    {
      ConstructorValues values = (ConstructorValues)target;
      if (values.Created != null)
      {
        _setter?.ReadNil(values.Created);
        return;
      }
      values.Values[_index] = _nil;
      Received(values);
    }

    private void Received(ConstructorValues values)
    {
      if (_setter is null && Bit != 1UL << 63 && (values.Awaited &= ~Bit) == 0)
        _plan.Construct(values); // the properties after it are set directly
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
        if (plan.CaseInsensitive)
          BindIgnoringCase(bound, plan, def);
      }
      _bindings.TryAdd(plan, bound);
      return ReferenceEquals(bound, NotBound) ? null : bound;
    }

    /// <summary>
    /// <see cref="MsgPackOptions.PropertyNameCaseInsensitive"/>: the properties the entry names only ignoring case (see <see cref="ComplexTypeDef.IdsIgnoringCase"/>).
    /// </summary>
    private static void BindIgnoringCase(PropReader[] bound, ReadPlan plan, ComplexTypeDef def)
    {
      string[] names = new string[plan.Props.Length];
      for (int t = 0; t < names.Length; t++)
        names[t] = plan.Props[t].Name;
      Dictionary<string, int> ids = def.IdsIgnoringCase(names);
      if (ids.Count == 0)
        return;
      for (int t = 0; t < names.Length; t++)
        if (ids.TryGetValue(names[t], out int position) && bound[position] is null)
          bound[position] = plan.Props[t];
    }
  }
}
