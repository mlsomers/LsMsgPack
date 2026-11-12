# LsMsgPack.Data

ADO.NET data readers (SqlClient, Npgsql, SQLite...) to MsgPack and back, without a class per table. An add-on to [LsMsgPack](https://www.nuget.org/packages/LsMsgPack).

- **Result sets and table dumps as streams**: the rows are written while the data reader reads them, with one schema per result set. Memory stays flat, whatever the number of rows. With an index, a file of dumps lets a reader go to a table, a row, or a key directly.
- **Dumps read back as a `DbDataReader`** (`MsgPackDataReader`), e.g. to restore a table with `SqlBulkCopy.WriteToServer(reader)`.
- **Rows into objects** like Dapper's `Query<T>`, with typed getters (no boxing): 1.7x as fast as Dapper on an in-memory reader.
- **One payload** with the bytes of `Serialize(List<T>)`, for any reader of that list.

## Result sets and dumps

```csharp
using LsMsgPack;
using LsMsgPack.Data;
using LsMsgPack.Streaming;

LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions { SchemaVersion = SchemaVersion.Described });

using (MsgPackStreamWriter writer = new MsgPackStreamWriter(file, serializer, index: new MsgPackStreamIndexOptions { PointInterval = 1000 }))
{
  using (DbDataReader reader = procedure.ExecuteReader())
    writer.WriteResultSets(reader, new[] { "Customers", "Orders" });   // every result set

  using (DbDataReader reader = Select("Invoices"))
    writer.WriteTable(reader, "Invoices", new MsgPackTableOptions { PointColumn = "InvoiceDate" });
}   // Dispose writes the index
```

## Reading them back

```csharp
using (MsgPackStreamReader reader = new MsgPackStreamReader(file, serializer))
{
  MsgPackStreamEntry orders = reader.ReadIndexes().SelectMany(i => i.Entries).Last(e => (string)e.Identifier == "Orders");

  using (MsgPackDataReader rows = new MsgPackDataReader(reader, orders, leaveOpen: true))   // as a DbDataReader
    bulkCopy.WriteToServer(rows);

  reader.Seek(orders);                                    // or into a class
  reader.ReadHead<List<Order>>();
  reader.Read();
  while (!reader.AtEnd)
    Process(reader.ReadObject<Order>());
}
```

`new MsgPackDataReader(streamReader)` reads the tables one after another as result sets, also from a stream that doesn't seek (`MsgPackDataReader.OpenAsync` for asynchronous reads).

## Rows into objects, and one payload

```csharp
List<Order> orders = serializer.ReadRows<Order>(reader);     // [Column("name")], [NotMapped], case and underscores ignored
byte[] data = serializer.SerializeRows<Order>(reader);       // the bytes of Serialize(List<Order>)
```

See [data readers](https://github.com/mlsomers/LsMsgPack/blob/master/docs/DataReader.md) for the details and the measurements, and [streaming](https://github.com/mlsomers/LsMsgPack/blob/master/docs/Streaming.md) for the stream writer and reader.
