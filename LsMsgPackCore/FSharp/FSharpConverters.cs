using LsMsgPack.Converters;
using LsMsgPack.Meta;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LsMsgPack.FSharp
{
  /// <summary>
  /// A getter of a struct, called on the value itself (an open instance delegate of a value type takes it by reference).
  /// </summary>
  internal delegate TResult RefGetter<TOwner, TResult>(ref TOwner owner);

  /// <summary>
  /// Typed delegates over the compiled members of F# types (bound once per converter: no reflection per value).
  /// </summary>
  internal static class FSharpAccess
  {
    internal static Func<TOwner, TResult> Getter<TOwner, TResult>(MethodInfo getter)
    {
      if (!typeof(TOwner).IsValueType)
        return (Func<TOwner, TResult>)Delegate.CreateDelegate(typeof(Func<TOwner, TResult>), getter);
      RefGetter<TOwner, TResult> byRef = (RefGetter<TOwner, TResult>)Delegate.CreateDelegate(typeof(RefGetter<TOwner, TResult>), getter);
      return owner => byRef(ref owner);
    }

    internal static Func<TArgument, TResult> Factory<TArgument, TResult>(MethodInfo factory)
    {
      return (Func<TArgument, TResult>)Delegate.CreateDelegate(typeof(Func<TArgument, TResult>), factory);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2090", Justification = TrimReasons.FSharp)]
    internal static MethodInfo PropertyGetter<TOwner>(string name)
    {
      return typeof(TOwner).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod()
        ?? throw new MsgPackException($"{typeof(TOwner).FullName} has no property {name}: not an F# type as FSharp.Core compiles it.");
    }

    [UnconditionalSuppressMessage("Trimming", "IL2090", Justification = TrimReasons.FSharp)]
    internal static MethodInfo StaticMethod<TOwner>(string name, Type parameter)
    {
      return typeof(TOwner).GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, new[] { parameter }, null)
        ?? throw new MsgPackException($"{typeof(TOwner).FullName} has no static method {name}({parameter.Name}): not an F# type as FSharp.Core compiles it.");
    }

    /// <summary>
    /// A converter of FSharp.Core's types, made for the types of the values (<see cref="Activator"/>: no generated code covers F# projects).
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = TrimReasons.FSharp)]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = TrimReasons.FSharp)]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = TrimReasons.FSharp)]
    [UnconditionalSuppressMessage("Trimming", "IL2071", Justification = TrimReasons.FSharp)]
    /// <param name="definition">One of the converter classes of this file (its constructors are kept)</param>
    internal static MsgPackConverter Create([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] Type definition, Type[] arguments, params object[] constructorArguments)
    {
      Type converterType = definition.MakeGenericType(arguments);
      try
      {
        return (MsgPackConverter)Activator.CreateInstance(converterType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, constructorArguments, null);
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null)
      {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw; // not reached
      }
    }
  }

  internal static class FSharpOptionConverters
  {
    /// <summary>
    /// The converter of <c>'T option</c> or <c>'T voption</c>: a value type is written as its nullable (None is nil, <c>Some 0</c> is 0), anything else as itself.
    /// </summary>
    internal static MsgPackConverter Create(Type optionType, bool valueOption)
    {
      Type value = optionType.GenericTypeArguments[0];
      bool nullable = value.IsValueType && Nullable.GetUnderlyingType(value) is null;
      return FSharpAccess.Create(nullable ? typeof(FSharpNullableOptionConverter<,>) : typeof(FSharpOptionConverter<,>), new[] { optionType, value }, valueOption);
    }
  }

  /// <summary>
  /// <c>'T option</c> / <c>'T voption</c> of a class (or a nullable) as its value: <c>Some x</c> is x, None nil. <c>Some null</c> and <c>Some None</c> are nil too, so they are read as None
  /// (as System.Text.Json and MessagePack.FSharpExtensions do).
  /// </summary>
  internal sealed class FSharpOptionConverter<TOption, T> : MsgPackConverter<TOption, T>
  {
    private readonly Func<TOption, int> _tag; // value options only (None is the struct's default); an option that is not null is Some
    private readonly Func<TOption, T> _value;
    private readonly Func<T, TOption> _some;

    internal FSharpOptionConverter(bool valueOption)
    {
      _tag = valueOption ? FSharpAccess.Getter<TOption, int>(FSharpAccess.PropertyGetter<TOption>("Tag")) : null;
      _value = FSharpAccess.Getter<TOption, T>(FSharpAccess.PropertyGetter<TOption>("Value"));
      _some = FSharpAccess.Factory<T, TOption>(FSharpAccess.StaticMethod<TOption>(valueOption ? "NewValueSome" : "Some", typeof(T)));
    }

    public override T ToSurrogate(TOption value)
    {
      if (_tag != null && _tag(value) == 0)
        return default;
      return _value(value);
    }

    public override TOption FromSurrogate(T surrogate)
    {
      return _some(surrogate);
    }
  }

  /// <summary>
  /// <c>'T option</c> / <c>'T voption</c> of a value type as its nullable: <c>Some 0</c> is 0, None nil.
  /// </summary>
  internal sealed class FSharpNullableOptionConverter<TOption, T> : MsgPackConverter<TOption, T?> where T : struct
  {
    private readonly Func<TOption, int> _tag;
    private readonly Func<TOption, T> _value;
    private readonly Func<T, TOption> _some;

    internal FSharpNullableOptionConverter(bool valueOption)
    {
      _tag = valueOption ? FSharpAccess.Getter<TOption, int>(FSharpAccess.PropertyGetter<TOption>("Tag")) : null;
      _value = FSharpAccess.Getter<TOption, T>(FSharpAccess.PropertyGetter<TOption>("Value"));
      _some = FSharpAccess.Factory<T, TOption>(FSharpAccess.StaticMethod<TOption>(valueOption ? "NewValueSome" : "Some", typeof(T)));
    }

    public override T? ToSurrogate(TOption value)
    {
      if (_tag != null && _tag(value) == 0)
        return null;
      return _value(value);
    }

    public override TOption FromSurrogate(T? surrogate)
    {
      return _some(surrogate.GetValueOrDefault()); // never null (nil is not converted)
    }
  }

  internal static class FSharpCaseNameConverters
  {
    /// <summary>
    /// The converter of the values of <paramref name="type"/>, which are fieldless cases of <paramref name="union"/>: the union type (a union of fieldless cases, or the fieldless cases of a
    /// larger reference union) or the class of one fieldless case.
    /// </summary>
    internal static MsgPackConverter Create(Type type, FSharpUnion union)
    {
      return FSharpAccess.Create(typeof(FSharpCaseNameConverter<>), new[] { type }, union);
    }
  }

  /// <summary>
  /// A fieldless case of an F# union as its name (<c>"Red"</c>, <c>"Empty"</c>), as System.Text.Json writes them. Read by its name, exactly or ignoring case.
  /// </summary>
  internal sealed class FSharpCaseNameConverter<T> : MsgPackConverter<T, string>
  {
    private readonly FSharpUnion _union;
    private readonly Func<T, int> _tag; // null for the class of one case
    private readonly string _name;

    internal FSharpCaseNameConverter(FSharpUnion union)
    {
      _union = union;
      if (typeof(T) == union.Type)
        _tag = FSharpAccess.Getter<T, int>(union.TagGetter);
      else
        _name = union.CaseOfClass(typeof(T)).Name;
    }

    public override string ToSurrogate(T value)
    {
      return _name ?? _union.Cases[_tag(value)].Name;
    }

    public override T FromSurrogate(string surrogate)
    {
      object value = _union.FieldlessCaseNamed(surrogate);
      if (!(value is T found))
        throw new MsgPackException($"The case \"{surrogate}\" of the F# union {_union.Type.FullName} cannot be read into a {typeof(T).Name}.");
      return found;
    }
  }

  /// <summary>
  /// A struct union with fields in several cases (FSharp.Core's Result, <c>[&lt;Struct&gt;] type Shape = Circle of r: float | Square of s: float</c>): its cases share one type, so they have no
  /// class to name in a type id. Refused when a value is written or read rather than written without its fields; types that only mention it still get their schemas.
  /// </summary>
  internal sealed class FSharpUnsupportedConverter<T> : MsgPackConverter<T, object>
  {
    public override object ToSurrogate(T value)
    {
      throw Unsupported();
    }

    public override T FromSurrogate(object surrogate)
    {
      throw Unsupported();
    }

    private static MsgPackException Unsupported()
    {
      return new MsgPackException($"{TypeResolver.GetTypeName(typeof(T), false)} is an F# struct union with fields in several cases, which LsMsgPack does not write or read (yet): make it a reference union (without [<Struct>]), or register a converter for it in MsgPackOptions.Converters (docs/FSharp.md).");
    }
  }

  internal static class FSharpSingleCaseConverters
  {
    /// <summary>
    /// The converter of a union of one case with one field (<c>type Email = Email of string</c>).
    /// </summary>
    internal static MsgPackConverter Create(Type type, UnionCase single)
    {
      return FSharpAccess.Create(typeof(FSharpSingleCaseConverter<,>), new[] { type, single.Fields[0].PropertyType }, single);
    }
  }

  /// <summary>
  /// A union of one case with one field as that field (<c>Email "a@b"</c> is <c>"a@b"</c>), as FSharp.SystemTextJson writes them by default: value objects stay plain values.
  /// </summary>
  internal sealed class FSharpSingleCaseConverter<TUnion, TField> : MsgPackConverter<TUnion, TField>
  {
    private readonly Func<TUnion, TField> _field;
    private readonly Func<TField, TUnion> _create;

    internal FSharpSingleCaseConverter(UnionCase single)
    {
      _field = FSharpAccess.Getter<TUnion, TField>(single.Fields[0].GetGetMethod(true));
      _create = FSharpAccess.Factory<TField, TUnion>(single.Factory);
    }

    public override TField ToSurrogate(TUnion value)
    {
      return _field(value);
    }

    public override TUnion FromSurrogate(TField surrogate)
    {
      return _create(surrogate);
    }
  }
}
