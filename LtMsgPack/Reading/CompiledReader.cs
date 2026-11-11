using LsMsgPack;
using LsMsgPack.Meta;
using LtMsgPack.Generated;
using LtMsgPack.IO;
using LtMsgPack.Writing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LtMsgPack.Reading
{
  internal delegate void CompiledReadPositional<T>(ReadContext c, T o, PropReader[] props, int items, GeneratedReadPlan p);

  internal delegate bool CompiledReadMap<T>(ReadContext c, T o, ReadPlan plan, PropReader[] bound, int count, GeneratedReadPlan p);

  internal delegate void CompiledRefReadPositional<T>(ReadContext c, ref T o, PropReader[] props, int items, GeneratedReadPlan p);

  internal delegate bool CompiledRefReadMap<T>(ReadContext c, ref T o, ReadPlan plan, PropReader[] bound, int count, GeneratedReadPlan p);

  /// <summary>
  /// The reader of one <see cref="ReadPlan"/> compiled at runtime (see <see cref="CompiledReader"/>).
  /// </summary>
  internal interface ICompiledReader
  {
    /// <summary>
    /// Compiles the reader of a layout (throws when it cannot).
    /// </summary>
    /// <param name="map">The map layout, otherwise arrays</param>
    void Compile(bool map);

    /// <summary>
    /// Whether the reader of the layout is compiled (the tests ask).
    /// </summary>
    bool IsCompiled(bool map);
  }

  /// <summary>
  /// What the compiled readers of a class and of a struct share: the plan and its members, the reader of a layout the plan did not compile yet, and which arrays of properties are in the plan's order.
  /// </summary>
  internal sealed class CompiledReads
  {
    internal readonly Type Type;
    internal readonly CompiledReader.Member[] Members;

    /// <summary>
    /// Maps keyed by names: the key of each property (its name with the header of a string, as the writers write it), null with the indexed schema.
    /// </summary>
    internal readonly byte[][] Keys;
    private readonly PropReader[] _props;
    private readonly bool _atOnce;
    private ICompiledReader _owner;
    private int _positionalAsked;
    private int _mapAsked;

    /// <summary>
    /// The array of properties checked last (the plan's own, or its binding to the schema read last) and how many of its positions are the plan's properties in the plan's order.
    /// </summary>
    private Order _last;

    private sealed class Order
    {
      internal readonly PropReader[] Props;
      internal readonly int Limit;

      internal Order(PropReader[] props, int limit)
      {
        Props = props;
        Limit = limit;
      }
    }

    /// <param name="atOnce">A layout is compiled when its first object is read, on the calling thread (<see cref="LtMsgPackOptions.CompileReadersAfter"/> 0), otherwise on the thread pool</param>
    /// <param name="names">Maps are keyed by names (not with <see cref="MsgPackOptions.PropertyNameCaseInsensitive"/>: ReadPlan.Map stays false then)</param>
    internal CompiledReads(Type type, CompiledReader.Member[] members, PropReader[] props, bool atOnce, bool names)
    {
      Type = type;
      Members = members;
      if (names)
      {
        Keys = new byte[props.Length][];
        for (int t = 0; t < props.Length; t++)
          Keys[t] = CompiledReader.Key(props[t].NameBytes);
      }
      _props = props;
      _atOnce = atOnce;
      _last = new Order(props, props.Length);
    }

    internal void SetOwner(ICompiledReader owner)
    {
      _owner = owner;
    }

    /// <summary>
    /// The compiled code of an array reads the values by position, straight-line: position t is the plan's property t. So it reads the positions of <paramref name="props"/> (the plan's properties,
    /// or their binding to a schema, which is in the order of the writer's class) up to the first one that is another property, or none (a class with other properties).
    /// </summary>
    /// <returns>The number of positions the compiled code reads; the runtime's loop reads the ones after them</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int Limit(PropReader[] props)
    {
      Order last = _last;
      return ReferenceEquals(last.Props, props) ? last.Limit : Check(props);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int Check(PropReader[] props)
    {
      PropReader[] own = _props;
      int limit = 0;
      while (limit < own.Length && limit < props.Length && ReferenceEquals(props[limit], own[limit]))
        limit++;
      _last = new Order(props, limit); // one store: other threads read the pair
      return limit;
    }

    /// <summary>
    /// A layout the plan did not compile (data in the other layout than the options write) is read by the runtime's loop until its reader is compiled: once, a failure keeps the runtime's loop.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void Ask(bool map)
    {
      if (Interlocked.Exchange(ref map ? ref _mapAsked : ref _positionalAsked, 1) != 0)
        return;
      if (_atOnce)
        TryCompile(map);
      else
        ThreadPool.UnsafeQueueUserWorkItem(state => ((CompiledReads)state).TryCompile(map), this); // no ExecutionContext: nothing of the caller's is needed or kept
    }

    private void TryCompile(bool map)
    {
      try
      {
        _owner.Compile(map);
      }
      catch (Exception) // a type or runtime the expressions do not handle: the runtime's loop reads it, as before
      {
      }
    }

    /// <summary>
    /// The layout is compiled by the plan (not asked for again).
    /// </summary>
    internal void Compiled(bool map)
    {
      if (map)
        _mapAsked = 1;
      else
        _positionalAsked = 1;
    }
  }

  /// <summary>
  /// The reader of a class compiled at runtime: what the source generator writes as <see cref="GeneratedObjectCode{T}.ReadPositional"/> and <see cref="GeneratedObjectCode{T}.ReadMap"/>.
  /// Derived from <see cref="GeneratedObjectCode"/> itself: one virtual call and the delegate per object.
  /// </summary>
  internal sealed class CompiledObjectReader<T> : GeneratedObjectCode, ICompiledReader where T : class
  {
    private readonly CompiledReads _reads;
    private CompiledReadPositional<T> _positional;
    private CompiledReadMap<T> _map;

    public CompiledObjectReader(CompiledReads reads) : base(null, null, GeneratedCapabilities.ReadPositional | GeneratedCapabilities.ReadPositionalNil | GeneratedCapabilities.ReadMap | GeneratedCapabilities.ReadFormats)
    {
      _reads = reads;
      reads.SetOwner(this);
    }

    public void Compile(bool map)
    {
      if (map)
        _map = _reads.Keys is null ? CompiledReader.Map<CompiledReadMap<T>>(_reads.Type, false, _reads.Members) : CompiledReader.Names<CompiledReadMap<T>>(_reads.Type, false, _reads.Members, _reads.Keys);
      else
        _positional = CompiledReader.Positional<CompiledReadPositional<T>>(_reads.Type, false, _reads.Members);
      _reads.Compiled(map);
    }

    public bool IsCompiled(bool map)
    {
      return map ? _map != null : _positional != null;
    }

    internal override void ReadPositionalBoxed(ReadContext c, object target, PropReader[] props, int items, GeneratedReadPlan p)
    {
      CompiledReadPositional<T> positional = _positional;
      int direct = 0;
      if (positional is null)
        _reads.Ask(false);
      else
      {
        int limit = _reads.Limit(props);
        direct = items < limit ? items : limit;
        positional(c, Unsafe.As<T>(target), props, direct, p); // an instance of the plan's type
      }
      if (direct < items)
        CompiledReader.Rest(c, target, props, direct, items);
    }

    internal override bool ReadMapBoxed(ReadContext c, object target, ReadPlan plan, PropReader[] bound, int count, GeneratedReadPlan p)
    {
      CompiledReadMap<T> map = _map;
      if (map is null)
      {
        _reads.Ask(true);
        return CompiledReader.Entries(c, target, plan, bound, count);
      }
      return map(c, Unsafe.As<T>(target), plan, bound, count, p);
    }

    internal override bool ReadValueBoxed(ReadContext c, object target, int id, GeneratedReadPlan p)
    {
      return false; // the loops are compiled, not a call per value
    }

    internal override int WriteBoxed(WriteContext c, object value, byte[][] keys, FullPropertyInfo[] infos, GeneratedWritePlan p, out int end)
    {
      throw new NotSupportedException();
    }

    internal override void WriteUnrolledBoxed(WriteContext c, object value, FullPropertyInfo[] infos, GeneratedWritePlan p)
    {
      throw new NotSupportedException();
    }
  }

  /// <summary>
  /// <see cref="CompiledObjectReader{T}"/> for a struct by reference (<see cref="GeneratedStructCode{T}"/>: ObjectReader.ReadByRef and ReadPositionalByRef call it, a struct in a box through its Unboxer).
  /// </summary>
  internal sealed class CompiledStructReader<T> : GeneratedStructCode<T>, ICompiledReader where T : struct
  {
    private readonly CompiledReads _reads;
    private CompiledRefReadPositional<T> _positional;
    private CompiledRefReadMap<T> _map;

    public CompiledStructReader(CompiledReads reads) : base(null, null, GeneratedCapabilities.ReadPositional | GeneratedCapabilities.ReadPositionalNil | GeneratedCapabilities.ReadMap | GeneratedCapabilities.ReadFormats)
    {
      _reads = reads;
      reads.SetOwner(this);
      Unboxer = new StructUnboxer<T, T>(); // what GeneratedValues.Struct gives the generated code: a struct read into a box
    }

    public void Compile(bool map)
    {
      if (map)
        _map = _reads.Keys is null ? CompiledReader.Map<CompiledRefReadMap<T>>(_reads.Type, true, _reads.Members) : CompiledReader.Names<CompiledRefReadMap<T>>(_reads.Type, true, _reads.Members, _reads.Keys);
      else
        _positional = CompiledReader.Positional<CompiledRefReadPositional<T>>(_reads.Type, true, _reads.Members);
      _reads.Compiled(map);
    }

    public bool IsCompiled(bool map)
    {
      return map ? _map != null : _positional != null;
    }

    public override void ReadPositional(ReadContext c, ref T target, PropReader[] props, int items, GeneratedReadPlan p)
    {
      CompiledRefReadPositional<T> positional = _positional;
      int direct = 0;
      if (positional is null)
        _reads.Ask(false);
      else
      {
        int limit = _reads.Limit(props);
        direct = items < limit ? items : limit;
        positional(c, ref target, props, direct, p);
      }
      if (direct < items)
        CompiledReader.Rest(c, ref target, props, direct, items);
    }

    public override bool ReadMap(ReadContext c, ref T target, ReadPlan plan, PropReader[] bound, int count, GeneratedReadPlan p)
    {
      CompiledRefReadMap<T> map = _map;
      if (map is null)
      {
        _reads.Ask(true);
        return CompiledReader.Entries(c, ref target, plan, bound, count);
      }
      return map(c, ref target, plan, bound, count, p);
    }
  }

  /// <summary>
  /// Readers compiled per <see cref="ReadPlan"/> at runtime (<see cref="LtMsgPackOptions.CompileReadersAfter"/>): the code of the source generator's readers as expression trees, so the per property
  /// costs of the runtime's plans go for the values of simple types (a virtual call and a setter delegate per property: <see cref="PropReader{TTarget, TValue}"/>). The counterpart of the compiled writers (CompiledWriter).
  /// <para>Per plan. Arrays are read straight-line: position t is the plan's property t (see <see cref="CompiledReads.Limit"/>). Maps with the indexed schema by a loop over the keys with a switch over
  /// the plan's properties (<see cref="PropReader.CompiledId"/>), as the generated code. Maps keyed by names straight-line, each key compared with the next property's name as constants
  /// (<see cref="Names{TDelegate}"/>; not with PropertyNameCaseInsensitive: ReadPlan.Map is not set then, its rules stay in the runtime's loop). Invoices, A/B on .NET 8: arrays 5.7 -> 4.4 ms,
  /// maps with the schema 5.7 -> 5.3 ms, with names 6.8-6.9 -> 4.7 ms (a loop over the keys with the names compared inline: 6.7 ms; a call per key: 7.9 ms).</para>
  /// <para>A value is read here when it is in the format the reader of its type reads directly (the same calls as that reader makes), otherwise the property's reader reads it from where it starts:
  /// the same values, conversions and errors. Properties of other types (objects, collections...) and the ones without a setter this code can call are read by their readers.</para>
  /// </summary>
  internal static class CompiledReader
  {
    private const string Reason = "Only where the runtime compiles dynamic code (CompiledWriter.Supported): the expressions reference the plan's properties (kept as the model types are) and LtMsgPack's own members.";

    /// <summary>
    /// A property of the plan (by its position).
    /// </summary>
    internal sealed class Member
    {
      /// <summary>
      /// The property or field.
      /// </summary>
      internal readonly MemberInfo Info;

      /// <summary>
      /// How this code reads the value (of the underlying type of a nullable), None when the property's reader reads it.
      /// </summary>
      internal readonly GeneratedKind Kind;
      internal readonly Type Type;

      /// <summary>
      /// A [DefaultValue]: what a nil of the array layout sets is the reader's to decide (PropReader.ReadNil).
      /// </summary>
      internal readonly bool HasDefaultValue;

      internal Member(MemberInfo info, Type type, GeneratedKind kind, bool hasDefaultValue)
      {
        Info = info;
        Type = type;
        Kind = kind;
        HasDefaultValue = hasDefaultValue;
      }
    }

    /// <returns>The code for the plan's properties, with the reader of the options' layout compiled; null when this type is not compiled (nothing to gain: no property this code reads itself)</returns>
    /// <param name="props">The plan's readers (each has its position as <see cref="PropReader.CompiledId"/>)</param>
    /// <exception cref="Exception">The reader could not be compiled</exception>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = TrimReasons.Fallback)]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.Fallback)] // CompiledStructReader's struct constraint asks for a parameterless constructor, none is called
    [UnconditionalSuppressMessage("Trimming", "IL2071", Justification = TrimReasons.Fallback)]
    internal static GeneratedObjectCode Create(Serializer serializer, Type type, FullPropertyInfo[] infos, PropReader[] props)
    {
      if (type.ContainsGenericParameters || type.IsAbstract || type.IsInterface)
        return null;

      Member[] members = new Member[infos.Length];
      bool any = false;
      for (int t = 0; t < infos.Length; t++)
      {
        FullPropertyInfo info = infos[t];
        MemberInfo member = info.Member;
        Type memberType = info.DeclaredType;
        GeneratedKind kind = GeneratedKind.None;
        if (!info.IsComputed && CanSet(member) && member.DeclaringType.IsAssignableFrom(type))
          kind = serializer.ReadKind(memberType);
        any |= kind != GeneratedKind.None;
        members[t] = new Member(member, memberType, kind, info.CustomAttributes.ContainsKey(nameof(DefaultValueAttribute)));
      }
      if (!any)
        return null;

      bool names = !serializer.Options._useInexedSchema;
      CompiledReads reads = new CompiledReads(type, members, props, serializer.CompileReadersAfter == 0, names);
      Type codeType = (type.IsValueType ? typeof(CompiledStructReader<>) : typeof(CompiledObjectReader<>)).MakeGenericType(type);
      GeneratedObjectCode code = (GeneratedObjectCode)Activator.CreateInstance(codeType, new object[] { reads });
      if (serializer.Options._objectLayout == ObjectLayout.Array)
        ((ICompiledReader)code).Compile(false);
      else if (!names || !serializer.Options._propertyNameCaseInsensitive)
        ((ICompiledReader)code).Compile(true);
      return code; // the other layout (also arrays read by a serializer of maps with names) when its first object is read
    }

    /// <summary>
    /// An instance property with a setter or a field that is not readonly: what an expression assigns (whatever its visibility; init-only setters too).
    /// </summary>
    private static bool CanSet(MemberInfo member)
    {
      if (member is FieldInfo field)
        return !field.IsStatic && !field.IsInitOnly && !field.IsLiteral;
      PropertyInfo property = member as PropertyInfo;
      if (property is null || property.GetIndexParameters().Length != 0)
        return false;
      MethodInfo set = property.GetSetMethod(true);
      return set != null && !set.IsStatic;
    }

    #region The runtime's loops (what the compiled code does not read)

    /// <summary>
    /// The values of an array from position <paramref name="from"/> on, as ObjectReader.ReadPositional's loop reads them.
    /// </summary>
    internal static void Rest(ReadContext c, object target, PropReader[] props, int from, int items)
    {
      for (int t = from; t < items; t++)
      {
        if (t >= props.Length || props[t] is null)
          c.R.Skip(c.Depth); // not a property of this type (added by a newer writer, or ignored here)
        else if (c.R.TryReadNil())
          props[t].ReadNil(target);
        else
          props[t].Read(c, target);
      }
    }

    /// <summary>
    /// <see cref="Rest(ReadContext, object, PropReader[], int, int)"/> for a struct by reference.
    /// </summary>
    internal static void Rest<T>(ReadContext c, ref T target, PropReader[] props, int from, int items)
    {
      for (int t = from; t < items; t++)
      {
        if (t >= props.Length || props[t] is null)
          c.R.Skip(c.Depth);
        else if (c.R.TryReadNil())
          GeneratedIO.ReadNil(props[t], ref target);
        else
          GeneratedIO.Read(props[t], c, ref target);
      }
    }

    /// <summary>
    /// The entries of a map, as ObjectReader.Read's loop reads them.
    /// </summary>
    /// <returns>false on a key that loop does not read: the object is read again from its start</returns>
    internal static bool Entries(ReadContext c, object target, ReadPlan plan, PropReader[] bound, int count)
    {
      return Entries(c, target, plan, bound, count, 0);
    }

    /// <param name="next">Keys by name: the property the search for the next key starts at (ReadPlan.FindByName)</param>
    internal static bool Entries(ReadContext c, object target, ReadPlan plan, PropReader[] bound, int count, int next)
    {
      for (int t = 0; t < count; t++)
      {
        if (!GeneratedIO.TryReadKey(c, plan, bound, ref next, out PropReader prop))
          return false;
        if (prop is null)
          c.R.Skip(c.Depth); // not a property of this type (removed, or ignored here)
        else
          prop.Read(c, target);
      }
      return true;
    }

    /// <summary>
    /// <see cref="Entries(ReadContext, object, ReadPlan, PropReader[], int)"/> for a struct by reference.
    /// </summary>
    internal static bool Entries<T>(ReadContext c, ref T target, ReadPlan plan, PropReader[] bound, int count)
    {
      return Entries(c, ref target, plan, bound, count, 0);
    }

    /// <inheritdoc cref="Entries(ReadContext, object, ReadPlan, PropReader[], int, int)"/>
    internal static bool Entries<T>(ReadContext c, ref T target, ReadPlan plan, PropReader[] bound, int count, int next)
    {
      for (int t = 0; t < count; t++)
      {
        if (!GeneratedIO.TryReadKey(c, plan, bound, ref next, out PropReader prop))
          return false;
        if (prop is null)
          c.R.Skip(c.Depth);
        else
          GeneratedIO.Read(prop, c, ref target);
      }
      return true;
    }

    #endregion

    /// <summary>
    /// The values of an array by position (what ObjectReader.ReadPositional's loop and the generated ReadPositional do, for the first <c>items</c> positions, which are the plan's properties in its order):
    /// a nil sets the value the writer leaves out.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Reason)]
    internal static TDelegate Positional<TDelegate>(Type type, bool byRef, Member[] members) where TDelegate : Delegate
    {
      Builder b = new Builder(type, byRef, members);
      ParameterExpression props = Expression.Parameter(typeof(PropReader[]), "props");
      ParameterExpression items = Expression.Parameter(typeof(int), "items");
      LabelTarget done = Expression.Label("done");

      List<Expression> body = new List<Expression>();
      body.Add(Expression.Assign(b.R, Expression.Field(b.C, nameof(ReadContext.R))));
      for (int t = 0; t < members.Length; t++)
      {
        body.Add(Expression.IfThen(Expression.LessThanOrEqual(items, Expression.Constant(t)), Expression.Return(done)));
        Expression prop = Expression.ArrayIndex(props, Expression.Constant(t));
        body.Add(Expression.IfThenElse(b.TryReadNil(), b.Nil(t, prop), b.Value(t, prop, null)));
      }
      body.Add(Expression.Label(done));
      return Expression.Lambda<TDelegate>(Expression.Block(typeof(void), b.Variables(), body), b.C, b.O, props, items, b.P).Compile();
    }

    /// <summary>
    /// The entries of a map with the indexed schema (what ObjectReader.Read's loop and the generated ReadMap do): a loop over the keys with a switch over the properties, as the generated code has it.
    /// A key that is an index into the schema as the writers write it (a positive fixint) is read here, any other key by <see cref="GeneratedIO.TryReadKey"/>: with that call per key
    /// maps were read slower than by the runtime's loop, where the JIT inlines the key's code.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Reason)]
    internal static TDelegate Map<TDelegate>(Type type, bool byRef, Member[] members) where TDelegate : Delegate
    {
      Builder b = new Builder(type, byRef, members);
      ParameterExpression plan = Expression.Parameter(typeof(ReadPlan), "plan");
      ParameterExpression bound = Expression.Parameter(typeof(PropReader[]), "bound");
      ParameterExpression count = Expression.Parameter(typeof(int), "count"); // counted down: the entries left
      ParameterExpression prop = Expression.Variable(typeof(PropReader), "prop");
      ParameterExpression pos = Expression.Variable(typeof(int), "pos");
      ParameterExpression index = Expression.Variable(typeof(int), "index");
      ParameterExpression next = Expression.Variable(typeof(int), "next"); // only for the call: the address of a local keeps it out of the registers
      ParameterExpression other = Expression.Variable(typeof(PropReader), "other");
      LabelTarget end = Expression.Label(typeof(bool), "end");
      LabelTarget again = Expression.Label("again");
      Expression position = Expression.Field(b.R, nameof(MsgPackReader.Pos));
      MethodInfo tryReadKey = typeof(GeneratedIO).GetMethod(nameof(GeneratedIO.TryReadKey));
      MethodInfo skip = typeof(MsgPackReader).GetMethod(nameof(MsgPackReader.Skip));

      // prop = the property of the key; false for a key the runtime's way reads
      Expression otherKey = Expression.Block(
        Expression.IfThen(Expression.Not(Expression.Call(tryReadKey, b.C, plan, bound, next, other)), Expression.Return(end, Expression.Constant(false))),
        Expression.Assign(prop, other));
      Expression key = Expression.Block(
        Expression.Assign(pos, position),
        Expression.IfThenElse(
          Expression.AndAlso(Expression.LessThan(pos, Expression.Field(b.R, nameof(MsgPackReader.End))),
            Expression.AndAlso(Expression.LessThan(Expression.Assign(index, Expression.Convert(Expression.ArrayIndex(Expression.Field(b.R, nameof(MsgPackReader.Buf)), pos), typeof(int))), Expression.ArrayLength(bound)),
              Expression.LessThanOrEqual(index, Expression.Constant(0x7F)))), // a positive fixint: the index of a property of the schema's entry
          Expression.Block(Expression.Assign(position, Expression.Add(pos, Expression.Constant(1))), Expression.Assign(prop, Expression.ArrayIndex(bound, index))),
          otherKey));

      List<SwitchCase> cases = new List<SwitchCase>();
      for (int t = 0; t < members.Length; t++)
        if (members[t].Kind != GeneratedKind.None)
          cases.Add(Expression.SwitchCase(Expression.Block(typeof(void), b.Entry(t, prop)), Expression.Constant(t)));

      List<Expression> loop = new List<Expression>();
      loop.Add(Expression.IfThen(Expression.Equal(count, Expression.Constant(0)), Expression.Return(end, Expression.Constant(true))));
      loop.Add(Expression.PreDecrementAssign(count));
      loop.Add(key);
      loop.Add(Expression.IfThen(Expression.Equal(prop, Expression.Constant(null, typeof(PropReader))),
        Expression.Block(Expression.Call(b.R, skip, Expression.Field(b.C, nameof(ReadContext.Depth))), Expression.Continue(again)))); // not a property of this type (removed, or ignored here)
      loop.Add(Expression.Switch(typeof(void), Expression.Field(prop, nameof(PropReader.CompiledId)), Expression.Block(typeof(void), b.Runtime(prop)), null, cases));

      List<Expression> body = new List<Expression>();
      body.Add(Expression.Assign(b.R, Expression.Field(b.C, nameof(ReadContext.R))));
      body.Add(Expression.Assign(next, Expression.Constant(0)));
      body.Add(Expression.Loop(Expression.Block(typeof(void), loop), null, again));
      body.Add(Expression.Label(end, Expression.Constant(true)));

      List<ParameterExpression> variables = b.Variables();
      variables.Add(prop);
      variables.Add(pos);
      variables.Add(index);
      variables.Add(next);
      variables.Add(other);
      return Expression.Lambda<TDelegate>(Expression.Block(typeof(bool), variables, body), b.C, b.O, plan, bound, count, b.P).Compile();
    }

    /// <summary>
    /// The entries of a map keyed by names, straight-line: the code of property t reads the next entry when its key is the name of property t (the writers write the keys in the plan's order,
    /// FilterDefaultValues leaves some out), the key compared as constants (<see cref="GeneratedIO.KeyIs"/>: no call and no loop per key; the source generator writes the same code). The entries after the first key that is not the next property's
    /// (another order, a property this class does not have, another string header, a key that makes the object be read again) are read by the runtime's loop (<see cref="Entries(ReadContext, object, ReadPlan, PropReader[], int, int)"/>).
    /// </summary>
    /// <param name="keys">The key of each property (<see cref="Key"/>)</param>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Reason)]
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = Reason)]
    internal static TDelegate Names<TDelegate>(Type type, bool byRef, Member[] members, byte[][] keys) where TDelegate : Delegate
    {
      Builder b = new Builder(type, byRef, members);
      ParameterExpression plan = Expression.Parameter(typeof(ReadPlan), "plan");
      ParameterExpression bound = Expression.Parameter(typeof(PropReader[]), "bound");
      ParameterExpression count = Expression.Parameter(typeof(int), "count"); // counted down: the entries left
      ParameterExpression next = Expression.Variable(typeof(int), "next");
      LabelTarget end = Expression.Label(typeof(bool), "end");
      Expression position = Expression.Field(b.R, nameof(MsgPackReader.Pos));
      Expression props = Expression.Field(plan, nameof(ReadPlan.Props));
      MethodInfo entries = byRef
        ? typeof(CompiledReader).GetMethods(BindingFlags.Static | BindingFlags.NonPublic).First(m => m.Name == nameof(Entries) && m.IsGenericMethodDefinition && m.GetParameters().Length == 6).MakeGenericMethod(type)
        : typeof(CompiledReader).GetMethod(nameof(Entries), BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(ReadContext), typeof(object), typeof(ReadPlan), typeof(PropReader[]), typeof(int), typeof(int) }, null);
      MethodInfo keyIs = typeof(GeneratedIO).GetMethod(nameof(GeneratedIO.KeyIs));
      MethodInfo longKeyIs = typeof(GeneratedIO).GetMethod(nameof(GeneratedIO.LongKeyIs));

      List<Expression> body = new List<Expression>();
      body.Add(Expression.Assign(b.R, Expression.Field(b.C, nameof(ReadContext.R))));
      body.Add(Expression.Assign(next, Expression.Constant(0)));
      body.Add(Expression.IfThen(Expression.Field(b.C, nameof(ReadContext.Schema)), Expression.Return(end, Expression.Call(entries, b.C, b.O, plan, bound, count, next)))); // not data this code is compiled for
      for (int t = 0; t < members.Length; t++)
      {
        byte[] key = keys[t];
        Expression matches = key.Length <= 32
          ? Expression.Call(keyIs, b.R, Expression.Constant(key.Length), Expression.Constant(Word(key, 0)), Expression.Constant(Word(key, 8)), Expression.Constant(Word(key, 16)), Expression.Constant(Word(key, key.Length - Math.Min(8, WordSize(key.Length)))))
          : Expression.Call(longKeyIs, b.R, Expression.Constant(key));
        body.Add(Expression.IfThen(Expression.Equal(count, Expression.Constant(0)), Expression.Return(end, Expression.Constant(true))));
        body.Add(Expression.IfThen(matches, Expression.Block(typeof(void),
          Expression.AddAssign(position, Expression.Constant(key.Length)),
          Expression.PreDecrementAssign(count),
          Expression.Assign(next, Expression.Constant(t + 1)),
          Expression.Block(typeof(void), b.Entry(t, Expression.ArrayIndex(props, Expression.Constant(t)))))));
      }
      body.Add(Expression.Label(end, Expression.OrElse(Expression.Equal(count, Expression.Constant(0)), Expression.Call(entries, b.C, b.O, plan, bound, count, next))));

      List<ParameterExpression> variables = b.Variables();
      variables.Add(next);
      return Expression.Lambda<TDelegate>(Expression.Block(typeof(bool), variables, body), b.C, b.O, plan, bound, count, b.P).Compile();
    }

    /// <summary>
    /// A key as the writers write a property's name: the header of a string in its smallest format (MsgPackWriter), then the UTF-8 bytes.
    /// </summary>
    internal static byte[] Key(byte[] name)
    {
      int header = name.Length < 32 ? 1 : name.Length <= byte.MaxValue ? 2 : name.Length <= ushort.MaxValue ? 3 : 5;
      byte[] key = new byte[header + name.Length];
      if (header == 1)
        key[0] = (byte)(0xA0 | name.Length);
      else if (header == 2)
      {
        key[0] = 0xD9;
        key[1] = (byte)name.Length;
      }
      else if (header == 3)
      {
        key[0] = 0xDA;
        key[1] = (byte)(name.Length >> 8);
        key[2] = (byte)name.Length;
      }
      else
      {
        key[0] = 0xDB;
        for (int t = 0; t < 4; t++)
          key[1 + t] = (byte)(name.Length >> (24 - 8 * t));
      }
      Buffer.BlockCopy(name, 0, key, header, name.Length);
      return key;
    }

    /// <summary>
    /// The size of the words <see cref="GeneratedIO.KeyIs"/> compares for a key of <paramref name="length"/> bytes.
    /// </summary>
    private static int WordSize(int length)
    {
      return length >= 8 ? 8 : length >= 4 ? 4 : length >= 2 ? 2 : 1;
    }

    /// <summary>
    /// The word of <see cref="WordSize"/> bytes of the key at <paramref name="offset"/>, little-endian (as <see cref="GeneratedIO.KeyIs"/> reads the data; LtMsgPack.Generator's Emitter.Word), 0 past the key's end.
    /// </summary>
    private static ulong Word(byte[] key, int offset)
    {
      int size = WordSize(key.Length);
      if (offset + size > key.Length)
        return 0;
      ulong word = 0;
      for (int t = size - 1; t >= 0; t--)
        word = (word << 8) | key[offset + t];
      return word;
    }

    /// <summary>
    /// The expressions of one compiled reader.
    /// </summary>
    private sealed class Builder
    {
      private static readonly MethodInfo ReadMethod = typeof(PropReader).GetMethod(nameof(PropReader.Read), new Type[] { typeof(ReadContext), typeof(object) });
      private static readonly MethodInfo ReadNilMethod = typeof(PropReader).GetMethod(nameof(PropReader.ReadNil), new Type[] { typeof(object) });
      private static readonly MethodInfo ReadRefMethod = typeof(GeneratedIO).GetMethod(nameof(GeneratedIO.Read));
      private static readonly MethodInfo ReadNilRefMethod = typeof(GeneratedIO).GetMethod(nameof(GeneratedIO.ReadNil));
      private static readonly MethodInfo TryReadNilMethod = typeof(MsgPackReader).GetMethod(nameof(MsgPackReader.TryReadNil));
      private static readonly MethodInfo TryReadDecimalMethod = typeof(GeneratedIO).GetMethod(nameof(GeneratedIO.TryReadDecimal), new Type[] { typeof(MsgPackReader), typeof(GeneratedReadPlan), typeof(decimal).MakeByRefType() });
      private static readonly MethodInfo TryReadGuidMethod = typeof(GeneratedIO).GetMethod(nameof(GeneratedIO.TryReadGuid), new Type[] { typeof(MsgPackReader), typeof(GeneratedReadPlan), typeof(Guid).MakeByRefType() });

      internal readonly ParameterExpression C = Expression.Parameter(typeof(ReadContext), "c");
      internal readonly ParameterExpression O;
      internal readonly ParameterExpression P = Expression.Parameter(typeof(GeneratedReadPlan), "p");
      internal readonly ParameterExpression R = Expression.Variable(typeof(MsgPackReader), "r");

      private readonly Type _type;
      private readonly bool _byRef;
      private readonly Member[] _members;
      private readonly ParameterExpression _start = Expression.Variable(typeof(int), "start");
      private bool _usesStart;

      /// <summary>
      /// One local per value type (not per property): the method's locals are zeroed per call.
      /// </summary>
      private readonly Dictionary<Type, ParameterExpression> _values = new Dictionary<Type, ParameterExpression>();

      internal Builder(Type type, bool byRef, Member[] members)
      {
        _type = type;
        _byRef = byRef;
        _members = members;
        O = Expression.Parameter(byRef ? type.MakeByRefType() : type, "o");
      }

      internal List<ParameterExpression> Variables()
      {
        List<ParameterExpression> variables = new List<ParameterExpression>(_values.Values);
        variables.Add(R);
        if (_usesStart)
          variables.Add(_start);
        return variables;
      }

      private ParameterExpression Local(Type type)
      {
        if (!_values.TryGetValue(type, out ParameterExpression value))
          _values.Add(type, value = Expression.Variable(type, "v"));
        return value;
      }

      internal Expression TryReadNil()
      {
        return Expression.Call(R, TryReadNilMethod);
      }

      /// <summary>
      /// The property's reader reads the value (PropReader.Read; a struct by reference).
      /// </summary>
      [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
      [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = Reason)]
      internal Expression Runtime(Expression prop)
      {
        if (_byRef)
          return Expression.Call(ReadRefMethod.MakeGenericMethod(_type), prop, C, O);
        return Expression.Call(prop, ReadMethod, C, O);
      }

      /// <summary>
      /// The property's reader sets the value of a nil of the array layout (PropReader.ReadNil).
      /// </summary>
      [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
      [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = Reason)]
      private Expression RuntimeNil(Expression prop)
      {
        if (_byRef)
          return Expression.Call(ReadNilRefMethod.MakeGenericMethod(_type), prop, O);
        return Expression.Call(prop, ReadNilMethod, O);
      }

      private Expression Set(Member member, Expression value)
      {
        if (value.Type != member.Type)
          value = Expression.Convert(value, member.Type); // into a nullable
        return Expression.Assign(Expression.MakeMemberAccess(O, member.Info), value); // a property or field; a struct by reference
      }

      /// <summary>
      /// A nil in the array layout (it is read): the value the writer leaves out, which without a [DefaultValue] is the default of the type whatever the options are (FullPropertyInfo.NilValue).
      /// </summary>
      internal Expression Nil(int position, Expression prop)
      {
        Member member = _members[position];
        if (member.Kind == GeneratedKind.None || member.HasDefaultValue)
          return RuntimeNil(prop);
        return Set(member, Expression.Default(member.Type));
      }

      /// <summary>
      /// The value of an entry of a map: as <see cref="Value"/>, a nil sets the default of the type (as the readers of the types return it for a nil).
      /// </summary>
      internal Expression Entry(int position, Expression prop)
      {
        Member member = _members[position];
        if (member.Kind == GeneratedKind.None)
          return Runtime(prop);
        Expression nil = Set(member, Expression.Default(member.Type));
        if (Nullable.GetUnderlyingType(member.Type) != null)
          return Expression.IfThenElse(TryReadNil(), nil, Value(position, prop, null)); // usually null: before the value
        return Value(position, prop, Expression.IfThenElse(TryReadNil(), nil, Runtime(prop)));
      }

      /// <summary>
      /// The value at the reader's position (not a nil of the array layout): set when it is in the format the reader of its type reads directly, otherwise <paramref name="otherwise"/>
      /// (the position is where the value starts).
      /// </summary>
      /// <param name="otherwise">null: the property's reader reads it</param>
      [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Reason)]
      internal Expression Value(int position, Expression prop, Expression otherwise)
      {
        Member member = _members[position];
        if (member.Kind == GeneratedKind.None)
          return Runtime(prop);
        if (otherwise is null)
          otherwise = Runtime(prop);

        Type type = Nullable.GetUnderlyingType(member.Type) ?? member.Type;
        switch (member.Kind)
        {
          case GeneratedKind.String: return Fast(member, nameof(MsgPackReader.TryReadString), typeof(string), null, otherwise);
          case GeneratedKind.Boolean: return Fast(member, nameof(MsgPackReader.TryReadBool), typeof(bool), null, otherwise);
          case GeneratedKind.Single: return Fast(member, nameof(MsgPackReader.TryReadSingle), typeof(float), null, otherwise);
          case GeneratedKind.Double: return Fast(member, nameof(MsgPackReader.TryReadDouble), typeof(double), null, otherwise);
          case GeneratedKind.Int64: return Fast(member, nameof(MsgPackReader.TryReadInt64), typeof(long), null, otherwise);
          case GeneratedKind.UInt64: return Fast(member, nameof(MsgPackReader.TryReadUInt64), typeof(ulong), null, otherwise);
          case GeneratedKind.DateTime: return Fast(member, nameof(MsgPackReader.TryReadDateTime), typeof(DateTime), null, otherwise);
          case GeneratedKind.TimeSpan: return Fast(member, nameof(MsgPackReader.TryReadInt64), typeof(long), v => Expression.New(typeof(TimeSpan).GetConstructor(new Type[] { typeof(long) }), v), otherwise);
          case GeneratedKind.Enum: // values out of range wrap around (EnumReader)
            return Fast(member, nameof(MsgPackReader.TryReadInt64), typeof(long), v => Expression.Convert(Expression.Convert(v, Enum.GetUnderlyingType(type)), type), otherwise);
          case GeneratedKind.Decimal: // the built-in extension, or the strings of DecimalFormat.String
            {
              ParameterExpression v = Local(typeof(decimal));
              return Expression.IfThenElse(Expression.Call(TryReadDecimalMethod, R, P, v), Set(member, v), otherwise);
            }
          case GeneratedKind.Guid: // bin 16, or also the strings of GuidFormat.String
            {
              ParameterExpression v = Local(typeof(Guid));
              return Expression.IfThenElse(Expression.Call(TryReadGuidMethod, R, P, v), Set(member, v), otherwise);
            }
          case GeneratedKind.SByte: return Ranged(member, type, sbyte.MinValue, sbyte.MaxValue, otherwise);
          case GeneratedKind.Int16: return Ranged(member, type, short.MinValue, short.MaxValue, otherwise);
          case GeneratedKind.Int32: return Ranged(member, type, int.MinValue, int.MaxValue, otherwise);
          case GeneratedKind.Byte: return Ranged(member, type, byte.MinValue, byte.MaxValue, otherwise);
          case GeneratedKind.UInt16: return Ranged(member, type, ushort.MinValue, ushort.MaxValue, otherwise);
          case GeneratedKind.UInt32: return Ranged(member, type, uint.MinValue, uint.MaxValue, otherwise);
          case GeneratedKind.Char: return Ranged(member, type, char.MinValue, char.MaxValue, otherwise);
        }
        throw new InvalidOperationException("Unknown kind " + member.Kind);
      }

      /// <summary>
      /// A typed read of <see cref="MsgPackReader"/>, which leaves the position where it was when the value is in another format.
      /// </summary>
      /// <param name="convert">Makes the member's value of the value read, null when it is the value</param>
      [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Reason)]
      private Expression Fast(Member member, string method, Type read, Func<Expression, Expression> convert, Expression otherwise)
      {
        ParameterExpression v = Local(read);
        MethodInfo tryRead = typeof(MsgPackReader).GetMethod(method, new Type[] { read.MakeByRefType() });
        return Expression.IfThenElse(Expression.Call(R, tryRead, v), Set(member, convert is null ? v : convert(v)), otherwise);
      }

      /// <summary>
      /// An integer read as a long and checked (IntegerReader): out of range the position is restored, the property's reader reports the overflow as it does.
      /// </summary>
      [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Reason)]
      private Expression Ranged(Member member, Type type, long min, long max, Expression otherwise)
      {
        _usesStart = true;
        ParameterExpression v = Local(typeof(long));
        MethodInfo tryRead = typeof(MsgPackReader).GetMethod(nameof(MsgPackReader.TryReadInt64), new Type[] { typeof(long).MakeByRefType() });
        Expression pos = Expression.Field(R, nameof(MsgPackReader.Pos));
        Expression inRange = Expression.AndAlso(Expression.Call(R, tryRead, v),
          Expression.AndAlso(Expression.GreaterThanOrEqual(v, Expression.Constant(min)), Expression.LessThanOrEqual(v, Expression.Constant(max))));
        return Expression.Block(
          Expression.Assign(_start, pos),
          Expression.IfThenElse(inRange, Set(member, Expression.Convert(v, type)), Expression.Block(Expression.Assign(pos, _start), otherwise)));
      }
    }
  }
}
