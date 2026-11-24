using LsMsgPack.Meta;
using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LsMsgPack.Converters
{
  /// <summary>
  /// Writes the values of one type as the values of another type, its surrogate, and reads them back (both serializers, see docs/Converters.md). Derive from <see cref="MsgPackConverter{T, TSurrogate}"/>,
  /// then register the converter in <see cref="MsgPackOptions.Converters"/> or put <see cref="MsgPackConverterAttribute"/> on the type.
  /// <para>A converter is used wherever a value of exactly <see cref="ConvertedType"/> is written or read: properties, fields, elements, dictionary keys and values, the root.
  /// Values of classes deriving from it are written as objects of their own type, with a type id.</para>
  /// </summary>
  public abstract class MsgPackConverter
  {
    private FullPropertyInfo _surrogateInfo;

    internal MsgPackConverter() // only the generic converters derive from it (the serializers create their handlers through Accept)
    {
    }

    /// <summary>
    /// The type whose values are converted.
    /// </summary>
    public abstract Type ConvertedType { get; }

    /// <summary>
    /// The type the values are written as.
    /// </summary>
    public abstract Type SurrogateType { get; }

    /// <summary>
    /// The surrogate as a declared value: it is written as any value declared as <see cref="SurrogateType"/> (no type id, unless it is of a type deriving from it).
    /// </summary>
    internal FullPropertyInfo SurrogateInfo
    {
      get { return _surrogateInfo ?? (_surrogateInfo = new FullPropertyInfo(SurrogateType)); } // the same for every thread that creates it
    }

    /// <param name="value">Not null, of exactly <see cref="ConvertedType"/></param>
    internal abstract object ToSurrogateBoxed(object value);

    /// <param name="surrogate">Not null</param>
    internal abstract object FromSurrogateBoxed(object surrogate);

    /// <summary>
    /// Calls the visitor with the type arguments of this converter: the serializers create their typed handlers and readers this way, so compilers ahead of time see the instantiations (no MakeGenericType).
    /// </summary>
    internal abstract TResult Accept<TResult>(IMsgPackConverterVisitor<TResult> visitor);
  }

  /// <summary>
  /// Writes values of <typeparamref name="T"/> as <typeparamref name="TSurrogate"/> and reads them back: e.g. a value object as its inner value, a type of another library as a class of your own, a <c>Money</c> as a string.
  /// <para>The surrogate is written exactly as a value declared as <typeparamref name="TSurrogate"/> would be (a string stays a string, a class gets its own schema entry), so tools and other languages read it as such.
  /// Neither method is called for null: a null value is nil, and nil is read as null (the default of a struct).</para>
  /// </summary>
  /// <typeparam name="T">The converted type: not <c>object</c>, not a <see cref="Nullable{T}"/> (a converter for a struct also converts its nullable)</typeparam>
  /// <typeparam name="TSurrogate">The type it is written as</typeparam>
  public abstract class MsgPackConverter<T, TSurrogate> : MsgPackConverter
  {
    public sealed override Type ConvertedType
    {
      get { return typeof(T); }
    }

    public sealed override Type SurrogateType
    {
      get { return typeof(TSurrogate); }
    }

    /// <summary>
    /// The value to write instead of <paramref name="value"/> (a null result is written as nil).
    /// </summary>
    /// <param name="value">Not null</param>
    public abstract TSurrogate ToSurrogate(T value);

    /// <summary>
    /// The value a surrogate that was read stands for.
    /// </summary>
    /// <param name="surrogate">Not null</param>
    public abstract T FromSurrogate(TSurrogate surrogate);

    internal sealed override object ToSurrogateBoxed(object value)
    {
      return ToSurrogate((T)value);
    }

    internal sealed override object FromSurrogateBoxed(object surrogate)
    {
      return FromSurrogate((TSurrogate)surrogate);
    }

    internal sealed override TResult Accept<TResult>(IMsgPackConverterVisitor<TResult> visitor)
    {
      return visitor.Surrogate(this);
    }
  }

  /// <summary>
  /// What a serializer creates per kind of converter (see <see cref="MsgPackConverter.Accept"/>).
  /// </summary>
  internal interface IMsgPackConverterVisitor<TResult>
  {
    TResult Surrogate<T, TSurrogate>(MsgPackConverter<T, TSurrogate> converter);

    TResult Binary<T>(MsgPackBinaryConverter<T> converter);

    /// <summary>
    /// A converter without type arguments (<see cref="BoxedConverter"/>): its values are converted boxed.
    /// </summary>
    TResult Boxed(BoxedConverter converter);
  }

  /// <summary>
  /// A converter of boxed values, for the converters the library makes for types only known at runtime where it has no code for their type arguments (NativeAOT: the F# converters of
  /// value types). The serializers write and read its values boxed (LsMsgPack: its boxed writer and Core's conversion).
  /// </summary>
  internal abstract class BoxedConverter : MsgPackConverter
  {
    private readonly Type _converted;
    private readonly Type _surrogate;

    protected BoxedConverter(Type converted, Type surrogate)
    {
      _converted = converted;
      _surrogate = surrogate;
    }

    public sealed override Type ConvertedType
    {
      get { return _converted; }
    }

    public sealed override Type SurrogateType
    {
      get { return _surrogate; }
    }

    internal sealed override TResult Accept<TResult>(IMsgPackConverterVisitor<TResult> visitor)
    {
      return visitor.Boxed(this);
    }
  }

  /// <summary>
  /// The converter of a class, struct or enum of your own, or of one property or field (see <see cref="MsgPackConverter{T, TSurrogate}"/>): created once by its public parameterless constructor.
  /// <para>On a type: a converter in <see cref="MsgPackOptions.Converters"/> for the same type is used instead. Not inherited: classes deriving from the type are written as objects.</para>
  /// <para>On a property or field: converts the member's own value (its T is the member's type, or the underlying type of a nullable member), before the type's and the options' converters.
  /// Not the elements of a collection, not a value of a derived class, not the same property of an overriding class (not inherited).</para>
  /// </summary>
  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
  public sealed class MsgPackConverterAttribute : Attribute
  {
    /// <param name="converterType">A class deriving from <see cref="MsgPackConverter{T, TSurrogate}"/> whose T is the type the attribute is on</param>
    public MsgPackConverterAttribute([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type converterType)
    {
      ConverterType = converterType;
    }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type ConverterType { get; }
  }

  /// <summary>
  /// Finds the converters of types (<see cref="MsgPackOptions.ConverterFor"/>): those of the options first, then <see cref="MsgPackConverterAttribute"/> (cached per type, global).
  /// </summary>
  internal static class ConverterLookup
  {
    /// <summary>
    /// The attribute's converter of each type that was looked up (null for none).
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Entry> ByAttribute = new ConcurrentDictionary<Type, Entry>();

    /// <summary>
    /// The entries of the types looked up last, compared by reference before the dictionary: asked per value by LsMsgPackDebug and Core's conversions, where a dictionary lookup per value
    /// made LsMsgPackDebug write the invoices 5-9% and read them 9-15% slower. Entries of the dictionary, so a miss allocates nothing; shared by the threads (an entry is replaced as a whole).
    /// </summary>
    private static readonly Entry[] Recent = new Entry[16];
    private static int _nextRecent;

    private sealed class Entry
    {
      internal readonly Type Type;
      internal readonly MsgPackConverter Converter;

      internal Entry(Type type, MsgPackConverter converter)
      {
        Type = type;
        Converter = converter;
      }
    }

    private const int MaxChain = 32;

    internal static readonly MsgPackConverter[] None = new MsgPackConverter[0];

    /// <summary>
    /// The converter of the type in the list, then by its attribute; null for none. A <see cref="Nullable{T}"/> gets the converter of its underlying type.
    /// </summary>
    /// <param name="type">A runtime or declared type</param>
    internal static MsgPackConverter Find(MsgPackConverter[] converters, Type type)
    {
      if (converters.Length != 0)
      {
        MsgPackConverter listed = FindListed(converters, type);
        if (listed != null)
          return listed;
      }
      if (type == typeof(string) || type == typeof(int) || type == typeof(long) || type == typeof(bool) || type == typeof(double) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(Guid) || type == typeof(object))
        return null; // the most common values first, compared as constants
      Entry[] recent = Recent;
      for (int t = 0; t < recent.Length; t++)
      {
        Entry entry = recent[t];
        if (entry is null)
          break; // filled in order
        if (entry.Type == type)
          return entry.Converter;
      }
      return CannotHaveAttribute(type) ? null : Remember(type);
    }

    /// <summary>
    /// The converter of the options for the type (or for the underlying type of a nullable: Nullable.GetUnderlyingType allocates, only with converters in the options).
    /// </summary>
    private static MsgPackConverter FindListed(MsgPackConverter[] converters, Type type)
    {
      for (int t = 0; t < converters.Length; t++)
      {
        if (converters[t].ConvertedType == type)
          return converters[t];
      }
      Type underlying = type.IsValueType && type.IsGenericType ? Nullable.GetUnderlyingType(type) : null;
      if (underlying is null)
        return null;
      for (int t = 0; t < converters.Length; t++)
      {
        if (converters[t].ConvertedType == underlying)
          return converters[t];
      }
      return null;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static MsgPackConverter Remember(Type type)
    {
      Entry entry = EntryOf(type);
      Recent[(_nextRecent++) & (Recent.Length - 1)] = entry;
      return entry.Converter;
    }

    private static Entry EntryOf(Type type)
    {
      if (ByAttribute.TryGetValue(type, out Entry entry))
        return entry;
      return ByAttribute.GetOrAdd(type, Create);
    }

    /// <summary>
    /// The entry of a type: its attribute's converter, otherwise the built-in one of an F# type (options, unions written as a name or their one field, see FSharp/FSharpTypes.cs);
    /// of a nullable the one of its underlying type.
    /// </summary>
    private static Entry Create(Type type)
    {
      Type underlying = type.IsValueType ? Nullable.GetUnderlyingType(type) : null;
      if (underlying != null)
        return new Entry(type, CannotHaveAttribute(underlying) ? null : EntryOf(underlying).Converter);
      return new Entry(type, FromAttribute(type) ?? FSharp.FSharpTypes.ConverterFor(type));
    }

    /// <summary>
    /// The types an application cannot put the attribute on (framework types are converted through the options): no lookup for them per value.
    /// </summary>
    private static bool CannotHaveAttribute(Type type)
    {
      return type.IsPrimitive || type == typeof(string) || type == typeof(object) || type.IsArray || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(Guid);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "The converter type of the attribute keeps its public parameterless constructor (DynamicallyAccessedMembers).")]
    private static MsgPackConverter FromAttribute(Type type)
    {
      MsgPackConverterAttribute attribute = type.GetCustomAttribute<MsgPackConverterAttribute>(false);
      if (attribute is null)
        return null;
      Type converterType = attribute.ConverterType;
      if (converterType is null || !typeof(MsgPackConverter).IsAssignableFrom(converterType) || converterType.IsAbstract)
        throw new MsgPackException($"The [MsgPackConverter] of {type.FullName} names {converterType?.FullName ?? "null"}, which is not a class deriving from MsgPackConverter<{type.Name}, TSurrogate>.");
      MsgPackConverter converter;
      try
      {
        converter = (MsgPackConverter)Activator.CreateInstance(converterType);
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null)
      {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw;
      }
      if (converter.ConvertedType != type)
        throw new MsgPackException($"The [MsgPackConverter] of {type.FullName} names {converterType.FullName}, which converts {converter.ConvertedType.FullName}.");
      ThrowIfInvalid(converter, None);
      return converter;
    }

    /// <summary>
    /// The converter of a member's [MsgPackConverter] (<see cref="Meta.PropertyAttributeSet.Converter"/>), null without one.
    /// </summary>
    /// <param name="type">The member's type</param>
    /// <param name="attributes">The member's attributes by class name</param>
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "The converter type of the attribute keeps its public parameterless constructor (DynamicallyAccessedMembers).")]
    internal static MsgPackConverter OfMember(MemberInfo member, Type type, System.Collections.Generic.Dictionary<string, object> attributes)
    {
      if (!attributes.TryGetValue(nameof(MsgPackConverterAttribute), out object found) || !(found is MsgPackConverterAttribute attribute))
        return null;
      string name = member.DeclaringType?.Name + "." + member.Name;
      Type converterType = attribute.ConverterType;
      if (converterType is null || !typeof(MsgPackConverter).IsAssignableFrom(converterType) || converterType.IsAbstract)
        throw new MsgPackException($"The [MsgPackConverter] of {name} names {converterType?.FullName ?? "null"}, which is not a class deriving from MsgPackConverter<{type.Name}, TSurrogate>.");
      MsgPackConverter converter;
      try
      {
        converter = (MsgPackConverter)Activator.CreateInstance(converterType);
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null)
      {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw;
      }
      if (converter.ConvertedType != type && converter.ConvertedType != Nullable.GetUnderlyingType(type))
        throw new MsgPackException($"The [MsgPackConverter] of {name} names {converterType.FullName}, which converts {converter.ConvertedType.FullName}, not {type.FullName} (a member's converter is for its own value, not its elements).");
      ThrowIfInvalid(converter, None);
      return converter;
    }

    /// <summary>
    /// The checks of a converter: the types it may convert, and no chain of converters that comes back to a type it converted (writing it would never end).
    /// </summary>
    /// <param name="converters">The converters of the options it belongs to (empty for one found by its attribute)</param>
    internal static void ThrowIfInvalid(MsgPackConverter converter, MsgPackConverter[] converters)
    {
      Type type = converter.ConvertedType;
      if (type == typeof(object) || Nullable.GetUnderlyingType(type) != null || type.ContainsGenericParameters || type.IsPointer || type.IsByRef || type.IsInterface || type.IsAbstract)
        throw new ArgumentException($"{converter.GetType().FullName} converts {type.FullName}: a converter is for a concrete type, not object, an interface, an abstract class or a Nullable<T> (the converter of a struct also converts its nullable).");
      Type surrogate = converter.SurrogateType;
      for (int t = 0; t < MaxChain; t++)
      {
        surrogate = Nullable.GetUnderlyingType(surrogate) ?? surrogate;
        if (surrogate == type)
          throw new ArgumentException($"{converter.GetType().FullName} converts {type.FullName} to {converter.SurrogateType.FullName}, which converters turn back into {type.Name}: writing it would never end.");
        MsgPackConverter next = FindIn(converters, surrogate) ?? (CannotHaveAttribute(surrogate) ? null : AttributeOf(surrogate));
        if (next is null)
          return;
        surrogate = next.SurrogateType;
      }
      throw new ArgumentException($"{converter.GetType().FullName}: more than {MaxChain} converters follow each other.");
    }

    private static MsgPackConverter FindIn(MsgPackConverter[] converters, Type type)
    {
      for (int t = 0; t < converters.Length; t++)
      {
        if (converters[t].ConvertedType == type)
          return converters[t];
      }
      return null;
    }

    /// <summary>
    /// The attribute's converter without validating it (the chain check of another converter; it is validated when it is used itself).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "The converter type of the attribute keeps its public parameterless constructor (DynamicallyAccessedMembers).")]
    private static MsgPackConverter AttributeOf(Type type)
    {
      if (ByAttribute.TryGetValue(type, out Entry found))
        return found.Converter;
      MsgPackConverterAttribute attribute = type.GetCustomAttribute<MsgPackConverterAttribute>(false);
      if (attribute?.ConverterType is null || !typeof(MsgPackConverter).IsAssignableFrom(attribute.ConverterType) || attribute.ConverterType.IsAbstract)
        return null;
      MsgPackConverter converter = Activator.CreateInstance(attribute.ConverterType) as MsgPackConverter;
      return converter?.ConvertedType == type ? converter : null;
    }

    /// <summary>
    /// The converters for <see cref="MsgPackOptions.Converters"/>: a copy, checked (one per type).
    /// </summary>
    internal static MsgPackConverter[] Checked(MsgPackConverter[] converters)
    {
      if (converters is null || converters.Length == 0)
        return None;
      MsgPackConverter[] copy = new MsgPackConverter[converters.Length];
      int count = 0;
      for (int t = 0; t < converters.Length; t++)
      {
        MsgPackConverter converter = converters[t];
        if (converter is null)
          continue;
        if (FindIn(copy, converter.ConvertedType, count) != null)
          throw new ArgumentException($"Two converters for {converter.ConvertedType.FullName}.", nameof(MsgPackOptions.Converters));
        copy[count++] = converter;
      }
      if (count != copy.Length)
        Array.Resize(ref copy, count);
      for (int t = 0; t < copy.Length; t++)
        ThrowIfInvalid(copy[t], copy);
      return copy;
    }

    private static MsgPackConverter FindIn(MsgPackConverter[] converters, Type type, int count)
    {
      for (int t = 0; t < count; t++)
      {
        if (converters[t].ConvertedType == type)
          return converters[t];
      }
      return null;
    }
  }
}
