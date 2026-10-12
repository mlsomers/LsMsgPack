using System;
using System.ComponentModel;
using System.Reflection;

namespace LsMsgPack.Generated
{
  /// <summary>
  /// What the source generator wrote for one type: how to create it and the accessors of the properties it declares. The serializers derive from it to add their typed parts
  /// (LtMsgPack: the handlers and readers of the type, created without <see cref="Type.MakeGenericType"/>).
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public abstract class GeneratedType
  {
    private static readonly GeneratedProperty[] NoProperties = new GeneratedProperty[0];

    internal readonly Type Type;

    /// <summary>
    /// Calls the accessible parameterless constructor, null when there is none (the runtime creates the type as before).
    /// </summary>
    internal readonly Func<object> Creator;

    /// <summary>
    /// The properties the type declares (not the inherited ones: those are in the description of the base type, matched by <see cref="PropertyInfo.DeclaringType"/>).
    /// </summary>
    internal readonly GeneratedProperty[] Properties;

    internal GeneratedType(Type type, Func<object> creator, GeneratedProperty[] properties)
    {
      Type = type;
      Creator = creator;
      Properties = properties ?? NoProperties;
    }

    /// <summary>
    /// The generated accessors of the property, when they were generated for this property (the same declaring type, name and property type: a model in another assembly may have changed since).
    /// </summary>
    internal GeneratedProperty FindProperty(PropertyInfo property)
    {
      GeneratedProperty[] properties = Properties;
      for (int t = 0; t < properties.Length; t++)
      {
        GeneratedProperty generated = properties[t];
        if (generated.Name == property.Name && generated.PropertyType == property.PropertyType && generated.DeclaringType == property.DeclaringType)
          return generated;
      }
      return null;
    }
  }

  /// <summary>
  /// The generated accessors of one property.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public abstract class GeneratedProperty
  {
    internal readonly string Name;

    internal GeneratedProperty(string name)
    {
      Name = name;
    }

    internal abstract Type DeclaringType { get; }

    internal abstract Type PropertyType { get; }

    internal abstract bool CanGet { get; }

    internal abstract bool CanSet { get; }

    /// <summary>
    /// The getter for any instance of the declaring type (LsMsgPack's PropertyAccessor), null without a generated getter.
    /// </summary>
    internal abstract Func<object, object> BoxedGetter { get; }

    /// <param name="reflection">Sets values that are not of the property type (conversions of reflection), the property</param>
    /// <returns>null without a generated setter</returns>
    internal abstract Action<object, object> BoxedSetter(PropertyInfo reflection);
  }

  /// <summary>
  /// The generated accessors of a property of <typeparamref name="TTarget"/> (a class).
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public abstract class GeneratedProperty<TTarget, TValue> : GeneratedProperty where TTarget : class
  {
    /// <summary>
    /// null when the getter is not accessible to the generated code
    /// </summary>
    internal readonly Func<TTarget, TValue> Get;

    /// <summary>
    /// null when the setter is not accessible to the generated code, or init-only
    /// </summary>
    internal readonly Action<TTarget, TValue> Set;

    internal GeneratedProperty(string name, Func<TTarget, TValue> get, Action<TTarget, TValue> set) : base(name)
    {
      Get = get;
      Set = set;
    }

    internal override Type DeclaringType { get { return typeof(TTarget); } }

    internal override Type PropertyType { get { return typeof(TValue); } }

    internal override bool CanGet { get { return Get != null; } }

    internal override bool CanSet { get { return Set != null; } }

    internal override Func<object, object> BoxedGetter
    {
      get
      {
        Func<TTarget, TValue> get = Get;
        if (get is null)
          return null;
        return instance => get((TTarget)instance);
      }
    }

    internal override Action<object, object> BoxedSetter(PropertyInfo reflection)
    {
      Action<TTarget, TValue> set = Set;
      if (set is null)
        return null;

      return (instance, value) => // as PropertyAccessor's typed delegates
      {
        if (value is TValue typed)
          set((TTarget)instance, typed);
        else if (value is null && (object)default(TValue) is null) // reference types and Nullable<T>
          set((TTarget)instance, default(TValue));
        else
          reflection.SetValue(instance, value, null); // reflection also converts other values (e.g. null to the default of a value type, primitive widening)
      };
    }
  }
}
