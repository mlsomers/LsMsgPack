using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace LtMsgPack.Generator
{
  // The models of the incremental pipeline: values only (no symbols, syntax or locations), so the pipeline can compare them and skip unchanged steps.

  /// <summary>
  /// An immutable array compared by its items.
  /// </summary>
  internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T> where T : IEquatable<T>
  {
    private readonly ImmutableArray<T> _items;

    public EquatableArray(ImmutableArray<T> items)
    {
      _items = items;
    }

    public static readonly EquatableArray<T> Empty = new EquatableArray<T>(ImmutableArray<T>.Empty);

    public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public int Length => Items.Length;

    public T this[int index] => Items[index];

    public bool Equals(EquatableArray<T> other)
    {
      ImmutableArray<T> mine = Items;
      ImmutableArray<T> theirs = other.Items;
      if (mine.Length != theirs.Length)
        return false;
      for (int t = 0; t < mine.Length; t++)
        if (!EqualityComparer<T>.Default.Equals(mine[t], theirs[t]))
          return false;
      return true;
    }

    public override bool Equals(object obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
      int hash = 17;
      ImmutableArray<T> items = Items;
      for (int t = 0; t < items.Length; t++)
        hash = unchecked(hash * 31 + (items[t]?.GetHashCode() ?? 0));
      return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
  }

  internal static class EquatableArray
  {
    public static EquatableArray<T> ToEquatable<T>(this IEnumerable<T> items) where T : IEquatable<T> => new EquatableArray<T>(ImmutableArray.CreateRange(items));
  }

  /// <summary>
  /// Where a diagnostic is reported (Location itself refers to a syntax tree).
  /// </summary>
  internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
  {
    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    public static LocationInfo From(Location location)
    {
      if (location is null || location.SourceTree is null)
        return null;
      return new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }
  }

  internal sealed record DiagnosticInfo(string Id, LocationInfo Location, EquatableArray<StringValue> Arguments)
  {
    public Diagnostic ToDiagnostic()
    {
      object[] arguments = new object[Arguments.Length];
      for (int t = 0; t < arguments.Length; t++)
        arguments[t] = Arguments[t].Value;
      return Diagnostic.Create(Diagnostics.Get(Id), Location?.ToLocation(), arguments);
    }

    public static DiagnosticInfo Create(string id, Location location, params string[] arguments)
    {
      List<StringValue> values = new List<StringValue>(arguments.Length);
      for (int t = 0; t < arguments.Length; t++)
        values.Add(new StringValue(arguments[t]));
      return new DiagnosticInfo(id, LocationInfo.From(location), values.ToEquatable());
    }
  }

  /// <summary>
  /// A string in an <see cref="EquatableArray{T}"/>.
  /// </summary>
  internal readonly record struct StringValue(string Value);

  /// <summary>
  /// How the runtime is told about a type (the factories of LtMsgPack.Generated.GeneratedValues).
  /// </summary>
  internal enum TypeModelKind
  {
    /// <summary>A class, abstract class or interface: accessors of the properties it declares, its constructor</summary>
    Object,
    Enum,

    /// <summary>T? (<see cref="TypeModel.Argument"/> is T)</summary>
    Nullable,

    /// <summary>List&lt;T&gt; (<see cref="TypeModel.Argument"/> is T)</summary>
    List,

    /// <summary>The static class of the factory methods of an immutable or frozen collection (ImmutableArray, FrozenSet...): only kept for trimming (its public methods), not described</summary>
    Factory,

    /// <summary>T[] (<see cref="TypeModel.Argument"/> is T)</summary>
    Array,

    /// <summary>Another collection or dictionary, its constructor</summary>
    Collection,

    /// <summary>Only the instantiations of the boxed handler and reader</summary>
    Boxed
  }

  internal sealed record PropertyModel(
    string Name, // the name of the property (the string the runtime matches)
    string Access, // the name in member access (escaped when it is a keyword)
    string TypeName, // fully qualified
    bool CanGet,
    bool CanSet);

  /// <summary>
  /// A member of the generated writer and reader of a class: the properties the runtime may serialize, inherited ones too (LtMsgPack.Generated.GeneratedMember).
  /// </summary>
  internal sealed record MemberModel(
    string DeclaringType, // fully qualified
    string Name,
    string Access, // the name in member access (escaped when it is a keyword)
    string TypeName, // fully qualified
    string Kind, // LtMsgPack.Generated.GeneratedKind, None when the runtime writes and reads it
    bool Nullable, // Kind is the one of the underlying type of a Nullable
    string ValueType, // the type of the value the kind writes (the underlying type of a Nullable), fully qualified
    string EnumUnderlying, // the keyword of the underlying type of an enum (int, byte...)
    bool CanGet, // the generated code writes it (its getter is accessible, no [DefaultValue])
    bool CanSet,
    bool Unrolled); // kept by the default static filters (the unrolled writer)

  internal sealed record TypeModel(
    TypeModelKind Kind,
    string TypeName, // fully qualified (global::...)
    string Argument, // the element or underlying type, fully qualified (Collection: the element type when there is a Factory)
    bool HasCreator,
    bool KeepMetadata, // its properties and constructors are kept by the trimmer (DynamicDependency)
    EquatableArray<PropertyModel> Properties,
    EquatableArray<MemberModel> Members, // the generated writer and reader (concrete classes), empty when there is none
    string Factory = null); // Collection: the static method that creates the immutable or frozen collection from its elements (with its type arguments), null for others

  /// <summary>
  /// A context class: one written by the user (with LtMsgPackSerializable attributes), or the one of the assembly (LtMsgPackGenerate, MSBuild properties).
  /// </summary>
  internal sealed record ContextModel(
    string Namespace, // null for the global namespace
    string Name,
    EquatableArray<StringValue> ContainingTypes, // outermost first (partial classes)
    bool DeclaredByUser,
    string FullName, // fully qualified, for the registration
    bool CanRegister, // accessible from a top-level class of the assembly (for the module initializer)
    EquatableArray<TypeModel> Types,
    EquatableArray<DiagnosticInfo> Diagnostics);

  /// <summary>
  /// The compile-time options (assembly attribute and MSBuild properties) and what the compilation offers.
  /// </summary>
  internal sealed record GenerateOptions(
    int Include, // LtMsgPackInclude
    string ContextName,
    bool AutoRegister,
    string RootNamespace,
    bool ModuleInitializers, // C# 9 or later
    bool HasModuleInitializerAttribute,
    bool HasDynamicDependency,
    bool Trimmed); // PublishTrimmed, PublishAot, IsAotCompatible or IsTrimmable: LTMP010 is reported
}
