using LsMsgPack.Meta;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LtMsgPack.Writing
{
  internal delegate bool RefPredicate<T>(ref T value);

  /// <summary>
  /// Whether a struct value is the default value FilterDefaultValues leaves out: LsMsgPack.Core compares it with <c>Activator.CreateInstance(type).Equals(value)</c>,
  /// which for a struct without its own Equals is ValueType.Equals. That compares the fields by reflection, boxing each of them, whenever the struct has float or double fields,
  /// references or padding (otherwise it compares the bytes). This is the same comparison compiled once per type: each field with the Equals of its type against its default.
  /// <para>A framework struct with its own Equals that implements IEquatable&lt;T&gt; (System.Numerics.Vector3, Quaternion, the matrices: written by their fields) is compared by its typed Equals with the default,
  /// what its Equals(object) does, without the box (as the source generator's IsDefault).
  /// Any other struct with its own Equals or a declared parameterless constructor (its "default" is what the constructor makes), or one the expression cannot describe, is compared as Core does.</para>
  /// </summary>
  internal static class StructDefault<T>
  {
    private static readonly object DefaultBox = CreateDefault();

    private static readonly RefPredicate<T> Predicate = Create();

    internal static bool IsDefault(ref T value)
    {
      return Predicate(ref value);
    }

    /// <summary>What Core compares with (FilterDefaultValues.DefaultInstances, PropertyAttributeSet's TypeDefault)</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2087", Justification = TrimReasons.ModelTypes)]
    private static object CreateDefault()
    {
      return typeof(T).IsValueType ? Activator.CreateInstance(typeof(T)) : null;
    }

    /// <summary>As LsMsgPack.Core compares (FilterDefaultValues), the value boxed</summary>
    private static bool IsDefaultBoxed(ref T value)
    {
      return DefaultBox.Equals(value);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2090", Justification = TrimReasons.ModelTypes)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only compiled where the runtime supports dynamic code (PropertyAccessor.CanCompile), a failure compares boxed.")]
    private static RefPredicate<T> Create()
    {
      Type type = typeof(T);
      if (!PropertyAccessor.CanCompile) // without dynamic code (NativeAOT, a generated struct) expressions are interpreted: compare as Core does
        return IsDefaultBoxed;
#if NET5_0_OR_GREATER
      if (!RuntimeFeature.IsDynamicCodeSupported) // never true after CanCompile, but a constant for NativeAOT: it then leaves out the expression compiler
        return IsDefaultBoxed;
#endif
      if (type.IsValueType && OwnEquals(type))
        return TypedEquals(type) ?? IsDefaultBoxed;
      if (!type.IsValueType || OwnEquals(type) || type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null) // Core's default is what that constructor makes
        return IsDefaultBoxed;
      try
      {
        ParameterExpression value = Expression.Parameter(type.MakeByRefType(), "value");
        Expression body = FieldsAreDefault(value, type, 0);
        if (body is null)
          return IsDefaultBoxed;
        return Expression.Lambda<RefPredicate<T>>(body, value).Compile();
      }
      catch (Exception) // a runtime or type the expression does not handle: compare as Core does
      {
        return IsDefaultBoxed;
      }
    }

    /// <returns>null when the fields cannot be compared here (then the struct is compared boxed)</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    private static Expression FieldsAreDefault(Expression value, Type type, int depth)
    {
      if (depth > 16 || type.IsExplicitLayout) // overlapping fields: ValueType.Equals may compare the bytes instead
        return null;
      FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); // the fields ValueType.Equals compares
      Expression all = Expression.Constant(true);
      for (int t = fields.Length - 1; t >= 0; t--)
      {
        FieldInfo field = fields[t];
        if (field.IsDefined(typeof(FixedBufferAttribute), false)) // the buffer is more than its one field
          return null;
        Expression isDefault = FieldIsDefault(Expression.Field(value, field), field.FieldType, depth);
        if (isDefault is null)
          return null;
        all = t == fields.Length - 1 ? isDefault : Expression.AndAlso(isDefault, all);
      }
      return all;
    }

    /// <summary>
    /// The field of the default instance (all zero, the struct has no parameterless constructor) equals the value, as ValueType.Equals compares them: <c>defaultField.Equals(valueField)</c>.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
    private static Expression FieldIsDefault(Expression value, Type type, int depth)
    {
      if (type.IsPointer || type.IsByRef)
        return null;
      if (!type.IsValueType) // a null field of the default instance equals a null
        return Expression.ReferenceEqual(value, Expression.Constant(null, type));
      if (type.IsEnum)
      {
        Type underlying = Enum.GetUnderlyingType(type);
        return Expression.Equal(Expression.Convert(value, underlying), Expression.Default(underlying));
      }
      if (type.IsPrimitive) // float and double too: 0f.Equals(-0f) is true, 0f.Equals(NaN) false, as ==
        return Expression.Equal(value, Expression.Default(type));
      if (type == typeof(decimal)) // decimal.Equals: 0.00m equals 0m
        return Expression.Equal(value, Expression.Constant(0m));
      Type nullable = Nullable.GetUnderlyingType(type);
      if (nullable != null) // a Nullable without a value is boxed as null
        return Expression.Not(Expression.Property(value, type.GetProperty("HasValue")));
      if (type == typeof(Guid) || type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(DateTimeOffset)) // their Equals(T) is their Equals(object)
        return Expression.Call(Expression.Default(type), type.GetMethod(nameof(Equals), new Type[] { type }), value);
      if (OwnEquals(type)) // compared boxed, as ValueType.Equals does
      {
        object defaultField = Expression.Lambda<Func<object>>(Expression.Convert(Expression.Default(type), typeof(object))).Compile()(); // zero, not what a parameterless constructor makes
        return Expression.Call(Expression.Constant(defaultField), typeof(object).GetMethod(nameof(Equals), new Type[] { typeof(object) }), Expression.Convert(value, typeof(object)));
      }
      return FieldsAreDefault(value, type, depth + 1);
    }

    /// <summary>
    /// <c>default(T).Equals(value)</c> through IEquatable&lt;T&gt; for a framework struct (whose Equals(object) calls it), null for other structs: their Equals(object) may differ from Equals(T).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only compiled where the runtime supports dynamic code (PropertyAccessor.CanCompile), a failure compares boxed.")]
    private static RefPredicate<T> TypedEquals(Type type)
    {
      if (!TypeResolver.IsFrameworkAssembly(type.Assembly) || !typeof(IEquatable<T>).IsAssignableFrom(type)
        || type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null)
        return null;
      MethodInfo equals = type.GetMethod(nameof(Equals), BindingFlags.Instance | BindingFlags.Public, null, new Type[] { type }, null);
      if (equals is null)
        return null;
      try
      {
        ParameterExpression value = Expression.Parameter(type.MakeByRefType(), "value");
        return Expression.Lambda<RefPredicate<T>>(Expression.Call(Expression.Default(type), equals, value), value).Compile();
      }
      catch (Exception)
      {
        return null;
      }
    }

    /// <summary>
    /// The struct overrides Equals(object) (otherwise it is ValueType.Equals).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    private static bool OwnEquals(Type type)
    {
      MethodInfo equals = type.GetMethod(nameof(Equals), BindingFlags.Instance | BindingFlags.Public, null, new Type[] { typeof(object) }, null);
      return equals != null && equals.DeclaringType != typeof(ValueType);
    }
  }
}
