using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace LsMsgPack.Generated
{
  /// <summary>
  /// The code the source generator wrote for a set of types (the base of LtMsgPack's <c>LtMsgPackContext</c>): accessors, creators and the generic instantiations of the serializers.
  /// <para>The types are listed by the generated override of <see cref="AddTypes"/>, which is only called when the version of the generated code is supported (see <see cref="GeneratedContract"/>).
  /// Each is described when it is first looked up (<see cref="Describe"/>): with accessors for every property of every type a context costs too much to load at once (86 types, 1060 properties: 190 ms before the first value).</para>
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public abstract class GeneratedContext
  {
    private readonly int _contractVersion;
    private readonly object _lock = new object();
    private volatile GeneratedTypeSet _types;

    /// <param name="contractVersion">The version of the contract the code was generated for (<see cref="GeneratedContract.Current"/> of the generator's release)</param>
    protected GeneratedContext(int contractVersion)
    {
      _contractVersion = contractVersion;
    }

    /// <summary>
    /// The version of the contract the code was generated for.
    /// </summary>
    public int ContractVersion
    {
      get { return _contractVersion; }
    }

    /// <summary>
    /// Whether this runtime uses the generated code (its version is supported and it could be loaded).
    /// </summary>
    public bool IsUsable
    {
      get { return Types.Count > 0; }
    }

    /// <summary>
    /// Generated: adds the descriptions of the types (created by the factories of the serializer, e.g. LtMsgPack.Generated.GeneratedValues).
    /// </summary>
    protected abstract void AddTypes(GeneratedTypeSet types);

    /// <summary>
    /// Generated: the description of the type <see cref="AddTypes"/> added with this number (version 3; the code of earlier versions adds the descriptions).
    /// </summary>
    protected virtual GeneratedType Describe(int number)
    {
      return null;
    }

    /// <summary>
    /// The types, empty when the generated code is not used (see <see cref="IsUsable"/>).
    /// </summary>
    internal GeneratedTypeSet Types
    {
      get { return _types ?? Load(); }
    }

    /// <returns>Whether the context has the type (described now when it was not yet). A type that can't be described (generated against members this runtime does not have) is done as without generated code.</returns>
    internal bool TryGetType(Type type, out GeneratedType generated)
    {
      GeneratedTypeSet types = Types;
      generated = null;
      if (!types.TryGetPosition(type, out int position))
        return false;
      generated = Volatile.Read(ref types.Described[position]);
      if (generated is null)
        generated = DescribeAt(types, position);
      return generated != null;
    }

    private GeneratedType DescribeAt(GeneratedTypeSet types, int position)
    {
      lock (_lock)
      {
        GeneratedType generated = types.Described[position];
        if (generated != null || types.Numbers[position] < 0)
          return generated;
        try
        {
          generated = Describe(types.Numbers[position]);
        }
        catch (Exception ex) when (ex is TypeLoadException || ex is MissingMemberException) // generated against members this runtime does not have
        {
          Trace.TraceWarning($"LtMsgPack: the code generated in {GetType().FullName} does not fit this runtime ({ex.Message}). It is not used (rebuild with this version of LtMsgPack to use it).");
        }
        types.Numbers[position] = -1; // once
        Volatile.Write(ref types.Described[position], generated);
        return generated;
      }
    }

    private GeneratedTypeSet Load()
    {
      lock (_lock)
      {
        if (_types != null)
          return _types;

        GeneratedTypeSet types = new GeneratedTypeSet();
        if (!GeneratedContract.IsSupported(_contractVersion))
          Trace.TraceWarning($"LtMsgPack: the code generated in {GetType().FullName} is for version {_contractVersion} of the generated code, this runtime supports {GeneratedContract.MinSupported} to {GeneratedContract.Current}. It is not used (rebuild with this version of LtMsgPack to use it).");
        else
        {
          try
          {
            AddTypes(types);
          }
          catch (Exception ex) when (ex is TypeLoadException || ex is MissingMemberException) // generated against members this runtime does not have
          {
            types = new GeneratedTypeSet();
            Trace.TraceWarning($"LtMsgPack: the code generated in {GetType().FullName} does not fit this runtime ({ex.Message}). It is not used (rebuild with this version of LtMsgPack to use it).");
          }
        }
        types.Freeze();
        _types = types;
        return types;
      }
    }
  }

  /// <summary>
  /// Collects the descriptions of generated types (see <see cref="GeneratedContext.AddTypes"/>).
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public sealed class GeneratedTypeSet
  {
    private readonly Dictionary<Type, int> _positions = new Dictionary<Type, int>();
    private readonly List<GeneratedType> _adding = new List<GeneratedType>();
    private readonly List<int> _addingNumbers = new List<int>();

    /// <summary>
    /// The description per position in the set, null until the type is described.
    /// </summary>
    internal GeneratedType[] Described;

    /// <summary>
    /// The number for <see cref="GeneratedContext.Describe"/> per position in the set, negative when there is nothing (more) to describe.
    /// </summary>
    internal int[] Numbers;

    internal GeneratedTypeSet()
    {
    }

    internal int Count { get { return _positions.Count; } }

    internal IEnumerable<Type> Types { get { return _positions.Keys; } }

    internal bool TryGetPosition(Type type, out int position)
    {
      return _positions.TryGetValue(type, out position);
    }

    /// <summary>
    /// Adds the description of a type (the first one of a type is kept).
    /// </summary>
    public void Add(GeneratedType type)
    {
      if (type != null && !_positions.ContainsKey(type.Type))
      {
        _positions.Add(type.Type, _adding.Count);
        _adding.Add(type);
        _addingNumbers.Add(-1);
      }
    }

    /// <summary>
    /// Adds a type <see cref="GeneratedContext.Describe"/> describes when it is first looked up (the first one of a type is kept).
    /// </summary>
    public void Add(Type type, int number)
    {
      if (type != null && !_positions.ContainsKey(type))
      {
        _positions.Add(type, _adding.Count);
        _adding.Add(null);
        _addingNumbers.Add(number);
      }
    }

    internal void Freeze()
    {
      Described = _adding.ToArray();
      Numbers = _addingNumbers.ToArray();
    }
  }
}
