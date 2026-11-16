using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
#if !NET8_0_OR_GREATER
using System.Runtime.ExceptionServices;
#endif

namespace LsMsgPack.Meta
{
  /// <summary>
  /// The callbacks of a class or struct (docs/Callbacks.md): <see cref="IMsgPackOnSerializing"/>, <see cref="IMsgPackOnSerialized"/>, <see cref="IMsgPackOnDeserializing"/>, <see cref="IMsgPackOnDeserialized"/>,
  /// and System.Text.Json's <c>IJsonOnSerializing</c>... (found by name, the library is not referenced), each of the four only once: an own interface wins over System.Text.Json's.
  /// Found once per type (<see cref="Of"/>, null for a type without any), called for objects only (not collections, as System.Text.Json): Core's conversions, LsMsgPackDebug's
  /// SerializeComplex, LsMsgPack's handlers and read plans, LsMsgPack.Data's row mapper. An exception of a callback is passed on as it is, never marked as skippable (ReadDifferences).
  /// </summary>
  internal sealed class SerializationCallbacks
  {
    [Flags]
    internal enum Kinds
    {
      None = 0,
      Serializing = 1,
      Serialized = 2,
      Deserializing = 4,
      Deserialized = 8,
    }

    private const string JsonNamespace = "System.Text.Json.Serialization.";

    private static readonly ConcurrentDictionary<Type, SerializationCallbacks> Cache = new ConcurrentDictionary<Type, SerializationCallbacks>();

    /// <summary>
    /// The entries of the types looked up last, compared by reference before the dictionary (asked per object by LsMsgPackDebug's writer): entries of the dictionary, so a miss allocates nothing;
    /// shared by the threads (an entry is replaced as a whole).
    /// </summary>
    private static readonly SerializationCallbacks[] Recent = new SerializationCallbacks[16];
    private static int _nextRecent;

    private readonly Type _type;

    /// <summary>
    /// The callbacks the type has, by an own interface or System.Text.Json's.
    /// </summary>
    internal readonly Kinds Has;

    private readonly Kinds _own;

#if !NET8_0_OR_GREATER
    /// <summary>
    /// The methods of System.Text.Json's interfaces by the bit of their kind (index 0, 1, 2, 3), called by reflection (the .NET Standard builds cannot name them).
    /// </summary>
    private readonly MethodInfo[] _json;
#endif

    private SerializationCallbacks(Type type)
    {
      _type = type;
      if (type is null)
        return;
      _own = (typeof(IMsgPackOnSerializing).IsAssignableFrom(type) ? Kinds.Serializing : 0)
        | (typeof(IMsgPackOnSerialized).IsAssignableFrom(type) ? Kinds.Serialized : 0)
        | (typeof(IMsgPackOnDeserializing).IsAssignableFrom(type) ? Kinds.Deserializing : 0)
        | (typeof(IMsgPackOnDeserialized).IsAssignableFrom(type) ? Kinds.Deserialized : 0);
      Kinds json = JsonKinds(type, out MethodInfo[] methods);
#if !NET8_0_OR_GREATER
      _json = methods;
#endif
      Has = _own | json;
    }

    internal bool Serializes { get { return (Has & (Kinds.Serializing | Kinds.Serialized)) != 0; } }

    internal bool Deserializes { get { return (Has & (Kinds.Deserializing | Kinds.Deserialized)) != 0; } }

    /// <returns>The callbacks of the type, null when it has none</returns>
    internal static SerializationCallbacks Of(Type type)
    {
      SerializationCallbacks[] recent = Recent;
      for (int t = 0; t < recent.Length; t++)
      {
        SerializationCallbacks entry = recent[t];
        if (entry is null)
          break; // filled in order
        if (entry._type == type)
          return entry.Has == Kinds.None ? null : entry;
      }
      return Remember(type);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SerializationCallbacks Remember(Type type)
    {
      if (type is null)
        return null;
      if (!Cache.TryGetValue(type, out SerializationCallbacks found))
        found = Cache.GetOrAdd(type, t => new SerializationCallbacks(t));
      Recent[(_nextRecent++) & (Recent.Length - 1)] = found;
      return found.Has == Kinds.None ? null : found;
    }

    /// <summary>
    /// System.Text.Json's callback interfaces the type implements, by full name (no reference to the library: it is not loaded for this).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    private static Kinds JsonKinds(Type type, out MethodInfo[] methods)
    {
      methods = null;
      Kinds kinds = Kinds.None;
      Type[] interfaces = type.GetInterfaces();
      for (int t = 0; t < interfaces.Length; t++)
      {
        string name = interfaces[t].FullName;
        if (name is null || !name.StartsWith(JsonNamespace, StringComparison.Ordinal))
          continue;
        string shortName = name.Substring(JsonNamespace.Length);
        Kinds kind;
        switch (shortName)
        {
          case "IJsonOnSerializing": kind = Kinds.Serializing; break;
          case "IJsonOnSerialized": kind = Kinds.Serialized; break;
          case "IJsonOnDeserializing": kind = Kinds.Deserializing; break;
          case "IJsonOnDeserialized": kind = Kinds.Deserialized; break;
          default: continue;
        }
        kinds |= kind;
#if !NET8_0_OR_GREATER
        (methods ?? (methods = new MethodInfo[4]))[IndexOf(kind)] = JsonMethod(interfaces[t], shortName.Substring("IJson".Length)); // IJsonOnSerializing.OnSerializing
#endif
      }
      return kinds;
    }

#if !NET8_0_OR_GREATER
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
    private static MethodInfo JsonMethod(Type @interface, string name)
    {
      return @interface.GetMethod(name, Type.EmptyTypes);
    }

    private static int IndexOf(Kinds kind)
    {
      switch (kind)
      {
        case Kinds.Serializing: return 0;
        case Kinds.Serialized: return 1;
        case Kinds.Deserializing: return 2;
        default: return 3;
      }
    }
#endif

    internal void OnSerializing(object instance)
    {
      if ((_own & Kinds.Serializing) != 0)
        ((IMsgPackOnSerializing)instance).OnSerializing();
      else if ((Has & Kinds.Serializing) != 0)
        CallJson(instance, Kinds.Serializing);
    }

    internal void OnSerialized(object instance)
    {
      if ((_own & Kinds.Serialized) != 0)
        ((IMsgPackOnSerialized)instance).OnSerialized();
      else if ((Has & Kinds.Serialized) != 0)
        CallJson(instance, Kinds.Serialized);
    }

    /// <param name="instance">The object, a struct its box (the callback changes the box)</param>
    internal void OnDeserializing(object instance)
    {
      if ((_own & Kinds.Deserializing) != 0)
        ((IMsgPackOnDeserializing)instance).OnDeserializing();
      else if ((Has & Kinds.Deserializing) != 0)
        CallJson(instance, Kinds.Deserializing);
    }

    /// <param name="instance">The object, a struct its box (the callback changes the box)</param>
    internal void OnDeserialized(object instance)
    {
      if ((_own & Kinds.Deserialized) != 0)
        ((IMsgPackOnDeserialized)instance).OnDeserialized();
      else if ((Has & Kinds.Deserialized) != 0)
        CallJson(instance, Kinds.Deserialized);
    }

    /// <summary>
    /// A method of its own: on net8.0 its casts load System.Text.Json only when a type implements one of its interfaces (the JIT loads the types a method names when it compiles it).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CallJson(object instance, Kinds kind)
    {
#if NET8_0_OR_GREATER
      switch (kind)
      {
        case Kinds.Serializing: ((System.Text.Json.Serialization.IJsonOnSerializing)instance).OnSerializing(); break;
        case Kinds.Serialized: ((System.Text.Json.Serialization.IJsonOnSerialized)instance).OnSerialized(); break;
        case Kinds.Deserializing: ((System.Text.Json.Serialization.IJsonOnDeserializing)instance).OnDeserializing(); break;
        default: ((System.Text.Json.Serialization.IJsonOnDeserialized)instance).OnDeserialized(); break;
      }
#else
      try
      {
        _json[IndexOf(kind)].Invoke(instance, null);
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null)
      {
        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw; // not reached
      }
#endif
    }
  }
}
