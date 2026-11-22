using LsMsgPack.Meta;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LsMsgPack.FSharp
{
  /// <summary>
  /// F#'s immutable list and map are created from their elements (CollectionInfo's factory): a list has no constructor or Add, a map's IDictionary.Add throws.
  /// Sets need nothing (a constructor taking the elements).
  /// </summary>
  internal static class FSharpCollections
  {
    /// <summary>
    /// The factory of an F# list (<c>ListModule.OfSeq</c>) or map (<see cref="MapOf"/>, with the map's constructor as its second argument), null for other types.
    /// </summary>
    /// <param name="defaults">The arguments of the factory after the elements</param>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = TrimReasons.FSharp)]
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = TrimReasons.FSharp)]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.FSharp)]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.FSharp)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = TrimReasons.FSharp)]
    internal static MethodInfo FactoryOf(Type concrete, out object[] defaults)
    {
      defaults = null;
      if (FSharpTypes.IsDefinition(concrete, FSharpTypes.ListDefinition))
      {
        Type module = concrete.Assembly.GetType("Microsoft.FSharp.Collections.ListModule");
        MethodInfo ofSeq = module?.GetMethod("OfSeq", BindingFlags.Public | BindingFlags.Static);
        if (ofSeq is null)
          return null;
        defaults = new object[1];
        return ofSeq.MakeGenericMethod(concrete.GenericTypeArguments);
      }
      if (FSharpTypes.IsDefinition(concrete, FSharpTypes.MapDefinition))
      {
        Type[] arguments = concrete.GenericTypeArguments;
        Type tuples = typeof(IEnumerable<>).MakeGenericType(typeof(Tuple<,>).MakeGenericType(arguments));
        ConstructorInfo constructor = concrete.GetConstructor(new[] { tuples });
        if (constructor is null)
          return null;
        defaults = new object[] { null, constructor };
        return typeof(FSharpCollections).GetMethod(nameof(MapOf), BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(arguments);
      }
      return null;
    }

    /// <summary>
    /// A map of the pairs: its constructor takes tuples.
    /// </summary>
    private static object MapOf<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> pairs, ConstructorInfo constructor)
    {
      List<Tuple<TKey, TValue>> tuples = new List<Tuple<TKey, TValue>>();
      foreach (KeyValuePair<TKey, TValue> pair in pairs)
        tuples.Add(Tuple.Create(pair.Key, pair.Value));
      try
      {
        return constructor.Invoke(new object[] { tuples });
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null)
      {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw; // not reached
      }
    }
  }
}
