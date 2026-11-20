using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// The format an enum type or a member asks for by its attributes (<see cref="MsgPackEnumFormatAttribute"/>, or another serializer's that means the same), read by full name from the attribute data:
  /// no other serializer is loaded or referenced.
  /// </summary>
  internal static class EnumFormats
  {
    private sealed class Entry
    {
      internal readonly Type Type;
      internal readonly EnumFormat? Format;

      internal Entry(Type type, EnumFormat? format)
      {
        Type = type;
        Format = format;
      }
    }

    private static readonly ConcurrentDictionary<Type, Entry> Cache = new ConcurrentDictionary<Type, Entry>();
    private static volatile Entry _last; // LsMsgPackDebug asks per value: the type of the last one first (the hash of a Type is a call into the runtime)

    /// <summary>
    /// The format of the enum type's own attribute, null when it has none (the options decide).
    /// </summary>
    internal static EnumFormat? OfType(Type enumType)
    {
      Entry last = _last;
      if (last != null && last.Type == enumType)
        return last.Format;
      if (!Cache.TryGetValue(enumType, out Entry entry))
        entry = Cache.GetOrAdd(enumType, t => new Entry(t, Of(AttributeData(t))));
      _last = entry;
      return entry.Format;
    }

    /// <summary>
    /// The format the attributes of a member ask for, null when none does.
    /// </summary>
    internal static EnumFormat? OfMember(MemberInfo member)
    {
      return Of(AttributeData(member));
    }

    private static IList<CustomAttributeData> AttributeData(MemberInfo member)
    {
      try
      {
        return member.GetCustomAttributesData();
      }
      catch (Exception ex) when (ex is TypeLoadException || ex is System.IO.FileNotFoundException || ex is System.IO.FileLoadException || ex is NotSupportedException)
      {
        return new CustomAttributeData[0]; // an attribute of an assembly that is missing at runtime is no format
      }
    }

    /// <summary>
    /// The own attribute wins, then the first attribute of another serializer that decides.
    /// </summary>
    internal static EnumFormat? Of(IList<CustomAttributeData> attributes)
    {
      EnumFormat? found = null;
      for (int t = 0; t < attributes.Count; t++)
      {
        CustomAttributeData attribute = attributes[t];
        Type type = attribute.AttributeType;
        if (type == typeof(MsgPackEnumFormatAttribute))
          return attribute.ConstructorArguments.Count == 1 && attribute.ConstructorArguments[0].Value is int format ? (EnumFormat)format : (EnumFormat?)null;
        if (found == null)
          found = OfOtherSerializer(attribute, type.FullName);
      }
      return found;
    }

    private static EnumFormat? OfOtherSerializer(CustomAttributeData attribute, string name)
    {
      switch (name)
      {
        case "System.Text.Json.Serialization.JsonConverterAttribute": // [JsonConverter(typeof(JsonStringEnumConverter))], JsonStringEnumConverter<T>, JsonNumberEnumConverter<T>
          return OfConverter(FirstTypeArgument(attribute), "System.Text.Json.Serialization.JsonStringEnumConverter", "System.Text.Json.Serialization.JsonStringEnumConverter`1", "System.Text.Json.Serialization.JsonNumberEnumConverter`1");
        case "Newtonsoft.Json.JsonConverterAttribute": // [JsonConverter(typeof(StringEnumConverter))]: its naming arguments are not followed (the names as declared, or their [EnumMember] names)
          return OfConverter(FirstTypeArgument(attribute), "Newtonsoft.Json.Converters.StringEnumConverter", null, null);
        case "MessagePack.MessagePackFormatterAttribute": // [MessagePackFormatter(typeof(EnumAsStringFormatter<T>))]
          return OfConverter(FirstTypeArgument(attribute), null, "MessagePack.Formatters.EnumAsStringFormatter`1", "MessagePack.Formatters.GenericEnumFormatter`1");
        case "MsgPack.Serialization.MessagePackEnumAttribute": // MsgPack.Cli: SerializationMethod = EnumSerializationMethod.ByName (its default) or ByUnderlyingValue
          return OfMethod(attribute, EnumFormat.Name);
        case "MsgPack.Serialization.MessagePackEnumMemberAttribute": // MsgPack.Cli: SerializationMethod = EnumMemberSerializationMethod.Default (the enum decides), ByName or ByUnderlyingValue
          return OfMethod(attribute, null);
      }
      return null;
    }

    private static Type FirstTypeArgument(CustomAttributeData attribute)
    {
      IList<CustomAttributeTypedArgument> arguments = attribute.ConstructorArguments;
      return arguments.Count > 0 ? arguments[0].Value as Type : null;
    }

    /// <summary>
    /// A converter of a string enum converter's class (or one deriving from it): by name; of the number converter: by number; anything else decides nothing.
    /// </summary>
    private static EnumFormat? OfConverter(Type converter, string byName, string byNameGeneric, string byNumberGeneric)
    {
      for (Type t = converter; t != null && t != typeof(object); t = t.BaseType)
      {
        string name = t.IsGenericType ? t.GetGenericTypeDefinition().FullName : t.FullName;
        if (name == null)
          continue;
        if (name == byName || name == byNameGeneric)
          return EnumFormat.Name;
        if (name == byNumberGeneric)
          return EnumFormat.Number;
      }
      return null;
    }

    private static EnumFormat? OfMethod(CustomAttributeData attribute, EnumFormat? unset)
    {
      IList<CustomAttributeNamedArgument> named = attribute.NamedArguments;
      for (int t = 0; t < named.Count; t++)
      {
        if (named[t].MemberName != "SerializationMethod")
          continue;
        CustomAttributeTypedArgument value = named[t].TypedValue;
        string method = value.ArgumentType.IsEnum && value.Value != null ? Enum.GetName(value.ArgumentType, value.Value) : null;
        if (method == "ByName")
          return EnumFormat.Name;
        if (method == "ByUnderlyingValue")
          return EnumFormat.Number;
        return null;
      }
      return unset;
    }
  }
}
