using LsMsgPack.Streaming;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPack.Data
{
  /// <summary>
  /// Result sets of a data reader as root values of a <see cref="MsgPackStreamWriter"/>: per result set a head with the schema of its columns (once), then its rows in an array of unknown length,
  /// written while they are read. With an index (<see cref="MsgPackStreamIndexOptions"/>) every result set is an entry, named, with its columns in the metadata and points into its rows:
  /// a file of table dumps that a reader seeks in. <see cref="MsgPackDataReader"/> reads them back as a data reader; <c>ReadHead&lt;List&lt;T&gt;&gt;()</c> and <c>ReadObject&lt;T&gt;()</c> into classes.
  /// <para>The rows are written as the serializer writes an object of a class with a property per column (its name, its field type, Nullable for a value type): DBNull is nil, a 0 stays a 0.
  /// The first row decides the field types of providers that give the types of the current row (SQLite).</para>
  /// </summary>
  public static class StreamWriterDataExtensions
  {
    /// <summary>
    /// Writes the current result set of the reader (its rows from the reader's position on) as one root value: a head with the schema of the columns, then the rows.
    /// Call it once per table for a dump of several tables.
    /// </summary>
    /// <param name="name">The name of the table: the identifier of its index entry, and the name of its rows in the schema (unless <see cref="MsgPackTableOptions.RowTypeName"/>), null for none</param>
    /// <returns>The number of rows</returns>
    /// <exception cref="InvalidOperationException">Not at <see cref="MsgPackStreamWriter.Depth"/> 0, or a head waits for its body</exception>
    /// <exception cref="LsMsgPack.MsgPackException">Two columns have the same name, or one has none</exception>
    public static long WriteTable(this MsgPackStreamWriter writer, IDataReader reader, string name = null, MsgPackTableOptions options = null)
    {
      Check(writer, reader);
      return Write(writer, reader, name, name, options);
    }

    /// <summary>
    /// Writes every result set of the reader (the current one and the ones <see cref="IDataReader.NextResult"/> moves to, e.g. of a stored procedure), each as <see cref="WriteTable"/> does.
    /// Result sets without columns (statements that return no rows) are left out.
    /// </summary>
    /// <param name="names">The names of the result sets in their order, null (or fewer names): their number (0, 1, ...) as the identifier of their index entry</param>
    /// <returns>The number of result sets written</returns>
    public static int WriteResultSets(this MsgPackStreamWriter writer, IDataReader reader, IReadOnlyList<string> names = null, MsgPackTableOptions options = null)
    {
      Check(writer, reader);
      int written = 0, number = 0;
      do
      {
        if (reader.FieldCount == 0)
          continue;
        string name = names != null && number < names.Count ? names[number] : null;
        Write(writer, reader, name ?? (object)number, name, options);
        written++;
        number++;
      }
      while (reader.NextResult());
      return written;
    }

    /// <summary>
    /// <see cref="WriteTable"/> with the asynchronous reads of the reader and <see cref="MsgPackStreamWriter.FlushIfFullAsync"/> after the rows (for a writer created with <c>asynchronous: true</c>, e.g. on an HTTP response).
    /// </summary>
    public static async Task<long> WriteTableAsync(this MsgPackStreamWriter writer, DbDataReader reader, string name = null, MsgPackTableOptions options = null, CancellationToken cancellationToken = default(CancellationToken))
    {
      Check(writer, reader);
      return await WriteAsync(writer, reader, name, name, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <see cref="WriteResultSets"/> with the asynchronous reads of the reader and <see cref="MsgPackStreamWriter.FlushIfFullAsync"/> after the rows.
    /// </summary>
    public static async Task<int> WriteResultSetsAsync(this MsgPackStreamWriter writer, DbDataReader reader, IReadOnlyList<string> names = null, MsgPackTableOptions options = null, CancellationToken cancellationToken = default(CancellationToken))
    {
      Check(writer, reader);
      int written = 0, number = 0;
      do
      {
        if (reader.FieldCount == 0)
          continue;
        string name = names != null && number < names.Count ? names[number] : null;
        await WriteAsync(writer, reader, name ?? (object)number, name, options, cancellationToken).ConfigureAwait(false);
        written++;
        number++;
      }
      while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));
      return written;
    }

    private static long Write(MsgPackStreamWriter writer, IDataReader reader, object identifier, string name, MsgPackTableOptions options)
    {
      bool hasRow = reader.Read(); // before the plan: the first row decides the field types of some providers
      TableWriter table = new TableWriter(writer, reader, identifier, name, options);
      while (hasRow)
      {
        table.WriteRow();
        hasRow = reader.Read();
      }
      table.End();
      return table.Rows;
    }

    private static async Task<long> WriteAsync(MsgPackStreamWriter writer, DbDataReader reader, object identifier, string name, MsgPackTableOptions options, CancellationToken cancellationToken)
    {
      bool hasRow = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
      TableWriter table = new TableWriter(writer, reader, identifier, name, options);
      while (hasRow)
      {
        table.WriteRow();
        await writer.FlushIfFullAsync(cancellationToken).ConfigureAwait(false);
        hasRow = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
      }
      table.End();
      await writer.FlushIfFullAsync(cancellationToken).ConfigureAwait(false);
      return table.Rows;
    }

    private static void Check(MsgPackStreamWriter writer, IDataReader reader)
    {
      if (writer is null)
        throw new ArgumentNullException(nameof(writer));
      if (reader is null)
        throw new ArgumentNullException(nameof(reader));
    }
  }
}
