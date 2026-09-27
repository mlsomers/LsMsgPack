using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// Gets and sets the value of one property, shared by all <see cref="FullPropertyInfo"/> instances of that property (they can be per session).
  /// <para>Starts with reflection, once a property has been used <see cref="CompileAfterCalls"/> times, typed delegates bound to its get and set methods take over (when the runtime compiles code, see <see cref="CanCompile"/>).</para>
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
    /// Whether the runtime compiles code: the delegates need a generic class instantiated for the declaring type and property type at runtime,
    /// which is not possible (or not faster than reflection) without a JIT, e.g. NativeAOT, iOS or WebAssembly.
    /// </summary>
    internal static bool CanCompile
    {
      get
      {
        if (!MsgPackSettings.CompilePropertyAccessors)
          return false;

#if NETSTANDARD2_1_OR_GREATER
        return System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeCompiled;
#else
        return true; // .NET Standard 2.0 cannot ask, the runtimes that pick this build (e.g. .NET Framework) have a JIT. Otherwise switch off MsgPackSettings.CompilePropertyAccessors.
#endif
      }
    }

    /// <returns>null when the property cannot (or should not) get typed delegates, reflection is used instead</returns>
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
