using LsMsgPack.Meta;
using LtMsgPack.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;

namespace LtMsgPack.Writing
{
  /// <summary>
  /// How a value of a (runtime) type is written, decided in the same order as LsMsgPack's MsgPackItem.Pack.
  /// </summary>
  internal enum TypeKind
  {
    Bool, SByte, Int16, Int32, Int64, Byte, UInt16, UInt32, UInt64, Single, Double, String,
    Bin, // byte[] (and sbyte[], which the CLR considers a byte[])
    Guid, DateTime, DateTimeOffset, Extension, Enum,
    Map, // dictionaries and arrays of KeyValuePair<,>
    Array, // other collections
    Char, TimeSpan, Uri, DateOnly, TimeOnly,
    RawExtension, // a MsgPackExtension that was read (LsMsgPack writes the MpExt it read as itself)
    GuidString, DecimalString, DateTimeOffsetArray, // the formats of other libraries (LtMsgPackOptions.GuidFormat, DecimalFormat, DateTimeOffsetFormat)
    Complex // an object with properties
  }

  internal static class TypeKinds
  {
    /// <param name="extension">The custom extension of <see cref="TypeKind.Extension"/></param>
    internal static TypeKind Classify(Type type, LtExtension[] extensions, out LtExtension extension)
    {
      extension = null;
      if (type == typeof(bool)) return TypeKind.Bool;
      if (type == typeof(sbyte)) return TypeKind.SByte;
      if (type == typeof(short)) return TypeKind.Int16;
      if (type == typeof(int)) return TypeKind.Int32;
      if (type == typeof(long)) return TypeKind.Int64;
      if (type == typeof(byte)) return TypeKind.Byte;
      if (type == typeof(ushort)) return TypeKind.UInt16;
      if (type == typeof(uint)) return TypeKind.UInt32;
      if (type == typeof(ulong)) return TypeKind.UInt64;
      if (type == typeof(float)) return TypeKind.Single;
      if (type == typeof(double)) return TypeKind.Double;
      if (type == typeof(string)) return TypeKind.String;
      if (type == typeof(byte[]) || type == typeof(sbyte[])) return TypeKind.Bin;
      if (type == typeof(Guid)) return TypeKind.Guid;
      if (type.IsArray && type.GetArrayRank() == 1 && !type.GetElementType().IsValueType) return TypeKind.Array; // "value is object[]" (array covariance)
      if (type == typeof(DateTime)) return TypeKind.DateTime;
      if (type == typeof(DateTimeOffset)) return TypeKind.DateTimeOffset;

      for (int t = 0; t < extensions.Length; t++)
      {
        if (extensions[t] != null && extensions[t].SupportsType(type))
        {
          extension = extensions[t];
          return TypeKind.Extension;
        }
      }

      if (type.IsEnum) return TypeKind.Enum;
      if (IsArrayOfKeyValuePairs(type)) return TypeKind.Map;
      if (typeof(IDictionary).IsAssignableFrom(type)) return TypeKind.Map;
      if (type.IsArray) return TypeKind.Array;
      if (typeof(IEnumerable).IsAssignableFrom(type)) return TypeKind.Array;
      if (type == typeof(MsgPackExtension)) return TypeKind.RawExtension;
      if (type == typeof(char)) return TypeKind.Char;
      if (type == typeof(TimeSpan)) return TypeKind.TimeSpan;
      if (typeof(Uri).IsAssignableFrom(type)) return TypeKind.Uri;
      if (type == FrameworkTypeInfo.DateOnlyType) return TypeKind.DateOnly;
      if (type == FrameworkTypeInfo.TimeOnlyType) return TypeKind.TimeOnly;
      return TypeKind.Complex;
    }

    /// <summary>
    /// Values that are not wrapped with a type id even when they are assigned to another type (see LsMsgPack's SerializeObject).
    /// </summary>
    internal static bool NeverWrapped(Type type)
    {
      return type.IsPrimitive || type == typeof(string);
    }

    private static bool IsArrayOfKeyValuePairs(Type type)
    {
      if (!type.IsArray)
        return false;
      for (Type element = type.GetElementType(); element != null && element != typeof(object); element = element.BaseType)
      {
        if ((element.IsGenericType ? element.GetGenericTypeDefinition() : element) == typeof(KeyValuePair<,>))
          return true;
      }
      return false;
    }
  }
}
