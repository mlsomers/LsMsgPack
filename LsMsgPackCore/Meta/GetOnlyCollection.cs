using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// A collection property without a setter (<c>public List&lt;Line&gt; Lines { get; } = new List&lt;Line&gt;();</c>, see <see cref="TypeResolving.Filters.GetOnlyProperties.Collections"/>):
  /// reading fills the collection the getter returns, as Json.NET does (it is cleared first: the data is what the writer's collection held, items the constructor adds are not doubled).
  /// </summary>
  internal static class GetOnlyCollection
  {
    private static readonly ConcurrentDictionary<Type, Filler> Fillers = new ConcurrentDictionary<Type, Filler>();

    /// <summary>
    /// Whether a property of this type can be filled: a collection or dictionary with Add (IList, IDictionary, ICollection&lt;T&gt;, also those interfaces), not an array.
    /// A read-only collection of such a type (e.g. ReadOnlyCollection&lt;T&gt;) is left as it is when read.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    internal static bool IsFillable(Type type)
    {
      if (type.IsArray || type == typeof(string))
        return false;
      if (typeof(IList).IsAssignableFrom(type) || typeof(IDictionary).IsAssignableFrom(type) || IsCollectionInterface(type))
        return true;
      Type[] interfaces = type.GetInterfaces();
      for (int t = 0; t < interfaces.Length; t++)
        if (IsCollectionInterface(interfaces[t]))
          return true;
      return false;
    }

    private static bool IsCollectionInterface(Type type)
    {
      return type.IsInterface && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICollection<>);
    }

    /// <summary>
    /// Replaces the items of <paramref name="target"/> (what the getter returns) by those of <paramref name="items"/> (the value read, of the property's type).
    /// Nothing happens when the target is read-only or has a fixed size.
    /// </summary>
    internal static void Fill(object target, object items)
    {
      if (target is IDictionary dictionary && items is IDictionary entries)
      {
        if (dictionary.IsReadOnly || dictionary.IsFixedSize)
          return;
        dictionary.Clear();
        foreach (DictionaryEntry entry in entries)
          dictionary[entry.Key] = entry.Value; // the target may compare its keys otherwise (e.g. ignoring case): the last one wins, as LtMsgPack reads duplicate keys
        return;
      }

      if (target is IList list)
      {
        if (list.IsReadOnly || list.IsFixedSize)
          return;
        list.Clear();
        foreach (object item in (IEnumerable)items)
          list.Add(item);
        return;
      }

      Filler filler = Fillers.GetOrAdd(target.GetType(), t => new Filler(t));
      filler.Fill(target, (IEnumerable)items);
    }

    /// <summary>
    /// ICollection&lt;T&gt; (e.g. HashSet&lt;T&gt;, or a dictionary without IDictionary) by reflection, a rare case.
    /// </summary>
    private sealed class Filler
    {
      private readonly MethodInfo _isReadOnly;
      private readonly MethodInfo _clear;
      private readonly MethodInfo _add;

      [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
      [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties, typeof(ICollection<>))]
      [UnconditionalSuppressMessage("Trimming", "IL2065", Justification = TrimReasons.KeptMembers)]
      internal Filler(Type type)
      {
        Type[] interfaces = type.GetInterfaces();
        for (int t = 0; t < interfaces.Length; t++)
        {
          if (!IsCollectionInterface(interfaces[t]))
            continue;
          _isReadOnly = interfaces[t].GetProperty(nameof(ICollection<object>.IsReadOnly)).GetGetMethod();
          _clear = interfaces[t].GetMethod(nameof(ICollection<object>.Clear));
          _add = interfaces[t].GetMethod(nameof(ICollection<object>.Add));
          return;
        }
      }

      internal void Fill(object target, IEnumerable items)
      {
        if (_add is null)
          return;
        try
        {
          if ((bool)_isReadOnly.Invoke(target, null))
            return;
          _clear.Invoke(target, null);
          object[] argument = new object[1];
          foreach (object item in items)
          {
            argument[0] = item;
            _add.Invoke(target, argument);
          }
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
          ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
          throw; // not reached
        }
      }
    }
  }
}
