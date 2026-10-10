using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// How a framework value without settable properties is written: as a plain value both serializers write in their usual way (<see cref="FrameworkTypeInfo.ToPlain"/>).
  /// </summary>
  internal enum PlainForm
  {
    None,
    Half, // float32
    Text, // Version, StringBuilder: their text, CultureInfo: its name
    Rune, // its code point
    NInt, // nint as a long
    NUInt, // nuint as a ulong
    Binary, // Memory<byte>, ReadOnlyMemory<byte>, ArraySegment<byte>: bin, like a byte[]
    Complex, // [real, imaginary] (doubles)
    BigInteger // BigInteger, Int128, UInt128: an integer when it fits in 64 bits, otherwise extension type -2 (big-endian two's complement)
  }

  /// <summary>
  /// The plain value of an extension (<see cref="FrameworkTypeInfo.ToPlain"/>): a BigInteger beyond 64 bits.
  /// </summary>
  internal sealed class PlainExtension
  {
    internal readonly sbyte TypeCode;
    internal readonly byte[] Data;

    internal PlainExtension(sbyte typeCode, byte[] data)
    {
      TypeCode = typeCode;
      Data = data;
    }
  }

  /// <summary>
  /// Framework types without settable properties that are written like MessagePack-CSharp does: char as an integer, TimeSpan and TimeOnly as ticks, DateOnly as its day number, Uri as its original string,
  /// Half as a float32, Version and StringBuilder as their text, CultureInfo as its name, Rune as its code point, nint and nuint as integers, Memory&lt;byte&gt;, ReadOnlyMemory&lt;byte&gt; and ArraySegment&lt;byte&gt; as bin,
  /// Complex as [real, imaginary], KeyValuePair as [key, value] and tuples as an array of their items.
  /// BigInteger, Int128 and UInt128 are integers when they fit in 64 bits, otherwise extension type -2 (as the proposals for MsgPack's bigint, msgpack/msgpack#206, agree).
  /// <para>DateOnly, TimeOnly, Half, Rune, Int128 and UInt128 are found by name, .NET Standard does not have them.</para>
  /// </summary>
  internal static class FrameworkTypeInfo
  {
    internal static readonly Type DateOnlyType = typeof(DateTime).Assembly.GetType("System.DateOnly");
    internal static readonly Type TimeOnlyType = typeof(DateTime).Assembly.GetType("System.TimeOnly");
    internal static readonly Type HalfType = typeof(DateTime).Assembly.GetType("System.Half");
    internal static readonly Type RuneType = typeof(DateTime).Assembly.GetType("System.Text.Rune");
    internal static readonly Type Int128Type = typeof(DateTime).Assembly.GetType("System.Int128");
    internal static readonly Type UInt128Type = typeof(DateTime).Assembly.GetType("System.UInt128");

    /// <summary>
    /// The extension type of big integers (proposed in msgpack/msgpack#206, pull requests 248 and 249): the value in big-endian two's complement, as few bytes as possible.
    /// </summary>
    internal const sbyte BigIntegerExtensionType = -2;

    /// <summary>
    /// System.Collections.Immutable.ImmutableArray (the static class), null when the application does not have the assembly (.NET Standard does not reference it, .NET has it).
    /// </summary>
    internal static readonly Type ImmutableArrayType = Type.GetType("System.Collections.Immutable.ImmutableArray, System.Collections.Immutable", false);

    private static readonly ConcurrentDictionary<Type, PairInfo> Pairs = new ConcurrentDictionary<Type, PairInfo>();

    /// <summary>
    /// KeyValuePair&lt;TKey, TValue&gt; (Key and Value have no setters): an array [key, value], as MessagePack-CSharp writes it. Collections of pairs are maps (see CollectionInfo).
    /// </summary>
    internal static bool IsKeyValuePair(Type type)
    {
      return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);
    }

    /// <summary>
    /// How to read and create a KeyValuePair&lt;TKey, TValue&gt; (see <see cref="IsKeyValuePair"/>).
    /// </summary>
    internal static PairInfo GetPair(Type type)
    {
      return Pairs.GetOrAdd(type, t => new PairInfo(t));
    }

    internal sealed class PairInfo
    {
      internal readonly PropertyInfo Key;
      internal readonly PropertyInfo Value;
      internal readonly FullPropertyInfo KeyInfo;
      internal readonly FullPropertyInfo ValueInfo;
      private readonly Type _type;

      internal PairInfo(Type type)
      {
        _type = type;
        Key = type.GetProperty(nameof(KeyValuePair<object, object>.Key));
        Value = type.GetProperty(nameof(KeyValuePair<object, object>.Value));
        KeyInfo = new FullPropertyInfo(type.GenericTypeArguments[0]);
        ValueInfo = new FullPropertyInfo(type.GenericTypeArguments[1]);
      }

      internal object Create(object key, object value)
      {
        return Activator.CreateInstance(_type, key, value);
      }
    }

    #region Tuples

    private static readonly HashSet<Type> TupleDefinitions = new HashSet<Type>()
    {
      typeof(Tuple<>), typeof(Tuple<,>), typeof(Tuple<,,>), typeof(Tuple<,,,>), typeof(Tuple<,,,,>), typeof(Tuple<,,,,,>), typeof(Tuple<,,,,,,>), typeof(Tuple<,,,,,,,>),
      typeof(ValueTuple<>), typeof(ValueTuple<,>), typeof(ValueTuple<,,>), typeof(ValueTuple<,,,>), typeof(ValueTuple<,,,,>), typeof(ValueTuple<,,,,,>), typeof(ValueTuple<,,,,,,>), typeof(ValueTuple<,,,,,,,>)
    };

    private static readonly ConcurrentDictionary<Type, TupleInfo> Tuples = new ConcurrentDictionary<Type, TupleInfo>();

    /// <summary>
    /// Tuple&lt;...&gt; (Item1... have no setters) and ValueTuple&lt;...&gt; (fields, not properties): an array of the items, as MessagePack-CSharp and Nerdbank.MessagePack write them.
    /// The 8th item (Rest) is the tuple of the other items, an array in the array.
    /// </summary>
    internal static bool IsTuple(Type type)
    {
      return type.IsGenericType && TupleDefinitions.Contains(type.GetGenericTypeDefinition());
    }

    internal static TupleInfo GetTuple(Type type)
    {
      return Tuples.GetOrAdd(type, t => new TupleInfo(t));
    }

    internal sealed class TupleInfo
    {
      /// <summary>
      /// The items as they are declared (their values get type ids when they are of other types).
      /// </summary>
      internal readonly FullPropertyInfo[] Items;
      private readonly MemberInfo[] _members;
      private readonly object[] _defaults;
      private readonly ConstructorInfo _constructor;

      internal TupleInfo(Type type)
      {
        Type[] arguments = type.GenericTypeArguments;
        Items = new FullPropertyInfo[arguments.Length];
        _members = new MemberInfo[arguments.Length];
        _defaults = new object[arguments.Length];
        for (int t = 0; t < arguments.Length; t++)
        {
          string name = t == 7 ? "Rest" : "Item" + (t + 1).ToString(CultureInfo.InvariantCulture);
          _members[t] = type.IsValueType ? (MemberInfo)type.GetField(name) : type.GetProperty(name);
          Items[t] = new FullPropertyInfo(arguments[t]);
          _defaults[t] = arguments[t].IsValueType ? Activator.CreateInstance(arguments[t]) : null;
        }
        _constructor = type.GetConstructor(arguments);
      }

      internal object GetItem(object tuple, int index)
      {
        MemberInfo member = _members[index];
        return member is FieldInfo field ? field.GetValue(tuple) : ((PropertyInfo)member).GetValue(tuple);
      }

      /// <param name="items">The items, converted to the types of <see cref="Items"/>, a missing item is the default of its type</param>
      internal object Create(object[] items)
      {
        object[] args = new object[_defaults.Length];
        for (int t = args.Length - 1; t >= 0; t--)
          args[t] = t < items.Length && items[t] != null ? items[t] : _defaults[t];
        return Invoke(() => _constructor.Invoke(args));
      }
    }

    #endregion

    #region Plain forms

    private static readonly MethodInfo HalfToSingle = Operator(HalfType, HalfType, typeof(float));
    private static readonly MethodInfo DoubleToHalf = Operator(HalfType, typeof(double), HalfType);
    private static readonly MethodInfo Int128ToBig = Operator(typeof(BigInteger), Int128Type, typeof(BigInteger));
    private static readonly MethodInfo BigToInt128 = Operator(typeof(BigInteger), typeof(BigInteger), Int128Type);
    private static readonly MethodInfo UInt128ToBig = Operator(typeof(BigInteger), UInt128Type, typeof(BigInteger));
    private static readonly MethodInfo BigToUInt128 = Operator(typeof(BigInteger), typeof(BigInteger), UInt128Type);
    private static readonly BigInteger LongMin = long.MinValue;
    private static readonly BigInteger LongMax = long.MaxValue;
    private static readonly BigInteger ULongMax = ulong.MaxValue;
    private static readonly PropertyInfo RuneValue = RuneType?.GetProperty("Value");
    private static readonly ConstructorInfo RuneFromInt = RuneType?.GetConstructor(new[] { typeof(int) });

    /// <summary>
    /// A conversion operator (op_Explicit or op_Implicit) of <paramref name="declaringType"/>, found by its parameter and return type (the overloads differ only by their return type).
    /// </summary>
    private static MethodInfo Operator(Type declaringType, Type from, Type to)
    {
      if (declaringType is null)
        return null;
      MethodInfo[] methods = declaringType.GetMethods(BindingFlags.Public | BindingFlags.Static);
      for (int t = 0; t < methods.Length; t++)
      {
        MethodInfo method = methods[t];
        if ((method.Name == "op_Explicit" || method.Name == "op_Implicit") && method.ReturnType == to)
        {
          ParameterInfo[] parameters = method.GetParameters();
          if (parameters.Length == 1 && parameters[0].ParameterType == from)
            return method;
        }
      }
      return null;
    }

    /// <summary>
    /// The plain form of a type (exactly this type, not derived ones), <see cref="PlainForm.None"/> for any other type.
    /// </summary>
    internal static PlainForm GetPlainForm(Type type)
    {
      if (!type.IsValueType) // LsMsgPack asks for every object and collection
        return type == typeof(Version) || type == typeof(StringBuilder) || type == typeof(CultureInfo) ? PlainForm.Text : PlainForm.None;
      if (type == HalfType && HalfType != null) return PlainForm.Half;
      if (type == RuneType && RuneType != null) return PlainForm.Rune;
      if (type == typeof(IntPtr)) return PlainForm.NInt;
      if (type == typeof(UIntPtr)) return PlainForm.NUInt;
      if (type == typeof(Memory<byte>) || type == typeof(ReadOnlyMemory<byte>) || type == typeof(ArraySegment<byte>)) return PlainForm.Binary;
      if (type == typeof(Complex)) return PlainForm.Complex;
      if (IsBigInteger(type)) return PlainForm.BigInteger;
      return PlainForm.None;
    }

    /// <summary>
    /// BigInteger, Int128 or UInt128.
    /// </summary>
    internal static bool IsBigInteger(Type type)
    {
      return type == typeof(BigInteger) || (type != null && (type == Int128Type || type == UInt128Type));
    }

    /// <param name="value">A BigInteger, Int128 or UInt128</param>
    internal static BigInteger ToBigInteger(object value)
    {
      if (value is BigInteger big)
        return big;
      return (BigInteger)Invoke(() => (value.GetType() == Int128Type ? Int128ToBig : UInt128ToBig).Invoke(null, new[] { value }));
    }

    /// <exception cref="OverflowException">The value does not fit in Int128 or UInt128</exception>
    private static object FromBigInteger(BigInteger value, Type targetType)
    {
      if (targetType == typeof(BigInteger))
        return value;
      return Invoke(() => (targetType == Int128Type ? BigToInt128 : BigToUInt128).Invoke(null, new object[] { value }));
    }

    /// <summary>
    /// A long or ulong when the value fits in 64 bits (a long for the signed types, a ulong for UInt128 and values above long.MaxValue), otherwise extension type -2.
    /// </summary>
    private static object BigIntegerPlain(object value)
    {
      BigInteger big = ToBigInteger(value);
      bool unsigned = value.GetType() == UInt128Type;
      if (!unsigned && big >= LongMin && big <= LongMax)
        return (long)big;
      if (big.Sign >= 0 && big <= ULongMax)
        return (ulong)big;
      byte[] bytes = big.ToByteArray(); // little-endian two's complement, as short as possible
      Array.Reverse(bytes);
      return new PlainExtension(BigIntegerExtensionType, bytes);
    }

    /// <summary>
    /// MessagePack-CSharp's bin of a big integer (<c>LtMsgPackOptions.BigIntegerFormat.Binary</c>): little-endian two's complement, BigInteger.ToByteArray() or the 16 bytes of an Int128 or UInt128.
    /// </summary>
    internal static byte[] BigIntegerBinary(object value)
    {
      BigInteger big = ToBigInteger(value);
      byte[] bytes = big.ToByteArray();
      if (value is BigInteger)
        return bytes;
      byte[] fixedLength = new byte[16];
      if (big.Sign < 0)
        for (int t = fixedLength.Length - 1; t >= 0; t--)
          fixedLength[t] = 0xFF;
      Buffer.BlockCopy(bytes, 0, fixedLength, 0, Math.Min(bytes.Length, 16)); // a UInt128 above Int128.MaxValue has a 17th byte 0 (the sign)
      return fixedLength;
    }

    /// <summary>
    /// A big integer read from MessagePack-CSharp's bin (little-endian two's complement; an UInt128 is unsigned).
    /// </summary>
    private static object BigIntegerOfBinary(byte[] bytes, Type targetType)
    {
      if (targetType == UInt128Type)
      {
        byte[] unsigned = new byte[bytes.Length + 1];
        Buffer.BlockCopy(bytes, 0, unsigned, 0, bytes.Length);
        bytes = unsigned;
      }
      return FromBigInteger(new BigInteger(bytes), targetType);
    }

    /// <summary>
    /// Converts an extension without a registered custom extension: a big integer (type -2) read into BigInteger, Int128 or UInt128.
    /// </summary>
    internal static bool TryConvertExtension(IMsgPackExtension extension, Type targetType, out object result)
    {
      result = null;
      if (extension.TypeSpecifier != BigIntegerExtensionType || !IsBigInteger(targetType))
        return false;
      byte[] bytes = (byte[])extension.Data.Clone();
      Array.Reverse(bytes); // big-endian to the little-endian of BigInteger
      result = FromBigInteger(bytes.Length == 0 ? BigInteger.Zero : new BigInteger(bytes), targetType);
      return true;
    }

    /// <summary>
    /// The value both serializers write for a value of a type with a <see cref="PlainForm"/>: a float, string, int, long, ulong, byte[] (bin), double[] (an array of doubles) or <see cref="PlainExtension"/>.
    /// </summary>
    internal static object ToPlain(object value, PlainForm form)
    {
      switch (form)
      {
        case PlainForm.Half: return (float)HalfToSingle.Invoke(null, new[] { value });
        case PlainForm.Text: return value is CultureInfo culture ? culture.Name : value.ToString();
        case PlainForm.Rune: return (int)RuneValue.GetValue(value);
        case PlainForm.NInt: return ((IntPtr)value).ToInt64();
        case PlainForm.NUInt: return ((UIntPtr)value).ToUInt64();
        case PlainForm.Binary:
          if (value is Memory<byte> memory) return memory.ToArray();
          if (value is ReadOnlyMemory<byte> readOnly) return readOnly.ToArray();
          ArraySegment<byte> segment = (ArraySegment<byte>)value;
          return segment.Array is null ? new byte[0] : new ReadOnlySpan<byte>(segment.Array, segment.Offset, segment.Count).ToArray();
        case PlainForm.Complex:
          Complex complex = (Complex)value;
          return new double[] { complex.Real, complex.Imaginary };
        case PlainForm.BigInteger: return BigIntegerPlain(value);
      }
      throw new InvalidOperationException($"{value.GetType()} has no plain form.");
    }

    /// <summary>
    /// A MsgPack array read into a framework type that is written as an array: Complex ([real, imaginary]), DateTimeOffset ([timestamp, offset in minutes], see <see cref="MsgPackOptions.DateTimeOffsetFormat"/>).
    /// </summary>
    internal static bool TryConvertItems(object[] items, Type targetType, MsgPackOptions settings, out object result)
    {
      result = null;
      if (targetType == typeof(DateTimeOffset) && items.Length == 2 && items[0] is DateTime timestamp && IsInteger(items[1]))
      {
        long utcTicks = timestamp.Kind == DateTimeKind.Unspecified ? timestamp.Ticks : timestamp.ToUniversalTime().Ticks; // the moment as it was read (ReadDateTimeKind), Unspecified is the UTC clock time
        result = settings.OffsetOfArray(utcTicks, Convert.ToInt64(items[1], CultureInfo.InvariantCulture));
        return true;
      }
      if (targetType == typeof(Complex) && items.Length == 2 && IsNumber(items[0]) && IsNumber(items[1]))
      {
        result = new Complex(Convert.ToDouble(items[0], CultureInfo.InvariantCulture), Convert.ToDouble(items[1], CultureInfo.InvariantCulture));
        return true;
      }
      return false;
    }

    #endregion

    internal static readonly PropertyInfo DayNumber = DateOnlyType?.GetProperty("DayNumber");
    private static readonly MethodInfo FromDayNumber = DateOnlyType?.GetMethod("FromDayNumber", new[] { typeof(int) });
    internal static readonly PropertyInfo TimeOnlyTicks = TimeOnlyType?.GetProperty("Ticks");
    private static readonly ConstructorInfo TimeOnlyFromTicks = TimeOnlyType?.GetConstructor(new[] { typeof(long) });

    /// <summary>
    /// Converts the unpacked value of one of these types (char is converted like the other primitives).
    /// </summary>
    /// <returns>False when the target type is none of these types, or the value is not what they are written as</returns>
    internal static bool TryConvert(object val, Type targetType, out object result)
    {
      result = null;
      if (val is string text)
      {
        if (targetType == typeof(Uri))
          result = new Uri(text, UriKind.RelativeOrAbsolute);
        else if (targetType == typeof(Version))
          result = Version.Parse(text);
        else if (targetType == typeof(StringBuilder))
          result = new StringBuilder(text);
        else if (targetType == typeof(CultureInfo))
          result = CultureInfo.GetCultureInfo(text);
        else
          return false;
        return true;
      }

      if (val is byte[] bytes)
      {
        if (IsBigInteger(targetType))
          result = BigIntegerOfBinary(bytes, targetType); // MessagePack-CSharp
        else if (targetType == typeof(Memory<byte>))
          result = new Memory<byte>(bytes);
        else if (targetType == typeof(ReadOnlyMemory<byte>))
          result = new ReadOnlyMemory<byte>(bytes);
        else if (targetType == typeof(ArraySegment<byte>))
          result = new ArraySegment<byte>(bytes);
        else
          return false;
        return true;
      }

      if (targetType == HalfType && HalfType != null && IsNumber(val))
      {
        result = DoubleToHalf.Invoke(null, new object[] { Convert.ToDouble(val, CultureInfo.InvariantCulture) });
        return true;
      }

      if (!IsInteger(val))
        return false;
      else if (IsBigInteger(targetType))
        result = FromBigInteger(val is ulong unsigned ? new BigInteger(unsigned) : new BigInteger(Convert.ToInt64(val, CultureInfo.InvariantCulture)), targetType);
      else if (targetType == typeof(TimeSpan))
        result = new TimeSpan(Convert.ToInt64(val, CultureInfo.InvariantCulture));
      else if (targetType == DateOnlyType)
        result = Invoke(() => FromDayNumber.Invoke(null, new object[] { Convert.ToInt32(val, CultureInfo.InvariantCulture) }));
      else if (targetType == TimeOnlyType)
        result = Invoke(() => TimeOnlyFromTicks.Invoke(new object[] { Convert.ToInt64(val, CultureInfo.InvariantCulture) }));
      else if (targetType == RuneType && RuneType != null)
        result = Invoke(() => RuneFromInt.Invoke(new object[] { Convert.ToInt32(val, CultureInfo.InvariantCulture) }));
      else if (targetType == typeof(IntPtr))
        result = new IntPtr(Convert.ToInt64(val, CultureInfo.InvariantCulture));
      else if (targetType == typeof(UIntPtr))
        result = new UIntPtr(Convert.ToUInt64(val, CultureInfo.InvariantCulture));
      else
        return false;

      return true;
    }

    private static bool IsInteger(object val)
    {
      return val is int || val is long || val is byte || val is sbyte || val is short || val is ushort || val is uint || val is ulong;
    }

    private static bool IsNumber(object val)
    {
      return val is float || val is double || IsInteger(val);
    }

    /// <summary>
    /// Rethrows the exception of the invoked method (e.g. an out of range day number) as it is, not as a TargetInvocationException.
    /// </summary>
    private static object Invoke(Func<object> invoke)
    {
      try
      {
        return invoke();
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null)
      {
        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw; // not reached
      }
    }
  }
}
