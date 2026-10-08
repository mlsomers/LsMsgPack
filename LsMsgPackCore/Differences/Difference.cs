using System;
using System.Collections.Generic;
using System.Globalization;

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
    /// An object of a class that has no entry in the indexed schema of the data, and that could not be paired with one of the writer's classes. The object was skipped (left null).
    /// </summary>
    UnmatchedClass
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
    /// The class that is read: the class of the object with the unknown property or extra value, or the class without a schema entry (<see cref="DifferenceKind.UnmatchedClass"/>).
    /// </summary>
    public Type Class { get; }

    public DifferenceKind Kind { get; }

    /// <summary>
    /// The name of the property in the data (<see cref="DifferenceKind.UnknownProperty"/>), or the property the object was assigned to (<see cref="DifferenceKind.UnmatchedClass"/>, null for the root and the elements of collections).
    /// Null for <see cref="DifferenceKind.ExtraValue"/>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The position of the value in the array (<see cref="DifferenceKind.ExtraValue"/>), -1 otherwise.
    /// </summary>
    public int Position { get; }

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
    /// Class.Name, Class[position] or Class (in Class.Name).
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
          return "no schema entry, the object was skipped";
        default:
          return "not a property of the class, skipped";
      }
    }
  }
}
