using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace LsMsgPack.Generated
{
  /// <summary>
  /// The code the source generator wrote for a set of types (the base of LtMsgPack's <c>LtMsgPackContext</c>): accessors, creators and the generic instantiations of the serializers.
  /// <para>The types are listed by the generated override of <see cref="AddTypes"/>, which is only called when the version of the generated code is supported (see <see cref="GeneratedContract"/>).</para>
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public abstract class GeneratedContext
  {
    private readonly int _contractVersion;
    private readonly object _lock = new object();
    private volatile Dictionary<Type, GeneratedType> _types;

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
    /// The types by their <see cref="Type"/>, empty when the generated code is not used (see <see cref="IsUsable"/>).
    /// </summary>
    internal Dictionary<Type, GeneratedType> Types
    {
      get { return _types ?? Load(); }
    }

    internal bool TryGetType(Type type, out GeneratedType generated)
    {
      return Types.TryGetValue(type, out generated);
    }

    private Dictionary<Type, GeneratedType> Load()
    {
      lock (_lock)
      {
        if (_types != null)
          return _types;

        Dictionary<Type, GeneratedType> types = new Dictionary<Type, GeneratedType>();
        if (!GeneratedContract.IsSupported(_contractVersion))
          Trace.TraceWarning($"LtMsgPack: the code generated in {GetType().FullName} is for version {_contractVersion} of the generated code, this runtime supports {GeneratedContract.MinSupported} to {GeneratedContract.Current}. It is not used (rebuild with this version of LtMsgPack to use it).");
        else
        {
          try
          {
            GeneratedTypeSet set = new GeneratedTypeSet(types);
            AddTypes(set);
          }
          catch (Exception ex) when (ex is TypeLoadException || ex is MissingMemberException) // generated against members this runtime does not have
          {
            types.Clear();
            Trace.TraceWarning($"LtMsgPack: the code generated in {GetType().FullName} does not fit this runtime ({ex.Message}). It is not used (rebuild with this version of LtMsgPack to use it).");
          }
        }
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
    private readonly Dictionary<Type, GeneratedType> _types;

    internal GeneratedTypeSet(Dictionary<Type, GeneratedType> types)
    {
      _types = types;
    }

    /// <summary>
    /// Adds the description of a type (the first one of a type is kept).
    /// </summary>
    public void Add(GeneratedType type)
    {
      if (type != null && !_types.ContainsKey(type.Type))
        _types.Add(type.Type, type);
    }
  }
}
