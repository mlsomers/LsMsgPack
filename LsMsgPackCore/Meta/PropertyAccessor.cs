using LsMsgPack.Generated;
using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// Gets and sets the value of one property or field, shared by all <see cref="FullPropertyInfo"/> instances of that member (they can be per session).
  /// <para>Uses the accessors of the source generator when they are registered (<see cref="GeneratedTypes"/>).
  /// Otherwise starts with reflection, once a member has been used <see cref="CompileAfterCalls"/> times, typed delegates take over (when the runtime supports dynamic code, see <see cref="CanCompile"/>):
  /// bound to the get and set methods of a property, compiled from expression trees for a field (a field has no methods to bind to).</para>
  /// <para>Exceptions thrown by the getter or setter are rethrown as they are, not wrapped in a <see cref="TargetInvocationException"/> (as reflection does), so they do not depend on whether the delegates are bound yet.</para>
  /// </summary>
  internal sealed class PropertyAccessor
  {
    /// <summary>
    /// Binding the delegates costs about 6 µs per property (and about 1 ms of JIT for the first property of each value type), a delegate call saves about 25 ns compared to reflection.
    /// So properties that are only used a few times are left to reflection.
    /// </summary>
    internal const int CompileAfterCalls = 100;

    private static readonly ConcurrentDictionary<MemberInfo, PropertyAccessor> Cache = new ConcurrentDictionary<MemberInfo, PropertyAccessor>();

    /// <param name="member">A property or a field</param>
    public static PropertyAccessor Get(MemberInfo member)
    {
      if (Cache.TryGetValue(member, out PropertyAccessor accessor))
        return accessor;
      return Cache.GetOrAdd(member, m => m is FieldInfo field ? new PropertyAccessor(field) : new PropertyAccessor((PropertyInfo)m));
    }

    private readonly PropertyInfo _property; // null for a field
    private readonly FieldInfo _field; // null for a property
    private Func<object, object> _getter;
    private Action<object, object> _setter;
    private int _getCalls;
    private int _setCalls;

    /// <summary>
    /// A property without a setter or a readonly field whose value reading skips (a computed property, see <see cref="TypeResolving.Filters.GetOnlyProperties.Computed"/>):
    /// not of an anonymous type (its field is set), not taken by a constructor (see <see cref="ObjectConstructor"/>) and not a collection that can be filled (see <see cref="GetOnlyCollection"/>).
    /// </summary>
    internal readonly bool Computed;

    /// <summary>
    /// A field: read and set by reflection until the typed delegates are compiled, never set when it is readonly (only through a constructor, as a property without a setter).
    /// </summary>
    private PropertyAccessor(FieldInfo field)
    {
      _field = field;
      GeneratedProperty generated = GeneratedTypes.FindMember(field); // the accessors of the source generator (contract version 4), registered for the process
      if (generated != null)
      {
        _getter = generated.BoxedGetter;
        _setter = generated.BoxedSetter(field); // null for a readonly field
      }
      if (field.IsInitOnly)
      {
        if (GetOnlyCollection.IsFillable(field.FieldType))
          _setter = Fill;
        else if (ObjectConstructor.Sets(field))
          _setter = (obj, val) => throw new InvalidOperationException($"The readonly field {field.DeclaringType.Name}.{field.Name} is set by the constructor only."); // its values are collected for it (ObjectConstructor.Arguments)
        else
          Computed = true;
      }
    }

    private PropertyAccessor(PropertyInfo property)
    {
      _property = property;
      GeneratedProperty generated = GeneratedTypes.FindMember(property); // the accessors of the source generator, registered for the process
      if (generated != null)
      {
        _getter = generated.BoxedGetter;
        _setter = generated.BoxedSetter(property);
      }
      if (property.GetSetMethod(true) is null)
      {
        if (SerializationRules.IsAnonymous(property.DeclaringType))
          _setter = AnonymousSetter(property);
        else if (GetOnlyCollection.IsFillable(property.PropertyType))
          _setter = Fill;
        else
          Computed = !ObjectConstructor.Sets(property); // the constructor's values are collected for it (ObjectConstructor.Arguments), never set
      }
    }

    /// <summary>
    /// The setter of a collection property without one: fills the collection the getter returns. A nil (null) leaves it as the constructor made it, so does a getter returning null.
    /// </summary>
    private void Fill(object instance, object value)
    {
      if (value is null)
        return;
      object target = GetValue(instance);
      if (target != null && !ReferenceEquals(target, value))
        GetOnlyCollection.Fill(target, value);
    }

    /// <summary>
    /// The properties of an anonymous type have no setters (they are serialized all the same, see FilterNonSettable): its fields are set as the constructor sets them,
    /// "&lt;Name&gt;i__Field" in C#, "$Name" for a Key property in Visual Basic. Null when there is no such field (reflection then throws as for any property without a setter).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
    private static Action<object, object> AnonymousSetter(PropertyInfo property)
    {
      const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
      FieldInfo field = property.DeclaringType.GetField(string.Concat("<", property.Name, ">i__Field"), flags) ?? property.DeclaringType.GetField("$" + property.Name, flags);
      if (field is null || field.FieldType != property.PropertyType)
        return null;
      return (instance, value) => field.SetValue(instance, value);
    }

    public object GetValue(object instance)
    {
      try
      {
        Func<object, object> getter = _getter;
        if (getter != null)
          return getter(instance);

        if (Interlocked.Increment(ref _getCalls) == CompileAfterCalls)
          _getter = Compile()?.Getter ?? ReflectionGetter();

        return _field is null ? _property.GetValue(instance, null) : _field.GetValue(instance);
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null)
      {
        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw; // not reached
      }
    }

    public void SetValue(object instance, object value)
    {
      if (Computed) // the value is written only
        return;
      try
      {
        Action<object, object> setter = _setter;
        if (setter != null)
        {
          setter(instance, value);
          return;
        }

        if (Interlocked.Increment(ref _setCalls) == CompileAfterCalls)
          _setter = Compile()?.Setter ?? ReflectionSetter();

        if (_field is null)
          _property.SetValue(instance, value, null);
        else
          _field.SetValue(instance, value);
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null) // also from the reflection fallback of a bound setter
      {
        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw; // not reached
      }
    }

    private Func<object, object> ReflectionGetter()
    {
      FieldInfo field = _field;
      if (field != null)
        return obj => field.GetValue(obj);
      PropertyInfo property = _property;
      return obj => property.GetValue(obj, null);
    }

    private Action<object, object> ReflectionSetter()
    {
      FieldInfo field = _field;
      if (field != null)
        return (obj, val) => field.SetValue(obj, val);
      PropertyInfo property = _property;
      return (obj, val) => property.SetValue(obj, val, null);
    }

    private ITypedAccessor Compile()
    {
      return _field is null ? Compile(_property) : Compile(_field);
    }

    /// <summary>
    /// Whether the runtime supports dynamic code: the delegates need a generic class instantiated for the declaring type and property type at runtime,
    /// which is not possible without it (NativeAOT, iOS without the interpreter).
    /// <para>Not <c>IsDynamicCodeCompiled</c>, which is false on WebAssembly: there Mono interprets the code (also the generic instantiations an AOT build did not compile),
    /// and the delegates are still 3-4.7x faster than reflection (LsMsgPack, invoice benchmark, interpreter and AOT, .NET 10, October 2026).</para>
    /// </summary>
    internal static bool CanCompile
    {
      get
      {
        if (!MsgPackOptions.CompilePropertyAccessors)
          return false;

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        return System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;
#else
        return true; // .NET Standard 2.0 cannot ask, the runtimes that pick this build (e.g. .NET Framework) have a JIT. Otherwise switch off MsgPackOptions.CompilePropertyAccessors.
#endif
      }
    }

    /// <returns>null when the property cannot (or should not) get typed delegates, reflection is used instead</returns>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(TypedAccessor<,>))]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only called when the runtime supports dynamic code (CanCompile), a failure falls back to reflection.")]
    internal static ITypedAccessor Compile(PropertyInfo property)
    {
      Type declaringType = property.DeclaringType;
      Type type = property.PropertyType;
      if (!CanCompile
        || declaringType.IsValueType // an open instance delegate of a struct method needs the struct by reference (and setting a property of a boxed struct needs the box itself)
        || declaringType.ContainsGenericParameters
        || property.GetIndexParameters().Length > 0
        || type.IsByRef
        || type.IsPointer)
        return null;

      MethodInfo get = property.GetGetMethod(true);
      MethodInfo set = property.GetSetMethod(true);
      if ((get != null && get.IsStatic) || (set != null && set.IsStatic))
        return null;

      try
      {
        Type accessorType = typeof(TypedAccessor<,>).MakeGenericType(declaringType, type);
        return (ITypedAccessor)Activator.CreateInstance(accessorType, property, get, set);
      }
      catch (Exception) // e.g. a runtime that cannot create the generic type
      {
        return null;
      }
    }

    /// <returns>null when the field cannot (or should not) get typed delegates, reflection is used instead</returns>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(TypedAccessor<,>))]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only called when the runtime supports dynamic code (CanCompile), a failure falls back to reflection.")]
    internal static ITypedAccessor Compile(FieldInfo field)
    {
      Type declaringType = field.DeclaringType;
      Type type = field.FieldType;
      if (!CanCompile
        || declaringType.IsValueType // setting a field of a boxed struct needs the box itself (FieldInfo.SetValue sets it in the box)
        || declaringType.ContainsGenericParameters
        || field.IsStatic
        || type.IsByRef
        || type.IsPointer)
        return null;

      try
      {
        Type accessorType = typeof(TypedAccessor<,>).MakeGenericType(declaringType, type);
        return (ITypedAccessor)Activator.CreateInstance(accessorType, field);
      }
      catch (Exception) // e.g. a runtime that cannot create the generic type, or compile the expressions
      {
        return null;
      }
    }

    internal interface ITypedAccessor
    {
      /// <summary>null when the property has no getter</summary>
      Func<object, object> Getter { get; }

      /// <summary>null when the property has no setter</summary>
      Action<object, object> Setter { get; }
    }

    /// <summary>
    /// Typed delegates for a property or field of <typeparamref name="TTarget"/> (a class): bound to the get and set methods of a property (called like any other method, virtual properties call the override),
    /// compiled from expression trees for a field (no setter for a readonly field).
    /// </summary>
    private sealed class TypedAccessor<TTarget, TValue> : ITypedAccessor where TTarget : class
    {
      private readonly PropertyInfo _property;
      private readonly FieldInfo _field;
      private readonly Func<TTarget, TValue> _get;
      private readonly Action<TTarget, TValue> _set;

      public TypedAccessor(PropertyInfo property, MethodInfo get, MethodInfo set)
      {
        _property = property;
        if (get != null)
          _get = (Func<TTarget, TValue>)Delegate.CreateDelegate(typeof(Func<TTarget, TValue>), get);
        if (set != null)
          _set = (Action<TTarget, TValue>)Delegate.CreateDelegate(typeof(Action<TTarget, TValue>), set);
      }

      public TypedAccessor(FieldInfo field)
      {
        _field = field;
        _get = FieldDelegates.Getter<TTarget, TValue>(field);
        _set = FieldDelegates.Setter<TTarget, TValue>(field); // null when readonly
      }

      public Func<object, object> Getter { get { return _get is null ? null : (Func<object, object>)GetValue; } }

      public Action<object, object> Setter { get { return _set is null ? null : (Action<object, object>)SetValue; } }

      private object GetValue(object instance)
      {
        return _get((TTarget)instance);
      }

      private void SetValue(object instance, object value)
      {
        if (value is TValue typed)
          _set((TTarget)instance, typed);
        else if (value is null && (object)default(TValue) is null) // reference types and Nullable<T>
          _set((TTarget)instance, default(TValue));
        else if (_field is null)
          _property.SetValue(instance, value, null); // reflection also converts other values (e.g. null to the default of a value type, primitive widening)
        else
          _field.SetValue(instance, value);
      }
    }
  }
}
