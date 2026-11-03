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
    IsDefault = 32
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

    internal abstract int WriteBoxed(WriteContext c, object value, byte[][] keys, FullPropertyInfo[] infos, GeneratedWritePlan p, out int end);

    internal abstract void WriteUnrolledBoxed(WriteContext c, object value, FullPropertyInfo[] infos, GeneratedWritePlan p);

    internal abstract void ReadPositionalBoxed(ReadContext c, object target, PropReader[] props, int items, GeneratedReadPlan p);

    internal abstract bool ReadValueBoxed(ReadContext c, object target, int id, GeneratedReadPlan p);
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

    internal sealed override int WriteBoxed(WriteContext c, object value, byte[][] keys, FullPropertyInfo[] infos, GeneratedWritePlan p, out int end)
    {
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

    internal readonly bool Unrolled;
    private readonly PropWriter[] _props;

    internal GeneratedWritePlan(int[] ids, bool filterDefaults, bool unspecifiedIsUtc, bool trim, byte decimalCode, bool unrolled, PropWriter[] props)
    {
      Ids = ids;
      FilterDefaults = filterDefaults;
      UnspecifiedIsUtc = unspecifiedIsUtc;
      Trim = trim;
      DecimalCode = decimalCode;
      Unrolled = unrolled;
      _props = props;
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

    internal GeneratedReadPlan(byte decimalCode)
    {
      DecimalCode = decimalCode;
    }
  }

  /// <summary>
  /// Small helpers of the generated writers and readers (the same encodings as the runtime's handlers and readers).
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public static class GeneratedIO
  {
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
    /// The DateTime as the writer takes it (DateTimeHandler.Utc).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTime Utc(DateTime value, GeneratedWritePlan p)
    {
      return p.UnspecifiedIsUtc && value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value;
    }
  }
}
