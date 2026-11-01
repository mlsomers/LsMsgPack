using LsMsgPack.Meta;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace LtMsgPack.Data
{
  /// <summary>
  /// The .NET types of columns as a dump records them: by name in the index (<see cref="MsgPackDataStreams.ColumnsKey"/>), or as the kinds of schema version 2.
  /// Only these types: the data never picks another one (docs/security.md).
  /// </summary>
  internal static class ColumnTypes
  {
    private static readonly Dictionary<string, Type> ByName = Create();

    private static Dictionary<string, Type> Create()
    {
      Type[] types =
      {
        typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal), typeof(string), typeof(char), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan),
        typeof(Guid), typeof(byte[]), typeof(object),
      };
      Dictionary<string, Type> byName = new Dictionary<string, Type>(StringComparer.Ordinal);
      for (int t = 0; t < types.Length; t++)
        byName[types[t].FullName] = types[t];
      if (FrameworkTypeInfo.DateOnlyType != null)
        byName[FrameworkTypeInfo.DateOnlyType.FullName] = FrameworkTypeInfo.DateOnlyType;
      if (FrameworkTypeInfo.TimeOnlyType != null)
        byName[FrameworkTypeInfo.TimeOnlyType.FullName] = FrameworkTypeInfo.TimeOnlyType;
      return byName;
    }

    /// <summary>
    /// The name a dump records for the field type of a column (another type is recorded as object).
    /// </summary>
    internal static string NameOf(Type type)
    {
      return type != null && ByName.ContainsKey(type.FullName) ? type.FullName : typeof(object).FullName;
    }

    /// <returns>null for a name that is not one of the column types</returns>
    internal static Type FromName(object name)
    {
      return name is string text && ByName.TryGetValue(text, out Type type) ? type : null;
    }

    /// <summary>
    /// The type of a type expression of schema version 2 (a kind, or <c>["?", kind]</c>), object for anything else.
    /// </summary>
    internal static Type FromKind(object expression)
    {
      if (expression is object[] items && items.Length == 2 && "?".Equals(items[0]))
        expression = items[1];
      switch (expression as string)
      {
        case "bool": return typeof(bool);
        case "int": return typeof(int);
        case "long": return typeof(long);
        case "ulong": return typeof(ulong);
        case "float": return typeof(double);
        case "decimal": return typeof(decimal);
        case "string": return typeof(string);
        case "char": return typeof(char);
        case "bin": return typeof(byte[]);
        case "guid": return typeof(Guid);
        case "datetime": return typeof(DateTime);
        case "dto":
        case "dto-clock": return typeof(DateTimeOffset);
        case "timespan": return typeof(TimeSpan);
        case "dateonly": return FrameworkTypeInfo.DateOnlyType ?? typeof(object);
        case "timeonly": return FrameworkTypeInfo.TimeOnlyType ?? typeof(object);
        default: return typeof(object);
      }
    }

    /// <summary>
    /// A key of an index point (plain values only): the value of the point column, as near as a plain value gets.
    /// </summary>
    internal static object PlainKey(object value)
    {
      switch (value)
      {
        case null:
        case DBNull _: return null;
        case bool _:
        case string _:
        case byte[] _:
        case DateTime _:
        case float _:
        case double _:
        case sbyte _:
        case short _:
        case int _:
        case long _:
        case byte _:
        case ushort _:
        case uint _:
        case ulong _: return value;
        case DateTimeOffset offset: return offset.UtcDateTime;
        case TimeSpan span: return span.Ticks;
        case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture); // decimal, Guid, char...
        default: return value.ToString();
      }
    }

    /// <summary>
    /// The columns as a dump records them in its index entry: <c>[[name, type, dataTypeName], ...]</c>.
    /// </summary>
    internal static List<object> Describe(RowPlan plan)
    {
      List<object> columns = new List<object>(plan.Names.Length);
      for (int t = 0; t < plan.Names.Length; t++)
        columns.Add(new List<object> { plan.Names[t], NameOf(plan.Types[t]), plan.DataTypeNames[t] });
      return columns;
    }
  }
}
