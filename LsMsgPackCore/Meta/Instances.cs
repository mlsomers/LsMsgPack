using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// Creates the instances that are filled while deserializing (see <see cref="ObjectCreation"/>).
  /// </summary>
  internal static class Instances
  {
    /// <summary>
    /// Whether the type has a (public or non-public) parameterless constructor, without one <see cref="Activator.CreateInstance(Type, bool)"/> would throw (slow) for every instance.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, bool> HasParameterlessConstructor = new ConcurrentDictionary<Type, bool>();

    /// <summary>
    /// An object with properties, as <see cref="MsgPackOptions.ObjectCreation"/> says.
    /// </summary>
    internal static object CreateObject(Type type, MsgPackOptions settings)
    {
      return Create(type, settings?._objectCreation ?? ObjectCreation.ConstructorOrUninitialized);
    }

    /// <summary>
    /// A collection or dictionary: always with its constructor when it has one (an uninitialized collection is broken), only with it for <see cref="ObjectCreation.Constructor"/>.
    /// </summary>
    internal static object CreateCollection(Type type, MsgPackOptions settings)
    {
      return Create(type, settings?._objectCreation == ObjectCreation.Constructor ? ObjectCreation.Constructor : ObjectCreation.ConstructorOrUninitialized);
    }

    internal static object Create(Type type, ObjectCreation creation)
    {
      if (creation == ObjectCreation.Uninitialized)
        return Uninitialized(type);

      if (HasConstructor(type))
        return Construct(type);

      if (creation == ObjectCreation.Constructor)
        throw NoConstructor(type);

      return Uninitialized(type);
    }

    internal static bool HasConstructor(Type type)
    {
      return HasParameterlessConstructor.GetOrAdd(type, t => t.IsValueType || t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null);
    }

    /// <summary>
    /// With the parameterless constructor, an exception of the constructor is passed on as it is (not as the TargetInvocationException of Activator).
    /// </summary>
    private static object Construct(Type type)
    {
      try
      {
        return Activator.CreateInstance(type, true);
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null)
      {
        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw; // not reached
      }
    }

    internal static MsgPackException NoConstructor(Type type)
    {
      return new MsgPackException($"Unable to create {type.FullName}: it has no parameterless constructor and {nameof(MsgPackOptions)}.{nameof(MsgPackOptions.ObjectCreation)} is {nameof(ObjectCreation.Constructor)}.");
    }

    /// <summary>
    /// Without a constructor, and without the finalizer: some finalizers fail on an object that was never constructed, an exception on the finalizer thread ends the process.
    /// </summary>
    internal static object Uninitialized(Type type)
    {
      object result = GetUninitializedObject(type);
      GC.SuppressFinalize(result);
      return result;
    }

    internal static object GetUninitializedObject(Type type)
    {
      if (typeof(WeakReference).IsAssignableFrom(type) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(WeakReference<>)))
        throw new MsgPackException($"{type.FullName} is not created without its constructor: the garbage collector crashes the process on an uninitialized weak reference, also when its finalizer is suppressed (see docs/security.md).");

#if NETSTANDARD2_1_OR_GREATER
      return System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
#else
      try
      {
        return System.Runtime.Serialization.FormatterServices.GetSafeUninitializedObject(type);
      }
      catch (System.Security.SecurityException) // partial trust (.NET Framework)
      {
        return System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
      }
#endif
    }
  }
}
