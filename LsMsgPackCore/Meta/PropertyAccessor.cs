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
  /// Gets and sets the value of one property, shared by all <see cref="FullPropertyInfo"/> instances of that property (they can be per session).
  /// <para>Uses the accessors of the source generator when they are registered (<see cref="GeneratedTypes"/>).
  /// Otherwise starts with reflection, once a property has been used <see cref="CompileAfterCalls"/> times, typed delegates bound to its get and set methods take over (when the runtime supports dynamic code, see <see cref="CanCompile"/>).</para>
  /// <para>Exceptions thrown by the getter or setter are rethrown as they are, not wrapped in a <see cref="TargetInvocationException"/> (as reflection does), so they do not depend on whether the delegates are bound yet.</para>
  /// </summary>
  internal sealed class PropertyAccessor
  {
    /// <summary>
    /// Binding the delegates costs about 6 µs per property (and about 1 ms of JIT for the first property of each value type), a delegate call saves about 25 ns compared to reflection.
    /// So properties that are only used a few times are left to reflection.
    /// </summary>
    internal const int CompileAfterCalls = 100;

    private static readonly ConcurrentDictionary<PropertyInfo, PropertyAccessor> Cache = new ConcurrentDictionary<PropertyInfo, PropertyAccessor>();

    public static PropertyAccessor Get(PropertyInfo property)
    {
      return Cache.GetOrAdd(property, p => new PropertyAccessor(p));
    }

    private readonly PropertyInfo _property;
    private Func<object, object> _getter;
    private Action<object, object> _setter;
    private int _getCalls;
    private int _setCalls;

    private PropertyAccessor(PropertyInfo property)
    {
      _property = property;
      GeneratedProperty generated = GeneratedTypes.FindProperty(property); // the accessors of the source generator, registered for the process
      if (generated != null)
      {
        _getter = generated.BoxedGetter;
        _setter = generated.BoxedSetter(property);
      }
      else if (property.GetSetMethod(true) is null && SerializationRules.IsAnonymous(property.DeclaringType))
        _setter = AnonymousSetter(property);
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
          _getter = Compile(_property)?.Getter ?? (obj => _property.GetValue(obj, null));

        return _property.GetValue(instance, null);
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null)
      {
        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw; // not reached
      }
    }

    public void SetValue(object instance, object value)
    {
      try
      {
        Action<object, object> setter = _setter;
        if (setter != null)
        {
          setter(instance, value);
          return;
        }

        if (Interlocked.Increment(ref _setCalls) == CompileAfterCalls)
          _setter = Compile(_property)?.Setter ?? ((obj, val) => _property.SetValue(obj, val, null));

        _property.SetValue(instance, value, null);
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null) // also from the reflection fallback of a bound setter
      {
        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw; // not reached
      }
    }

    /// <summary>
    /// Whether the runtime supports dynamic code: the delegates need a generic class instantiated for the declaring type and property type at runtime,
    /// which is not possible without it (NativeAOT, iOS without the interpreter).
    /// <para>Not <c>IsDynamicCodeCompiled</c>, which is false on WebAssembly: there Mono interprets the code (also the generic instantiations an AOT build did not compile),
    /// and the delegates are still 3-4.7x faster than reflection (LtMsgPack, invoice benchmark, interpreter and AOT, .NET 10, October 2026).</para>
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

    internal interface ITypedAccessor
    {
      /// <summary>null when the property has no getter</summary>
      Func<object, object> Getter { get; }

      /// <summary>null when the property has no setter</summary>
      Action<object, object> Setter { get; }
    }

    /// <summary>
    /// Delegates bound to the get and set methods of a property of <typeparamref name="TTarget"/> (a class), called like any other method (virtual properties call the override).
    /// </summary>
    private sealed class TypedAccessor<TTarget, TValue> : ITypedAccessor where TTarget : class
    {
      private readonly PropertyInfo _property;
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
        else
          _property.SetValue(instance, value, null); // reflection also converts other values (e.g. null to the default of a value type, primitive widening)
      }
    }
  }
}
