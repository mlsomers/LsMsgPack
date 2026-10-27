using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// The attributes of a property, read once per property (static metadata): its own ones by class name, and where it may inherit them from (read when first asked, see FilterIgnoredAttribute).
  /// </summary>
  internal sealed class PropertyAttributeSet
  {
    private static readonly object[] None = new object[0];

    private readonly PropertyInfo _property;

    /// <summary>
    /// The attributes of the property itself, one per class name.
    /// </summary>
    internal readonly Dictionary<string, object> ByName;

    private volatile Looked _looked;

    /// <summary>
    /// The names AttributePropertyNameResolver reads from the attributes of other serializers, set when it first asks (the same value by any thread).
    /// </summary>
    internal TypeResolving.Names.AttributePropertyNameResolver.AttributeNames Names;

    private sealed class Looked
    {
      internal object[] Declared;
      internal object[] Inheritable;
      internal object[] Overridden;
      internal object[] Interfaces;
    }

    internal PropertyAttributeSet(PropertyInfo property)
    {
      _property = property;
      object[] atts = property.GetCustomAttributes(true);
      ByName = new Dictionary<string, object>(atts.Length);
      for (int t = atts.Length - 1; t >= 0; t--)
        ByName.TryAdd(atts[t].GetType().Name, atts[t]);
    }

    /// <summary>
    /// All attributes of the property itself (ByName keeps one per class name, and both JSON serializers call theirs JsonIgnoreAttribute).
    /// </summary>
    internal object[] Declared { get { return Look().Declared; } }

    /// <summary>
    /// The attributes of the property and the inherited attributes (<c>AttributeUsage(Inherited = true)</c>) of the properties it overrides, as <c>Attribute.GetCustomAttributes(property, true)</c> finds them.
    /// </summary>
    internal object[] Inheritable { get { return Look().Inheritable; } }

    /// <summary>
    /// All attributes of the properties it overrides (not its own).
    /// </summary>
    internal object[] Overridden { get { return Look().Overridden; } }

    /// <summary>
    /// The attributes of the properties with the same name, type and index parameters of the interfaces of the declaring type (as Json.NET looks for them).
    /// </summary>
    internal object[] Interfaces { get { return Look().Interfaces; } }

    private volatile int _optsIn; // 0: not decided yet, 1: no, 2: yes

    /// <summary>
    /// The property has an attribute that opts it in for another serializer (see <see cref="TypeResolving.Filters.NonPublicProperties.Attributed"/>).
    /// </summary>
    internal bool OptsIn
    {
      get
      {
        int optsIn = _optsIn;
        if (optsIn == 0)
          _optsIn = optsIn = TypeResolving.Filters.IncludeAttributes.Any(ByName.Values) ? 2 : 1; // the same for every thread that computes it
        return optsIn == 2;
      }
    }

    private volatile NilValues _nil;

    /// <summary>
    /// What a nil in the array layout sets (see <see cref="FullPropertyInfo.NilValue"/>): the value of [DefaultValue] when it fits the property (otherwise FilterDefaultValues never
    /// leaves out a value for it: <c>Equals</c> fails), and the default of the type.
    /// </summary>
    internal sealed class NilValues
    {
      internal object DefaultValue;
      internal object TypeDefault;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = TrimReasons.ModelTypes)]
    internal NilValues Nil
    {
      get
      {
        NilValues nil = _nil;
        if (nil != null)
          return nil;
        Type type = _property.PropertyType;
        Type underlying = Nullable.GetUnderlyingType(type);
        nil = new NilValues();
        if (type.IsValueType && underlying is null)
          nil.TypeDefault = Activator.CreateInstance(type); // as FilterDefaultValues compares with it
        if (ByName.TryGetValue(nameof(System.ComponentModel.DefaultValueAttribute), out object attribute) && attribute is System.ComponentModel.DefaultValueAttribute def
          && def.Value != null && ((underlying ?? type).IsInstanceOfType(def.Value)))
          nil.DefaultValue = def.Value;
        _nil = nil; // the same for every thread that computes it
        return nil;
      }
    }

    private Looked Look()
    {
      Looked looked = _looked;
      if (looked != null)
        return looked;

      looked = new Looked()
      {
        Declared = OrNone(_property.GetCustomAttributes(false)),
        // Attribute.GetCustomAttributes also looks at the overridden properties (PropertyInfo.GetCustomAttributes ignores inherit)
        Inheritable = OrNone(Attribute.GetCustomAttributes(_property, true)),
        Overridden = OrNone(OverriddenAttributes()),
        Interfaces = OrNone(InterfaceAttributes())
      };
      _looked = looked; // the same for every thread that computes it
      return looked;
    }

    private static object[] OrNone(object[] attributes)
    {
      return attributes is null || attributes.Length == 0 ? None : attributes;
    }

    private object[] OverriddenAttributes()
    {
      List<object> attributes = null;
      foreach (PropertyInfo overridden in OverriddenProperties(_property))
      {
        if (attributes is null)
          attributes = new List<object>();
        attributes.AddRange(overridden.GetCustomAttributes(false));
      }
      return attributes?.ToArray();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
    [UnconditionalSuppressMessage("Trimming", "IL2065", Justification = TrimReasons.ModelTypes)]
    private object[] InterfaceAttributes()
    {
      if (_property.DeclaringType is null)
        return null;
      List<object> attributes = null;
      Type[] index = Array.ConvertAll(_property.GetIndexParameters(), p => p.ParameterType);
      Type[] faces = _property.DeclaringType.GetInterfaces();
      for (int t = 0; t < faces.Length; t++)
      {
        PropertyInfo implemented = faces[t].GetProperty(_property.Name, BindingFlags.Instance | BindingFlags.Public, null, _property.PropertyType, index, null);
        if (implemented is null)
          continue;
        if (attributes is null)
          attributes = new List<object>();
        attributes.AddRange(implemented.GetCustomAttributes(false));
      }
      return attributes?.ToArray();
    }

    /// <summary>
    /// The properties of the base types that the property overrides (the same virtual slot: a property hiding another one with <c>new</c> does not override it).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
    private static IEnumerable<PropertyInfo> OverriddenProperties(PropertyInfo property)
    {
      MethodInfo accessor = property.GetGetMethod(true) ?? property.GetSetMethod(true);
      if (accessor is null || !accessor.IsVirtual || property.DeclaringType is null)
        yield break;
      MethodInfo slot = accessor.GetBaseDefinition();
      if (SameMethod(slot, accessor))
        yield break; // declares the slot, overrides nothing

      for (Type type = property.DeclaringType.BaseType; type != null; type = type.BaseType)
      {
        // Not GetProperty(name): indexers share the name "Item"
        PropertyInfo[] candidates = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int t = 0; t < candidates.Length; t++)
        {
          PropertyInfo candidate = candidates[t];
          if (!string.Equals(candidate.Name, property.Name, StringComparison.Ordinal))
            continue;
          MethodInfo candidateAccessor = candidate.GetGetMethod(true) ?? candidate.GetSetMethod(true);
          if (candidateAccessor != null && SameMethod(candidateAccessor.GetBaseDefinition(), slot))
            yield return candidate;
        }
      }
    }

    // MethodInfo equality also compares the type it was reflected from
    private static bool SameMethod(MethodInfo a, MethodInfo b)
    {
      return a.MetadataToken == b.MetadataToken && a.Module == b.Module;
    }
  }
}
