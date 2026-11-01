using System;
using System.Collections.Concurrent;
using System.Data;

namespace LtMsgPack.Data
{
  /// <summary>
  /// When a column is read without <see cref="IDataRecord.IsDBNull"/> (on SQLite a native call, ~70 ns per column, and its typed getters check again):
  /// after the first <see cref="CheckedRows"/> rows of a call, a column that had no NULL yet (in any call with the same plan) is read with its typed getter alone,
  /// when the getters of the reader's type are known to throw for a NULL (as the providers do: SQLite's InvalidOperationException, SqlClient's SqlNullValueException, InvalidCastException...).
  /// Such an exception is then checked with IsDBNull, so a NULL is still found (also where the provider's metadata says NOT NULL, e.g. the outer side of a join), and the column is checked from then on.
  /// Whether the getters throw is tried once per reader type, at the first NULL a check finds; a reader whose getters return a value for a NULL is checked on every row.
  /// </summary>
  internal static class NullChecks
  {
    /// <summary>
    /// The rows of a call read with IsDBNull on every column (they find the columns with NULLs without an exception, and teach the reader type).
    /// </summary>
    internal const int CheckedRows = 64;

    private static readonly ConcurrentDictionary<Type, bool> Throwing = new ConcurrentDictionary<Type, bool>();

    /// <summary>
    /// Whether the columns without NULLs so far can be read without IsDBNull from now on (asked at row <see cref="CheckedRows"/>).
    /// </summary>
    internal static bool CanSkip(IDataRecord record)
    {
      return Throwing.TryGetValue(record.GetType(), out bool throws) && throws;
    }

    /// <summary>
    /// IsDBNull found the first NULL of a column: the first one of the reader's type tells whether its getters throw for a NULL.
    /// </summary>
    internal static void Learn<TValue>(IDataRecord record, int ordinal, Func<IDataRecord, int, TValue> get)
    {
      Type type = record.GetType();
      if (Throwing.ContainsKey(type))
        return;
      bool throws;
      try
      {
        get(record, ordinal);
        throws = false;
      }
      catch (Exception)
      {
        throws = true;
      }
      Throwing.TryAdd(type, throws);
    }
  }
}
