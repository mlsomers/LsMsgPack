using LsMsgPack.Meta;
using LtMsgPack.IO;
using LtMsgPack.Reading;
using LtMsgPack.Writing;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LtMsgPack.Generated
{
  /// <summary>
  /// How the generated code writes and reads a member itself (anything else is done by the runtime: <see cref="None"/>).
  /// The runtime checks per serializer that the options write the type that way (e.g. Guids as bin, decimals with the built-in extension), otherwise the member is done by the runtime.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public enum GeneratedKind
  {
    None = 0,
    String, Boolean, SByte, Int16, Int32, Int64, Byte, UInt16, UInt32, UInt64, Single, Double, Char,
    Decimal, Guid, DateTime, TimeSpan,

    /// <summary>An enum, written as its underlying integer type</summary>
    Enum
  }

  /// <summary>
  /// A member of the generated writer and reader of a type (in the order of their ids).
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public sealed class GeneratedMember
  {
    internal readonly Type DeclaringType;
    internal readonly string Name;
    internal readonly Type PropertyType;
    internal readonly GeneratedKind Kind;
    internal readonly bool Nullable;
    internal readonly bool CanWrite;
    internal readonly bool CanRead;

    /// <param name="kind">Of the type, or of the underlying type of a Nullable (<paramref name="nullable"/>)</param>
    /// <param name="canWrite">The generated code writes it (its getter is accessible)</param>
    /// <param name="canRead">The generated code sets it (its setter is accessible)</param>
    public GeneratedMember(Type declaringType, string name, Type propertyType, GeneratedKind kind, bool nullable, bool canWrite, bool canRead)
    {
      DeclaringType = declaringType;
      Name = name;
      PropertyType = propertyType;
      Kind = kind;
      Nullable = nullable;
      CanWrite = canWrite;
      CanRead = canRead;
    }
  }

  /// <summary>
  /// What a generated writer and reader provides (later versions of the generator may add some: the runtime only calls what is there).
  /// </summary>
  [Flags]
  [EditorBrowsable(EditorBrowsableState.Never)]
  public enum GeneratedCapabilities
  {
    None = 0,

    /// <summary><see cref="GeneratedObjectCode{T}.Write"/></summary>
    Write = 1,

    /// <summary><see cref="GeneratedObjectCode{T}.WriteUnrolled"/></summary>
    WriteUnrolled = 2,

    /// <summary><see cref="GeneratedObjectCode{T}.ReadPositional"/></summary>
    ReadPositional = 4,

    /// <summary><see cref="GeneratedObjectCode{T}.ReadValue"/></summary>
    ReadValue = 8,

    /// <summary>
    /// <see cref="GeneratedObjectCode{T}.ReadPositional"/> sets the value of a nil (<see cref="Reading.PropReader.ReadNil"/>, version 2). Without it (version 1: a nil left the property as the constructor made it)
    /// the runtime reads the array layout itself.
    /// </summary>
    ReadPositionalNil = 16,

    /// <summary>
    /// <see cref="GeneratedStructCode{T}.IsDefault"/>: the default check of FilterDefaultValues for a struct (version 3). Without it the runtime compares (StructDefault).
    /// </summary>
    IsDefault = 32,

    /// <summary>
    /// <see cref="GeneratedObjectCode{T}.ReadMap"/> (version 3): the loop over the values of a map, instead of a call to <see cref="GeneratedObjectCode{T}.ReadValue"/> per value.
    /// </summary>
    ReadMap = 64,

    /// <summary>
    /// <see cref="GeneratedObjectCode{T}.WriteUnrolledMap"/> (version 4): the map layout written in sequence, when the runtime's properties are <see cref="GeneratedObjectCode.UnrolledIds"/>.
    /// </summary>
    WriteUnrolledMap = 128,

    /// <summary>
    /// The writers write Guids and decimals in the format of the plan (<see cref="GeneratedWritePlan.GuidString"/>, <see cref="GeneratedWritePlan.DecimalString"/>; version 4). Without it the runtime writes
    /// the members of a serializer with <c>GuidFormat.String</c> or <c>DecimalFormat.String</c>.
    /// </summary>
    WriteFormats = 256,

    /// <summary>
    /// <see cref="GeneratedObjectCode{T}.WriteUnrolledTrimmed"/> (version 4): the array layout with TrimTrailingNulls in sequence, when the runtime's properties are <see cref="GeneratedObjectCode.UnrolledIds"/>.
    /// </summary>
    WriteUnrolledTrimmed = 512,

    /// <summary>
    /// The readers read Guids and decimals as strings themselves when the serializer reads them as such (<see cref="GeneratedReadPlan.GuidString"/>, <see cref="GeneratedReadPlan.DecimalString"/>;
    /// <see cref="GeneratedIO.TryReadGuid(MsgPackReader, GeneratedReadPlan, out Guid)"/>, <see cref="GeneratedIO.TryReadDecimal(MsgPackReader, GeneratedReadPlan, out decimal)"/>; version 4).
    /// Without it the runtime reads the members of a serializer with <c>GuidFormat.String</c> or <c>DecimalFormat.String</c>.
    /// </summary>
    ReadFormats = 1024
  }

  /// <summary>
  /// The generated writer and reader of a class (the per property part of the runtime's ObjectPlan and ObjectReader).
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public abstract class GeneratedObjectCode
  {
    internal readonly GeneratedMember[] Members;

    /// <summary>
    /// The member ids in the order the unrolled writer writes them (the runtime's order of the properties as the generator predicts it, without the properties the default filters leave out).
    /// </summary>
    internal readonly int[] UnrolledIds;
    internal readonly GeneratedCapabilities Capabilities;

    internal GeneratedObjectCode(GeneratedMember[] members, int[] unrolledIds, GeneratedCapabilities capabilities)
    {
      Members = members ?? new GeneratedMember[0];
      UnrolledIds = unrolledIds ?? new int[0];
      Capabilities = capabilities;
    }

    /// <summary>
    /// <see cref="GeneratedObjectCode{T}.Write"/>, or for a map (<paramref name="keys"/> given) <see cref="GeneratedObjectCode{T}.WriteUnrolledMap"/> when <see cref="GeneratedWritePlan.UnrolledMap"/>.
    /// Decided here, not in ObjectPlan.Write, whose size decides whether it is inlined into the loops of collections (a dictionary of objects with compiled writers 7% slower).
    /// </summary>
    internal abstract int WriteBoxed(WriteContext c, object value, byte[][] keys, FullPropertyInfo[] infos, GeneratedWritePlan p, out int end);

    internal abstract void WriteUnrolledBoxed(WriteContext c, object value, FullPropertyInfo[] infos, GeneratedWritePlan p);

    internal abstract void ReadPositionalBoxed(ReadContext c, object target, PropReader[] props, int items, GeneratedReadPlan p);

    internal abstract bool ReadValueBoxed(ReadContext c, object target, int id, GeneratedReadPlan p);

    internal abstract bool ReadMapBoxed(ReadContext c, object target, ReadPlan plan, PropReader[] bound, int count, GeneratedReadPlan p);
  }

  /// <summary>
  /// The generated writer and reader of <typeparamref name="T"/> (the generator derives from it per class). Methods it does not override are not in <see cref="GeneratedObjectCode.Capabilities"/>.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public abstract class GeneratedObjectCode<T> : GeneratedObjectCode where T : class
  {
    protected GeneratedObjectCode(GeneratedMember[] members, int[] unrolledIds, GeneratedCapabilities capabilities) : base(members, unrolledIds, capabilities)
    {
    }

    /// <summary>
    /// Writes the values in the order of the runtime's properties (<see cref="GeneratedWritePlan.Ids"/>): with <paramref name="keys"/> as map entries (a value the filters leave out is not written),
    /// otherwise as array items (nil for a value the filters leave out).
    /// </summary>
    /// <param name="keys">The encoded property ids per position, null for the array layout</param>
    /// <param name="infos">The properties per position (for the runtime's writers)</param>
    /// <param name="end">With <see cref="GeneratedWritePlan.Trim"/>: the position after the last value that is not nil</param>
    /// <returns>Map: the number of entries. Array with <see cref="GeneratedWritePlan.Trim"/>: the number of values up to the last one that is not nil.</returns>
    public virtual int Write(WriteContext c, T value, byte[][] keys, FullPropertyInfo[] infos, GeneratedWritePlan p, out int end)
    {
      throw new NotSupportedException();
    }

    /// <summary>
    /// The array layout without trimming, when the runtime's properties are <see cref="GeneratedObjectCode.UnrolledIds"/> and the generated code writes all members it writes itself.
    /// </summary>
    public virtual void WriteUnrolled(WriteContext c, T value, FullPropertyInfo[] infos, GeneratedWritePlan p)
    {
      throw new NotSupportedException();
    }

    /// <summary>
    /// The map entries in sequence (<see cref="Write"/> with keys, without its loop and switch over the ids), when the runtime's properties are <see cref="GeneratedObjectCode.UnrolledIds"/>
    /// (<see cref="GeneratedWritePlan.UnrolledMap"/>): a value the filters leave out is not written.
    /// </summary>
    /// <param name="keys">The encoded property ids per position</param>
    /// <returns>The number of entries</returns>
    public virtual int WriteUnrolledMap(WriteContext c, T value, byte[][] keys, FullPropertyInfo[] infos, GeneratedWritePlan p)
    {
      throw new NotSupportedException();
    }

    /// <summary>
    /// The array layout with <see cref="GeneratedWritePlan.Trim"/> in sequence (<see cref="Write"/> without keys, without its loop and switch over the ids), when the runtime's properties are
    /// <see cref="GeneratedObjectCode.UnrolledIds"/> (<see cref="GeneratedWritePlan.UnrolledTrimmed"/>): nil for a value the filters leave out.
    /// </summary>
    /// <param name="end">The position after the last value that is not nil (the position at the call when there is none)</param>
    /// <returns>The number of values up to the last one that is not nil</returns>
    public virtual int WriteUnrolledTrimmed(WriteContext c, T value, FullPropertyInfo[] infos, GeneratedWritePlan p, out int end)
    {
      throw new NotSupportedException();
    }

    /// <summary>
    /// The values of an array (the header is read, the instance created): by position, a nil sets the value the writer leaves out (<see cref="Reading.PropReader.ReadNil"/>),
    /// positions without a property are skipped (as ObjectReader.ReadPositional).
    /// </summary>
    public virtual void ReadPositional(ReadContext c, T target, PropReader[] props, int items, GeneratedReadPlan p)
    {
      throw new NotSupportedException();
    }

    /// <summary>
    /// Reads the value at the reader's position into the member (a nil sets the default, as the runtime's readers do).
    /// </summary>
    /// <returns>false when the value is not in the format the generated code reads (the position is unchanged): the runtime reads it</returns>
    public virtual bool ReadValue(ReadContext c, T target, int id, GeneratedReadPlan p)
    {
      return false;
    }

    /// <summary>
    /// The values of a map (the header is read, the instance created), as ObjectReader's loop: the keys through <see cref="GeneratedIO.TryReadKey"/>, keys without a property are skipped, a value in another format is read by the runtime.
    /// A loop here rather than a call per value: the frame of a method with the code of every property is zeroed per call, 816 bytes on a class of 114 properties.
    /// </summary>
    /// <returns>false on a key this does not read (<see cref="GeneratedIO.TryReadKey"/>): the runtime reads the object from its start</returns>
    public virtual bool ReadMap(ReadContext c, T target, ReadPlan plan, PropReader[] bound, int count, GeneratedReadPlan p)
    {
      throw new NotSupportedException();
    }

    internal sealed override int WriteBoxed(WriteContext c, object value, byte[][] keys, FullPropertyInfo[] infos, GeneratedWritePlan p, out int end)
    {
      if (keys != null)
      {
        if (p.UnrolledMap)
        {
          end = 0; // only for trimmed arrays
          return WriteUnrolledMap(c, (T)value, keys, infos, p);
        }
      }
      else if (p.UnrolledTrimmed) // ObjectPlan.WriteTrimmed (arrays without trimming that are not unrolled call Write)
        return WriteUnrolledTrimmed(c, (T)value, infos, p, out end);
      if (p.UntilCompiled > 0)
        p.CountLoop();
      return Write(c, (T)value, keys, infos, p, out end);
    }

    internal sealed override void WriteUnrolledBoxed(WriteContext c, object value, FullPropertyInfo[] infos, GeneratedWritePlan p)
    {
      WriteUnrolled(c, (T)value, infos, p);
    }

    internal sealed override void ReadPositionalBoxed(ReadContext c, object target, PropReader[] props, int items, GeneratedReadPlan p)
    {
      ReadPositional(c, (T)target, props, items, p);
    }

    internal sealed override bool ReadValueBoxed(ReadContext c, object target, int id, GeneratedReadPlan p)
    {
      return ReadValue(c, (T)target, id, p);
    }

    internal sealed override bool ReadMapBoxed(ReadContext c, object target, ReadPlan plan, PropReader[] bound, int count, GeneratedReadPlan p)
    {
      return ReadMap(c, (T)target, plan, bound, count, p);
    }
  }

  /// <summary>
  /// The generated writer and reader of the struct <typeparamref name="T"/> (the generator derives from it per struct): the same as <see cref="GeneratedObjectCode{T}"/> on the struct by reference,
  /// so the runtime's plans write and read it without a box (ObjectPlan.WriteStruct, ObjectReader.ReadByRef). Methods it does not override are not in <see cref="GeneratedObjectCode.Capabilities"/>.
  /// <para>Without a struct constraint, so the plans (generic over any T) can call it; only <see cref="GeneratedValues.Struct{T}"/> makes it usable (it gives the code the unboxing of T for the boxed paths).</para>
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public abstract class GeneratedStructCode<T> : GeneratedObjectCode
  {
    /// <summary>
    /// The struct in a box by reference (set by <see cref="GeneratedValues.Struct{T}"/>, where T is known to be a struct).
    /// </summary>
    internal StructUnboxer<T> Unboxer;

    protected GeneratedStructCode(GeneratedMember[] members, int[] unrolledIds, GeneratedCapabilities capabilities) : base(members, unrolledIds, capabilities)
    {
    }

    /// <inheritdoc cref="GeneratedObjectCode{T}.Write"/>
    public virtual int Write(WriteContext c, ref T value, byte[][] keys, FullPropertyInfo[] infos, GeneratedWritePlan p, out int end)
    {
      throw new NotSupportedException();
    }

    /// <inheritdoc cref="GeneratedObjectCode{T}.WriteUnrolled"/>
    public virtual void WriteUnrolled(WriteContext c, ref T value, FullPropertyInfo[] infos, GeneratedWritePlan p)
    {
      throw new NotSupportedException();
    }

    /// <inheritdoc cref="GeneratedObjectCode{T}.WriteUnrolledMap"/>
    public virtual int WriteUnrolledMap(WriteContext c, ref T value, byte[][] keys, FullPropertyInfo[] infos, GeneratedWritePlan p)
    {
      throw new NotSupportedException();
    }

    /// <inheritdoc cref="GeneratedObjectCode{T}.WriteUnrolledTrimmed"/>
    public virtual int WriteUnrolledTrimmed(WriteContext c, ref T value, FullPropertyInfo[] infos, GeneratedWritePlan p, out int end)
    {
      throw new NotSupportedException();
    }

    /// <inheritdoc cref="GeneratedObjectCode{T}.ReadPositional"/>
    public virtual void ReadPositional(ReadContext c, ref T target, PropReader[] props, int items, GeneratedReadPlan p)
    {
      throw new NotSupportedException();
    }

    /// <inheritdoc cref="GeneratedObjectCode{T}.ReadValue"/>
    public virtual bool ReadValue(ReadContext c, ref T target, int id, GeneratedReadPlan p)
    {
      return false;
    }

    /// <inheritdoc cref="GeneratedObjectCode{T}.ReadMap"/>
    public virtual bool ReadMap(ReadContext c, ref T target, ReadPlan plan, PropReader[] bound, int count, GeneratedReadPlan p)
    {
      throw new NotSupportedException();
    }

    /// <summary>
    /// The value is the default FilterDefaultValues leaves out: what LsMsgPack.Core's <c>Activator.CreateInstance(type).Equals(value)</c> says (ValueType.Equals: each field with the Equals of its type
    /// against its default), without a box (the runtime compiles the same comparison where it can, StructDefault, otherwise compares boxed).
    /// </summary>
    public virtual bool IsDefault(ref T value)
    {
      throw new NotSupportedException();
    }

    internal sealed override int WriteBoxed(WriteContext c, object value, byte[][] keys, FullPropertyInfo[] infos, GeneratedWritePlan p, out int end)
    {
      if (keys != null)
      {
        if (p.UnrolledMap)
        {
          end = 0; // only for trimmed arrays
          return WriteUnrolledMap(c, ref Unboxer.Unbox(value), keys, infos, p);
        }
      }
      else if (p.UnrolledTrimmed)
        return WriteUnrolledTrimmed(c, ref Unboxer.Unbox(value), infos, p, out end);
      if (p.UntilCompiled > 0)
        p.CountLoop();
      return Write(c, ref Unboxer.Unbox(value), keys, infos, p, out end);
    }

    internal sealed override void WriteUnrolledBoxed(WriteContext c, object value, FullPropertyInfo[] infos, GeneratedWritePlan p)
    {
      WriteUnrolled(c, ref Unboxer.Unbox(value), infos, p);
    }

    internal sealed override void ReadPositionalBoxed(ReadContext c, object target, PropReader[] props, int items, GeneratedReadPlan p)
    {
      ReadPositional(c, ref Unboxer.Unbox(target), props, items, p); // set in the box
    }

    internal sealed override bool ReadValueBoxed(ReadContext c, object target, int id, GeneratedReadPlan p)
    {
      return ReadValue(c, ref Unboxer.Unbox(target), id, p);
    }

    internal sealed override bool ReadMapBoxed(ReadContext c, object target, ReadPlan plan, PropReader[] bound, int count, GeneratedReadPlan p)
    {
      return ReadMap(c, ref Unboxer.Unbox(target), plan, bound, count, p);
    }
  }

  /// <summary>
  /// The value in a box by reference, for code that is generic over any T (Unsafe.Unbox needs the struct constraint).
  /// </summary>
  internal abstract class StructUnboxer<T>
  {
    internal abstract ref T Unbox(object box);
  }

  internal sealed class StructUnboxer<TStruct, TSame> : StructUnboxer<TSame> where TStruct : struct
  {
    internal override ref TSame Unbox(object box)
    {
      return ref Unsafe.As<TStruct, TSame>(ref Unsafe.Unbox<TStruct>(box)); // TSame is TStruct (GeneratedValues.Struct)
    }
  }

  /// <summary>
  /// What the generated writer of a type needs to know about a serializer (its options do not change): which member each property position is, and the formats.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public sealed class GeneratedWritePlan
  {
    /// <summary>
    /// The generated member id per property position of the runtime, -1 for a property the runtime writes.
    /// </summary>
    public readonly int[] Ids;

    /// <summary>
    /// FilterDefaultValues (otherwise no dynamic filters): default values are left out.
    /// </summary>
    public readonly bool FilterDefaults;

    /// <summary>
    /// <see cref="LsMsgPack.MsgPackOptions.UnspecifiedDateTimeKind"/> is Utc.
    /// </summary>
    public readonly bool UnspecifiedIsUtc;

    /// <summary>
    /// <see cref="LsMsgPack.MsgPackOptions.TrimTrailingNulls"/> (array layout).
    /// </summary>
    public readonly bool Trim;

    /// <summary>
    /// The type code of the decimal extension.
    /// </summary>
    public readonly byte DecimalCode;

    /// <summary>
    /// Guids are written as strings of 36 characters (<see cref="GuidFormat.String"/>), otherwise as bin 16. Only for code with <see cref="GeneratedCapabilities.WriteFormats"/>.
    /// </summary>
    public readonly bool GuidString;

    /// <summary>
    /// Decimals are written as strings (<see cref="DecimalFormat.String"/>), otherwise with the built-in extension (<see cref="DecimalCode"/>). Only for code with <see cref="GeneratedCapabilities.WriteFormats"/>.
    /// </summary>
    public readonly bool DecimalString;

    internal readonly bool Unrolled;

    /// <summary>
    /// The array layout with trimming is written by <see cref="GeneratedObjectCode{T}.WriteUnrolledTrimmed"/>: the ids are the unrolled ones.
    /// </summary>
    internal readonly bool UnrolledTrimmed;

    /// <summary>
    /// Maps (also of the array layout: a collection with SerializeProperties) are written by <see cref="GeneratedObjectCode{T}.WriteUnrolledMap"/>: the ids are the unrolled ones (trimming does not apply to maps).
    /// </summary>
    internal readonly bool UnrolledMap;

    /// <summary>
    /// The code this plan is for (one field, so ObjectPlan can switch to a compiled writer with one store).
    /// </summary>
    internal readonly GeneratedObjectCode Code;

    /// <summary>
    /// The objects still to write by <see cref="GeneratedObjectCode{T}.Write"/>'s loop (the generated code of an order or properties other than it predicted, <see cref="Unrolled"/> and the others false)
    /// before the plan compiles its own writer (<see cref="Serializer.CompileWritersAfter"/>, <see cref="CountLoop"/>); 0 when it does not (any more), and for compiled code.
    /// Not exact with several threads, which is all right.
    /// </summary>
    internal int UntilCompiled;

    /// <summary>
    /// The plan that compiles the writer after <see cref="UntilCompiled"/>.
    /// </summary>
    internal ObjectPlan LoopPlan;
    private readonly PropWriter[] _props;
    private readonly Serializer _serializer;

    /// <summary>
    /// The handlers of the properties written by <see cref="Value{T}"/> (per position, a ValueHandler of the property's type), made at the first value: a handler can lead back to this plan's type.
    /// </summary>
    private readonly object[] _handlers;

    internal GeneratedWritePlan(Serializer serializer, GeneratedObjectCode code, int[] ids, bool filterDefaults, bool unspecifiedIsUtc, bool trim, byte decimalCode, bool guidString, bool decimalString,
      bool unrolled, bool unrolledMap, bool unrolledTrimmed, PropWriter[] props)
    {
      _serializer = serializer;
      Code = code;
      _handlers = new object[ids.Length];
      Ids = ids;
      FilterDefaults = filterDefaults;
      UnspecifiedIsUtc = unspecifiedIsUtc;
      Trim = trim;
      DecimalCode = decimalCode;
      GuidString = guidString;
      DecimalString = decimalString;
      Unrolled = unrolled;
      UnrolledMap = unrolledMap;
      UnrolledTrimmed = unrolledTrimmed;
      _props = props;
    }

    /// <summary>
    /// Counts an object written by the generated code's loop over the ids, and has the plan compile a writer for its properties after <see cref="Serializer.CompileWritersAfter"/> of them
    /// (on a JIT the compiled writer is straight-line for any order: a class of 120 properties in alphabetical order 1643 -> 1011 ns).
    /// Here, not in ObjectPlan.Write: counting there made it too large to be inlined into the loops of collections.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void CountLoop()
    {
      if (--UntilCompiled == 0)
        LoopPlan.CompileInBackground(_serializer);
    }

    /// <summary>
    /// The runtime writes the property at <paramref name="position"/> (with its key when <paramref name="key"/> is not null).
    /// </summary>
    /// <returns>false when the filters left the value out (nothing was written)</returns>
    public bool Runtime(int position, WriteContext c, object target, byte[] key, FullPropertyInfo info)
    {
      return _props[position].Write(c, target, key, info);
    }

    /// <summary>
    /// A value the generated code read itself but does not write itself (objects, collections...): what the runtime's <see cref="PropWriter{TTarget, TValue, TFilter}"/> does after its getter.
    /// </summary>
    /// <typeparam name="T">The type of the property at <paramref name="position"/></typeparam>
    /// <returns>false when the filters left the value out (nothing was written)</returns>
    public bool Value<T>(int position, WriteContext c, T value, byte[] key, FullPropertyInfo info)
    {
      ValueHandler<T> handler = Unsafe.As<ValueHandler<T>>(_handlers[position]) ?? CreateHandler<T>(position, info);
      if (FilterDefaults)
      {
        if (_props[position].BoxedFilters)
        {
          if (!_props[position].IncludeBoxed(value, info))
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

    private ValueHandler<T> CreateHandler<T>(int position, FullPropertyInfo info)
    {
      ValueHandler<T> handler = _serializer.Handler<T>(info);
      _handlers[position] = handler;
      return handler;
    }

    /// <summary>
    /// <see cref="Runtime(int, WriteContext, object, byte[], FullPropertyInfo)"/> for a struct by reference (<see cref="GeneratedStructCode{T}"/>).
    /// </summary>
    public bool Runtime<T>(int position, WriteContext c, ref T target, byte[] key, FullPropertyInfo info)
    {
      PropWriter prop = _props[position];
      if (prop.ByRef) // a property of this struct (the plan's type)
        return Unsafe.As<RefPropWriter<T>>(prop).Write(c, ref target, key, info);
      return prop.Write(c, (object)target, key, info); // by reflection: a copy in a box (only read)
    }
  }

  /// <summary>
  /// What the generated reader of a type needs to know about a serializer.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public sealed class GeneratedReadPlan
  {
    /// <summary>
    /// The type code of the decimal extension.
    /// </summary>
    public readonly byte DecimalCode;

    /// <summary>
    /// Guids are read as strings (<see cref="GuidFormat.String"/>; bin 16 too). Only for code with <see cref="GeneratedCapabilities.ReadFormats"/>.
    /// </summary>
    public readonly bool GuidString;

    /// <summary>
    /// Decimals are read as strings (<see cref="DecimalFormat.String"/>), otherwise from the built-in extension (<see cref="DecimalCode"/>). Only for code with <see cref="GeneratedCapabilities.ReadFormats"/>.
    /// </summary>
    public readonly bool DecimalString;

    internal GeneratedReadPlan(byte decimalCode, bool guidString, bool decimalString)
    {
      DecimalCode = decimalCode;
      GuidString = guidString;
      DecimalString = decimalString;
    }
  }

  /// <summary>
  /// Small helpers of the generated writers and readers (the same encodings as the runtime's handlers and readers).
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public static class GeneratedIO
  {
    /// <summary>
    /// The key of a value of a map, as ObjectReader's loop reads it: an index into the schema, or a name.
    /// </summary>
    /// <param name="bound">The properties by the indexes of the schema, null without a schema</param>
    /// <param name="next">The position after the property found last: names are usually written in the order of the properties</param>
    /// <param name="prop">The property, null when the type has none with this key: its value is to be skipped</param>
    /// <returns>false on another kind of key, or a reserved one ("" or "@")</returns>
    public static bool TryReadKey(ReadContext c, ReadPlan plan, PropReader[] bound, ref int next, out PropReader prop)
    {
      MsgPackReader r = c.R;
      if (c.Schema)
      {
        prop = null;
        if (!r.TryReadInt64(out long index))
          return false;
        if (index >= 0 && index < bound.Length)
          prop = bound[index];
        return true;
      }
      int length = r.TryReadStringHeader();
      if (length <= 0 || (length == 1 && r.Buf[r.Pos] == (byte)'@'))
      {
        prop = null;
        return false;
      }
      prop = plan.FindByName(r.Buf, r.Pos, length, ref next);
      r.Pos += length;
      return true;
    }

    /// <summary>
    /// The runtime reads the value into a struct by reference (<see cref="GeneratedStructCode{T}"/>): another format, or a property the generated code does not read.
    /// </summary>
    public static void Read<T>(PropReader prop, ReadContext c, ref T target)
    {
      if (prop.ByRef) // a property of this struct (the plan's type)
      {
        Unsafe.As<RefPropReader<T>>(prop).Read(c, ref target);
        return;
      }
      object box = target; // set by reflection: in a box, then copied back
      prop.Read(c, box);
      target = (T)box;
    }

    /// <summary>
    /// The runtime sets the value of a nil into a struct by reference (<see cref="PropReader.ReadNil"/>).
    /// </summary>
    public static void ReadNil<T>(PropReader prop, ref T target)
    {
      if (prop.ByRef)
      {
        Unsafe.As<RefPropReader<T>>(prop).ReadNil(ref target);
        return;
      }
      object box = target;
      prop.ReadNil(box);
      target = (T)box;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsZero(decimal value)
    {
      return DecimalHandler.IsZero(value);
    }

    /// <summary>
    /// The built-in decimal extension (DecimalHandler): fixext16 with the 16 bytes of the value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteDecimal(MsgPackWriter w, decimal value, byte typeCode)
    {
      w.Ensure(18);
      byte[] b = w.Buf;
      int at = w.Pos;
      b[at] = 0xD8;
      b[at + 1] = typeCode;
      Unsafe.WriteUnaligned(ref b[at + 2], value);
      w.Pos = at + 18;
    }

    /// <summary>
    /// A decimal as a string (<see cref="GeneratedWritePlan.DecimalString"/>, <see cref="GeneratedCapabilities.WriteFormats"/>).
    /// </summary>
    public static void DecimalString(MsgPackWriter w, decimal value)
    {
      w.DecimalString(value);
    }

    /// <summary>
    /// A Guid as a string of 36 characters (<see cref="GeneratedWritePlan.GuidString"/>, <see cref="GeneratedCapabilities.WriteFormats"/>).
    /// </summary>
    public static void GuidString(MsgPackWriter w, Guid value)
    {
      w.GuidString(value);
    }

    /// <summary>
    /// The built-in decimal extension (DecimalReader's fast path).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadDecimal(MsgPackReader r, byte typeCode, out decimal value)
    {
      int at = r.Pos;
      if (r.End - at >= 18 && r.Buf[at] == 0xD8 && r.Buf[at + 1] == typeCode)
      {
        value = Unsafe.ReadUnaligned<decimal>(ref r.Buf[at + 2]);
        r.Pos = at + 18;
        return true;
      }
      value = 0m;
      return false;
    }

    /// <summary>
    /// A Guid as the serializer's reader of Guids reads it directly: bin 16, with <see cref="GeneratedReadPlan.GuidString"/> also a string of the format the writers write
    /// (GuidStringReader; any other text is left to it). <see cref="GeneratedCapabilities.ReadFormats"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadGuid(MsgPackReader r, GeneratedReadPlan p, out Guid value)
    {
      return r.TryReadGuid(out value) || (p.GuidString && r.TryReadGuidString(out value));
    }

    /// <summary>
    /// A decimal as the serializer's reader of decimals reads it directly: the built-in extension, with <see cref="GeneratedReadPlan.DecimalString"/> also a string DecimalText parses
    /// (DecimalStringReader, which reads the built-in extension in place too; any other text is left to it). <see cref="GeneratedCapabilities.ReadFormats"/>.
    /// The extension first: the default format has no test per value (a value is never both).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadDecimal(MsgPackReader r, GeneratedReadPlan p, out decimal value)
    {
      return TryReadDecimal(r, p.DecimalCode, out value) || (p.DecimalString && r.TryReadDecimalString(out value));
    }

    /// <summary>
    /// The DateTime as the writer takes it (DateTimeHandler.Utc).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTime Utc(DateTime value, GeneratedWritePlan p)
    {
      return p.UnspecifiedIsUtc && value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value;
    }
  }
}
