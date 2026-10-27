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
  /// How the objects of a type are constructed (one lookup per object, see <see cref="Instances"/>): whether it has a parameterless constructor, and the constructor that takes
  /// the values of properties, as System.Text.Json calls it (immutable classes and structs: <c>Money(decimal amount, string currency)</c> with get-only <c>Amount</c> and <c>Currency</c>,
  /// records with positional parameters, a class with only <c>Owner(string name)</c> whose fields are initialized by it).
  /// <para>The constructor (<see cref="WithValues"/>): the one with [JsonConstructor] (System.Text.Json, Json.NET) or [SerializationConstructor] (MessagePack-CSharp), otherwise the only public one when there is no public parameterless constructor.
  /// A parameter takes the property with its name (ignoring case, the exact name first) and its type. It is used when it takes a property without a public setter (<see cref="Required"/>,
  /// FilterNonSettable keeps those), or when every parameter takes a property or has a default value and the type is a class or the constructor is marked (a struct is otherwise created
  /// by its default constructor, as System.Text.Json does). Until October 2026 only the first: a class with only <c>Owner(string name)</c> and a settable <c>Name</c> was created uninitialized.</para>
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

    /// <summary>
    /// The constructor takes a property without a public setter: its values can't be set otherwise, so it is called also with <see cref="ObjectCreation.Uninitialized"/>.
    /// </summary>
    internal readonly bool Required;

#if NET8_0_OR_GREATER
    /// <summary>
    /// Calls <see cref="WithValues"/> without wrapping its exceptions, about as fast as a compiled call (it emits one when the runtime supports dynamic code, otherwise reflection).
    /// </summary>
    private readonly ConstructorInvoker _invoker;
#endif

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

      ConstructorInfo constructor = Choose(type, out bool marked);
      if (constructor is null)
        return;

      ParameterInfo[] parameters = constructor.GetParameters();
      PropertyInfo[] props = FullPropertyInfo.GetProperties(type);
      Dictionary<string, int> parameterOf = new Dictionary<string, int>(StringComparer.Ordinal);
      bool setsReadOnly = false;
      bool allTaken = true; // every parameter takes a property or has a default value
      object[] defaults = new object[parameters.Length];
      for (int t = 0; t < parameters.Length; t++)
      {
        ParameterInfo parameter = parameters[t];
        bool hasDefault = parameter.HasDefaultValue && !(parameter.DefaultValue is DBNull) && !(parameter.DefaultValue is Missing);
        if (hasDefault)
          defaults[t] = parameter.DefaultValue;
        PropertyInfo prop = Find(props, parameter);
        if (prop is null || parameterOf.ContainsKey(prop.Name))
        {
          allTaken &= hasDefault || parameter.IsOptional;
          continue;
        }
        parameterOf.Add(prop.Name, t);
        setsReadOnly |= prop.GetSetMethod() is null;
      }
      // Otherwise created and set as before: a constructor taking anything else (a service, a parameter for no property) would get nulls, a struct has its default constructor
      if (!setsReadOnly && !(allTaken && (marked || !type.IsValueType)))
        return;

      WithValues = constructor;
      Required = setsReadOnly;
      _parameterOf = parameterOf;
      _defaults = defaults;
#if NET8_0_OR_GREATER
      _invoker = ConstructorInvoker.Create(constructor);
#endif
    }

    /// <summary>
    /// Whether the objects are created by <see cref="WithValues"/> with these options: always when it is <see cref="Required"/>, otherwise not with <see cref="ObjectCreation.Uninitialized"/>.
    /// </summary>
    internal bool CreatesWith(ObjectCreation creation)
    {
      return WithValues != null && (Required || creation != ObjectCreation.Uninitialized);
    }

    /// <summary>
    /// The number of parameters of <see cref="WithValues"/>.
    /// </summary>
    internal int ParameterCount { get { return _defaults.Length; } }

    /// <returns>Whether a parameter of <see cref="WithValues"/> takes the property with this name, and which</returns>
    internal bool TryGetParameter(string propertyName, out int index)
    {
      return _parameterOf.TryGetValue(propertyName, out index);
    }

    /// <summary>
    /// The value of a parameter whose property is not read: its default value, otherwise null (the default of the type, also for value types).
    /// </summary>
    internal object DefaultOf(int index)
    {
      return _defaults[index];
    }

    /// <summary>
    /// Calls <see cref="WithValues"/> with the first <see cref="ParameterCount"/> values (null for a value type is its default). An exception of the constructor is passed on as it is.
    /// </summary>
    internal object Invoke(object[] values)
    {
#if NET8_0_OR_GREATER
      return _invoker.Invoke(new Span<object>(values, 0, _defaults.Length));
#else
      object[] arguments = values;
      if (values.Length != _defaults.Length)
      {
        arguments = new object[_defaults.Length];
        Array.Copy(values, arguments, arguments.Length);
      }
      try
      {
        return WithValues.Invoke(arguments);
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null)
      {
        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw; // not reached
      }
#endif
    }

    /// <summary>
    /// A type that is written as an object with properties, of the user's (not a framework type, a collection or an anonymous type).
    /// </summary>
    private static bool TakesValues(Type type)
    {
      return !type.IsAbstract && !type.IsInterface && !type.IsArray && !type.IsPrimitive && !type.IsEnum && !type.IsPointer && !type.ContainsGenericParameters
        && !TypeResolver.IsFrameworkAssembly(type.Assembly) && !typeof(IEnumerable).IsAssignableFrom(type) && !SerializationRules.IsAnonymous(type);
    }

    /// <param name="marked">The constructor has [JsonConstructor] or [SerializationConstructor]</param>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    private static ConstructorInfo Choose(Type type, out bool marked)
    {
      ConstructorInfo[] all = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      ConstructorInfo found = null;
      ConstructorInfo single = null;
      int markedCount = 0, publicCount = 0;
      bool publicParameterless = false;
      for (int t = 0; t < all.Length; t++)
      {
        ConstructorInfo constructor = all[t];
        if (IsMarked(constructor))
        {
          found = constructor;
          markedCount++;
        }
        if (!constructor.IsPublic)
          continue;
        publicCount++;
        single = constructor;
        publicParameterless |= constructor.GetParameters().Length == 0;
      }

      marked = markedCount > 0;
      if (marked)
        return markedCount == 1 && found.GetParameters().Length > 0 ? found : null;
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

        object result = _constructor.Invoke(values);

        if (_properties != null)
          for (int t = 0; t < _properties.Count; t++)
            _properties[t].Key.SetValue(result, _properties[t].Value); // a struct: its box
        return result;
      }
    }
  }
}
