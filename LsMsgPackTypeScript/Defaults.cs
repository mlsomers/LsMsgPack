using LsMsgPack;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace LsMsgPackTypeScript
{
  /// <summary>
  /// The initial values of the fields of the TypeScript classes: what the .NET reader leaves in a property the data does not set (nil or left out), the value the constructor gave it.
  /// The class is created as the serializer creates it (its parameterless constructor, otherwise uninitialized: the defaults of the types), twice: a value that differs between the two
  /// (DateTime.Now, Guid.NewGuid()) gets the default of its type, the generated class cannot repeat it.
  /// </summary>
  internal sealed class Defaults
  {
    private const int MaxDepth = 8;
    private readonly ModelSet _models;
    private readonly GeneratorOptions _options;
    private readonly Dictionary<Type, Dictionary<string, string>> _byClass = new Dictionary<Type, Dictionary<string, string>>();
    private readonly List<string> _warnings;

    internal Defaults(ModelSet models, GeneratorOptions options, List<string> warnings)
    {
      _models = models;
      _options = options;
      _warnings = warnings;
    }

    /// <summary>
    /// The initializer of each serialized property of the class (inherited ones too), and a comment where the constructor's value was not taken.
    /// </summary>
    internal Dictionary<string, string> Of(ModelClass model, int depth = 0)
    {
      if (_byClass.TryGetValue(model.Type, out Dictionary<string, string> known))
        return known;
      Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
      _byClass.Add(model.Type, values); // before the values: a property of its own class (a tree) is made with new

      object first = model.IsAbstract ? null : Create(model.Type);
      if (first != null)
        System.Threading.Thread.Sleep(2); // DateTime.Now in a constructor: another value for the second instance
      object second = model.IsAbstract ? null : Create(model.Type);
      foreach (ModelProperty prop in model.AllProps)
      {
        Spec spec = _models.SpecOf(prop.Type);
        string value = TypeDefault(spec);
        if (first != null)
        {
          object x = Read(first, prop), y = Read(second, prop);
          string a = Value(x, spec, depth);
          string b = Value(y, spec, depth);
          if (a == null || b == null)
            _warnings.Add(string.Concat(model.Type.FullName, ".", prop.Name, ": the constructor's value has no TypeScript form, the class gets the default of the type."));
          else if (!string.Equals(a, b, StringComparison.Ordinal) || (IsScalar(x) && !Equals(x, y)))
            _warnings.Add(string.Concat(model.Type.FullName, ".", prop.Name, ": the constructor sets another value each time (", a, "), the class gets the default of the type."));
          else
            value = a;
        }
        values[prop.Name] = value;
      }
      return values;
    }

    private static bool IsScalar(object value)
    {
      return value != null && (value.GetType().IsPrimitive || value is DateTime || value is DateTimeOffset || value is Guid || value is TimeSpan || value is decimal);
    }

    private static object Create(Type type)
    {
      try
      {
        if (type.IsValueType || type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null)
          return Activator.CreateInstance(type, true); // as MsgPackOptions.ObjectCreation ConstructorOrUninitialized
        return RuntimeHelpers.GetUninitializedObject(type);
      }
      catch (Exception)
      {
        return null;
      }
    }

    private static object Read(object instance, ModelProperty prop)
    {
      try
      {
        return instance.GetType().GetProperty(prop.Name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance);
      }
      catch (Exception) // e.g. AmbiguousMatchException (new hides an inherited property), or the getter throws
      {
        return null;
      }
    }

    /// <summary>
    /// The value a left out property gets when the class does not set it: what the readers without classes use (the client's defaults, docs/TypeScriptClient.md "Values").
    /// </summary>
    internal string TypeDefault(Spec spec)
    {
      switch (spec.Name)
      {
        case "bool": return "false";
        case "int":
        case "float": return EnumDefault(spec) ?? "0";
        case "long":
        case "ulong": return EnumDefault(spec) ?? (_options.Int64 == "bigint" ? "0n" : "0");
        case "bigint": return "0n";
        case "decimal": return _options.Decimal == "string" ? "'0'" : "0";
        case "char": return "'\\0'";
        case "guid": return "'00000000-0000-0000-0000-000000000000'";
        case "datetime": return _options.Dates == "iso" ? "'0001-01-01T00:00:00.000Z'" : "new Date(-62135596800000)";
        case "dto":
        case "dto-clock": return _options.Dates == "iso" ? "'0001-01-01T00:00:00.000+00:00'" : "new Date(-62135596800000)";
        case "timespan": return "'00:00:00'";
        case "dateonly": return "'0001-01-01'";
        case "timeonly": return "'00:00:00'";
        case "struct": return string.Concat("new ", spec.Class.TsName, "()");
        case "pair": return string.Concat("{ Key: ", TypeDefault(spec.Items[0]), ", Value: ", TypeDefault(spec.Items[1]), " }");
      }
      return "null";
    }

    private string EnumDefault(Spec spec)
    {
      if (spec.Enum is null)
        return null;
      return Value(Activator.CreateInstance(spec.Enum), spec, 0);
    }

    /// <summary>
    /// A value as a TypeScript expression of the form the client reads it in (the options), null when it has none.
    /// </summary>
    internal string Value(object value, Spec spec, int depth)
    {
      if (value is null)
        return spec.Name == "struct" || spec.Name == "pair" ? TypeDefault(spec) : "null";
      if (depth > MaxDepth)
        return null;
      if (spec.Name == "?")
        return Value(value, spec.Items[0], depth);

      switch (spec.Name)
      {
        case "bool": return (bool)value ? "true" : "false";
        case "int":
          if (spec.Enum != null)
            return EnumMember(spec.Enum, value);
          return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        case "long":
        case "ulong":
          if (spec.Enum != null)
            return EnumMember(spec.Enum, value);
          string integer = Convert.ToString(value is Enum ? Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()), CultureInfo.InvariantCulture) : value, CultureInfo.InvariantCulture);
          return _options.Int64 == "bigint" ? integer + "n" : integer;
        case "float": return Number(Convert.ToDouble(value, CultureInfo.InvariantCulture));
        case "bigint": return string.Concat(Convert.ToString(value, CultureInfo.InvariantCulture), "n");
        case "decimal":
          string text = ((decimal)value).ToString(CultureInfo.InvariantCulture);
          return _options.Decimal == "string" ? Quote(text) : text;
        case "string":
        case "uri":
          return Quote(value is Uri uri ? uri.OriginalString : Convert.ToString(value, CultureInfo.InvariantCulture));
        case "char": return Quote(((char)value).ToString());
        case "guid": return Quote(((Guid)value).ToString("D"));
        case "datetime": return Moment(_models.Settings.ToTimestamp((DateTime)value), 0, false);
        case "dto":
        case "dto-clock":
          DateTimeOffset offset = (DateTimeOffset)value;
          return Moment(offset.UtcDateTime, (int)offset.Offset.TotalMinutes, true);
        case "timespan": return Quote(((TimeSpan)value).ToString("c", CultureInfo.InvariantCulture));
        case "dateonly": return Quote(((DateOnly)value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        case "timeonly":
          TimeOnly time = (TimeOnly)value;
          return Quote(time.Ticks % TimeSpan.TicksPerSecond == 0 ? time.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : time.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
        case "bin": return Bytes(value);
        case "list": return List(value, spec.Items[0], depth);
        case "dict": return Dictionary(value, spec.Items[1], depth);
        case "pair":
          object key = value.GetType().GetProperty("Key").GetValue(value);
          object item = value.GetType().GetProperty("Value").GetValue(value);
          string k = Value(key, spec.Items[0], depth + 1), v = Value(item, spec.Items[1], depth + 1);
          return k == null || v == null ? null : string.Concat("{ Key: ", k, ", Value: ", v, " }");
        case "tuple":
          List<string> items = new List<string>();
          for (int t = 0; t < spec.Items.Length; t++)
          {
            object element = TupleItem(value, t);
            string e = Value(element, spec.Items[t], depth + 1);
            if (e == null)
              return null;
            items.Add(e);
          }
          return string.Concat("[", string.Join(", ", items), "]");
        case "class":
        case "struct":
          return Object(value, depth);
      }
      // any: a value of a known form
      if (value is string || value is bool || value is int || value is long || value is double)
        return Value(value, _models.SpecOf(value.GetType()), depth);
      ModelClass model = _models.ClassOf(value.GetType());
      return model is null ? null : Object(value, depth);
    }

    private static object TupleItem(object tuple, int index)
    {
      string name = index < 7 ? "Item" + (index + 1).ToString(CultureInfo.InvariantCulture) : "Rest";
      return (object)tuple.GetType().GetField(name)?.GetValue(tuple) ?? tuple.GetType().GetProperty(name)?.GetValue(tuple);
    }

    /// <summary>
    /// An object of a model: <c>new X()</c>, with the values that differ from what its class sets.
    /// </summary>
    private string Object(object value, int depth)
    {
      ModelClass model = _models.ClassOf(value.GetType());
      if (model is null || model.IsAbstract)
        return null;
      Dictionary<string, string> defaults = Of(model, depth + 1);
      List<string> differ = new List<string>();
      foreach (ModelProperty prop in model.AllProps)
      {
        string v = Value(Read(value, prop), _models.SpecOf(prop.Type), depth + 1);
        if (v == null)
          return null;
        if (!defaults.TryGetValue(prop.Name, out string d) || !string.Equals(d, v, StringComparison.Ordinal))
          differ.Add(string.Concat(TypeScriptWriter.PropertyKey(prop.Name), ": ", v));
      }
      string created = string.Concat("new ", model.TsName, "()");
      return differ.Count == 0 ? created : string.Concat("Object.assign(", created, ", { ", string.Join(", ", differ), " })");
    }

    private string List(object value, Spec element, int depth)
    {
      List<string> items = new List<string>();
      foreach (object item in (IEnumerable)value)
      {
        string v = Value(item, element, depth + 1);
        if (v == null)
          return null;
        items.Add(v);
      }
      return string.Concat("[", string.Join(", ", items), "]");
    }

    private string Dictionary(object value, Spec valueSpec, int depth)
    {
      List<string> entries = new List<string>();
      foreach (object entry in (IEnumerable)value)
      {
        Type type = entry.GetType();
        object key = entry is DictionaryEntry de ? de.Key : type.GetProperty("Key")?.GetValue(entry);
        object item = entry is DictionaryEntry de2 ? de2.Value : type.GetProperty("Value")?.GetValue(entry);
        string text = Convert.ToString(key, CultureInfo.InvariantCulture);
        string v = Value(item, valueSpec, depth + 1);
        if (v == null || text == "__proto__")
          return null;
        entries.Add(string.Concat(Quote(text), ": ", v));
      }
      return entries.Count == 0 ? "{}" : string.Concat("{ ", string.Join(", ", entries), " }");
    }

    private static string Bytes(object value)
    {
      byte[] bytes = value as byte[] ?? (value is sbyte[] signed ? signed.Select(b => (byte)b).ToArray() : null);
      if (bytes is null)
        return null;
      return string.Concat("new Uint8Array([", string.Join(", ", bytes.Select(b => b.ToString(CultureInfo.InvariantCulture))), "])");
    }

    private string EnumMember(Type type, object value)
    {
      object underlying = Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture);
      string name = Enum.GetName(type, value);
      bool emitted = _models.Enums.Contains(type);
      if (name != null && emitted && !type.IsDefined(typeof(FlagsAttribute), false))
        return string.Concat(_models.EnumName(type), ".", TypeScriptWriter.Member(name));
      bool int64 = Enum.GetUnderlyingType(type) == typeof(long) || Enum.GetUnderlyingType(type) == typeof(ulong);
      string text = Convert.ToString(underlying, CultureInfo.InvariantCulture);
      return int64 && _options.Int64 == "bigint" ? text + "n" : text;
    }

    /// <param name="utc">The moment</param>
    private string Moment(DateTime utc, int offsetMinutes, bool withOffset)
    {
      long ms = (long)Math.Floor((utc - DateTime.UnixEpoch).TotalMilliseconds);
      if (_options.Dates != "iso")
        return string.Concat("new Date(", ms.ToString(CultureInfo.InvariantCulture), ")");
      DateTime moment = DateTime.UnixEpoch.AddMilliseconds(ms);
      if (!withOffset)
        return Quote(moment.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)); // as Date.toISOString
      DateTime clock = moment.AddMinutes(offsetMinutes);
      int abs = Math.Abs(offsetMinutes);
      return Quote(string.Concat(clock.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture), offsetMinutes < 0 ? "-" : "+",
        (abs / 60).ToString("00", CultureInfo.InvariantCulture), ":", (abs % 60).ToString("00", CultureInfo.InvariantCulture)));
    }

    private static string Number(double value)
    {
      if (double.IsNaN(value))
        return "NaN";
      if (double.IsPositiveInfinity(value))
        return "Infinity";
      if (double.IsNegativeInfinity(value))
        return "-Infinity";
      if (value == 0 && double.IsNegative(value))
        return "-0";
      return value.ToString("R", CultureInfo.InvariantCulture);
    }

    internal static string Quote(string text)
    {
      StringBuilder sb = new StringBuilder(text.Length + 2);
      sb.Append('\'');
      foreach (char c in text)
      {
        switch (c)
        {
          case '\'': sb.Append("\\'"); break;
          case '\\': sb.Append("\\\\"); break;
          case '\n': sb.Append("\\n"); break;
          case '\r': sb.Append("\\r"); break;
          case '\t': sb.Append("\\t"); break;
          case '\0': sb.Append("\\x00"); break; // not \0: followed by a digit that is an octal escape
          default:
            if (c < 0x20 || c == '\u2028' || c == '\u2029' || char.IsSurrogate(c)) // line separators end a line in older JavaScript
              sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else
              sb.Append(c);
            break;
        }
      }
      return sb.Append('\'').ToString();
    }
  }
}
