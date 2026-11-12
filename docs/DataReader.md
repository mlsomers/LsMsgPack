# Data readers (LsMsgPack.Data)

The `LsMsgPack.Data` package connects ADO.NET data readers (SqlClient, Npgsql, SQLite, MySqlConnector...) to LsMsgPack, without a class per table:

- **Result sets and table dumps as streams**: `WriteTable` and `WriteResultSets` write the rows of a data reader to a `MsgPackStreamWriter` while they are read. Each result set gets one schema. With an index, a file of dumps lets a reader go to a table or a row directly.
- **Dumps read back as a data reader**: `MsgPackDataReader` is a `DbDataReader` over the tables of a stream, e.g. for `SqlBulkCopy.WriteToServer(reader)`.
- **One payload**: `SerializeRows` writes a result set as the bytes of a `List<T>`, for any reader of that list.
- **Rows into objects**: `ReadRows<T>` maps rows to objects like Dapper's `Query<T>`, with the serializer's rules for properties and serialized fields, names and object creation.

It's an add-on to [LsMsgPack](Streaming.md), in its own package so applications without databases don't carry it. It depends on exactly the LsMsgPack version it was built with. ADO.NET is part of .NET Standard 2.0, so there are no other dependencies.

## Result sets and table dumps

```csharp
using LsMsgPack;
using LsMsgPack.Data;
using LsMsgPack.Streaming;

LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions { SchemaVersion = SchemaVersion.Described });

using (MsgPackStreamWriter writer = new MsgPackStreamWriter(file, serializer, index: new MsgPackStreamIndexOptions { PointInterval = 1000 }))
{
  // Every result set of a stored procedure
  using (DbDataReader reader = procedure.ExecuteReader())
    writer.WriteResultSets(reader, new[] { "Customers", "Orders" });

  // Or a dump, table by table
  foreach (string table in tables)
    using (DbDataReader reader = Select(table))
      writer.WriteTable(reader, table, new MsgPackTableOptions { PointColumn = "OrderDate" });
}
```

- **One root value per result set**: a head with the schema of the columns, then the rows in an array of unknown length. The rows are written while the data reader reads them, and the writer passes its buffer to the stream every 64 KiB. Memory stays flat, whatever the number of rows.
- **The schema** has one entry with the column names. With schema version 2 (`SchemaVersion.Described`) it also has the column types, so readers without classes (`MsgPackDataReader`, the TypeScript client) know them. Use it for dumps. It goes where the writer's `MsgPackStreamSchemas` says: inline, or a reference with `PerStream`/`External`.
- **A row** is written as the serializer writes an object of a class with a property per column: its name, its field type, and `Nullable` for a value type. DBNull is nil. A `0`, `false` or empty string stays a value, so a reader into `int?` gets what the database holds. The format options of the serializer apply (`GuidFormat`, `DecimalFormat`, `DateTimeOffsetFormat`...), and so does the layout: arrays by default, maps with `ObjectLayout.Map`.
- **Field types**: the first row decides them, because some providers (SQLite) give the type of the current row's value. A result set without rows still gets its columns.
- **Result sets without columns** (statements that return no rows) are left out by `WriteResultSets`.
- **Asynchronously**: `WriteTableAsync` and `WriteResultSetsAsync` read with `ReadAsync` and call `FlushIfFullAsync` after the rows. Use them with a writer created with `asynchronous: true`, e.g. on an HTTP response.
- **Column names** must be unique and must not be empty or `"@"`. Give such columns an alias in the query (`SELECT COUNT(*) AS Total`).
- **Errors**: when the data reader fails in the middle of a row, nothing of that row is written and the exception is passed on. The table's array stays open. With an index, `Dispose` marks the table `Incomplete` and its rows before the failing one stay readable through the index.

### With an index

With `MsgPackStreamIndexOptions` (see [the index](Streaming.md#the-index)) every table is an entry of the index:

- **Identifier**: the table's name (`WriteTable`), or for `WriteResultSets` the name you pass or the result set's number.
- **Content type**: `application/x-lsmsgpack-rows` (`MsgPackDataStreams.ContentType`).
- **Metadata**: the columns under `"columns"`: `[[name, .NET type, the provider's type name], ...]`, e.g. `["Amount", "System.Decimal", "decimal(18,2)"]`. Turn it off with `MsgPackTableOptions.ColumnMetadata = false`.
- **Element count**: the number of rows.
- **Points**: every `PointInterval` rows, and with `MsgPackTableOptions.PointColumn` wherever that column's value changes, with the value as the point's key. Order the rows by that column (a date, a customer id) to seek by it.

```csharp
using (MsgPackStreamReader reader = new MsgPackStreamReader(file, serializer))
{
  MsgPackStreamEntry orders = reader.ReadIndexes().SelectMany(i => i.Entries).Last(e => (string)e.Identifier == "Orders");

  // As a data reader, with the exact column types of the metadata
  using (MsgPackDataReader rows = new MsgPackDataReader(reader, orders, leaveOpen: true))
    bulkCopy.WriteToServer(rows);

  // Or into a class, from row 123456 on
  reader.Seek(orders);
  reader.ReadHead<List<Order>>();
  reader.Read();                         // ArrayStart
  reader.SeekElement(orders, 123456);
  Order order = reader.ReadObject<Order>();
}
```

## Reading dumps as a data reader

`MsgPackDataReader` reads the tables of a stream as result sets: `Read()` moves to the next row and `NextResult()` to the next table. It implements `DbDataReader`: typed getters, `GetValues`, `GetOrdinal` (ignoring case after the exact name), `GetBytes`/`GetChars`, `GetSchemaTable` and the async methods.

- `new MsgPackDataReader(streamReader)` reads the tables from the stream reader's position on, one after another, also from a stream that doesn't seek. `await MsgPackDataReader.OpenAsync(streamReader)` does the same with asynchronous reads (`ReadAsync`, `NextResultAsync`).
- `new MsgPackDataReader(streamReader, entry)` reads the table of an index entry. It seeks there, so it needs a stream that seeks.
- **Column names** come from the schema. `TableName` is the entry's identifier, otherwise the name of the rows in the schema (the table's name, as `WriteTable` writes it).
- **Column types**: the exact types of the entry's metadata, when the reader was opened at an entry. Otherwise the kinds of schema version 2: `int` is an `Int32`, `long` an `Int64`, `float` a `Double`, `datetime` a `DateTime`... Otherwise (schema version 1) `object`, with the values as the serializer reads them into object. The data never picks a type: the types come from a fixed list of framework types.
- **Values** are read as the column type, as the serializer reads a property of that type (the same formats and conversions), without boxing: a typed getter of the column's type returns the value as it was read, `GetValue` boxes it. A typed getter of another type converts (`GetInt32` of a `long` column).

## Rows into classes

The stream of a table is what `Serialize<List<T>>` writes, with an array of unknown length. So the serializer's readers read it into classes:

```csharp
reader.ReadHead<List<Order>>();
reader.Read();                                // ArrayStart
while (!reader.AtEnd)
  Process(reader.ReadObject<Order>());
reader.Read();                                // ArrayEnd

// asynchronously, as the rows arrive
await reader.ReadHeadAsync<List<Order>>();
await foreach (Order order in reader.ReadElementsAsync<Order>())
  Process(order);
```

The reader pairs `Order` with the rows' entry of the schema whatever its name, and matches the properties to the columns by name. The TypeScript client reads them with `readHead()` and `elements()` (see [TypeScriptClient.md](TypeScriptClient.md#streams)).

## One payload: SerializeRows

```csharp
byte[] data = serializer.SerializeRows<Order>(reader);   // or SerializeRows(reader, target stream)
List<Order> orders = serializer.Deserialize<List<Order>>(data);
```

- The bytes are the same as `Serialize(List<T>)` of a class with a property per column: the column names, the field types, `Nullable` for value types, in the column order. So any reader of that list reads them (LsMsgPackDebug included), without the escape sequences of streams.
- The rows are buffered, because the array header needs their number before them. For large results use the stream writer.
- The schema is inline, or a reference with `WriteSchemaReference` (the schema is added to the `SchemaStore`). An empty result has the schema of an empty list.
- Custom dynamic filters are not supported (`NotSupportedException`).

## Rows into objects: ReadRows

```csharp
List<Order> orders = serializer.ReadRows<Order>(reader);
foreach (Order order in serializer.EnumerateRows<Order>(reader)) { ... }
```

- **Properties and serialized fields**: the ones the serializer reads (its static filters, with the `SerializedFields` option for fields, `[IgnoreDataMember]`, `[JsonIgnore]`...), except those with `[NotMapped]`.
- **Column names**: `[Column("customer_name")]` (System.ComponentModel.DataAnnotations.Schema, as Entity Framework and linq2db use it; found by its name, so no reference is needed), otherwise the property id (the name, or what the options' property id resolvers return). Then the name ignoring case, then ignoring case and underscores (`first_name` sets `FirstName`). A column that matches no property or field, or several equally, is not read.
- **Values**: a column of the property's type is read with the typed getter (`GetInt32`, `GetString`..., no boxing). For a column of another type, the getter of the property's type is tried on the first row: SQLite, for example, reads `GetInt32` from INTEGER and `GetDateTime`, `GetDecimal` and `GetGuid` from TEXT. If that fails, the boxed value is converted as the serializer converts values (numbers, enums from integers or names, Guids from strings...).
- **DBNull** leaves the property or field as the constructor made it.
- **Objects** are created as `ObjectCreation` says, with the generated creators and setters when the class has generated code. Immutable classes are created by the constructor that takes their values, as the serializer creates them.
- Dapper reads a Guid stored as TEXT in SQLite only with a type handler. `ReadRows` reads it through SQLite's `GetGuid`.

## Performance

Measured on an i7-7700K with .NET 8 in Release, per row, best of 5 passes (`BenchmarkRows` in `LsMsgPack.DataTests`, 10,000 invoice rows, default options):

| | in-memory reader, 10 columns | SQLite in memory, native types, 9 columns |
|---|---|---|
| hand-written loop | 147 ns | 1918 ns |
| hand-written, `IsDBNull` on every column | | 2488 ns |
| Dapper 2.1.66 `Query<T>` / `Parse<T>` | 412 ns | 2857 ns |
| `ReadRows<T>` | **237 ns** (1.74x Dapper) | **2047 ns** (1.40x Dapper) |
| Dapper + LsMsgPack `Serialize` | 847 ns | 3144 ns |
| `SerializeRows` | 379 ns | 1948 ns |
| `WriteTable` (stream writer) | **356 ns** (2.4x Dapper + `Serialize`) | **1880 ns** (1.67x) |
| Dapper + System.Text.Json | 1268 ns | 3734 ns |

- **Allocations per row** (in memory): Dapper 330 B, `ReadRows` 138 B (the object and its strings, as the hand-written loop), `WriteTable` 25 B, `SerializeRows` 122 B (the result; none to a stream), Dapper + `Serialize` 871 B.
- **Typed getters**: Dapper reads every value with `GetValue`, boxed. The mapper and the writers use the typed getters, which makes the difference on the in-memory reader.
- **With a real provider** the provider takes most of the time, and `ReadRows`, `SerializeRows` and `WriteTable` cost about what a hand-written loop costs.
- **NULL checks**: SQLite's `IsDBNull` is a native call of about 65 ns per column, and its typed getters check again. After the first 64 rows of a call, a column that had no NULL yet is read with its typed getter alone, when the reader's getters throw for a NULL, as the providers' getters do (SQLite, SqlClient, Npgsql...). Such an exception is checked with `IsDBNull`, so a NULL that comes later is still a NULL, also in a column the provider calls NOT NULL (the outer side of a join). That column is then checked on every row. Whether a reader type's getters throw is tried once, at the first NULL it has; a reader whose getters return a value for a NULL (0, null) is checked on every row. The provider's schema (`GetSchemaTable`'s `AllowDBNull`) is not used: on SQLite it takes about 10 ms per query, and it says NOT NULL for the outer side of a join.
- **Values the provider converts**: on SQLite with dates and decimals stored as TEXT the provider parses the text for both: `ReadRows` 3.1 µs per row, Dapper 3.7 µs.
- **`MsgPackDataReader`** over a dump in memory (the invoices, opened at the index entry): `Read` alone 240 ns per row, with `GetValues` 375 ns (boxed), `ReadAsync` + `GetValues` 400 ns. The typed getters cost 5-12 ns each.
