using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;

namespace LsMsgPack.Generated
{
  /// <summary>
  /// The generated code registered for the whole process: the generated module initializer of an assembly registers its contexts (C# 9 and later, unless switched off with <c>AutoRegister</c>).
  /// <para>Looked up when a plan, handler, reader or accessor is created (once per type or property), never per value.</para>
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public static class GeneratedTypes
  {
    private static readonly object Lock = new object();
    private static GeneratedContext[] _contexts = new GeneratedContext[0];

    /// <summary>
    /// The type found per type (null: none), cleared when a context is registered (an assembly loaded later may describe types that were looked up before).
    /// </summary>
    private static ConcurrentDictionary<Type, GeneratedType> _found = new ConcurrentDictionary<Type, GeneratedType>();

    /// <summary>
    /// Registers generated code for the whole process (LsMsgPack's property accessors and creators, and LtMsgPack unless <c>LtMsgPackOptions.UseRegisteredContexts</c> is off).
    /// The first registered context that describes a type is used for it.
    /// </summary>
    public static void Register(GeneratedContext context)
    {
      if (context is null)
        throw new ArgumentNullException(nameof(context));

      lock (Lock)
      {
        GeneratedContext[] contexts = _contexts;
        for (int t = 0; t < contexts.Length; t++)
          if (ReferenceEquals(contexts[t], context))
            return;

        GeneratedContext[] grown = new GeneratedContext[contexts.Length + 1];
        Array.Copy(contexts, grown, contexts.Length);
        grown[contexts.Length] = context;
        _contexts = grown;
        _found = new ConcurrentDictionary<Type, GeneratedType>();
      }
    }

    /// <summary>
    /// The registered contexts (a copy).
    /// </summary>
    internal static GeneratedContext[] Contexts
    {
      get { return (GeneratedContext[])_contexts.Clone(); }
    }

    /// <returns>null when no registered context describes the type</returns>
    internal static GeneratedType Find(Type type)
    {
      if (type is null || _contexts.Length == 0)
        return null;

      ConcurrentDictionary<Type, GeneratedType> found = _found;
      if (found.TryGetValue(type, out GeneratedType generated))
        return generated;

      generated = Find(_contexts, type);
      found.TryAdd(type, generated);
      return generated;
    }

    /// <returns>The first description of the type in the contexts, null when there is none</returns>
    internal static GeneratedType Find(GeneratedContext[] contexts, Type type)
    {
      for (int t = 0; t < contexts.Length; t++)
        if (contexts[t].TryGetType(type, out GeneratedType generated))
          return generated;
      return null;
    }

    /// <returns>The generated accessors of the property in the registered contexts, null when there are none</returns>
    internal static GeneratedProperty FindProperty(PropertyInfo property)
    {
      GeneratedType declaring = Find(property.DeclaringType);
      return declaring?.FindProperty(property);
    }
  }
}
