using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LsMsgPack
{
  /// <summary>
  /// What did not match between the data and the classes it was read into (see <see cref="ReadDifferences"/>).
  /// </summary>
  public enum DifferenceKind
  {
    /// <summary>
    /// A property in the data that the class does not have (or that its static filters leave out). The value was skipped.
    /// </summary>
    UnknownProperty,

    /// <summary>
    /// An object written as an array (<see cref="ObjectLayout.Array"/>) without the indexed schema has more values than the class has properties. The values were skipped.
    /// </summary>
    ExtraValue,

    /// <summary>
    /// An object of a class that has no entry in the indexed schema of the data, and that could not be paired with one of the writer's classes
    /// (none, or more than one: <see cref="Difference.WriterClasses"/>). The object was skipped (left null).
    /// </summary>
    UnmatchedClass,

    /// <summary>
    /// A type id in the data (a type name, or with the indexed schema the writer's class) that did not resolve to a type. With the indexed schema, or when the declared type cannot be created
    /// (abstract, an interface), the object was skipped (left null; an error, see <see cref="MsgPackOptions.ReadErrors"/>). Without the schema it was read as the declared type, as it is without collecting the differences
    /// (not an error, <see cref="Difference.IsError"/> is false).
    /// <para>A type that is found but does not fit (not assignable, refused by the <see cref="MsgPackOptions.TypeGuard"/>) still throws (see docs/security.md).</para>
    /// </summary>
    UnresolvedType,

    /// <summary>
    /// A value that was read but could not be converted into its property (a string where an int is declared, a number too large for it, a name that is not a value of the enum...).
    /// The property keeps what the constructor gave it (<see cref="Difference.Error"/> is the first exception). Values of collections fail the property that holds the collection.
    /// </summary>
    InvalidValue
  }

  /// <summary>
  /// One kind of difference for one class, counted over the whole payload.
  /// </summary>
  [Serializable]
  public sealed class Difference
  {
    internal Difference(Type type, DifferenceKind kind, string name, int position)
    {
      Class = type;
      Kind = kind;
      Name = name;
      Position = position;
    }

    /// <summary>
    /// The class that is read: the class of the object with the unknown property or extra value, the class without a schema entry (<see cref="DifferenceKind.UnmatchedClass"/>),
    /// the declared type of a value whose type id was not found (<see cref="DifferenceKind.UnresolvedType"/>), or the class with the property that could not be read (<see cref="DifferenceKind.InvalidValue"/>).
    /// </summary>
    public Type Class { get; }

    public DifferenceKind Kind { get; }

    /// <summary>
    /// The name of the property in the data (<see cref="DifferenceKind.UnknownProperty"/>), the property the object was assigned to (<see cref="DifferenceKind.UnmatchedClass"/>, null for the root and the elements of collections),
    /// the type name in the data (<see cref="DifferenceKind.UnresolvedType"/>), or the property that could not be read (<see cref="DifferenceKind.InvalidValue"/>). Null for <see cref="DifferenceKind.ExtraValue"/>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The position of the value in the array (<see cref="DifferenceKind.ExtraValue"/>), -1 otherwise.
    /// </summary>
    public int Position { get; }

    /// <summary>
    /// <see cref="DifferenceKind.UnmatchedClass"/>: the writer's classes the class was found for (by the properties both sides have), when it was more than one. Then neither was used:
    /// the values only say which property of the writer's class they are, not which of these classes it was. Null otherwise.
    /// </summary>
    public IReadOnlyList<Type> WriterClasses { get; internal set; }

    internal bool _skipped;

    /// <summary>
    /// A value that could not be read (subject to <see cref="MsgPackOptions.ReadErrors"/>): skipped, or thrown with <see cref="ReadErrorHandling.FailFast"/>.
    /// True for <see cref="DifferenceKind.InvalidValue"/>, <see cref="DifferenceKind.UnmatchedClass"/> and an <see cref="DifferenceKind.UnresolvedType"/> whose object was skipped
    /// (not when it was read as the declared type). Unknown properties and extra values are differences, not errors.
    /// </summary>
    public bool IsError { get { return Kind == DifferenceKind.InvalidValue || Kind == DifferenceKind.UnmatchedClass || (Kind == DifferenceKind.UnresolvedType && _skipped); } }

    /// <summary>
    /// The exception of the first occurrence of an error (<see cref="IsError"/>), null otherwise.
    /// </summary>
    public Exception Error { get; internal set; }

    /// <summary>
    /// The number of times it was found in the data.
    /// </summary>
    public int Count { get; internal set; }

    internal readonly List<object> _samples = new List<object>();

    /// <summary>
    /// The first objects it was found on (at most <see cref="ReadDifferences.MaxSamples"/>): the objects with the unknown property or extra value, or the objects holding the object that was skipped (<see cref="DifferenceKind.UnmatchedClass"/>).
    /// <para>In the order they were read: LsMsgPack converts the values of a collection or object from the last to the first, so with more than <see cref="ReadDifferences.MaxSamples"/> it keeps others than LtMsgPack.
    /// Their paths are found in <see cref="ReadDifferences.Root"/> (<see cref="ReadDifferences.PathOf"/>), the report lists them in the order of the graph.</para>
    /// </summary>
    public IReadOnlyList<object> Samples { get { return _samples; } }

    /// <summary>
    /// Class.Name, Class[position], Class (Name) or Class ("type name").
    /// </summary>
    public string Subject
    {
      get
      {
        string type = Class is null ? "?" : Class.Name;
        switch (Kind)
        {
          case DifferenceKind.ExtraValue:
            return string.Concat(type, "[", Position.ToString(CultureInfo.InvariantCulture), "]");
          case DifferenceKind.UnmatchedClass:
            return Name is null ? type : string.Concat(type, " (", Name, ")");
          case DifferenceKind.UnresolvedType:
            return string.Concat(type, " (\"", Name, "\")");
          default:
            return string.Concat(type, ".", Name);
        }
      }
    }

    public override string ToString()
    {
      return string.Concat(Subject, ": ", Describe(), ", ", Count.ToString(CultureInfo.InvariantCulture), Count == 1 ? " time" : " times");
    }

    internal string Describe()
    {
      switch (Kind)
      {
        case DifferenceKind.ExtraValue:
          return "value after the last property, skipped";
        case DifferenceKind.UnmatchedClass:
          if (WriterClasses != null && WriterClasses.Count > 0)
            return string.Concat("no schema entry (read where the writer had ", Names(WriterClasses), ", so neither was used), the object was skipped");
          return "no schema entry, the object was skipped";
        case DifferenceKind.UnresolvedType:
          return _skipped ? "type not found, the object was skipped" : string.Concat("type not found, read as ", Class is null ? "?" : Class.Name);
        case DifferenceKind.InvalidValue:
          return string.Concat("could not be read (", Error is null ? "?" : Error.Message, "), skipped");
        default:
          return "not a property of the class, skipped";
      }
    }

    private static string Names(IReadOnlyList<Type> types)
    {
      StringBuilder names = new StringBuilder();
      for (int t = 0; t < types.Count; t++)
      {
        if (t > 0)
          names.Append(t == types.Count - 1 ? " and " : ", ");
        names.Append(types[t] is null ? "?" : types[t].Name);
      }
      return names.ToString();
    }
  }
}
