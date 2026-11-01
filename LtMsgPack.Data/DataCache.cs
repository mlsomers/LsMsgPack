using LtMsgPack.Writing;
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace LtMsgPack.Data
{
  /// <summary>
  /// The plans of one serializer (its options do not change): the row plan used last (the same query usually has the same columns) and the mapper used last per class.
  /// </summary>
  internal sealed class DataCache
  {
    private static readonly ConditionalWeakTable<Serializer, DataCache> All = new ConditionalWeakTable<Serializer, DataCache>();

    internal volatile RowPlan LastPlan;

    /// <summary>
    /// A RowMapper&lt;T&gt; per T.
    /// </summary>
    internal readonly ConcurrentDictionary<Type, object> Mappers = new ConcurrentDictionary<Type, object>();

    internal static DataCache Of(Serializer serializer)
    {
      return All.GetValue(serializer, s => new DataCache());
    }

    internal RowPlan Plan(Serializer serializer, System.Data.IDataRecord record, string typeName)
    {
      RowPlan plan = RowPlan.For(serializer, record, typeName, LastPlan);
      LastPlan = plan;
      return plan;
    }

    internal RowMapper<T> Mapper<T>(Serializer serializer, System.Data.IDataRecord record) where T : class
    {
      Mappers.TryGetValue(typeof(T), out object last);
      RowMapper<T> mapper = RowMapper<T>.For(serializer, record, (RowMapper<T>)last);
      if (!ReferenceEquals(mapper, last))
        Mappers[typeof(T)] = mapper;
      return mapper;
    }
  }
}
