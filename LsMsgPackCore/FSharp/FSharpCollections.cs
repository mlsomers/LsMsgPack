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
        if (!PropertyAccessor.CanCompile) // no code for MapOf of value types (NativeAOT): F#'s Map.ofSeq, which the application has when it calls it with these types
        {
          MethodInfo ofSeq = concrete.Assembly.GetType("Microsoft.FSharp.Collections.MapModule")?.GetMethod("OfSeq", BindingFlags.Public | BindingFlags.Static);
          Type tupleType = typeof(Tuple<,>).MakeGenericType(arguments);
          ConstructorInfo tuple = tupleType.GetConstructor(arguments);
          if (ofSeq is null || tuple is null)
            return null;
          defaults = new object[] { null, ofSeq.MakeGenericMethod(arguments), tuple };
          return typeof(FSharpCollections).GetMethod(nameof(MapOfBoxed), BindingFlags.NonPublic | BindingFlags.Static);
        }
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
    /// A map of the pairs without generic code of its own (no dynamic code): the tuples made by their constructor, the map by <c>Map.ofSeq</c>, all by reflection.
    /// </summary>
    /// <param name="pairs">The KeyValuePairs of the map's types</param>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = TrimReasons.FSharp)]
    private static object MapOfBoxed(Array pairs, MethodInfo ofSeq, ConstructorInfo tuple)
    {
      Array tuples = Array.CreateInstance(tuple.DeclaringType, pairs.Length); // an array of a reference type
      FrameworkTypeInfo.PairInfo pair = FrameworkTypeInfo.GetPair(pairs.GetType().GetElementType());
      object[] arguments = new object[2];
      try
      {
        for (int t = 0; t < pairs.Length; t++)
        {
          object entry = pairs.GetValue(t);
          arguments[0] = pair.Key.GetValue(entry);
          arguments[1] = pair.Value.GetValue(entry);
          tuples.SetValue(tuple.Invoke(arguments), t);
        }
        return FSharpAccess.Invoke(ofSeq, null, tuples);
      }
      catch (NotSupportedException ex) // NativeAOT has no code for Map.ofSeq or the tuples of these types: the application never makes such a map
      {
        Type[] types = ofSeq.GetGenericArguments();
        string names = Instances.Display(types[0]) + ", " + Instances.Display(types[1]);
        throw Instances.NoCode("an F# Map<" + names + ">", "Call Map.ofSeq<" + names + "> once in the application (e.g. Map.ofSeq [ (key, value) ])", ex);
      }
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
