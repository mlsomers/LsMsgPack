using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// The <c>bool ShouldSerialize{Name}()</c> method of a property or field (<see cref="TypeResolving.Filters.FilterShouldSerialize"/>), found as Json.NET finds it:
  /// public, parameterless, returning bool (an instance method, or a static one), on the type declaring the member or inherited by it (overrides are called virtually;
  /// a method of a derived class for an inherited member is not seen, nor one hiding it with <c>new</c>).
  /// A typed delegate for classes where the runtime supports dynamic code, reflection otherwise.
  /// </summary>
  internal abstract class ShouldSerializeMethod
  {
    internal const string Prefix = "ShouldSerialize";

    internal abstract bool Call(object owner);

    /// <returns>null when the member has no such method</returns>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(TypedShouldSerialize<>))]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ShouldSerializeMethods)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only when the runtime supports dynamic code (CanCompile), a failure falls back to reflection.")]
    internal static ShouldSerializeMethod Find(MemberInfo member)
    {
      Type declaring = member.DeclaringType;
      if (declaring is null)
        return null;
      // As Json.NET's GetMethod(name, Type.EmptyTypes): public, instance or static (called without the owner)
      MethodInfo method = declaring.GetMethod(Prefix + member.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static, null, Type.EmptyTypes, null);
      if (method is null || method.ReturnType != typeof(bool) || method.ContainsGenericParameters)
        return null;

      if (PropertyAccessor.CanCompile && !method.IsStatic && !declaring.IsValueType && !declaring.ContainsGenericParameters)
      {
        try
        {
          Type typed = typeof(TypedShouldSerialize<>).MakeGenericType(declaring);
          return (ShouldSerializeMethod)Activator.CreateInstance(typed, method);
        }
        catch (Exception) // e.g. a runtime that cannot create the generic type
        {
        }
      }
      return new ReflectedShouldSerialize(method);
    }

    private sealed class ReflectedShouldSerialize : ShouldSerializeMethod
    {
      private readonly MethodInfo _method;

      internal ReflectedShouldSerialize(MethodInfo method)
      {
        _method = method;
      }

      internal override bool Call(object owner)
      {
        try
        {
          return (bool)_method.Invoke(owner, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
          System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
          throw;
        }
      }
    }

    private sealed class TypedShouldSerialize<T> : ShouldSerializeMethod where T : class
    {
      private readonly Func<T, bool> _call;

      public TypedShouldSerialize(MethodInfo method)
      {
        _call = (Func<T, bool>)Delegate.CreateDelegate(typeof(Func<T, bool>), method);
      }

      internal override bool Call(object owner)
      {
        return _call((T)owner);
      }
    }
  }
}
