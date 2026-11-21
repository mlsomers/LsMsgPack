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
    /// <param name="constructor">The type's (<see cref="ObjectConstructor.Of"/>, which the caller asks for its callbacks too)</param>
    internal static object CreateObjectOrArguments(Type type, ObjectConstructor constructor, MsgPackOptions settings)
    {
      ObjectCreation creation = settings?._objectCreation ?? ObjectCreation.ConstructorOrUninitialized;
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

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
      if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported && TypeResolver.IsFrameworkAssembly(type.Assembly))
        throw TrimmedConstructor(type);
#endif
      return Uninitialized(type);
    }

    /// <summary>
    /// NativeAOT: a framework type whose parameterless constructor reflection doesn't see (its metadata trimmed away). Created uninitialized it would be broken
    /// (a List without its array: a NullReferenceException at the first Add), so it is refused.
    /// </summary>
    private static MsgPackException TrimmedConstructor(Type type)
    {
      return new MsgPackException($"Unable to create {type}: without dynamic code (NativeAOT) its constructor was trimmed away. Create the serializer with the context of LsMsgPack's source generator "
        + "(it creates the collections its classes hold), or keep the constructor (e.g. [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(...))] in the application).");
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
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
      catch (NotSupportedException ex) when (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported) // NativeAOT: e.g. a List<Guid> for an IList<Guid> the application never creates
      {
        throw NoCode(Display(type), "Create one in the application (e.g. new " + Display(type) + "())", ex);
      }
#endif
    }

    /// <summary>
    /// NativeAOT has no code for an instantiation the application never uses (a value type argument), e.g. the List&lt;Guid&gt; created for a member declared as IList&lt;Guid&gt;.
    /// </summary>
    /// <param name="missing">What has no code (the type or the factory method)</param>
    /// <param name="fix">How the application gets it</param>
    internal static MsgPackException NoCode(string missing, string fix, Exception ex)
    {
      return new MsgPackException($"Unable to create {missing}: without dynamic code (NativeAOT) the application has no code for it (it never uses it). {fix}, "
        + "or create the serializer with the context of LsMsgPack's source generator (it creates the collections its classes hold).", ex);
    }

    /// <summary>
    /// The name as C# writes it, for messages: List&lt;Guid&gt;, not List`1[System.Guid].
    /// </summary>
    internal static string Display(Type type)
    {
      if (type.IsArray)
        return Display(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
      if (!type.IsGenericType)
        return type.Name;
      string name = type.Name;
      int tick = name.IndexOf('`');
      Type[] arguments = type.GetGenericArguments();
      string[] names = new string[arguments.Length];
      for (int t = 0; t < arguments.Length; t++)
        names[t] = Display(arguments[t]);
      return (tick < 0 ? name : name.Substring(0, tick)) + "<" + string.Join(", ", names) + ">";
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
