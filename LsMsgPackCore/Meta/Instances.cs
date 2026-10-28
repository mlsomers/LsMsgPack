using LsMsgPack.Generated;
using System;
using System.Diagnostics.CodeAnalysis;
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
    /// An object with properties, as <see cref="MsgPackOptions.ObjectCreation"/> says.
    /// </summary>
    internal static object CreateObject(Type type, MsgPackOptions settings)
    {
      return Create(type, settings?._objectCreation ?? ObjectCreation.ConstructorOrUninitialized);
    }

    /// <summary>
    /// <see cref="CreateObject"/>, or for a type created by the constructor that takes its values (<see cref="ObjectConstructor.WithValues"/>) the arguments that collect the values:
    /// ValueConverter sets the properties on them, then creates the object (<see cref="ObjectConstructor.Arguments.Create"/>). A constructor that sets properties without a public setter
    /// is called also with <see cref="ObjectCreation.Uninitialized"/>: their values could not be set otherwise.
    /// </summary>
    internal static object CreateObjectOrArguments(Type type, MsgPackOptions settings)
    {
      ObjectCreation creation = settings?._objectCreation ?? ObjectCreation.ConstructorOrUninitialized;
      ObjectConstructor constructor = ObjectConstructor.Of(type);
      if (constructor.CreatesWith(creation))
        return constructor.Collect();
      if (creation == ObjectCreation.Uninitialized)
        return Uninitialized(type);
      return Create(type, creation, constructor.Parameterless);
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

      return Create(type, creation, ObjectConstructor.Of(type).Parameterless);
    }

    private static object Create(Type type, ObjectCreation creation, bool parameterless)
    {
      Func<object> generated = GeneratedTypes.Find(type)?.Creator; // the constructor call of the source generator, registered for the process: also where reflection does not see the constructor (trimmed, NativeAOT)
      if (generated != null)
        return generated(); // an exception of the constructor is passed on as it is

      if (parameterless)
        return Construct(type);

      if (creation == ObjectCreation.Constructor)
        throw NoConstructor(type);

      return Uninitialized(type);
    }

    /// <summary>
    /// With the parameterless constructor, an exception of the constructor is passed on as it is (not as the TargetInvocationException of Activator).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = TrimReasons.ModelTypes)]
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

    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = TrimReasons.ModelTypes)]
    internal static object GetUninitializedObject(Type type)
    {
      if (typeof(WeakReference).IsAssignableFrom(type) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(WeakReference<>)))
        throw new MsgPackException($"{type.FullName} is not created without its constructor: the garbage collector crashes the process on an uninitialized weak reference, also when its finalizer is suppressed (see docs/security.md).");

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
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
