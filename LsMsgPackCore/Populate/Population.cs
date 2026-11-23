using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// How reading treats the value of a member (docs/Populate.md, <see cref="MsgPackOptions.Populate"/>).
  /// </summary>
  internal enum MemberPopulation : byte
  {
    /// <summary>
    /// As without populating: a settable member gets the value read, a collection without a setter is filled (its accessor), any other member without a setter is skipped.
    /// </summary>
    Replace,

    /// <summary>
    /// The value is read into the object the member holds (<see cref="ValueConverter.ConvertInto"/>).
    /// </summary>
    Into,

    /// <summary>
    /// The collection the member holds is cleared and filled with the items read (<see cref="GetOnlyCollection.Fill"/>).
    /// </summary>
    Fill,

    /// <summary>
    /// The value is skipped: a member without a setter that an attribute opts out (<c>[MsgPackPopulate(false)]</c>).
    /// </summary>
    Skip
  }

  /// <summary>
  /// Decides per member whether reading fills the instance it holds (docs/Populate.md): the member's own facts once per member (<see cref="PropertyAttributeSet.Population"/>),
  /// then the options. Shared by both serializers (LsMsgPack decides when it builds a read plan).
  /// </summary>
  internal static class Population
  {
    /// <summary>
    /// What kind of member it is, for populating (static, once per member).
    /// </summary>
    internal enum Candidate : byte
    {
      /// <summary>
      /// Never populated: a value type, string, array, object, a framework type, a member a constructor takes...
      /// </summary>
      None,

      /// <summary>
      /// An auto-property without a setter or a readonly field holding an object (<c>public Address ShipTo { get; } = new Address();</c>).
      /// </summary>
      GetOnlyObject,

      /// <summary>
      /// A property without a setter whose getter is written by hand: a calculation, or it may return shared state. Only with an attribute.
      /// </summary>
      ComputedObject,

      /// <summary>
      /// A collection without a setter (filled whatever the option says, unless an attribute opts out).
      /// </summary>
      GetOnlyCollection,

      /// <summary>
      /// A settable member holding an object.
      /// </summary>
      SettableObject,

      /// <summary>
      /// A settable member holding a collection with Add (not an array).
      /// </summary>
      SettableCollection
    }

    /// <summary>
    /// The member's facts: its kind and what its attributes (or its class's) ask for.
    /// </summary>
    internal sealed class MemberFacts
    {
      internal readonly Candidate Kind;

      /// <summary>
      /// True or false by an attribute of the member or of its declaring type, null when none says.
      /// </summary>
      internal readonly bool? Asked;

      internal MemberFacts(Candidate kind, bool? asked)
      {
        Kind = kind;
        Asked = asked;
      }
    }

    private static readonly MemberFacts NoCandidate = new MemberFacts(Candidate.None, null);

    /// <summary>
    /// How reading treats the value of <paramref name="prop"/> with these settings.
    /// </summary>
    internal static MemberPopulation Of(FullPropertyInfo prop, MsgPackOptions settings)
    {
      PropertyAttributeSet attributes = prop.Attributes;
      if (attributes is null)
        return MemberPopulation.Replace;
      MemberFacts facts = attributes.Population;
      bool populate;
      switch (facts.Kind)
      {
        case Candidate.GetOnlyCollection:
          return facts.Asked == false ? MemberPopulation.Skip : MemberPopulation.Replace; // its accessor fills it
        case Candidate.GetOnlyObject:
          populate = facts.Asked ?? settings._populate >= PopulateMembers.GetOnly;
          if (!populate)
            return facts.Asked == false ? MemberPopulation.Skip : MemberPopulation.Replace; // skipped as a computed property
          break;
        case Candidate.ComputedObject:
          populate = facts.Asked == true;
          if (!populate)
            return facts.Asked == false ? MemberPopulation.Skip : MemberPopulation.Replace;
          break;
        case Candidate.SettableObject:
        case Candidate.SettableCollection:
          populate = facts.Asked ?? settings._populate == PopulateMembers.All;
          if (!populate)
            return MemberPopulation.Replace;
          break;
        default:
          return MemberPopulation.Replace;
      }
      if (settings.ConverterFor(prop.DeclaredType, prop) != null) // the value is its surrogate
        return MemberPopulation.Replace;
      return facts.Kind == Candidate.SettableCollection ? MemberPopulation.Fill : MemberPopulation.Into;
    }

    /// <summary>
    /// Whether a member is ever populated or skipped by an attribute (the facts only, not the options): Core's conversions look at the options only for these.
    /// </summary>
    internal static bool MayPopulate(FullPropertyInfo prop)
    {
      PropertyAttributeSet attributes = prop.Attributes;
      if (attributes is null)
        return false;
      MemberFacts facts = attributes.Population;
      return facts.Kind != Candidate.None && (facts.Kind != Candidate.GetOnlyCollection || facts.Asked == false);
    }

    /// <summary>
    /// Whether the member does not take a new value: populated members without a setter (they are skipped when the instance cannot be read into).
    /// </summary>
    internal static bool IsGetOnly(FullPropertyInfo prop)
    {
      Candidate kind = prop.Attributes.Population.Kind;
      return kind == Candidate.GetOnlyObject || kind == Candidate.ComputedObject || kind == Candidate.GetOnlyCollection;
    }

    /// <summary>
    /// The member's facts (called once per member by <see cref="PropertyAttributeSet.Population"/>).
    /// </summary>
    internal static MemberFacts FactsOf(MemberInfo member, Type type)
    {
      if (member.DeclaringType is null || SerializationRules.IsAnonymous(member.DeclaringType) || type.IsValueType || type.IsPointer || type.IsByRef)
        return NoCandidate;
      bool getOnly;
      bool auto;
      if (member is FieldInfo field)
      {
        getOnly = field.IsInitOnly;
        auto = true; // the field itself holds the instance
      }
      else
      {
        PropertyInfo property = (PropertyInfo)member;
        MethodInfo get = property.GetGetMethod(true);
        if (get is null || get.IsStatic || property.GetIndexParameters().Length > 0)
          return NoCandidate;
        getOnly = property.GetSetMethod(true) is null;
        auto = get.IsDefined(typeof(CompilerGeneratedAttribute), false); // C# and VB auto-properties: the getter returns the backing field
      }
      if (getOnly && ObjectConstructor.Sets(member)) // the constructor takes its value
        return NoCandidate;

      Candidate kind;
      if (GetOnlyCollection.IsFillable(type))
        kind = getOnly ? Candidate.GetOnlyCollection : Candidate.SettableCollection;
      else if (IsObjectType(type))
        kind = !getOnly ? Candidate.SettableObject : auto ? Candidate.GetOnlyObject : Candidate.ComputedObject;
      else
        return NoCandidate;
      return new MemberFacts(kind, Asked(member));
    }

    /// <summary>
    /// A declared type that holds an object with members of its own: a class or interface of the application (not string, arrays, object, delegates, Type, framework types).
    /// </summary>
    private static bool IsObjectType(Type type)
    {
      return !type.IsValueType && type != typeof(string) && type != typeof(object) && !type.IsArray && !SerializationRules.IsNeverWritten(type) && !IsFramework(type);
    }

    private static readonly ConcurrentDictionary<Assembly, bool> FrameworkAssemblies = new ConcurrentDictionary<Assembly, bool>();

    private static bool IsFramework(Type type)
    {
      Assembly assembly = type.Assembly;
      if (!FrameworkAssemblies.TryGetValue(assembly, out bool framework))
        framework = FrameworkAssemblies.GetOrAdd(assembly, a => TypeResolver.IsFrameworkAssembly(a));
      return framework;
    }

    private static readonly ConcurrentDictionary<Type, bool> Readable = new ConcurrentDictionary<Type, bool>();

    /// <summary>
    /// Whether the object a member holds can be read into with these settings (its runtime type): a class with members of its own, without a converter, not created by a constructor taking its values.
    /// Otherwise the member is read as without populating (skipped when it has no setter).
    /// </summary>
    internal static bool CanReadInto(object existing, MsgPackOptions settings)
    {
      Type type = existing.GetType();
      if (!Readable.TryGetValue(type, out bool readable))
        readable = Readable.GetOrAdd(type, t => !t.IsValueType && !t.IsArray && t != typeof(string) && t != typeof(object) && !IsFramework(t) && !SerializationRules.IsAnonymous(t) && !GetOnlyCollection.IsFillable(t));
      return readable && settings.ConverterFor(type) is null && !ObjectConstructor.Of(type).CreatesWith(settings._objectCreation);
    }

    /// <summary>
    /// The target of the serializers' Populate methods: an object <see cref="CanReadInto"/>, or a collection with Add that is not read-only.
    /// </summary>
    /// <returns>true for a collection (read as a new one, then filled), false for an object (read into)</returns>
    /// <exception cref="ArgumentNullException">No target</exception>
    /// <exception cref="ArgumentException">Anything else</exception>
    internal static bool ThrowIfNotPopulatable(object target, MsgPackOptions settings)
    {
      if (target is null)
        throw new ArgumentNullException(nameof(target));
      Type type = target.GetType();
      if (GetOnlyCollection.IsFillable(type))
      {
        if (!GetOnlyCollection.CanFill(target))
          throw new ArgumentException($"The {type.Name} cannot be filled: it is read-only or has a fixed size.", nameof(target));
        return true;
      }
      if (CanReadInto(target, settings))
        return false;
      string why = settings.ConverterFor(type) != null ? "it has a converter (its values are written as their surrogate)"
        : ObjectConstructor.Of(type).CreatesWith(settings._objectCreation) ? "it is created by its constructor taking the values"
        : "it is not a class with members of its own (a string, an array, object, a framework type)";
      throw new ArgumentException($"The data cannot be read into a {type.Name}: {why}.", nameof(target));
    }

    #region Attributes

    private static readonly ConcurrentDictionary<Type, bool?> TypeAsked = new ConcurrentDictionary<Type, bool?>();

    /// <summary>
    /// The member's own attribute, otherwise its declaring type's; null when none says.
    /// </summary>
    private static bool? Asked(MemberInfo member)
    {
      bool? own = Of(AttributeData(member));
      if (own != null)
        return own;
      Type declaring = member.DeclaringType;
      if (!TypeAsked.TryGetValue(declaring, out bool? asked))
        asked = TypeAsked.GetOrAdd(declaring, t => Of(AttributeData(t)));
      return asked;
    }

    private static IList<CustomAttributeData> AttributeData(MemberInfo member)
    {
      try
      {
        return member.GetCustomAttributesData();
      }
      catch (Exception ex) when (ex is TypeLoadException || ex is System.IO.FileNotFoundException || ex is System.IO.FileLoadException || ex is NotSupportedException)
      {
        return new CustomAttributeData[0]; // an attribute of an assembly that is missing at runtime decides nothing
      }
    }

    /// <summary>
    /// The own attribute wins, then the first attribute of another serializer that decides (by full name: no other serializer is loaded).
    /// </summary>
    private static bool? Of(IList<CustomAttributeData> attributes)
    {
      bool? found = null;
      for (int t = 0; t < attributes.Count; t++)
      {
        CustomAttributeData attribute = attributes[t];
        Type type = attribute.AttributeType;
        if (type == typeof(MsgPackPopulateAttribute))
          return attribute.ConstructorArguments.Count == 1 && attribute.ConstructorArguments[0].Value is bool populate ? populate : true;
        if (found != null)
          continue;
        switch (type.FullName)
        {
          case "System.Text.Json.Serialization.JsonObjectCreationHandlingAttribute": // JsonObjectCreationHandling: Replace = 0, Populate = 1
            if (attribute.ConstructorArguments.Count == 1 && attribute.ConstructorArguments[0].Value is int handling)
              found = handling == 1;
            break;
          case "Newtonsoft.Json.JsonPropertyAttribute": // ObjectCreationHandling: Auto = 0 (reuse an existing object, Json.NET's default), Reuse = 1, Replace = 2
            IList<CustomAttributeNamedArgument> named = attribute.NamedArguments;
            for (int n = 0; n < named.Count; n++)
              if (named[n].MemberName == "ObjectCreationHandling" && named[n].TypedValue.Value is int creation)
                found = creation != 2;
            break;
        }
      }
      return found;
    }

    #endregion
  }
}
