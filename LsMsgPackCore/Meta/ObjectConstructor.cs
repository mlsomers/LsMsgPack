using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// How the objects of a type are constructed (one lookup per object, see <see cref="Instances"/>): whether it has a parameterless constructor, and the constructor that sets
  /// properties without a public setter (immutable classes and structs: <c>Money(decimal amount, string currency)</c> with get-only <c>Amount</c> and <c>Currency</c>), as System.Text.Json calls it.
  /// <para>The constructor (<see cref="WithValues"/>): the one with [JsonConstructor] (System.Text.Json, Json.NET) or [SerializationConstructor] (MessagePack-CSharp), otherwise the only public one when there is no public parameterless constructor.
  /// A parameter takes the property with its name (ignoring case, the exact name first) and its type. It is used only when it takes a property without a public setter
  /// (FilterNonSettable keeps those): otherwise the type is created and its properties set as before (records with positional parameters have init setters).</para>
  /// <para>Not for framework types (refused or written as their plain value, see SerializationRules.ThrowIfUnsupportedFrameworkType), collections and anonymous types (their fields are set, see PropertyAccessor).</para>
  /// </summary>
  internal sealed class ObjectConstructor
  {
    private static readonly ConcurrentDictionary<Type, ObjectConstructor> Cache = new ConcurrentDictionary<Type, ObjectConstructor>();

    /// <summary>
    /// Whether the type has a (public or non-public) parameterless constructor, without one <see cref="Activator.CreateInstance(Type, bool)"/> would throw (slow) for every instance.
    /// </summary>
    internal readonly bool Parameterless;

    /// <summary>
    /// The constructor taking the values of properties, null when the type is created without arguments.
    /// </summary>
    internal readonly ConstructorInfo WithValues;

    private readonly Type _type;

    /// <summary>
    /// The parameter of each property name.
    /// </summary>
    private readonly Dictionary<string, int> _parameterOf;

    /// <summary>
    /// The default of the parameters no property is read into: their default value, otherwise null (the default of the type, also for value types).
    /// </summary>
    private readonly object[] _defaults;

    internal static ObjectConstructor Of(Type type)
    {
      if (!Cache.TryGetValue(type, out ObjectConstructor found))
        found = Cache.GetOrAdd(type, t => new ObjectConstructor(t));
      return found;
    }

    /// <summary>
    /// Whether a constructor of the type the property was found on sets it (for FilterNonSettable).
    /// </summary>
    internal static bool Sets(PropertyInfo property)
    {
      Type type = property.ReflectedType;
      if (type is null)
        return false;
      ObjectConstructor constructor = Of(type);
      return constructor._parameterOf != null && constructor._parameterOf.ContainsKey(property.Name);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    private ObjectConstructor(Type type)
    {
      _type = type;
      Parameterless = type.IsValueType || type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null;
      if (!TakesValues(type))
        return;

      ConstructorInfo constructor = Choose(type);
      if (constructor is null)
        return;

      ParameterInfo[] parameters = constructor.GetParameters();
      PropertyInfo[] props = FullPropertyInfo.GetProperties(type);
      Dictionary<string, int> parameterOf = new Dictionary<string, int>(StringComparer.Ordinal);
      bool setsReadOnly = false;
      object[] defaults = new object[parameters.Length];
      for (int t = 0; t < parameters.Length; t++)
      {
        ParameterInfo parameter = parameters[t];
        if (parameter.HasDefaultValue && !(parameter.DefaultValue is DBNull) && !(parameter.DefaultValue is Missing))
          defaults[t] = parameter.DefaultValue;
        PropertyInfo prop = Find(props, parameter);
        if (prop is null || parameterOf.ContainsKey(prop.Name))
          continue;
        parameterOf.Add(prop.Name, t);
        setsReadOnly |= prop.GetSetMethod() is null;
      }
      if (!setsReadOnly) // set as before, the constructor is not needed
        return;

      WithValues = constructor;
      _parameterOf = parameterOf;
      _defaults = defaults;
    }

    /// <summary>
    /// A type that is written as an object with properties, of the user's (not a framework type, a collection or an anonymous type).
    /// </summary>
    private static bool TakesValues(Type type)
    {
      return !type.IsAbstract && !type.IsInterface && !type.IsArray && !type.IsPrimitive && !type.IsEnum && !type.IsPointer && !type.ContainsGenericParameters
        && !TypeResolver.IsFrameworkAssembly(type.Assembly) && !typeof(IEnumerable).IsAssignableFrom(type) && !SerializationRules.IsAnonymous(type);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    private static ConstructorInfo Choose(Type type)
    {
      ConstructorInfo[] all = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      ConstructorInfo marked = null;
      ConstructorInfo single = null;
      int markedCount = 0, publicCount = 0;
      bool publicParameterless = false;
      for (int t = 0; t < all.Length; t++)
      {
        ConstructorInfo constructor = all[t];
        if (IsMarked(constructor))
        {
          marked = constructor;
          markedCount++;
        }
        if (!constructor.IsPublic)
          continue;
        publicCount++;
        single = constructor;
        publicParameterless |= constructor.GetParameters().Length == 0;
      }

      if (markedCount > 0)
        return markedCount == 1 && marked.GetParameters().Length > 0 ? marked : null;
      return publicCount == 1 && !publicParameterless ? single : null;
    }

    /// <summary>
    /// [JsonConstructor] of System.Text.Json or Json.NET, [SerializationConstructor] of MessagePack-CSharp: recognized by name, LsMsgPack does not reference those libraries.
    /// </summary>
    private static bool IsMarked(ConstructorInfo constructor)
    {
      foreach (CustomAttributeData attribute in constructor.CustomAttributes)
      {
        switch (attribute.AttributeType.FullName)
        {
          case "System.Text.Json.Serialization.JsonConstructorAttribute":
          case "Newtonsoft.Json.JsonConstructorAttribute":
          case "MessagePack.SerializationConstructorAttribute":
            return true;
        }
      }
      return false;
    }

    /// <returns>The property the parameter sets: of the same type, the same name or (when that is not there) the only one with the name ignoring case. Null when none.</returns>
    private static PropertyInfo Find(PropertyInfo[] props, ParameterInfo parameter)
    {
      if (string.IsNullOrEmpty(parameter.Name))
        return null;
      PropertyInfo found = null;
      bool ambiguous = false;
      for (int t = 0; t < props.Length; t++)
      {
        PropertyInfo prop = props[t];
        if (prop.PropertyType != parameter.ParameterType || prop.GetGetMethod() is null || !string.Equals(prop.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
          continue;
        if (string.Equals(prop.Name, parameter.Name, StringComparison.Ordinal))
          return prop;
        ambiguous = found != null;
        found = prop;
      }
      return ambiguous ? null : found;
    }

    /// <summary>
    /// Collects the values of an object until it is created by <see cref="Create"/>: stands for the object while its values are read (ValueConverter, ReadDifferences).
    /// </summary>
    internal Arguments Collect()
    {
      return new Arguments(this);
    }

    internal sealed class Arguments
    {
      private readonly ObjectConstructor _constructor;
      private readonly object[] _values;
      private readonly bool[] _set;
      private List<KeyValuePair<FullPropertyInfo, object>> _properties; // the properties no parameter takes, set after the constructor

      /// <summary>
      /// ReadDifferences kept it as a sample (it gets the object once created).
      /// </summary>
      internal bool Reported;

      internal Arguments(ObjectConstructor constructor)
      {
        _constructor = constructor;
        _values = new object[constructor._defaults.Length];
        _set = new bool[_values.Length];
      }

      /// <summary>
      /// The type of the object.
      /// </summary>
      internal Type Type { get { return _constructor._type; } }

      internal void Set(FullPropertyInfo prop, object value)
      {
        if (_constructor._parameterOf.TryGetValue(prop.PropertyInfo.Name, out int index))
        {
          _values[index] = value;
          _set[index] = true;
        }
        else
          (_properties ?? (_properties = new List<KeyValuePair<FullPropertyInfo, object>>())).Add(new KeyValuePair<FullPropertyInfo, object>(prop, value));
      }

      /// <summary>
      /// Calls the constructor, then sets the properties it does not take. A parameter whose property is not in the data gets the value the writer leaves out
      /// (<see cref="FullPropertyInfo.NilValue"/>, as a nil in the array layout: a missing value is a left out default), one that takes no property here its default value.
      /// An exception of the constructor is passed on as it is.
      /// </summary>
      /// <param name="props">The properties read</param>
      internal object Create(FullPropertyInfo[] props, MsgPackOptions settings)
      {
        object[] values = _values;
        for (int t = values.Length - 1; t >= 0; t--)
          if (!_set[t])
            values[t] = _constructor._defaults[t];
        for (int t = props.Length - 1; t >= 0; t--)
          if (_constructor._parameterOf.TryGetValue(props[t].PropertyInfo.Name, out int index) && !_set[index])
            values[index] = props[t].NilValue(settings);

        object result;
        try
        {
          result = _constructor.WithValues.Invoke(values);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
          ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
          throw; // not reached
        }

        if (_properties != null)
          for (int t = 0; t < _properties.Count; t++)
            _properties[t].Key.SetValue(result, _properties[t].Value); // a struct: its box
        return result;
      }
    }
  }
}
