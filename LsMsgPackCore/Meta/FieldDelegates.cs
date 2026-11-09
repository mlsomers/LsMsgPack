using LsMsgPack.Generated;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// Typed delegates that get and set a field, compiled from expression trees: a field has no get or set method to bind a delegate to (as the serializers do for properties).
  /// Compiling costs about 85 µs per delegate (Delegate.CreateDelegate for a property: 2 µs), a call 1.4 ns (FieldInfo.GetValue: 13 ns on .NET 10, 59 ns on .NET 8, boxed).
  /// Expressions skip visibility, so non-public fields work too. Only where the runtime supports dynamic code (the callers check it, a failure leaves them to reflection).
  /// <para>No setter for a readonly field: it is only set through a constructor (see FilterNonSettable).</para>
  /// </summary>
  internal static class FieldDelegates
  {
    private const string Reason = "Only called when the runtime supports dynamic code (the callers check it), a failure falls back to reflection.";

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
    internal static Func<TTarget, TValue> Getter<TTarget, TValue>(FieldInfo field) where TTarget : class
    {
      ParameterExpression target = Expression.Parameter(typeof(TTarget), "target");
      return Expression.Lambda<Func<TTarget, TValue>>(Expression.Field(target, field), target).Compile();
    }

    /// <returns>null for a readonly field</returns>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
    internal static Action<TTarget, TValue> Setter<TTarget, TValue>(FieldInfo field) where TTarget : class
    {
      if (field.IsInitOnly)
        return null;
      ParameterExpression target = Expression.Parameter(typeof(TTarget), "target");
      ParameterExpression value = Expression.Parameter(typeof(TValue), "value");
      return Expression.Lambda<Action<TTarget, TValue>>(Expression.Assign(Expression.Field(target, field), value), target, value).Compile();
    }

    /// <summary>
    /// Reads the field of a struct by reference (no copy of the struct).
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
    internal static StructGetter<TTarget, TValue> StructGetter<TTarget, TValue>(FieldInfo field) where TTarget : struct
    {
      ParameterExpression target = Expression.Parameter(typeof(TTarget).MakeByRefType(), "target");
      return Expression.Lambda<StructGetter<TTarget, TValue>>(Expression.Field(target, field), target).Compile();
    }

    /// <summary>
    /// Sets the field of a struct by reference.
    /// </summary>
    /// <returns>null for a readonly field</returns>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Reason)]
    internal static StructSetter<TTarget, TValue> StructSetter<TTarget, TValue>(FieldInfo field) where TTarget : struct
    {
      if (field.IsInitOnly)
        return null;
      ParameterExpression target = Expression.Parameter(typeof(TTarget).MakeByRefType(), "target");
      ParameterExpression value = Expression.Parameter(typeof(TValue), "value");
      return Expression.Lambda<StructSetter<TTarget, TValue>>(Expression.Assign(Expression.Field(target, field), value), target, value).Compile();
    }
  }
}
