// Prototype: object -> bytes and bytes -> object without MsgPackItem trees (no source generator, no Reflection.Emit, no Expression.Compile).
// Plans are built once per type by reflection; values are read and written through typed delegates (Delegate.CreateDelegate, as PropertyAccessor does)
// and generic virtual dispatch, so primitives are never boxed. Produces the same bytes as LsMsgPack (default settings) for the supported types:
// bool, integers, string, Guid, decimal, DateTime, enums, Nullable<T>, List<T>, T[] and classes (polymorphic values get a type id).
using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Direct
{
  public sealed class Writer
  {
    public byte[] Buf;
    public int Pos;

    public Writer(int capacity = 4096) { Buf = new byte[capacity]; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Ensure(int n) { if (Pos + n > Buf.Length) Array.Resize(ref Buf, Math.Max(Buf.Length * 2, Pos + n)); }

    public void Byte(byte b) { Ensure(1); Buf[Pos++] = b; }

    public void Raw(byte[] bytes)
    {
      Ensure(bytes.Length);
      if (bytes.Length == 1) Buf[Pos++] = bytes[0];
      else { Buffer.BlockCopy(bytes, 0, Buf, Pos, bytes.Length); Pos += bytes.Length; }
    }

    public void Nil() { Byte(0xC0); }
    public void Bool(bool v) { Byte(v ? (byte)0xC3 : (byte)0xC2); }

    // MpInt with DynamicallyCompact: non-negative values always use the unsigned formats, fixint for -31..-1
    public void UInt(ulong v)
    {
      Ensure(9);
      byte[] b = Buf;
      if (v <= 0x7F) b[Pos++] = (byte)v;
      else if (v <= 0xFF) { b[Pos++] = 0xCC; b[Pos++] = (byte)v; }
      else if (v <= 0xFFFF) { b[Pos++] = 0xCD; BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(Pos), (ushort)v); Pos += 2; }
      else if (v <= 0xFFFFFFFF) { b[Pos++] = 0xCE; BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(Pos), (uint)v); Pos += 4; }
      else { b[Pos++] = 0xCF; BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(Pos), v); Pos += 8; }
    }

    public void Int(long v)
    {
      if (v >= 0) { UInt((ulong)v); return; }
      Ensure(9);
      byte[] b = Buf;
      if (v >= -0x1F) b[Pos++] = (byte)(sbyte)v;
      else if (v >= sbyte.MinValue) { b[Pos++] = 0xD0; b[Pos++] = (byte)(sbyte)v; }
      else if (v >= short.MinValue) { b[Pos++] = 0xD1; BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(Pos), (short)v); Pos += 2; }
      else if (v >= int.MinValue) { b[Pos++] = 0xD2; BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(Pos), (int)v); Pos += 4; }
      else { b[Pos++] = 0xD3; BinaryPrimitives.WriteInt64BigEndian(b.AsSpan(Pos), v); Pos += 8; }
    }

    public void Str(string s)
    {
      if (s is null) { Nil(); return; }
      int n = Encoding.UTF8.GetByteCount(s);
      Ensure(n + 5);
      byte[] b = Buf;
      if (n < 32) b[Pos++] = (byte)(0xA0 | n);
      else if (n < 256) { b[Pos++] = 0xD9; b[Pos++] = (byte)n; }
      else if (n <= 0xFFFF) { b[Pos++] = 0xDA; BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(Pos), (ushort)n); Pos += 2; }
      else { b[Pos++] = 0xDB; BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(Pos), (uint)n); Pos += 4; }
      Pos += Encoding.UTF8.GetBytes(s, 0, s.Length, b, Pos);
    }

    public void Guid(Guid g) { Ensure(18); Buf[Pos++] = 0xC4; Buf[Pos++] = 16; g.TryWriteBytes(Buf.AsSpan(Pos)); Pos += 16; } // same order as ToByteArray

    public void Decimal(decimal d) { Ensure(18); Buf[Pos++] = 0xD8; Buf[Pos++] = 1; MemoryMarshal.Write(Buf.AsSpan(Pos), in d); Pos += 16; } // raw memory, as MpDecimal

    static readonly DateTime Zero = MpDateTime.Zero, MaxFExt4 = MpDateTime.MaxFExt4, MaxFExt8 = MpDateTime.MaxFExt8;

    public void DateTime(DateTime dt) // MpDateTime.FromDateTime
    {
      dt = dt.ToUniversalTime();
      Ensure(15);
      long seconds = (dt - Zero).Ticks / TimeSpan.TicksPerSecond;
      long fraction = dt.Ticks % TimeSpan.TicksPerSecond;
      if (dt < Zero || dt > MaxFExt8)
      {
        Buf[Pos++] = 0xC7; Buf[Pos++] = 12; Buf[Pos++] = 0xFF;
        BinaryPrimitives.WriteUInt32BigEndian(Buf.AsSpan(Pos), (uint)fraction * 100); Pos += 4;
        BinaryPrimitives.WriteInt64BigEndian(Buf.AsSpan(Pos), seconds); Pos += 8;
      }
      else if (dt > MaxFExt4 || fraction != 0)
      {
        Buf[Pos++] = 0xD7; Buf[Pos++] = 0xFF;
        BinaryPrimitives.WriteUInt64BigEndian(Buf.AsSpan(Pos), ((ulong)fraction * 100) << 34 | (ulong)seconds); Pos += 8;
      }
      else
      {
        Buf[Pos++] = 0xD6; Buf[Pos++] = 0xFF;
        BinaryPrimitives.WriteUInt32BigEndian(Buf.AsSpan(Pos), (uint)seconds); Pos += 4;
      }
    }

    public void ArrayHeader(int n)
    {
      Ensure(5);
      if (n < 16) Buf[Pos++] = (byte)(0x90 | n);
      else if (n <= 0xFFFF) { Buf[Pos++] = 0xDC; BinaryPrimitives.WriteUInt16BigEndian(Buf.AsSpan(Pos), (ushort)n); Pos += 2; }
      else { Buf[Pos++] = 0xDD; BinaryPrimitives.WriteUInt32BigEndian(Buf.AsSpan(Pos), (uint)n); Pos += 4; }
    }

    /// <summary>The number of map entries is known after writing them (default values are skipped): reserve the header and patch it</summary>
    public int ReserveMapHeader(int maxCount)
    {
      int at = Pos;
      int size = maxCount < 16 ? 1 : 3;
      Ensure(size);
      Pos += size;
      return at;
    }

    public void PatchMapHeader(int at, int maxCount, int count)
    {
      if (maxCount < 16) { Buf[at] = (byte)(0x80 | count); return; }
      if (count < 16) // shrink the 3 byte map16 header to a fixmap
      {
        Buffer.BlockCopy(Buf, at + 3, Buf, at + 1, Pos - at - 3);
        Pos -= 2;
        Buf[at] = (byte)(0x80 | count);
        return;
      }
      if (count > 0xFFFF) throw new NotSupportedException("map32");
      Buf[at] = 0xDE;
      BinaryPrimitives.WriteUInt16BigEndian(Buf.AsSpan(at + 1), (ushort)count);
    }

    public byte[] ToArray() { byte[] r = new byte[Pos]; Buffer.BlockCopy(Buf, 0, r, 0, Pos); return r; }
  }

  public sealed class Reader
  {
    public byte[] Buf;
    public int Pos;

    public Reader(byte[] buf, int pos) { Buf = buf; Pos = pos; }

    public bool TryNil() { if (Buf[Pos] == 0xC0) { Pos++; return true; } return false; }

    public bool Bool()
    {
      byte c = Buf[Pos++];
      if (c == 0xC3) return true;
      if (c == 0xC2) return false;
      throw Unexpected(c, "bool");
    }

    public long Int64()
    {
      byte c = Buf[Pos++];
      if (c <= 0x7F) return c;
      if (c >= 0xE0) return (sbyte)c;
      long v;
      switch (c)
      {
        case 0xCC: return Buf[Pos++];
        case 0xCD: v = BinaryPrimitives.ReadUInt16BigEndian(Buf.AsSpan(Pos)); Pos += 2; return v;
        case 0xCE: v = BinaryPrimitives.ReadUInt32BigEndian(Buf.AsSpan(Pos)); Pos += 4; return v;
        case 0xCF: v = checked((long)BinaryPrimitives.ReadUInt64BigEndian(Buf.AsSpan(Pos))); Pos += 8; return v;
        case 0xD0: return (sbyte)Buf[Pos++];
        case 0xD1: v = BinaryPrimitives.ReadInt16BigEndian(Buf.AsSpan(Pos)); Pos += 2; return v;
        case 0xD2: v = BinaryPrimitives.ReadInt32BigEndian(Buf.AsSpan(Pos)); Pos += 4; return v;
        case 0xD3: v = BinaryPrimitives.ReadInt64BigEndian(Buf.AsSpan(Pos)); Pos += 8; return v;
      }
      throw Unexpected(c, "integer");
    }

    /// <returns>the length of the string, <see cref="Pos"/> is at its first byte; -1 for nil</returns>
    public int StrHeader()
    {
      byte c = Buf[Pos++];
      if ((c & 0xE0) == 0xA0) return c & 0x1F;
      int n;
      switch (c)
      {
        case 0xD9: return Buf[Pos++];
        case 0xDA: n = BinaryPrimitives.ReadUInt16BigEndian(Buf.AsSpan(Pos)); Pos += 2; return n;
        case 0xDB: n = checked((int)BinaryPrimitives.ReadUInt32BigEndian(Buf.AsSpan(Pos))); Pos += 4; return n;
        case 0xC0: return -1;
      }
      throw Unexpected(c, "string");
    }

    public string Str()
    {
      int n = StrHeader();
      if (n < 0) return null;
      string s = Encoding.UTF8.GetString(Buf, Pos, n);
      Pos += n;
      return s;
    }

    public int MapHeader()
    {
      byte c = Buf[Pos++];
      if ((c & 0xF0) == 0x80) return c & 0x0F;
      if (c == 0xDE) { int n = BinaryPrimitives.ReadUInt16BigEndian(Buf.AsSpan(Pos)); Pos += 2; return n; }
      if (c == 0xDF) { int n = checked((int)BinaryPrimitives.ReadUInt32BigEndian(Buf.AsSpan(Pos))); Pos += 4; return n; }
      throw Unexpected(c, "map");
    }

    public int ArrayHeader()
    {
      byte c = Buf[Pos++];
      if ((c & 0xF0) == 0x90) return c & 0x0F;
      if (c == 0xDC) { int n = BinaryPrimitives.ReadUInt16BigEndian(Buf.AsSpan(Pos)); Pos += 2; return n; }
      if (c == 0xDD) { int n = checked((int)BinaryPrimitives.ReadUInt32BigEndian(Buf.AsSpan(Pos))); Pos += 4; return n; }
      throw Unexpected(c, "array");
    }

    public decimal Decimal()
    {
      if (Buf[Pos] != 0xD8 || Buf[Pos + 1] != 1) throw Unexpected(Buf[Pos], "decimal (fixext16 type 1)");
      decimal d = MemoryMarshal.Read<decimal>(Buf.AsSpan(Pos + 2, 16));
      Pos += 18;
      return d;
    }

    public Guid Guid()
    {
      if (Buf[Pos] != 0xC4 || Buf[Pos + 1] != 16) throw Unexpected(Buf[Pos], "Guid (bin8 of 16 bytes)");
      Guid g = new Guid(Buf.AsSpan(Pos + 2, 16));
      Pos += 18;
      return g;
    }

    static readonly DateTime Zero = MpDateTime.Zero;

    public DateTime DateTime() // MpDateTime.ConvertExt, returned as local time like MpDateTime.Value
    {
      byte c = Buf[Pos];
      if (c == 0xD6 && Buf[Pos + 1] == 0xFF)
      {
        uint seconds = BinaryPrimitives.ReadUInt32BigEndian(Buf.AsSpan(Pos + 2));
        Pos += 6;
        return Zero.AddSeconds(seconds).ToLocalTime();
      }
      if (c == 0xD7 && Buf[Pos + 1] == 0xFF)
      {
        ulong v = BinaryPrimitives.ReadUInt64BigEndian(Buf.AsSpan(Pos + 2));
        Pos += 10;
        return Zero.AddSeconds(v & 0x3FFFFFFFF).Add(new TimeSpan((long)(v >> 34) / 100)).ToLocalTime();
      }
      if (c == 0xC7 && Buf[Pos + 1] == 12 && Buf[Pos + 2] == 0xFF)
      {
        uint nanos = BinaryPrimitives.ReadUInt32BigEndian(Buf.AsSpan(Pos + 3));
        long seconds = BinaryPrimitives.ReadInt64BigEndian(Buf.AsSpan(Pos + 7));
        Pos += 15;
        TimeSpan sub = TimeSpan.FromTicks(nanos / 100);
        DateTime local = Zero.AddSeconds(seconds).ToLocalTime();
        return seconds < 0 ? local - sub : local + sub;
      }
      throw Unexpected(c, "timestamp");
    }

    public void Skip()
    {
      byte c = Buf[Pos];
      if (c <= 0x7F || c >= 0xE0 || c == 0xC0 || c == 0xC2 || c == 0xC3) { Pos++; return; }
      if ((c & 0xF0) == 0x80) { int n = MapHeader(); for (int i = 0; i < 2 * n; i++) Skip(); return; }
      if ((c & 0xF0) == 0x90) { int n = ArrayHeader(); for (int i = 0; i < n; i++) Skip(); return; }
      if ((c & 0xE0) == 0xA0) { Pos += 1 + (c & 0x1F); return; }
      switch (c)
      {
        case 0xCC: case 0xD0: Pos += 2; return;
        case 0xCD: case 0xD1: Pos += 3; return;
        case 0xCE: case 0xD2: case 0xCA: Pos += 5; return;
        case 0xCF: case 0xD3: case 0xCB: Pos += 9; return;
        case 0xD9: case 0xC4: Pos += 2 + Buf[Pos + 1]; return;
        case 0xDA: case 0xC5: Pos += 3 + BinaryPrimitives.ReadUInt16BigEndian(Buf.AsSpan(Pos + 1)); return;
        case 0xDB: case 0xC6: Pos += 5 + (int)BinaryPrimitives.ReadUInt32BigEndian(Buf.AsSpan(Pos + 1)); return;
        case 0xD4: Pos += 3; return;
        case 0xD5: Pos += 4; return;
        case 0xD6: Pos += 6; return;
        case 0xD7: Pos += 10; return;
        case 0xD8: Pos += 18; return;
        case 0xC7: Pos += 3 + Buf[Pos + 1]; return;
        case 0xC8: Pos += 4 + BinaryPrimitives.ReadUInt16BigEndian(Buf.AsSpan(Pos + 1)); return;
        case 0xC9: Pos += 6 + (int)BinaryPrimitives.ReadUInt32BigEndian(Buf.AsSpan(Pos + 1)); return;
        case 0xDC: case 0xDD: { int n = ArrayHeader(); for (int i = 0; i < n; i++) Skip(); return; }
        case 0xDE: case 0xDF: { int n = MapHeader(); for (int i = 0; i < 2 * n; i++) Skip(); return; }
      }
      throw Unexpected(c, "a value");
    }

    Exception Unexpected(byte c, string expected) { return new FormatException($"Expected {expected} at {Pos} but found 0x{c:X2}"); }
  }

  // ------------------------------------------------------------------ value writers and readers (one per type, generic so values are not boxed)

  public abstract class ValueWriter<T>
  {
    /// <summary>FilterDefaultValues: null, "", default(T) of value types (a non-null Nullable is always written)</summary>
    public abstract bool IsDefault(T value);
    public abstract void Write(Writer w, T value);
  }

  public abstract class ValueReader<T>
  {
    public abstract T Read(Reader r);
  }

  sealed class BoolRW : ValueWriter<bool> { public override bool IsDefault(bool v) { return !v; } public override void Write(Writer w, bool v) { w.Bool(v); } }
  sealed class Int32W : ValueWriter<int> { public override bool IsDefault(int v) { return v == 0; } public override void Write(Writer w, int v) { w.Int(v); } }
  sealed class Int64W : ValueWriter<long> { public override bool IsDefault(long v) { return v == 0; } public override void Write(Writer w, long v) { w.Int(v); } }
  sealed class StringW : ValueWriter<string> { public override bool IsDefault(string v) { return string.IsNullOrEmpty(v); } public override void Write(Writer w, string v) { w.Str(v); } }
  sealed class GuidW : ValueWriter<Guid> { public override bool IsDefault(Guid v) { return v == Guid.Empty; } public override void Write(Writer w, Guid v) { w.Guid(v); } }
  sealed class DecimalW : ValueWriter<decimal> { public override bool IsDefault(decimal v) { return v == 0m; } public override void Write(Writer w, decimal v) { w.Decimal(v); } }
  sealed class DateTimeW : ValueWriter<DateTime> { public override bool IsDefault(DateTime v) { return v.Ticks == 0; } public override void Write(Writer w, DateTime v) { w.DateTime(v); } }

  sealed class BoolR : ValueReader<bool> { public override bool Read(Reader r) { return r.Bool(); } }
  sealed class Int32R : ValueReader<int> { public override int Read(Reader r) { return checked((int)r.Int64()); } }
  sealed class Int64R : ValueReader<long> { public override long Read(Reader r) { return r.Int64(); } }
  sealed class StringR : ValueReader<string> { public override string Read(Reader r) { return r.Str(); } }
  sealed class GuidR : ValueReader<Guid> { public override Guid Read(Reader r) { return r.Guid(); } }
  sealed class DecimalR : ValueReader<decimal> { public override decimal Read(Reader r) { return r.Decimal(); } }
  sealed class DateTimeR : ValueReader<DateTime> { public override DateTime Read(Reader r) { return r.DateTime(); } }

  sealed class EnumW<TEnum> : ValueWriter<TEnum> where TEnum : struct
  {
    static readonly bool Unsigned = Type.GetTypeCode(Enum.GetUnderlyingType(typeof(TEnum))) is TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64;
    static long ToInt64(TEnum v)
    {
      switch (Unsafe.SizeOf<TEnum>())
      {
        case 1: return Unsigned ? Unsafe.As<TEnum, byte>(ref v) : Unsafe.As<TEnum, sbyte>(ref v);
        case 2: return Unsigned ? Unsafe.As<TEnum, ushort>(ref v) : Unsafe.As<TEnum, short>(ref v);
        case 4: return Unsigned ? Unsafe.As<TEnum, uint>(ref v) : Unsafe.As<TEnum, int>(ref v);
        default: return Unsafe.As<TEnum, long>(ref v);
      }
    }
    public override bool IsDefault(TEnum v) { return ToInt64(v) == 0; }
    public override void Write(Writer w, TEnum v)
    {
      if (Unsigned && Unsafe.SizeOf<TEnum>() == 8) w.UInt(Unsafe.As<TEnum, ulong>(ref v));
      else w.Int(ToInt64(v));
    }
  }

  sealed class EnumR<TEnum> : ValueReader<TEnum> where TEnum : struct
  {
    public override TEnum Read(Reader r)
    {
      long v = r.Int64();
      switch (Unsafe.SizeOf<TEnum>())
      {
        case 1: { byte b = (byte)v; return Unsafe.As<byte, TEnum>(ref b); }
        case 2: { ushort s = (ushort)v; return Unsafe.As<ushort, TEnum>(ref s); }
        case 4: { uint i = (uint)v; return Unsafe.As<uint, TEnum>(ref i); }
        default: return Unsafe.As<long, TEnum>(ref v);
      }
    }
  }

  sealed class NullableW<T> : ValueWriter<T?> where T : struct
  {
    readonly ValueWriter<T> _inner;
    public NullableW(ValueWriter<T> inner) { _inner = inner; }
    public override bool IsDefault(T? v) { return !v.HasValue; }
    public override void Write(Writer w, T? v) { if (v.HasValue) _inner.Write(w, v.GetValueOrDefault()); else w.Nil(); }
  }

  sealed class NullableR<T> : ValueReader<T?> where T : struct
  {
    readonly ValueReader<T> _inner;
    public NullableR(ValueReader<T> inner) { _inner = inner; }
    public override T? Read(Reader r) { if (r.TryNil()) return null; return _inner.Read(r); }
  }

  sealed class ListW<E> : ValueWriter<List<E>>
  {
    readonly Func<ValueWriter<E>> _elementFactory;
    ValueWriter<E> _element;
    public ListW(Func<ValueWriter<E>> element) { _elementFactory = element; }
    public override bool IsDefault(List<E> v) { return v is null; }
    public override void Write(Writer w, List<E> v)
    {
      if (v is null) { w.Nil(); return; }
      ValueWriter<E> element = _element ?? (_element = _elementFactory());
      w.ArrayHeader(v.Count);
      for (int i = 0; i < v.Count; i++) element.Write(w, v[i]);
    }
  }

  sealed class ListR<E> : ValueReader<List<E>>
  {
    readonly Func<ValueReader<E>> _elementFactory;
    ValueReader<E> _element;
    public ListR(Func<ValueReader<E>> element) { _elementFactory = element; }
    public override List<E> Read(Reader r)
    {
      if (r.TryNil()) return null;
      ValueReader<E> element = _element ?? (_element = _elementFactory());
      int n = r.ArrayHeader();
      List<E> list = new List<E>(n);
      for (int i = 0; i < n; i++) list.Add(element.Read(r));
      return list;
    }
  }

  sealed class ObjectW<T> : ValueWriter<T> where T : class
  {
    readonly Context _ctx;
    TypePlan _plan;
    public ObjectW(Context ctx) { _ctx = ctx; }
    public override bool IsDefault(T v) { return v is null; }
    public override void Write(Writer w, T v)
    {
      if (v is null) { w.Nil(); return; }
      Type runtime = v.GetType();
      if (runtime == typeof(T)) (_plan ?? (_plan = _ctx.Plan(typeof(T)))).Write(w, v, false);
      else _ctx.Plan(runtime).Write(w, v, true); // declared as a base type or interface: add the type id (AddTypeIdOption.IfAmbiguious)
    }
  }

  sealed class ObjectR<T> : ValueReader<T> where T : class
  {
    readonly Context _ctx;
    TypePlan _plan;
    public ObjectR(Context ctx) { _ctx = ctx; }
    public override T Read(Reader r)
    {
      if (r.TryNil()) return null;
      int n = r.MapHeader();
      TypePlan plan;
      if (n > 0 && r.Buf[r.Pos] == 0xA0) // the type id (key "") is written first
      {
        r.Pos++;
        plan = _ctx.PlanByTypeId(r);
        n--;
      }
      else plan = _plan ?? (_plan = _ctx.Plan(typeof(T)));
      return (T)plan.Read(r, n);
    }
  }

  // ------------------------------------------------------------------ properties

  public abstract class Prop
  {
    public string Name;
    public byte[] Key;        // the pre-encoded map key (schema index or name)
    public byte[] NameUtf8;   // for matching names when reading
    public abstract bool TryWrite(Writer w, object target);
    public abstract void Read(Reader r, object target);
  }

  sealed class Prop<TTarget, TValue> : Prop where TTarget : class
  {
    readonly Func<TTarget, TValue> _get;
    readonly Action<TTarget, TValue> _set;
    readonly Context _ctx;
    ValueWriter<TValue> _writer;
    ValueReader<TValue> _reader;

    public Prop(PropertyInfo property, Context ctx)
    {
      _ctx = ctx;
      _get = (Func<TTarget, TValue>)Delegate.CreateDelegate(typeof(Func<TTarget, TValue>), property.GetGetMethod(true));
      _set = (Action<TTarget, TValue>)Delegate.CreateDelegate(typeof(Action<TTarget, TValue>), property.GetSetMethod(true));
    }

    public override bool TryWrite(Writer w, object target)
    {
      TValue v = _get((TTarget)target);
      ValueWriter<TValue> writer = _writer ?? (_writer = _ctx.Writer<TValue>());
      if (writer.IsDefault(v)) return false;
      w.Raw(Key);
      writer.Write(w, v);
      return true;
    }

    public override void Read(Reader r, object target)
    {
      ValueReader<TValue> reader = _reader ?? (_reader = _ctx.Reader<TValue>());
      _set((TTarget)target, reader.Read(r));
    }
  }

  static class Creator
  {
    public static Func<object> For(Type type) { return ((ICreator)Activator.CreateInstance(typeof(Creator<>).MakeGenericType(type))).Create; }
  }
  interface ICreator { object Create(); }
  sealed class Creator<T> : ICreator where T : new() { public object Create() { return new T(); } }

  public sealed class TypePlan
  {
    public Type Type;
    public Prop[] Props;          // in schema order (the order of FullPropertyInfo.GetStaticallyIncludedProps)
    public Prop[] ByWriterIndex;  // reading with a schema: the writer's property index -> local property (null: unknown here, skipped)
    public byte[] TypeIdEntry;    // key "" and the type id
    public Func<object> Create;

    public void Write(Writer w, object obj, bool withTypeId)
    {
      int max = Props.Length + (withTypeId ? 1 : 0);
      int at = w.ReserveMapHeader(max);
      int count = 0;
      if (withTypeId) { w.Raw(TypeIdEntry); count++; }
      Prop[] props = Props;
      for (int i = 0; i < props.Length; i++)
        if (props[i].TryWrite(w, obj)) count++;
      w.PatchMapHeader(at, max, count);
    }

    public object Read(Reader r, int count)
    {
      object obj = Create();
      int next = 0; // names: the property expected next (they are written in order), local: a shared field would be written by all threads
      for (int i = 0; i < count; i++)
      {
        byte c = r.Buf[r.Pos];
        Prop prop;
        if (c <= 0x7F) // schema index
        {
          r.Pos++;
          prop = c < ByWriterIndex.Length ? ByWriterIndex[c] : null;
        }
        else if ((c & 0xE0) == 0xA0 || c == 0xD9) // property name
        {
          int n = r.StrHeader();
          prop = FindByName(new ReadOnlySpan<byte>(r.Buf, r.Pos, n), ref next);
          r.Pos += n;
        }
        else
        {
          long index = r.Int64();
          prop = index >= 0 && index < ByWriterIndex.Length ? ByWriterIndex[index] : null;
        }

        if (prop is null) r.Skip();
        else prop.Read(r, obj);
      }
      return obj;
    }

    Prop FindByName(ReadOnlySpan<byte> name, ref int next)
    {
      Prop[] props = Props;
      if (next < props.Length && name.SequenceEqual(props[next].NameUtf8)) return props[next++];
      for (int i = 0; i < props.Length; i++)
        if (name.SequenceEqual(props[i].NameUtf8)) { next = i + 1; return props[i]; }
      return null;
    }
  }

  /// <summary>
  /// The plans of one mode: property names as keys, or the indexes of a (cached) schema.
  /// Built once and shared, the plans are only mutated while they are being built (the prototype does not lock).
  /// </summary>
  public sealed class Context
  {
    readonly IndexedSchemaTypeResolver _schema; // null: property names
    readonly MsgPackSettings _settings = new MsgPackSettings();
    readonly Dictionary<Type, TypePlan> _plans = new Dictionary<Type, TypePlan>();
    readonly Dictionary<Type, object> _writers = new Dictionary<Type, object>();
    readonly Dictionary<Type, object> _readers = new Dictionary<Type, object>();
    static readonly Func<Type, MsgPackSettings, FullPropertyInfo[]> StaticallyIncluded =
      (Func<Type, MsgPackSettings, FullPropertyInfo[]>)Delegate.CreateDelegate(typeof(Func<Type, MsgPackSettings, FullPropertyInfo[]>),
        typeof(FullPropertyInfo).GetMethod("GetStaticallyIncludedProps", BindingFlags.NonPublic | BindingFlags.Static));

    public Context(IndexedSchemaTypeResolver schema) { _schema = schema; }

    public TypePlan Plan(Type type)
    {
      if (_plans.TryGetValue(type, out TypePlan plan)) return plan;
      plan = new TypePlan { Type = type, Create = Creator.For(type) };
      _plans[type] = plan; // before the properties, the type graph may be recursive

      ComplexTypeDef def = null;
      if (_schema != null && !_schema.ByType.TryGetValue(type, out def))
        throw new NotSupportedException($"{type.Name} is not in the schema"); // a real implementation would extend the schema or fall back to names

      FullPropertyInfo[] included = StaticallyIncluded(type, _settings); // the same properties and order as the serializer
      List<Prop> props = new List<Prop>(included.Length);
      foreach (FullPropertyInfo full in included)
      {
        PropertyInfo pi = full.PropertyInfo;
        Prop prop = (Prop)Activator.CreateInstance(typeof(Prop<,>).MakeGenericType(pi.DeclaringType, pi.PropertyType), pi, this);
        prop.Name = pi.Name;
        prop.NameUtf8 = Encoding.UTF8.GetBytes(pi.Name);
        Writer key = new Writer(32);
        if (def is null) key.Str(pi.Name);
        else key.Int(def.IdByName[pi.Name]);
        prop.Key = key.ToArray();
        props.Add(prop);
      }
      plan.Props = props.ToArray();

      if (def != null)
      {
        Writer entry = new Writer(8);
        entry.Str(string.Empty);
        entry.Int(def.TypeId);
        plan.TypeIdEntry = entry.ToArray();

        // Bind the writer's property list (by name) to the local properties
        plan.ByWriterIndex = new Prop[def.Props.Count];
        for (int i = 0; i < def.Props.Count; i++)
          plan.ByWriterIndex[i] = plan.Props.FirstOrDefaultByName(def.Props[i]);
      }
      else
        plan.ByWriterIndex = new Prop[0];
      return plan;
    }

    public TypePlan PlanByTypeId(Reader r)
    {
      long id = r.Int64();
      if (_schema is null || id < 0 || id >= _schema.ByTypeId.Count || _schema.ByTypeId[(int)id].Type is null)
        throw new NotSupportedException("type ids need a schema in this prototype");
      return Plan(_schema.ByTypeId[(int)id].Type);
    }

    public ValueWriter<T> Writer<T>()
    {
      if (!_writers.TryGetValue(typeof(T), out object w)) _writers[typeof(T)] = w = CreateWriter(typeof(T));
      return (ValueWriter<T>)w;
    }

    public ValueReader<T> Reader<T>()
    {
      if (!_readers.TryGetValue(typeof(T), out object r)) _readers[typeof(T)] = r = CreateReader(typeof(T));
      return (ValueReader<T>)r;
    }

    object CreateWriter(Type t)
    {
      if (t == typeof(bool)) return new BoolRW();
      if (t == typeof(int)) return new Int32W();
      if (t == typeof(long)) return new Int64W();
      if (t == typeof(string)) return new StringW();
      if (t == typeof(Guid)) return new GuidW();
      if (t == typeof(decimal)) return new DecimalW();
      if (t == typeof(DateTime)) return new DateTimeW();
      if (t.IsEnum) return Activator.CreateInstance(typeof(EnumW<>).MakeGenericType(t));
      Type underlying = Nullable.GetUnderlyingType(t);
      if (underlying != null) return Activator.CreateInstance(typeof(NullableW<>).MakeGenericType(underlying), GetType().GetMethod(nameof(Writer)).MakeGenericMethod(underlying).Invoke(this, null));
      if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
      {
        Type e = t.GenericTypeArguments[0];
        Delegate factory = Delegate.CreateDelegate(typeof(Func<>).MakeGenericType(typeof(ValueWriter<>).MakeGenericType(e)), this, GetType().GetMethod(nameof(Writer)).MakeGenericMethod(e));
        return Activator.CreateInstance(typeof(ListW<>).MakeGenericType(e), factory);
      }
      if (t.IsClass && t != typeof(object) && !typeof(System.Collections.IEnumerable).IsAssignableFrom(t)) return Activator.CreateInstance(typeof(ObjectW<>).MakeGenericType(t), this);
      throw new NotSupportedException($"prototype: {t}");
    }

    object CreateReader(Type t)
    {
      if (t == typeof(bool)) return new BoolR();
      if (t == typeof(int)) return new Int32R();
      if (t == typeof(long)) return new Int64R();
      if (t == typeof(string)) return new StringR();
      if (t == typeof(Guid)) return new GuidR();
      if (t == typeof(decimal)) return new DecimalR();
      if (t == typeof(DateTime)) return new DateTimeR();
      if (t.IsEnum) return Activator.CreateInstance(typeof(EnumR<>).MakeGenericType(t));
      Type underlying = Nullable.GetUnderlyingType(t);
      if (underlying != null) return Activator.CreateInstance(typeof(NullableR<>).MakeGenericType(underlying), GetType().GetMethod(nameof(Reader)).MakeGenericMethod(underlying).Invoke(this, null));
      if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
      {
        Type e = t.GenericTypeArguments[0];
        Delegate factory = Delegate.CreateDelegate(typeof(Func<>).MakeGenericType(typeof(ValueReader<>).MakeGenericType(e)), this, GetType().GetMethod(nameof(Reader)).MakeGenericMethod(e));
        return Activator.CreateInstance(typeof(ListR<>).MakeGenericType(e), factory);
      }
      if (t.IsClass && t != typeof(object) && !typeof(System.Collections.IEnumerable).IsAssignableFrom(t)) return Activator.CreateInstance(typeof(ObjectR<>).MakeGenericType(t), this);
      throw new NotSupportedException($"prototype: {t}");
    }
  }

  static class PropExtensions
  {
    public static Prop FirstOrDefaultByName(this Prop[] props, string name)
    {
      foreach (Prop p in props) if (p.Name == name) return p;
      return null;
    }
  }

  /// <summary>
  /// Today's wire format (the schema inline, followed by the body) with process-local caches instead of a schema exchange:
  /// the writer copies the schema bytes it made once, the reader looks the incoming schema bytes up (content-addressed) and reuses the bound plans.
  /// </summary>
  public sealed class InlineSchemaSerializer
  {
    readonly byte[] _schemaBytes;
    readonly Context _writeCtx;
    readonly MsgPackSettings _settings;
    readonly Dictionary<ulong, KeyValuePair<byte[], Context>> _bySchema = new Dictionary<ulong, KeyValuePair<byte[], Context>>();
    byte[] _lastSchema;
    Context _lastCtx;
    [ThreadStatic] static Writer _writer;

    public InlineSchemaSerializer(byte[] schemaBytes, IndexedSchemaTypeResolver bound, MsgPackSettings settings)
    {
      _schemaBytes = schemaBytes;
      _writeCtx = new Context(bound);
      _settings = settings;
    }

    public byte[] Serialize<T>(T value)
    {
      Writer w = _writer ?? (_writer = new Writer());
      w.Pos = 0;
      w.Raw(_schemaBytes);
      _writeCtx.Writer<T>().Write(w, value);
      return w.ToArray();
    }

    public T Deserialize<T>(byte[] payload)
    {
      Reader r = new Reader(payload, 0);
      r.Skip(); // the schema
      ReadOnlySpan<byte> schema = new ReadOnlySpan<byte>(payload, 0, r.Pos);
      Context ctx;
      if (_lastSchema != null && schema.SequenceEqual(_lastSchema)) ctx = _lastCtx; // usually the same schema as the previous message
      else
      {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in schema) { hash ^= b; hash *= 1099511628211UL; }
        if (!_bySchema.TryGetValue(hash, out KeyValuePair<byte[], Context> entry) || !schema.SequenceEqual(entry.Key))
        {
          byte[] copy = schema.ToArray();
          IndexedSchemaTypeResolver bound = IndexedSchemaTypeResolver.Unpack(new System.IO.MemoryStream(copy), _settings);
          entry = new KeyValuePair<byte[], Context>(copy, new Context(bound));
          _bySchema[hash] = entry; // unbounded here; a real cache needs a size limit
        }
        _lastSchema = entry.Key;
        ctx = _lastCtx = entry.Value;
      }
      return ctx.Reader<T>().Read(r);
    }
  }

  /// <summary>Entry points: names (plain MsgPack, the same bytes as UseInexedSchema = false) or a cached schema (10 byte reference + body)</summary>
  public sealed class DirectSerializer
  {
    readonly Context _ctx;
    readonly byte[] _reference; // fixext8, ext type 2, 64 bit schema hash
    [ThreadStatic] static Writer _writer;

    public DirectSerializer(IndexedSchemaTypeResolver schema, ulong hash)
    {
      _ctx = new Context(schema);
      if (schema != null)
      {
        _reference = new byte[10];
        _reference[0] = 0xD7; _reference[1] = 2;
        BinaryPrimitives.WriteUInt64BigEndian(_reference.AsSpan(2), hash);
      }
    }

    public byte[] Serialize<T>(T value)
    {
      Writer w = _writer ?? (_writer = new Writer());
      w.Pos = 0;
      if (_reference != null) w.Raw(_reference);
      _ctx.Writer<T>().Write(w, value);
      return w.ToArray();
    }

    public T Deserialize<T>(byte[] payload)
    {
      int start = 0;
      if (_reference != null)
      {
        if (!payload.AsSpan(0, 10).SequenceEqual(_reference)) throw new FormatException("unknown schema"); // GetSchema(hash) would go here
        start = 10;
      }
      return _ctx.Reader<T>().Read(new Reader(payload, start));
    }
  }
}
