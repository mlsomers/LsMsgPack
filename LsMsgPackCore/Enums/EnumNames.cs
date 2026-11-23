using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// The names of an enum as <see cref="EnumFormat.Name"/> writes and every reader reads them (static metadata, cached per type): the custom name of a value
  /// (<c>[JsonStringEnumMemberName]</c>, <c>[EnumMember(Value = ...)]</c>, PolyType's <c>[EnumMemberShape(Name = ...)]</c>, by full name) or its .NET name; of aliases the first declared.
  /// Values are handled as their bits in a ulong (signed ones sign-extended), so one table serves every underlying type.
  /// </summary>
  internal sealed class EnumNames
  {
    private static readonly ConcurrentDictionary<Type, EnumNames> Cache = new ConcurrentDictionary<Type, EnumNames>();

    /// <summary>
    /// The .NET Standard attribute this class reads by name, named here so trimmers that keep only the attributes whose type the code uses (Unity's High stripping) keep it.
    /// </summary>
    internal static readonly Type[] ReadByName = { typeof(System.Runtime.Serialization.EnumMemberAttribute) };

    internal readonly Type Type;
    internal readonly bool IsFlags;
    private readonly bool _signed;

    /// <summary>
    /// The distinct values, ascending as ulong, and the name written for each.
    /// </summary>
    internal readonly ulong[] Values;
    internal readonly string[] Names;

    private readonly ulong[] _flags; // the distinct values but zero, descending: the parts of a combination
    private readonly Dictionary<string, ulong> _exact; // the written names and the .NET names
    private readonly Dictionary<string, ulong> _ignoringCase; // the same, the first one of names that differ only in case

    // The tables of the naming policies (MsgPackOptions.EnumNamingPolicy), by policy, then type
    private static readonly ConcurrentDictionary<MsgPackNamingPolicy, ConcurrentDictionary<Type, EnumNames>> ByPolicy = new ConcurrentDictionary<MsgPackNamingPolicy, ConcurrentDictionary<Type, EnumNames>>();

    internal static EnumNames Of(Type type)
    {
      if (Cache.TryGetValue(type, out EnumNames names))
        return names;
      return Cache.GetOrAdd(type, t => new EnumNames(t, null));
    }

    /// <summary>
    /// The names as the options write them: <see cref="MsgPackOptions.EnumNamingPolicy"/> converts the .NET names (custom names stay as they are); readers take the .NET names too.
    /// </summary>
    internal static EnumNames Of(Type type, MsgPackOptions settings)
    {
      return Of(type, settings._enumNamingPolicy);
    }

    internal static EnumNames Of(Type type, MsgPackNamingPolicy policy)
    {
      if (policy is null)
        return Of(type);
      ConcurrentDictionary<Type, EnumNames> cache;
      if (!ByPolicy.TryGetValue(policy, out cache))
        cache = ByPolicy.GetOrAdd(policy, p => new ConcurrentDictionary<Type, EnumNames>());
      if (cache.TryGetValue(type, out EnumNames names))
        return names;
      return cache.GetOrAdd(type, t => new EnumNames(t, policy));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.EnumFields)]
    private EnumNames(Type type, MsgPackNamingPolicy policy)
    {
      Type = type;
      IsFlags = type.IsDefined(typeof(FlagsAttribute), false);
      TypeCode code = Type.GetTypeCode(Enum.GetUnderlyingType(type));
      _signed = code == TypeCode.SByte || code == TypeCode.Int16 || code == TypeCode.Int32 || code == TypeCode.Int64;

      FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Static); // the declaration order (Enum.GetNames sorts by value, aliases in any order)
      List<KeyValuePair<ulong, string>> written = new List<KeyValuePair<ulong, string>>(fields.Length);
      _exact = new Dictionary<string, ulong>(fields.Length * 2, StringComparer.Ordinal);
      _ignoringCase = new Dictionary<string, ulong>(fields.Length * 2, StringComparer.OrdinalIgnoreCase);
      Dictionary<ulong, string> seen = new Dictionary<ulong, string>(); // the written name of each value (an alias writes the first one's)
      for (int t = 0; t < fields.Length; t++)
      {
        FieldInfo field = fields[t];
        ulong bits = ToBits(field.GetRawConstantValue());
        string name = CustomName(field) ?? (policy is null ? field.Name : MsgPackNamingPolicy.Convert(policy, field.Name));
        if (!seen.TryGetValue(bits, out string first))
        {
          seen.Add(bits, name);
          written.Add(new KeyValuePair<ulong, string>(bits, name));
          first = name;
        }
        if (_exact.TryGetValue(first, out ulong other) && other != bits)
          throw new MsgPackException($"The enum {type.FullName} writes two values as \"{first}\" ({other} and {bits}): give one of them another name ([EnumMember(Value = ...)], [JsonStringEnumMemberName]), or write it as a number.");
        _exact.TryAdd(first, bits);
        _exact.TryAdd(name, bits); // an alias's own name (custom or .NET) reads as its value too
      }
      for (int t = 0; t < fields.Length; t++) // the .NET names after all written names, so a written name keeps its value (custom names may swap .NET names)
      {
        FieldInfo field = fields[t];
        _exact.TryAdd(field.Name, ToBits(field.GetRawConstantValue()));
      }
      foreach (KeyValuePair<string, ulong> entry in _exact) // Dictionary order: insertion order while nothing was removed, so the custom names come first
        _ignoringCase.TryAdd(entry.Key, entry.Value);

      written.Sort((a, b) => a.Key.CompareTo(b.Key)); // List.Sort is not stable, but the values are distinct
      Values = new ulong[written.Count];
      Names = new string[written.Count];
      int nonZero = 0;
      for (int t = 0; t < written.Count; t++)
      {
        Values[t] = written[t].Key;
        Names[t] = written[t].Value;
        if (Values[t] != 0)
          nonZero++;
      }
      _flags = new ulong[nonZero];
      for (int t = Values.Length - 1, at = 0; t >= 0; t--)
      {
        if (Values[t] != 0)
          _flags[at++] = Values[t];
      }
    }

    /// <summary>
    /// The first custom name of System.Text.Json, the data contracts or PolyType (Nerdbank.MessagePack), by full name.
    /// </summary>
    private static string CustomName(FieldInfo field)
    {
      IList<CustomAttributeData> attributes;
      try
      {
        attributes = field.GetCustomAttributesData();
      }
      catch (Exception ex) when (ex is TypeLoadException || ex is System.IO.FileNotFoundException || ex is System.IO.FileLoadException || ex is NotSupportedException)
      {
        return null;
      }
      string json = null, contract = null, shape = null;
      for (int t = 0; t < attributes.Count; t++)
      {
        CustomAttributeData attribute = attributes[t];
        switch (attribute.AttributeType.FullName)
        {
          case "System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute":
            if (attribute.ConstructorArguments.Count == 1)
              json = attribute.ConstructorArguments[0].Value as string;
            break;
          case "System.Runtime.Serialization.EnumMemberAttribute":
            contract = Named(attribute, "Value");
            break;
          case "PolyType.EnumMemberShapeAttribute":
            shape = Named(attribute, "Name");
            break;
        }
      }
      string name = json ?? contract ?? shape;
      return string.IsNullOrEmpty(name) ? null : name;
    }

    private static string Named(CustomAttributeData attribute, string member)
    {
      IList<CustomAttributeNamedArgument> named = attribute.NamedArguments;
      for (int t = 0; t < named.Count; t++)
      {
        if (named[t].MemberName == member)
          return named[t].TypedValue.Value as string;
      }
      return null;
    }

    /// <summary>
    /// A value of the enum or of an integer type as its bits (signed types sign-extended).
    /// </summary>
    internal static ulong ToBits(object value)
    {
      switch (Convert.GetTypeCode(value))
      {
        case TypeCode.SByte:
        case TypeCode.Int16:
        case TypeCode.Int32:
        case TypeCode.Int64:
          return unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture));
        default:
          return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
      }
    }

    /// <summary>
    /// The boxed enum value of these bits.
    /// </summary>
    internal object ToEnum(ulong bits)
    {
      return _signed ? Enum.ToObject(Type, unchecked((long)bits)) : Enum.ToObject(Type, bits);
    }

    /// <summary>
    /// The position of the value in <see cref="Values"/>, -1 when it has no name of its own.
    /// </summary>
    internal int IndexOf(ulong bits)
    {
      return Array.BinarySearch(Values, bits) is int at && at >= 0 ? at : -1;
    }

    /// <summary>
    /// The text written for these bits, null to write the number: a value without a name that is no combination of named flags (as System.Text.Json and Json.NET write such values).
    /// </summary>
    internal string NameOf(ulong bits)
    {
      int at = IndexOf(bits);
      if (at >= 0)
        return Names[at];
      return IsFlags ? Combination(bits) : null;
    }

    /// <summary>
    /// A [Flags] value as the names of its parts, "A, B" in ascending order (the largest named parts first, as Enum.ToString), null when bits remain that no name covers.
    /// </summary>
    internal string Combination(ulong bits)
    {
      if (bits == 0)
        return null;
      ulong remaining = bits;
      List<int> parts = null;
      for (int t = 0; t < _flags.Length && remaining != 0; t++)
      {
        ulong flag = _flags[t];
        if ((remaining & flag) == flag)
        {
          remaining &= ~flag;
          if (parts == null)
            parts = new List<int>();
          parts.Add(IndexOf(flag));
        }
      }
      if (remaining != 0 || parts == null)
        return null;
      StringBuilder text = new StringBuilder();
      for (int t = parts.Count - 1; t >= 0; t--)
      {
        if (text.Length > 0)
          text.Append(", ");
        text.Append(Names[parts[t]]);
      }
      return text.ToString();
    }

    /// <summary>
    /// The written names and the .NET names with their values, the written ones first (LsMsgPack matches them as bytes).
    /// </summary>
    internal List<KeyValuePair<string, ulong>> AllNames()
    {
      List<KeyValuePair<string, ulong>> all = new List<KeyValuePair<string, ulong>>(_exact.Count);
      foreach (KeyValuePair<string, ulong> entry in _exact)
        all.Add(entry);
      return all;
    }

    /// <summary>
    /// A name read into the enum: a written or .NET name (exactly, then ignoring case), names joined by commas (combined), or an integer as text.
    /// </summary>
    internal bool TryParse(string text, out ulong bits)
    {
      if (_exact.TryGetValue(text, out bits))
        return true;
      if (text.IndexOf(',') < 0)
        return TryParseOne(text.Trim(), out bits);
      string[] parts = text.Split(',');
      bits = 0;
      for (int t = 0; t < parts.Length; t++)
      {
        if (!TryParseOne(parts[t].Trim(), out ulong part))
          return false;
        bits |= part;
      }
      return true;
    }

    private bool TryParseOne(string text, out ulong bits)
    {
      if (_exact.TryGetValue(text, out bits) || _ignoringCase.TryGetValue(text, out bits))
        return true;
      if (text.Length > 0 && (char.IsDigit(text[0]) || text[0] == '-' || text[0] == '+'))
      {
        if (_signed && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long signed))
        {
          bits = unchecked((ulong)signed);
          return true;
        }
        if (!_signed && ulong.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out bits))
          return true;
      }
      bits = 0;
      return false;
    }

    /// <summary>
    /// A name read into the enum (see <see cref="TryParse"/>), boxed; an <see cref="ArgumentException"/> (as Enum.Parse) for a name the enum does not have.
    /// </summary>
    internal object Parse(string text)
    {
      if (TryParse(text, out ulong bits))
        return ToEnum(bits);
      throw new ArgumentException($"Requested value '{text}' was not found in enum {Type.Name}.");
    }
  }
}
