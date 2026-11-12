using LsMsgPack.Meta;
using LsMsgPack.Extensions;
using LsMsgPack.Generated;
using LsMsgPack.IO;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace LsMsgPack.Writing
{
  internal delegate int CompiledWrite<T>(WriteContext c, T o, byte[][] keys, FullPropertyInfo[] infos, GeneratedWritePlan p, out int end);

  internal delegate void CompiledWriteUnrolled<T>(WriteContext c, T o, FullPropertyInfo[] infos, GeneratedWritePlan p);

  internal delegate int CompiledRefWrite<T>(WriteContext c, ref T o, byte[][] keys, FullPropertyInfo[] infos, GeneratedWritePlan p, out int end);

  internal delegate void CompiledRefWriteUnrolled<T>(WriteContext c, ref T o, FullPropertyInfo[] infos, GeneratedWritePlan p);

  /// <summary>
  /// The writer of one <see cref="ObjectPlan"/> compiled at runtime (see <see cref="CompiledWriter"/>).
  /// </summary>
  internal interface ICompiledCode
  {
    /// <summary>
    /// Compiles the writer the plan uses for its layout (the others at their first call).
    /// </summary>
    /// <param name="map">The map layout (<see cref="ObjectLayout.Map"/>), otherwise arrays</param>
    void Compile(GeneratedWritePlan plan, bool map);
  }

  /// <summary>
  /// The code the source generator would write for a class (<see cref="GeneratedObjectCode{T}.Write"/> and <see cref="GeneratedObjectCode{T}.WriteUnrolled"/>), compiled at runtime for one plan.
  /// </summary>
  internal sealed class CompiledObjectCode<T> : GeneratedObjectCode<T>, ICompiledCode where T : class
  {
    private readonly MemberInfo[] _properties; // properties and fields
    private readonly ShouldSerializeCalls _calls;
    private CompiledWrite<T> _map;
    private CompiledWrite<T> _items;
    private CompiledWriteUnrolled<T> _unrolled;

    /// <param name="calls">null when no member calls a ShouldSerialize method</param>
    public CompiledObjectCode(GeneratedMember[] members, MemberInfo[] properties, ShouldSerializeCalls calls) : base(members, CompiledWriter.Positions(members.Length), GeneratedCapabilities.Write | GeneratedCapabilities.WriteUnrolled | GeneratedCapabilities.WriteUnrolledMap | GeneratedCapabilities.WriteFormats)
    {
      _properties = properties;
      _calls = calls;
    }

    public void Compile(GeneratedWritePlan plan, bool map)
    {
      if (map)
        _map = CompiledWriter.Write<CompiledWrite<T>>(typeof(T), false, Members, _properties, _calls, plan, true);
      else if (plan.Unrolled)
        _unrolled = CompiledWriter.Unrolled<CompiledWriteUnrolled<T>>(typeof(T), false, Members, _properties, _calls, plan);
      else
        _items = CompiledWriter.Write<CompiledWrite<T>>(typeof(T), false, Members, _properties, _calls, plan, false);
    }

    public override int Write(WriteContext c, T value, byte[][] keys, FullPropertyInfo[] infos, GeneratedWritePlan p, out int end)
    {
      CompiledWrite<T> write = keys != null
        ? _map ?? (_map = CompiledWriter.Write<CompiledWrite<T>>(typeof(T), false, Members, _properties, _calls, p, true)) // also the maps of the array layout: a collection with SerializeProperties
        : _items;
      return write(c, value, keys, infos, p, out end);
    }

    public override int WriteUnrolledMap(WriteContext c, T value, byte[][] keys, FullPropertyInfo[] infos, GeneratedWritePlan p)
    {
      CompiledWrite<T> write = _map ?? (_map = CompiledWriter.Write<CompiledWrite<T>>(typeof(T), false, Members, _properties, _calls, p, true)); // also the maps of the array layout: a collection with SerializeProperties
      return write(c, value, keys, infos, p, out int end);
    }

    public override void WriteUnrolled(WriteContext c, T value, FullPropertyInfo[] infos, GeneratedWritePlan p)
    {
      _unrolled(c, value, infos, p); // compiled with the plan (only Unrolled plans call it)
    }
  }

  /// <summary>
  /// <see cref="CompiledObjectCode{T}"/> for a struct by reference (<see cref="GeneratedStructCode{T}"/>).
  /// </summary>
  internal sealed class CompiledStructCode<T> : GeneratedStructCode<T>, ICompiledCode where T : struct
  {
    private readonly MemberInfo[] _properties; // properties and fields
    private CompiledRefWrite<T> _map;
    private CompiledRefWrite<T> _items;
    private CompiledRefWriteUnrolled<T> _unrolled;

    /// <param name="calls">Always null: a struct's ShouldSerialize method is called boxed, by the runtime's writer of the property</param>
    public CompiledStructCode(GeneratedMember[] members, MemberInfo[] properties, ShouldSerializeCalls calls) : base(members, CompiledWriter.Positions(members.Length), GeneratedCapabilities.Write | GeneratedCapabilities.WriteUnrolled | GeneratedCapabilities.WriteUnrolledMap | GeneratedCapabilities.WriteFormats)
    {
      _properties = properties;
      Unboxer = new StructUnboxer<T, T>(); // what GeneratedValues.Struct gives the generated code: a struct written as an object
    }

    public void Compile(GeneratedWritePlan plan, bool map)
    {
      if (map)
        _map = CompiledWriter.Write<CompiledRefWrite<T>>(typeof(T), true, Members, _properties, null, plan, true);
      else if (plan.Unrolled)
        _unrolled = CompiledWriter.Unrolled<CompiledRefWriteUnrolled<T>>(typeof(T), true, Members, _properties, null, plan);
      else
        _items = CompiledWriter.Write<CompiledRefWrite<T>>(typeof(T), true, Members, _properties, null, plan, false);
    }

    public override int Write(WriteContext c, ref T value, byte[][] keys, FullPropertyInfo[] infos, GeneratedWritePlan p, out int end)
    {
      CompiledRefWrite<T> write = keys != null
        ? _map ?? (_map = CompiledWriter.Write<CompiledRefWrite<T>>(typeof(T), true, Members, _properties, null, p, true))
        : _items;
      return write(c, ref value, keys, infos, p, out end);
    }

    public override int WriteUnrolledMap(WriteContext c, ref T value, byte[][] keys, FullPropertyInfo[] infos, GeneratedWritePlan p)
    {
      CompiledRefWrite<T> write = _map ?? (_map = CompiledWriter.Write<CompiledRefWrite<T>>(typeof(T), true, Members, _properties, null, p, true)); // also the maps of the array layout: a collection with SerializeProperties
      return write(c, ref value, keys, infos, p, out int end);
    }

    public override void WriteUnrolled(WriteContext c, ref T value, FullPropertyInfo[] infos, GeneratedWritePlan p)
    {
      _unrolled(c, ref value, infos, p);
    }
  }

  /// <summary>
  /// The ShouldSerialize methods a compiled writer calls itself (FilterShouldSerialize, see <see cref="Serializer.TypedShouldSerializeFilter"/>).
  /// </summary>
  internal sealed class ShouldSerializeCalls
  {
    /// <summary>
    /// Per member (null for the members without one).
    /// </summary>
    internal readonly MethodInfo[] Methods;

    /// <summary>
    /// FilterDefaultValues is asked before the method (<see cref="DefaultsThenShouldSerializeFilter"/>).
    /// </summary>
    internal readonly bool DefaultsFirst;

    internal ShouldSerializeCalls(MethodInfo[] methods, bool defaultsFirst)
    {
      Methods = methods;
      DefaultsFirst = defaultsFirst;
    }
  }

  /// <summary>
  /// Writers compiled per <see cref="ObjectPlan"/> at runtime: the code of the source generator (<see cref="GeneratedObjectCode{T}"/>) as expression trees, for the plan's properties and options,
  /// so the per property costs of the runtime's plans go (a getter delegate and two virtual calls per property: <see cref="PropWriter{TTarget, TValue, TFilter}"/> → <see cref="ValueHandler{T}"/>).
  /// <para>A member per property of the plan, in its order: the ids of <see cref="GeneratedWritePlan"/> are the positions. Values of the types the generated code writes itself are written here
  /// (Serializer.Fits holds by construction: the kinds come from the same type infos), the others go to the runtime's handler of their type (<see cref="GeneratedWritePlan.Value{T}"/>,
  /// also a property with [DefaultValue]: its boxed filters), properties without an accessible instance getter to the property's writer (<see cref="GeneratedWritePlan.Runtime"/>).</para>
  /// <para>Per plan rather than per type: the positions and FilterDefaultValues are constants, the code has no loop or switch over the members (one method per layout, with the code of each property once).</para>
  /// </summary>
  internal static class CompiledWriter
  {
    /// <summary>
    /// Where expressions are compiled to IL that the JIT compiles: not where the runtime interprets them (NativeAOT; Mono's interpreter on WebAssembly, where the plans' delegates are faster).
    /// </summary>
    internal static readonly bool Supported = IsSupported();

    /// <summary>
    /// Why the expressions are safe: compiled only where the runtime compiles dynamic code, over the plan's properties (kept as the model types are) and LsMsgPack's own public members.
    /// </summary>
    private const string Reason = "Only where the runtime compiles dynamic code (CompiledWriter.Supported): the expressions reference the plan's properties (kept as the model types are) and LsMsgPack's own members.";

    private static bool IsSupported()
    {
      if (!PropertyAccessor.CanCompile)
        return false;
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
      return System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeCompiled;
#else
      return true; // the runtimes that pick the .NET Standard 2.0 build (.NET Framework) have a JIT
#endif
    }

    private static bool IsByRefLike(Type type)
    {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
      return type.IsByRefLike;
#else
      object[] attributes = type.GetCustomAttributes(false);
      for (int t = 0; t < attributes.Length; t++)
        if (attributes[t].GetType().FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute")
          return true;
      return false;
#endif
    }

    internal static int[] Positions(int count)
    {
      int[] positions = new int[count];
      for (int t = 0; t < count; t++)
        positions[t] = t;
      return positions;
    }

    /// <returns>The code for the plan's properties (without compiled writers yet: <see cref="ICompiledCode.Compile"/>), null when this type is not compiled</returns>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = TrimReasons.Fallback)]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.Fallback)] // CompiledStructCode's struct constraint asks for a parameterless constructor, none is called
    [UnconditionalSuppressMessage("Trimming", "IL2071", Justification = TrimReasons.Fallback)] // the same, as the .NET 10 tools report it
    internal static GeneratedObjectCode Create(Serializer serializer, Type type, FullPropertyInfo[] infos)
    {
      if (type.ContainsGenericParameters || type.IsAbstract || type.IsInterface || IsByRefLike(type))
        return null;

      GeneratedMember[] members = new GeneratedMember[infos.Length];
      MemberInfo[] properties = new MemberInfo[infos.Length];
      MethodInfo[] calls = null;
      for (int t = 0; t < infos.Length; t++)
      {
        MemberInfo member = infos[t].Member;
        Type propertyType = infos[t].DeclaredType;
        bool canWrite;
        if (member is FieldInfo field)
          canWrite = !field.IsStatic; // read directly (expressions skip visibility)
        else
        {
          MethodInfo get = ((PropertyInfo)member).GetGetMethod(true);
          canWrite = get != null && !get.IsStatic && get.GetParameters().Length == 0;
        }
        canWrite = canWrite && member.DeclaringType.IsAssignableFrom(type) && !propertyType.IsByRef && !propertyType.IsPointer && !IsByRefLike(propertyType);
        Type underlying = Nullable.GetUnderlyingType(propertyType);
        GeneratedKind kind = canWrite && !infos[t].CustomAttributes.ContainsKey(nameof(DefaultValueAttribute)) ? KindOf(serializer, underlying ?? propertyType) : GeneratedKind.None;
        if (serializer.CallsShouldSerialize(infos[t]))
        {
          // Called here when this code writes the value itself and the property writer would call it typed; otherwise the runtime's PropWriter calls it (GeneratedWritePlan.Value has no owner)
          MethodInfo call = kind != GeneratedKind.None && !type.IsValueType && serializer.TypedShouldSerializeFilter(infos[t], member.DeclaringType) != null
            ? ((Delegate)infos[t].Attributes.ShouldSerialize.Typed).Method : null;
          if (call is null)
          {
            canWrite = false;
            kind = GeneratedKind.None;
          }
          else
          {
            if (calls is null)
              calls = new MethodInfo[infos.Length];
            calls[t] = call;
          }
        }
        members[t] = new GeneratedMember(member.DeclaringType, member.Name, propertyType, kind, underlying != null && kind != GeneratedKind.None, canWrite, false);
        properties[t] = member;
      }

      Type codeType = (type.IsValueType ? typeof(CompiledStructCode<>) : typeof(CompiledObjectCode<>)).MakeGenericType(type);
      return (GeneratedObjectCode)Activator.CreateInstance(codeType, new object[] { members, properties, calls is null ? null : new ShouldSerializeCalls(calls, serializer.ShouldSerializeDefaultsFirst) });
    }

    /// <summary>
    /// The kind the generated code would have for the type, when these options write it that way (Serializer.Fits).
    /// </summary>
    internal static GeneratedKind KindOf(Serializer serializer, Type type)
    {
      WriteTypeInfo info = serializer.GetTypeInfo(type);
      switch (info.Kind)
      {
        case TypeKind.String: return GeneratedKind.String;
        case TypeKind.Bool: return GeneratedKind.Boolean;
        case TypeKind.SByte: return GeneratedKind.SByte;
        case TypeKind.Int16: return GeneratedKind.Int16;
        case TypeKind.Int32: return GeneratedKind.Int32;
        case TypeKind.Int64: return GeneratedKind.Int64;
        case TypeKind.Byte: return GeneratedKind.Byte;
        case TypeKind.UInt16: return GeneratedKind.UInt16;
        case TypeKind.UInt32: return GeneratedKind.UInt32;
        case TypeKind.UInt64: return GeneratedKind.UInt64;
        case TypeKind.Single: return GeneratedKind.Single;
        case TypeKind.Double: return GeneratedKind.Double;
        case TypeKind.Char: return GeneratedKind.Char;
        case TypeKind.Guid:
        case TypeKind.GuidString: return GeneratedKind.Guid; // in the plan's format (GeneratedCapabilities.WriteFormats)
        case TypeKind.DecimalString: return GeneratedKind.Decimal;
        case TypeKind.DateTime: return GeneratedKind.DateTime;
        case TypeKind.TimeSpan: return GeneratedKind.TimeSpan;
        case TypeKind.Enum: return GeneratedKind.Enum;
        case TypeKind.Extension: return info.Extension != null && info.Extension.GetType() == typeof(DecimalExtension) ? GeneratedKind.Decimal : GeneratedKind.None;
        default: return GeneratedKind.None;
      }
    }

    /// <summary>
    /// <see cref="GeneratedObjectCode{T}.Write"/>, one delegate per kind of call (the keys are known per object, not tested per property): map entries with keys (<paramref name="map"/>),
    /// or array items without (nil for a value the filters leave out, trimming with <see cref="GeneratedWritePlan.Trim"/>).
    /// </summary>
    /// <param name="map">The delegate is only called with keys</param>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Reason)]
    internal static TDelegate Write<TDelegate>(Type type, bool byRef, GeneratedMember[] members, MemberInfo[] properties, ShouldSerializeCalls calls, GeneratedWritePlan plan, bool map) where TDelegate : Delegate
    {
      Builder b = new Builder(type, byRef, members, properties, calls, plan);
      ParameterExpression keys = Expression.Parameter(typeof(byte[][]), "keys");
      ParameterExpression end = Expression.Parameter(typeof(int).MakeByRefType(), "end");
      ParameterExpression count = Expression.Variable(typeof(int), "count");
      ParameterExpression last = Expression.Variable(typeof(int), "last");
      ParameterExpression start = Expression.Variable(typeof(int), "start");
      Expression pos = Expression.Field(b.W, nameof(MsgPackWriter.Pos));
      Expression noKey = Expression.Constant(null, typeof(byte[]));
      bool trim = plan.Trim && !map;

      List<Expression> body = new List<Expression>();
      body.Add(Expression.Assign(b.W, Expression.Field(b.C, nameof(WriteContext.W))));
      body.Add(Expression.Assign(count, Expression.Constant(0)));
      body.Add(Expression.Assign(end, pos));
      if (trim)
        body.Add(Expression.Assign(last, Expression.Constant(0)));
      int[] ids = plan.Ids;
      for (int t = 0; t < ids.Length; t++)
      {
        if (map)
        {
          body.Add(Expression.IfThen(b.Member(t, ids[t], Expression.ArrayIndex(keys, Expression.Constant(t))), Expression.PreIncrementAssign(count)));
          continue;
        }
        if (!trim)
        {
          body.Add(Expression.IfThenElse(b.Member(t, ids[t], noKey), Expression.PreIncrementAssign(count), b.Nil())); // nil for a value the filters leave out
          continue;
        }
        Expression written = Expression.Block(Expression.Assign(last, Expression.Constant(t + 1)), Expression.Assign(end, pos)); // after a value that is not nil
        if (b.WritesItself(ids[t]))
        {
          body.Add(b.Item(t, ids[t], written)); // the code knows where it writes a nil
          continue;
        }
        body.Add(Expression.Assign(start, pos));
        body.Add(Expression.IfThenElse(b.Member(t, ids[t], noKey), Expression.PreIncrementAssign(count), b.Nil()));
        body.Add(Expression.IfThen(Expression.OrElse( // the runtime's writers may write a nil too: nil is the only value of one byte 0xC0
            Expression.NotEqual(pos, Expression.Add(start, Expression.Constant(1))),
            Expression.NotEqual(Expression.ArrayIndex(Expression.Field(b.W, nameof(MsgPackWriter.Buf)), start), Expression.Constant((byte)0xC0))),
          written));
      }
      body.Add(trim ? last : count);

      List<ParameterExpression> variables = b.Variables();
      variables.Add(count);
      if (trim)
      {
        variables.Add(last);
        variables.Add(start);
      }
      return Expression.Lambda<TDelegate>(Expression.Block(typeof(int), variables, body), b.C, b.O, keys, b.Infos, b.P, end).Compile();
    }

    /// <summary>
    /// <see cref="GeneratedObjectCode{T}.WriteUnrolled"/>: the array layout without trimming.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Reason)]
    internal static TDelegate Unrolled<TDelegate>(Type type, bool byRef, GeneratedMember[] members, MemberInfo[] properties, ShouldSerializeCalls calls, GeneratedWritePlan plan) where TDelegate : Delegate
    {
      Builder b = new Builder(type, byRef, members, properties, calls, plan);
      List<Expression> body = new List<Expression>();
      body.Add(Expression.Assign(b.W, Expression.Field(b.C, nameof(WriteContext.W))));
      int[] ids = plan.Ids;
      for (int t = 0; t < ids.Length; t++)
        body.Add(b.Item(t, ids[t]));
      body.Add(Expression.Empty());
      return Expression.Lambda<TDelegate>(Expression.Block(typeof(void), b.Variables(), body), b.C, b.O, b.Infos, b.P).Compile();
    }

    /// <summary>
    /// The expressions of one compiled writer.
    /// </summary>
    private sealed class Builder
    {
      private static readonly MethodInfo RuntimeMethod = typeof(GeneratedWritePlan).GetMethod(nameof(GeneratedWritePlan.Runtime), new Type[] { typeof(int), typeof(WriteContext), typeof(object), typeof(byte[]), typeof(FullPropertyInfo) });
      private static readonly MethodInfo RuntimeRefMethod = FindRuntimeRef();
      private static readonly MethodInfo ValueMethod = typeof(GeneratedWritePlan).GetMethod(nameof(GeneratedWritePlan.Value));

      internal readonly ParameterExpression C = Expression.Parameter(typeof(WriteContext), "c");
      internal readonly ParameterExpression O;
      internal readonly ParameterExpression Infos = Expression.Parameter(typeof(FullPropertyInfo[]), "infos");
      internal readonly ParameterExpression P = Expression.Parameter(typeof(GeneratedWritePlan), "p");
      internal readonly ParameterExpression W = Expression.Variable(typeof(MsgPackWriter), "w");

      private readonly Type _type;
      private readonly bool _byRef;
      private readonly GeneratedMember[] _members;
      private readonly MemberInfo[] _properties; // properties and fields
      private readonly ShouldSerializeCalls _calls;
      private readonly bool _filter;
      private readonly bool _guidString;
      private readonly bool _decimalString;

      /// <summary>
      /// One local per value type (not per property): the method's locals are zeroed per call.
      /// </summary>
      private readonly Dictionary<Type, ParameterExpression> _values = new Dictionary<Type, ParameterExpression>();

      internal Builder(Type type, bool byRef, GeneratedMember[] members, MemberInfo[] properties, ShouldSerializeCalls calls, GeneratedWritePlan plan)
      {
        _type = type;
        _byRef = byRef;
        _members = members;
        _properties = properties;
        _calls = calls;
        _filter = plan.FilterDefaults;
        _guidString = plan.GuidString;
        _decimalString = plan.DecimalString;
        O = Expression.Parameter(byRef ? type.MakeByRefType() : type, "o");
      }

      private static MethodInfo FindRuntimeRef()
      {
        MethodInfo[] methods = typeof(GeneratedWritePlan).GetMethods();
        for (int t = 0; t < methods.Length; t++)
          if (methods[t].Name == nameof(GeneratedWritePlan.Runtime) && methods[t].IsGenericMethodDefinition)
            return methods[t];
        throw new MissingMethodException(nameof(GeneratedWritePlan), nameof(GeneratedWritePlan.Runtime));
      }

      internal List<ParameterExpression> Variables()
      {
        List<ParameterExpression> variables = new List<ParameterExpression>(_values.Values);
        variables.Add(W);
        return variables;
      }

      [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Reason)]
      [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
      internal Expression Nil()
      {
        return Expression.Call(W, nameof(MsgPackWriter.Nil), null);
      }

      private ParameterExpression Value(Type type)
      {
        if (!_values.TryGetValue(type, out ParameterExpression value))
          _values.Add(type, value = Expression.Variable(type, "v"));
        return value;
      }

      private Expression Get(int id)
      {
        return Expression.MakeMemberAccess(O, _properties[id]); // a property or field; a struct by reference: no copy
      }

      [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
      [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = Reason)] // .NET 10 trim tools: Value<T>/Runtime<T> of the plan's types
      private Expression Runtime(int position, Expression key)
      {
        Expression info = Expression.ArrayIndex(Infos, Expression.Constant(position));
        if (_byRef)
          return Expression.Call(P, RuntimeRefMethod.MakeGenericMethod(_type), Expression.Constant(position), C, O, key, info);
        return Expression.Call(P, RuntimeMethod, Expression.Constant(position), C, Expression.Convert(O, typeof(object)), key, info);
      }

      /// <summary>
      /// The value at the position (with its key, unless <paramref name="key"/> is the constant null of an array item), as the switch of the generated Write does.
      /// </summary>
      /// <returns>A bool: false when the filters left the value out (nothing was written)</returns>
      [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Reason)]
      [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
      [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = Reason)] // .NET 10 trim tools: Value<T>/Runtime<T> of the plan's types
      internal Expression Member(int position, int id, Expression key)
      {
        if (id < 0 || !_members[id].CanWrite)
          return Runtime(position, key);
        GeneratedMember member = _members[id];
        if (member.Kind == GeneratedKind.None) // read here, written by the runtime's handler of its type
          return Expression.Call(P, ValueMethod.MakeGenericMethod(member.PropertyType), Expression.Constant(position), C, Get(id), key, Expression.ArrayIndex(Infos, Expression.Constant(position)));

        ParameterExpression v = Value(member.PropertyType);
        Expression written = key is ConstantExpression // the constant null: an array item
          ? Expression.Block(WriteValue(member, v, null), Expression.Constant(true))
          : Expression.Block(Expression.Call(W, nameof(MsgPackWriter.Raw), null, key), WriteValue(member, v, null), Expression.Constant(true));
        Expression leftOut = LeftOut(id, member, v);
        if (leftOut != null)
          written = Expression.Condition(leftOut, Expression.Constant(false), written);
        return Expression.Block(Expression.Assign(v, Get(id)), written);
      }

      /// <summary>
      /// The member's value is written by this code (not by the runtime's writers).
      /// </summary>
      internal bool WritesItself(int id)
      {
        return id >= 0 && _members[id].CanWrite && _members[id].Kind != GeneratedKind.None;
      }

      /// <summary>
      /// The array item at the position, as the generated WriteUnrolled does (and WriteUnrolledTrimmed, with <paramref name="written"/>).
      /// </summary>
      /// <param name="written">After a value that is not nil, null for nothing (only for <see cref="WritesItself"/>)</param>
      internal Expression Item(int position, int id, Expression written = null)
      {
        if (!WritesItself(id))
          return Expression.IfThen(Expression.Not(Member(position, id, Expression.Constant(null, typeof(byte[])))), Nil());
        GeneratedMember member = _members[id];
        ParameterExpression v = Value(member.PropertyType);
        Expression write = WriteValue(member, v, written);
        Expression leftOut = LeftOut(id, member, v);
        if (leftOut != null)
          write = Expression.IfThenElse(leftOut, Nil(), write);
        return Expression.Block(Expression.Assign(v, Get(id)), write);
      }

      /// <summary>
      /// The dynamic filters for the member's value (read before, as the runtime's PropWriter reads it before the filters): FilterDefaultValues and the ShouldSerialize method,
      /// in the order of the typed filter mode (Serializer.TypedShouldSerializeFilter).
      /// </summary>
      /// <returns>A bool, true when the value is left out; null without filters</returns>
      private Expression LeftOut(int id, GeneratedMember member, Expression v)
      {
        Expression omitted = _filter ? Omitted(member, v) : null;
        MethodInfo call = _calls?.Methods[id];
        if (call is null)
          return omitted;
        Expression skip = Expression.Not(Expression.Call(O, call)); // virtual when the method is, as the delegate calls it
        if (omitted is null)
          return skip;
        return _calls.DefaultsFirst ? Expression.OrElse(omitted, skip) : Expression.OrElse(skip, omitted);
      }

      /// <summary>
      /// FilterDefaultValues for the member's value (Emitter.Omitted, as ValueHandler.IncludeByDefault decides it).
      /// </summary>
      [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Reason)]
      private static Expression Omitted(GeneratedMember member, Expression v)
      {
        if (member.Nullable)
          return Expression.Not(Expression.Property(v, "HasValue"));
        switch (member.Kind)
        {
          case GeneratedKind.String: return Expression.Equal(v, Expression.Constant(null, typeof(string))); // an empty string is not a default value
          case GeneratedKind.Boolean: return Expression.Not(v);
          case GeneratedKind.Decimal: return Expression.Call(typeof(GeneratedIO), nameof(GeneratedIO.IsZero), null, v);
          case GeneratedKind.Guid: return Expression.Equal(v, Expression.Default(typeof(Guid))); // default, not a constant: a Guid constant would be a boxed value in the closure
          case GeneratedKind.DateTime: // DateTime.Equals ignores the Kind
          case GeneratedKind.TimeSpan: return Expression.Equal(Expression.Property(v, "Ticks"), Expression.Constant(0L));
          case GeneratedKind.Enum:
            Type underlying = Enum.GetUnderlyingType(v.Type);
            return Expression.Equal(Expression.Convert(v, underlying), Expression.Default(underlying));
          default: return Expression.Equal(v, Expression.Default(v.Type)); // numbers and char
        }
      }

      /// <summary>
      /// The value as the runtime's handler of its type writes it (Emitter.WriteValue).
      /// </summary>
      /// <param name="written">After a value that is not nil, null for nothing</param>
      [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Reason)]
      [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
      private Expression WriteValue(GeneratedMember member, Expression v, Expression written)
      {
        if (member.Nullable)
        {
          ParameterExpression u = Value(Nullable.GetUnderlyingType(member.PropertyType));
          return Expression.IfThenElse(Expression.Property(v, "HasValue"),
            Expression.Block(Expression.Assign(u, Expression.Call(v, "GetValueOrDefault", null)), WriteValue(member.Kind, u, written)),
            Nil());
        }
        return WriteValue(member.Kind, v, written);
      }

      [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Reason)]
      private Expression WriteValue(GeneratedKind kind, Expression v, Expression written)
      {
        if (kind == GeneratedKind.String)
        {
          Expression write = Writer(nameof(MsgPackWriter.String), v);
          return Expression.IfThenElse(Expression.Equal(v, Expression.Constant(null, typeof(string))), Nil(), written is null ? write : Expression.Block(write, written));
        }
        Expression value = WriteNotNil(kind, v);
        return written is null ? value : Expression.Block(value, written);
      }

      /// <summary>
      /// A value of the kind (never nil).
      /// </summary>
      [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Reason)]
      private Expression WriteNotNil(GeneratedKind kind, Expression v)
      {
        switch (kind)
        {
          case GeneratedKind.Boolean: return Writer(nameof(MsgPackWriter.Bool), v);
          case GeneratedKind.SByte: return Writer(nameof(MsgPackWriter.SByte), v);
          case GeneratedKind.Int16: return Writer(nameof(MsgPackWriter.Int16), v);
          case GeneratedKind.Int32: return Writer(nameof(MsgPackWriter.Int32), v);
          case GeneratedKind.Int64: return Writer(nameof(MsgPackWriter.Int64), v);
          case GeneratedKind.Byte: return Writer(nameof(MsgPackWriter.UInt8), v);
          case GeneratedKind.UInt16: return Writer(nameof(MsgPackWriter.UInt16), v);
          case GeneratedKind.UInt32: return Writer(nameof(MsgPackWriter.UInt32), v);
          case GeneratedKind.UInt64: return Writer(nameof(MsgPackWriter.UInt64), v);
          case GeneratedKind.Single: return Writer(nameof(MsgPackWriter.Single), v);
          case GeneratedKind.Double: return Writer(nameof(MsgPackWriter.Double), v);
          case GeneratedKind.Char: return Writer(nameof(MsgPackWriter.UInt16), Expression.Convert(v, typeof(ushort))); // as an unsigned 16 bit integer, like MessagePack-CSharp
          case GeneratedKind.Decimal:
            if (_decimalString)
              return Writer(nameof(MsgPackWriter.DecimalString), v);
            return Expression.Call(typeof(GeneratedIO), nameof(GeneratedIO.WriteDecimal), null, W, v, Expression.Field(P, nameof(GeneratedWritePlan.DecimalCode)));
          case GeneratedKind.Guid: return Writer(_guidString ? nameof(MsgPackWriter.GuidString) : nameof(MsgPackWriter.Guid), v);
          case GeneratedKind.DateTime: return Writer(nameof(MsgPackWriter.DateTime), Expression.Call(typeof(GeneratedIO), nameof(GeneratedIO.Utc), null, v, P));
          case GeneratedKind.TimeSpan: return Writer(nameof(MsgPackWriter.Int64), Expression.Property(v, "Ticks"));
          case GeneratedKind.Enum: return EnumWriter(v);
        }
        throw new InvalidOperationException("Unknown kind " + kind);
      }

      private Expression EnumWriter(Expression v)
      {
        Type underlying = Enum.GetUnderlyingType(v.Type);
        string method;
        if (underlying == typeof(sbyte)) method = nameof(MsgPackWriter.SByte);
        else if (underlying == typeof(short)) method = nameof(MsgPackWriter.Int16);
        else if (underlying == typeof(int)) method = nameof(MsgPackWriter.Int32);
        else if (underlying == typeof(long)) method = nameof(MsgPackWriter.Int64);
        else if (underlying == typeof(byte)) method = nameof(MsgPackWriter.UInt8);
        else if (underlying == typeof(ushort)) method = nameof(MsgPackWriter.UInt16);
        else if (underlying == typeof(uint)) method = nameof(MsgPackWriter.UInt32);
        else method = nameof(MsgPackWriter.UInt64);
        return Writer(method, Expression.Convert(v, underlying));
      }

      private Expression Writer(string method, Expression v)
      {
        return Expression.Call(W, typeof(MsgPackWriter).GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { v.Type }, null), v); // GuidString and DecimalString are internal
      }
    }
  }
}
