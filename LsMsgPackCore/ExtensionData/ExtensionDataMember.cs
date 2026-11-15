using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// The member of a class that keeps the members of the data the class does not have (docs/ExtensionData.md): a <see cref="MsgPackExtensionData"/> (raw values), or an IDictionary&lt;string, object&gt;
  /// marked with <see cref="MsgPackExtensionDataAttribute"/> or <c>[JsonExtensionData]</c> (System.Text.Json's or Json.NET's, by name; plain values). Never a property in the data itself
  /// (<see cref="IsExtensionMember"/> keeps it out of the candidates). Found once per class (<see cref="Of"/>), in the class or its base classes, public or not.
  /// </summary>
  internal sealed class ExtensionDataMember
  {
    private static readonly ConcurrentDictionary<Type, ExtensionDataMember> Cache = new ConcurrentDictionary<Type, ExtensionDataMember>();

    /// <summary>
    /// The entries of the classes looked up last, compared by reference before the dictionary (asked per object by LsMsgPackDebug and Core's conversions, as ConverterLookup's recent entries):
    /// entries of the dictionary, so a miss allocates nothing; shared by the threads (an entry is replaced as a whole).
    /// </summary>
    private static readonly ExtensionDataMember[] Recent = new ExtensionDataMember[16];
    private static int _nextRecent;

    /// <summary>
    /// The class (also of an entry without a member).
    /// </summary>
    private readonly Type _type;

    private const string OwnAttribute = "MsgPackExtensionDataAttribute";
    private const string JsonAttribute = "JsonExtensionDataAttribute"; // System.Text.Json.Serialization's and Newtonsoft.Json's

    /// <summary>
    /// The member, for its accessors and the name in messages and differences.
    /// </summary>
    internal readonly FullPropertyInfo Info;

    /// <summary>
    /// An IDictionary&lt;string, object&gt; (plain values, names only), otherwise a <see cref="MsgPackExtensionData"/>.
    /// </summary>
    internal readonly bool IsDictionary;

    private readonly Type _dictionaryType;

    private ExtensionDataMember(Type type)
    {
      _type = type;
    }

    private ExtensionDataMember(Type type, MemberInfo member, bool isDictionary, Type dictionaryType)
    {
      _type = type;
      Info = FullPropertyInfo.ForMember(member);
      IsDictionary = isDictionary;
      _dictionaryType = dictionaryType;
    }

    /// <summary>
    /// The extension data member of the class (or struct), null when it has none.
    /// </summary>
    /// <exception cref="MsgPackException">Two such members, or the attribute on a member of another type</exception>
    internal static ExtensionDataMember Of(Type type)
    {
      ExtensionDataMember[] recent = Recent; // asked for the types of objects only (no test for primitives: IsPrimitive is a call)
      for (int t = 0; t < recent.Length; t++)
      {
        ExtensionDataMember entry = recent[t];
        if (entry is null)
          break; // filled in order
        if (entry._type == type)
          return entry.Info is null ? null : entry;
      }
      return Remember(type);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static ExtensionDataMember Remember(Type type)
    {
      if (type is null)
        return null;
      if (!Cache.TryGetValue(type, out ExtensionDataMember member))
        member = Cache.GetOrAdd(type, Find(type));
      Recent[(_nextRecent++) & (Recent.Length - 1)] = member;
      return member.Info is null ? null : member;
    }

    /// <summary>
    /// A member that is the extension data member of its class if it is the only one (a <see cref="MsgPackExtensionData"/>, or marked as one): never a candidate for serialization.
    /// </summary>
    internal static bool IsExtensionMember(MemberInfo member)
    {
      Type type = (member as PropertyInfo)?.PropertyType ?? (member as FieldInfo)?.FieldType;
      return type == typeof(MsgPackExtensionData) || IsMarked(member);
    }

    private static bool IsMarked(MemberInfo member)
    {
      Dictionary<string, object> attributes = FullPropertyInfo.AttributesOf(member).ByName;
      return attributes.ContainsKey(OwnAttribute) || attributes.ContainsKey(JsonAttribute);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
    private static ExtensionDataMember Find(Type type)
    {
      if (TypeResolver.IsFrameworkAssembly(type.Assembly))
        return new ExtensionDataMember(type);
      MemberInfo found = null;
      List<string> names = new List<string>();
      const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
      for (Type declaring = type; declaring != null && declaring != typeof(object) && declaring != typeof(ValueType); declaring = declaring.BaseType)
      {
        if (TypeResolver.IsFrameworkAssembly(declaring.Assembly))
          continue;
        PropertyInfo[] properties = declaring.GetProperties(Declared);
        for (int t = 0; t < properties.Length; t++)
        {
          PropertyInfo property = properties[t];
          if (property.GetIndexParameters().Length > 0 || names.Contains(property.Name)) // overridden in a derived class
            continue;
          if (IsCandidate(property, property.PropertyType, property.GetGetMethod() != null))
          {
            names.Add(property.Name);
            found = Choose(type, found, property);
          }
        }
        FieldInfo[] fields = declaring.GetFields(Declared);
        for (int t = 0; t < fields.Length; t++)
        {
          FieldInfo field = fields[t];
          if (FullPropertyInfo.IsCompilerName(field.Name) || names.Contains(field.Name))
            continue;
          if (IsCandidate(field, field.FieldType, field.IsPublic))
          {
            names.Add(field.Name);
            found = Choose(type, found, field);
          }
        }
      }
      if (found is null)
        return new ExtensionDataMember(type);

      Type memberType = (found as PropertyInfo)?.PropertyType ?? ((FieldInfo)found).FieldType;
      if (memberType == typeof(MsgPackExtensionData))
        return new ExtensionDataMember(type, found, false, null);
      return new ExtensionDataMember(type, found, true, DictionaryTypeOf(type, found, memberType));
    }

    private static bool IsCandidate(MemberInfo member, Type memberType, bool isPublic)
    {
      if (IsMarked(member))
        return true;
      return memberType == typeof(MsgPackExtensionData) && isPublic;
    }

    private static MemberInfo Choose(Type type, MemberInfo found, MemberInfo member)
    {
      if (found != null)
        throw new MsgPackException($"{type.Name} has two members for extension data ({found.Name} and {member.Name}): a class keeps the members of the data it does not have in one MsgPackExtensionData or [MsgPackExtensionData] dictionary.");
      return member;
    }

    /// <returns>The type created when the member is null: Dictionary&lt;string, object&gt;, or the member's own class</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    private static Type DictionaryTypeOf(Type type, MemberInfo member, Type memberType)
    {
      if (memberType.IsAssignableFrom(typeof(Dictionary<string, object>)))
        return typeof(Dictionary<string, object>);
      if (typeof(IDictionary<string, object>).IsAssignableFrom(memberType) && !memberType.IsAbstract && !memberType.IsInterface && memberType.GetConstructor(Type.EmptyTypes) != null)
        return memberType;
      throw new MsgPackException($"{type.Name}.{member.Name} is marked as extension data, but is a {memberType.Name}: LsMsgPack keeps the members of the data a class does not have in a MsgPackExtensionData, "
        + "or in a dictionary of string to object (IDictionary<string, object>, Dictionary<string, object> or a class implementing it with a parameterless constructor).");
    }

    /// <summary>
    /// Reading: keeps the values (by their names or positions) in the member of the object: in the container it has, otherwise in a new one it is set to
    /// (not when it has no setter: then they are lost). A dictionary keeps the names only.
    /// </summary>
    /// <param name="settings">The settings of the reader (how it reads timestamps, the values it writes with its extensions)</param>
    internal void Keep(object instance, List<KeptValue> kept, MsgPackOptions settings)
    {
      object container = Info.GetValue(instance);
      bool created = container is null;
      if (created)
        container = IsDictionary ? Instances.CreateCollection(_dictionaryType, settings) : new MsgPackExtensionData();

      if (container is MsgPackExtensionData data)
      {
        for (int t = 0; t < kept.Count; t++)
          data.Keep(kept[t].Name, kept[t].Position, KeptValues.Encode(kept[t].Value, settings, false));
      }
      else
      {
        IDictionary<string, object> dictionary = (IDictionary<string, object>)container;
        for (int t = 0; t < kept.Count; t++)
          if (kept[t].Name != null)
            dictionary[kept[t].Name] = KeptValues.ToDictionaryValue(kept[t].Value);
      }

      if (created && !Info.IsComputed)
        Info.SetValue(instance, container);
    }

    /// <summary>
    /// Writing: the kept values of the object written after its properties (both serializers, after the type id and before the values, so the schema gets the names in the same order),
    /// by name (<see cref="KeptEntry.Key"/>) or position, the bytes in the writer's byte order (a dictionary's values encoded, <see cref="KeptValues.Encode"/> compact). Null when there are none.
    /// <list type="bullet">
    /// <item>A name the class has itself (<paramref name="props"/>) is left out: the property wins. Nil is left out (as when it is read), so are a dictionary's reserved keys.</item>
    /// <item>With the indexed schema (<paramref name="schema"/>) the names get their positions in the class's entry from the caller (<see cref="WithSchemaIds"/>, LsMsgPack's inline schema).</item>
    /// <item>Without the schema a map gets the names, an array the values kept by position (after its properties); the others are left out (they have no place there).</item>
    /// </list>
    /// </summary>
    /// <param name="props">The serialized properties of the class (with their ids)</param>
    /// <param name="array">The object is written as an array (<see cref="ObjectLayout.Array"/>)</param>
    /// <param name="settings">The serializer's settings (byte order, how a dictionary's values are written, the property id resolvers' names)</param>
    internal List<KeptEntry> ToWrite(object instance, FullPropertyInfo[] props, bool array, bool schema, MsgPackOptions settings)
    {
      object container = Info.GetValue(instance);
      List<KeptEntry> result = null;
      if (container is MsgPackExtensionData data)
      {
        List<MsgPackExtensionData.Entry> entries = data.Entries;
        for (int t = 0; t < entries.Count; t++)
        {
          MsgPackExtensionData.Entry entry = entries[t];
          if (Writes(entry.Name, entry.Position, entry.Value, props, array, schema, settings))
            (result ?? (result = new List<KeptEntry>(entries.Count))).Add(new KeptEntry(entry.Name, entry.Position, KeptValues.InByteOrder(entry.Value, settings)));
        }
        return result;
      }
      if (container is IEnumerable<KeyValuePair<string, object>> dictionary)
      {
        foreach (KeyValuePair<string, object> entry in dictionary)
        {
          if (entry.Key is null || entry.Key.Length == 0 || entry.Key == MsgPackOptions.ContentKey || entry.Value is null)
            continue;
          if (!Writes(entry.Key, -1, null, props, array, schema, settings))
            continue;
          byte[] bytes = KeptValues.Encode(entry.Value, settings, true);
          (result ?? (result = new List<KeptEntry>())).Add(new KeptEntry(entry.Key, -1, KeptValues.InByteOrder(bytes, settings)));
        }
      }
      return result;
    }

    /// <param name="bytes">The kept bytes (nil is not written), null when not known yet</param>
    private bool Writes(string name, int position, byte[] bytes, FullPropertyInfo[] props, bool array, bool schema, MsgPackOptions settings)
    {
      if (bytes != null && bytes.Length == 1 && bytes[0] == (byte)MsgPackTypeId.MpNull)
        return false;
      if (name is null)
        return array && !schema && position >= props.Length;
      return !(array && !schema) && !OwnNamesOf(props, schema, settings).Contains(name);
    }

    /// <summary>
    /// The names of <see cref="ToWrite"/> as their positions in the type's entry of Core's schema session (added to it when it does not have them): the keys and positions.
    /// </summary>
    internal static List<KeptEntry> WithSchemaIds(List<KeptEntry> kept, Type type, MsgPackOptions settings)
    {
      TypeResolving.Types.IndexedSchemaTypeResolver schema = SerializationRules.GetIndexedSchema(settings);
      for (int t = 0; t < kept.Count; t++)
      {
        object id = schema.IdOfKeptName(type, (string)kept[t].Key, settings);
        kept[t] = new KeptEntry(id, (int)id, kept[t].Bytes);
      }
      return kept;
    }

    /// <summary>
    /// The names of the class's own properties (in the schema: <see cref="FullPropertyInfo.SchemaNameOf"/>, which asks the property id resolvers), kept for the property array they were made of:
    /// the plans' and frozen sessions' arrays are the same object by object.
    /// </summary>
    private HashSet<string> OwnNamesOf(FullPropertyInfo[] props, bool schema, MsgPackOptions settings)
    {
      OwnNames known = _ownNames;
      if (known != null && ReferenceEquals(known.Props, props) && known.Schema == schema)
        return known.Names;
      HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
      for (int t = 0; t < props.Length; t++)
      {
        string own = schema ? FullPropertyInfo.SchemaNameOf(props[t], settings) : props[t].PropertyId as string;
        if (own != null)
          names.Add(own);
      }
      _ownNames = new OwnNames(props, schema, names);
      return names;
    }

    private volatile OwnNames _ownNames;

    private sealed class OwnNames
    {
      internal readonly FullPropertyInfo[] Props;
      internal readonly bool Schema;
      internal readonly HashSet<string> Names;

      internal OwnNames(FullPropertyInfo[] props, bool schema, HashSet<string> names)
      {
        Props = props;
        Schema = schema;
        Names = names;
      }
    }

  }

  /// <summary>
  /// A kept value as it is written (see <see cref="ExtensionDataMember.ToWrite"/>): its key in a map (a name, or with the indexed schema its index), its position in an array, its bytes in the writer's byte order.
  /// </summary>
  internal readonly struct KeptEntry
  {
    internal readonly object Key;
    internal readonly int Position;
    internal readonly byte[] Bytes;

    internal KeptEntry(object key, int position, byte[] bytes)
    {
      Key = key;
      Position = position;
      Bytes = bytes;
    }
  }

  /// <summary>
  /// A value kept as extension data: by name, or by position (<see cref="Name"/> null) in an object written as an array without the indexed schema.
  /// Reading: the plain value (see KeptValues), before it is encoded or made a dictionary value.
  /// </summary>
  internal readonly struct KeptValue
  {
    internal readonly string Name;
    internal readonly int Position;
    internal readonly object Value;

    internal KeptValue(string name, int position, object value)
    {
      Name = name;
      Position = position;
      Value = value;
    }
  }
}
