# Streaming: MsgPackStreamWriter and MsgPackStreamReader

`MsgPackStreamWriter` and `MsgPackStreamReader` (LtMsgPack, namespace `LtMsgPack.Streaming`) write and read MsgPack values one after another on a `Stream`. Use them when the data is too large to buffer, when it's produced while it's written (rows of a database query), or when one stream holds several parts (the result sets of a stored procedure, the tables of a backup).

- **Values**: integers, floats, strings, bins, timestamps, Guids, decimals, extensions, and arrays and maps of a known length.
- **Values of unknown length**: arrays, maps, str and bin that are written before their length is known (see [the format](#the-format)).
- **Objects**: whole objects, written and read by the `LtMsgPackSerializer`, with their schema. Or the schema once for a whole root value, e.g. a table of objects of unknown length (see [one schema for a root value](#one-schema-for-a-root-value)).
- **Positions**: the writer reports the position after each value.
- **An index** (optional): the writer lists the root values with their positions, hashes and points into large arrays, and writes the list at the end of the stream. A reader of a file goes to any table or row directly (see [the index](#the-index)).

Both are synchronous and not thread-safe.

## Writing

```csharp
using LtMsgPack;
using LtMsgPack.Streaming;

LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions());
List<long> index = new List<long>();

using (MsgPackStreamWriter writer = new MsgPackStreamWriter(file, serializer))
{
  // A table whose number of rows isn't known up front
  writer.WriteArrayStart();
  while (rows.Read())
  {
    writer.WriteArrayHeader(2);   // a row of two columns
    writer.Write(rows.GetInt32(0));
    writer.Write(rows.GetString(1));
  }
  writer.WriteEnd();
  index.Add(writer.Position);     // where the next root value starts

  // An object with its schema
  index.Add(writer.WriteObject(order));
}
```

What the writer offers:

- **Values**:
  - `Write(...)` for `bool`, all integer types, `float`, `double`, `string`, `DateTime`, `Guid` and `decimal`.
  - `WriteNil()`, `WriteBinary(...)`, `WriteExtension(typeCode, data)`.
  - `WriteRaw(bytes)` for one value that's already encoded (the bytes are not checked).
- **Formats**: values are written as the serializer writes them: compact integers (`DynamicallyCompact`), the byte order (`EndianAction`), `GuidFormat`, `DecimalFormat` and `UnspecifiedDateTimeKind` of its options.
- **Known length**: `WriteArrayHeader(count)` and `WriteMapHeader(count)` are followed by their elements (keys and values for a map). They end by themselves after the last one.
- **Unknown length**:
  - `WriteArrayStart()` and `WriteMapStart()` are followed by any number of values, then `WriteEnd()`.
  - `WriteStringStart()` is followed by chunks (`Write(string)` or `WriteStringChunk(chars)`), then `WriteEnd()`. A chunk never splits a character: a high surrogate at the end of a chunk is written with the next one. That makes it suitable for the buffers of `IDataRecord.GetChars`.
  - `WriteBinaryStart()` is followed by chunks (`WriteBinary(...)`), then `WriteEnd()`. Use it for the buffers of `IDataRecord.GetBytes`.
- **Objects**: `WriteObject(value)` (see [objects and their schema](#objects-and-their-schema)), and `WriteHead<TRoot>()` for one schema before a root value (see [one schema for a root value](#one-schema-for-a-root-value)).
- **Checks**: the writer refuses what the format doesn't allow, with an `InvalidOperationException`:
  - an end where it doesn't belong (nothing open, an array of known length that still needs elements, a map after a key);
  - a value inside a str or bin of unknown length that isn't a chunk of it;
  - more than 2³²-1 elements or bytes in a value of unknown length;
  - nesting deeper than `MaxDepth`.
- **State**:
  - `Depth` is 0 between root values.
  - `Position` is the number of bytes written since the writer was created, including the ones still in the buffer.
- **Buffering**:
  - Values go into a buffer, which is written to the stream once it holds `FlushThreshold` bytes (64 KiB; 0: after every value).
  - `Flush()` writes the buffer and flushes the stream.
  - `Dispose()` does the same and closes the stream, unless the writer was created with `leaveOpen: true`. It doesn't end values that are still open, so a reader finds such data cut off.

## Reading

```csharp
using (MsgPackStreamReader reader = new MsgPackStreamReader(file, serializer))
{
  reader.Read();                                // ArrayStart, Length -1: unknown length
  while (!reader.AtEnd)
  {
    reader.Read();                              // ArrayStart of a row (Length 2)
    reader.Read(); int id = (int)reader.GetInt64();
    reader.Read(); string name = reader.GetString();
    reader.Read();                              // ArrayEnd of the row
  }
  reader.Read();                                // ArrayEnd of the table

  Order order = reader.ReadObject<Order>();
}
```

**Tokens.** `Read()` moves to the next token and returns false at the end of the stream between root values.
- `TokenType`: `Nil`, `Boolean`, `Integer`, `Float`, `String`, `Binary`, `Extension`, `ArrayStart`, `MapStart`, `ArrayEnd`, `MapEnd`, `StringStart`, `BinaryStart`, `StringEnd` or `BinaryEnd`.
  - Arrays and maps of a known length get their `ArrayEnd` / `MapEnd` too, after their last element, so one loop reads both kinds.
- `Length`: the number of elements or entries, -1 for an unknown length; the number of bytes of a str, bin or extension.
- `Depth`, `IsKey` (a key of a map).
- `TokenStart` and `Position`: where the token starts and ends in the stream.
- `AtEnd`: the innermost array or map ends with the next token, or at depth 0 the stream ends. Use it to loop over the elements of an array of unknown length, or the root values of a stream.

**Getting values.**
- `GetBoolean()`, `GetInt64()`, `GetUInt64()` (`OverflowException` when the value doesn't fit), `GetDouble()`.
- `GetString()`, `GetBytes()`, `GetDateTime()` (timestamps, as the serializer reads them), `GetExtension(out typeCode)`.
- `GetValue()` returns the value as the serializer reads it into `object`: `object[]` for arrays, `KeyValuePair<object, object>[]` for maps.
- At a `StringStart`, `BinaryStart`, `ArrayStart` or `MapStart`, `GetString()`, `GetBytes()` and `GetValue()` read the whole value. The reader is then at its end.
- `Skip()` passes the current array, map, str or bin without buffering it.

**Objects.** `ReadObject<T>()` reads the next value as `LtMsgPackSerializer.Deserialize<T>` does, with its schema. Values of unknown length inside it are read too: the reader replaces them by ones of known length before the serializer reads them. `ReadHead<TRoot>()` reads a schema written by `WriteHead<TRoot>()` (see [one schema for a root value](#one-schema-for-a-root-value)).

**The buffer.** The reader reads the stream in blocks into a buffer, so the stream doesn't have to seek (network streams work). Values that are read whole are buffered whole: objects, `GetValue`, `GetString`... The buffer only grows as far as the data that arrives, so a header that claims more than the stream holds costs no more memory than the stream holds.

**Errors.** Data that isn't valid throws a `MsgPackException` with its position:
- a reserved escape code;
- an escape byte at the end of the data;
- an end sequence where no value of unknown length can end (outside of one, directly inside an array or map of known length, where the value of a key comes);
- a chunk of a str or bin that isn't a str or bin;
- nesting deeper than `MaxDepth`;
- data that ends inside a value.

## Objects and their schema

With the indexed schema (`UseInexedSchema`, the default) an object is written as its schema, then the object. That's two MsgPack values, so on their own `WriteObject` and `ReadObject` only take root values of the stream (`Depth` 0). For objects in an array or map, write their schema once before it: see [one schema for a root value](#one-schema-for-a-root-value). Without the indexed schema an object is one value: it can also be an element of an array, e.g. of an array of unknown length:

```csharp
LtMsgPackSerializer plain = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false });
// writer: WriteArrayStart(), WriteObject(row) per row, WriteEnd()
// reader: Read(), then while (!reader.AtEnd) rows.Add(reader.ReadObject<Row>())
```

The third parameter of the writer, `MsgPackStreamSchemas`, says where the schemas go:

| Mode | The stream holds | Reading needs |
|---|---|---|
| `PerObject` (default) | every object as `Serialize` writes it with the schema inline | nothing else: each object can be read on its own, e.g. found through an index |
| `PerStream` | a schema inline the first time an object has it, a reference to it for the later objects (18 bytes). When a schema grows (a value needs a class or property it doesn't have yet), the grown schema is written inline once | a serializer with a `SchemaStore`: the reader registers the schemas it finds in the stream, so the later references find them |
| `External` | references only | a serializer whose `SchemaStore` holds the schemas. `writer.ReferencedSchemas` lists their ids: `store.GetSchema(id)` to keep them apart, or to append them to the stream |

`PerStream` and `External` need a serializer that writes schema references: `WriteSchemaReference = true` and a `SchemaStore` in its options. See [schema.md](schema.md) for the schema itself.

## One schema for a root value

`Serialize(list)` writes a `List<Row>` as one schema followed by the array of rows, and each row is only its values. The stream writer can write the same while the rows arrive: `WriteHead<TRoot>()` writes the schema of a root type, and the next root value is its body. In the body, `WriteObject(row)` writes only the values of the row, at any depth.

```csharp
using (MsgPackStreamWriter writer = new MsgPackStreamWriter(file, serializer))
{
  writer.WriteHead<List<Row>>();   // the schema, once
  writer.WriteArrayStart();        // the body: an array of unknown length
  while (rows.Read())
    writer.WriteObject(ToRow(rows));
  writer.WriteEnd();               // the end of the body (and of the head)
}

using (MsgPackStreamReader reader = new MsgPackStreamReader(file, serializer))
{
  reader.ReadHead<List<Row>>();
  reader.Read();                   // ArrayStart
  while (!reader.AtEnd)
    Process(reader.ReadObject<Row>());
  reader.Read();                   // ArrayEnd
}
```

- **The body** is the root value after the head. It can be anything the root type holds: arrays and maps of known or unknown length, values, and `WriteObject` for the objects in it, e.g. `WriteHead<Dictionary<string, List<Row>>>()` for several tables by name. The head ends with its body. The next root value needs a head of its own, or is a whole object again.
- **Declared types**: the type argument of `WriteObject<T>` is the type of the place in the root type (the element type of the list, the type of the property). It decides whether a type id is written, as in `Serialize<T>`. Write rows of a list of `Shape` with `WriteObject<Shape>(circle)`, so that their type ids are written. The writer doesn't check this against the root type.
- **The schema is complete**: the head always has the complete schema of the root type (`CompleteSchemas`, whatever the options say), because the rows come after it. It has the classes of the root type, of its properties and elements, and the classes deriving from them in the assemblies it reaches. A value that needs another class (one declared as `object`, or a class of an assembly the root type doesn't reach) is refused with an `InvalidOperationException`, and nothing is written for it.
- **Where the schema goes** follows `MsgPackStreamSchemas`: inline (`PerObject`, and the first time with `PerStream`) or a reference. Without a `SchemaStore` the writer's serializer keeps the schemas of the heads in a store of its own.
- **The same bytes as `Serialize`**: with arrays and maps of known length the head and body are what `Serialize<TRoot>` writes for the whole value with `CompleteSchemas`. `Deserialize<TRoot>` reads them, and so does `ReadObject<TRoot>()` with lengths unknown too. The other way around, `ReadHead<TRoot>()` reads any payload of `Serialize<TRoot>` in parts.
- **Objects written by hand**: when the root type is a class (not a collection), its values are written as the layout says. With `ObjectLayout.Array` (the default) that's an array of the values in `PropertyOrder`. With `ObjectLayout.Map` the keys are indexes into the schema, which the writer has no help for. Prefer collections as root types, and `WriteObject` for the objects.
- **Without the indexed schema** the head writes and reads nothing, and the body is written and read as without it.

## The index

With `MsgPackStreamIndexOptions` the writer keeps an index of the stream and writes it at the end. Every root value is an **entry**: a value, an array or map, an object with its schema, or a head with its body (a table). A reader of a stream that seeks (a file, a memory stream; over HTTP with range requests) reads the index and goes to an entry, or to a row of a table, without reading what comes before it.

```csharp
MsgPackStreamIndexOptions index = new MsgPackStreamIndexOptions
{
  PointInterval = 1000,        // a point every 1000 rows of a table
  CheckpointInterval = 10,     // an index every 10 entries, flushed: a crash keeps the entries before it
  Hasher = new Sha256Hasher()  // optional, see below
};
using (MsgPackStreamWriter writer = new MsgPackStreamWriter(file, serializer, MsgPackStreamSchemas.PerStream, false, index))
{
  foreach (Table table in tables)
  {
    writer.BeginEntry(table.Name, "application/x-rows");   // optional: names the next root value
    writer.WriteHead<List<Row>>();
    writer.WriteArrayStart();
    foreach (Row row in table.Rows)
      writer.WriteObject(row);
    writer.WriteEnd();
  }
}   // Dispose writes the index

using (MsgPackStreamReader reader = new MsgPackStreamReader(file, serializer))
{
  IReadOnlyList<MsgPackStreamIndex> indexes = reader.ReadIndexes();
  MsgPackStreamEntry orders = indexes.SelectMany(i => i.Entries).Last(e => (string)e.Identifier == "orders");
  reader.Seek(orders);
  reader.ReadHead<List<Row>>();
  reader.Read();                         // ArrayStart
  reader.SeekElement(orders, 123456);    // from the point before it
  Row row = reader.ReadObject<Row>();
}
```

**Entries** (`MsgPackStreamEntry`): the writer fills in `Offset` and `Length` (the bytes of the root value, its schema or head included), `ElementCount` for an array, `Points`, `Hash`. `BeginEntry(identifier, contentType)` names the next root value and returns its entry, to which `Modified` and `Metadata` can be added. The identifier is a string or an integer and doesn't have to be unique (when several entries have the same one, the last one is the current one). Metadata holds plain values: null, bool, integers, floats, strings, `byte[]`, `DateTime`, and lists and dictionaries of them. Stream-wide metadata goes in `MsgPackStreamIndexOptions.Metadata`.

**Points** (the sparse index of a large array): `PointInterval` adds one every so many elements of an entry that is an array (also the body of a head), and `AddPoint(key)` adds one before the next element, e.g. where the timestamp the rows are ordered by changes. The reader goes there with `Seek(entry, point)`, or to any element with `SeekElement(entry, n)`: to the point before it, then it passes the elements in between. Both work from inside the entry's array (after `Seek(entry)`, its head and its `ArrayStart`), so the reader knows the array and the schema of its objects.

**Schemas**: with `PerStream` an entry may refer to a schema written in an earlier entry, with `External` to a schema in the store. The index lists the schemas and where their inline copy is, and `ReadIndexes` registers those in the serializer's `SchemaStore`, so an entry can be read after `Seek`. With `PerObject` every entry has its schemas.

**Hashes and observers**: an `IMsgPackStreamHasher` hashes the bytes of every entry (`MsgPackStreamEntry.Hash`, the algorithm in `MsgPackStreamIndex.HashAlgorithm`). The writer feeds it from its buffer when it flushes and at the end of the entry: no extra flushes, no wrapping stream. Use the names of the [multicodec table](https://github.com/multiformats/multicodec) as `Algorithm` ("sha2-256", "blake3"...): with the code of the name and the hash, anyone can make a multihash. An adapter for `IncrementalHash` or System.IO.Hashing is a few lines:

```csharp
sealed class Sha256Hasher : IMsgPackStreamHasher
{
  private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
  public string Algorithm => "sha2-256";
  public int HashLength => 32;
  public void Append(ReadOnlySpan<byte> data) => _hash.AppendData(data);
  public int GetHashAndReset(Span<byte> destination) => _hash.GetHashAndReset(destination);
}
```

An `IMsgPackStreamObserver` gets the same bytes, and at the end of every entry the entry itself, to add to it (metadata, a content type). It may return bytes to write after the entry (a sidecar: a signature, a thumbnail): they are wrapped in an extension value that readers skip, and the entry gets their `SidecarOffset` and `SidecarLength`. Both are called on the writer's thread and the writer waits for them: keep them fast.

**Checkpoints and interruptions**: with `CheckpointInterval` the writer writes an index (`MsgPackStreamIndexStatus.Checkpoint`) after every so many entries and flushes the stream, and keeps only the entries since then in memory. Each index lists the entries since the one before it and points to it, so `ReadIndexes` returns them all, the first one first. After a crash the stream ends after the last checkpoint: `ReadIndexes(searchBackwards: true)` finds it (this reads a stream without an index completely, from the end). `Dispose` writes the last index: `Complete` between root values; `Interrupted` in the middle of one (an exception left an array open, or a head without its body), with that entry `Incomplete`. Its arrays and maps stay open: a reader that reads the stream value by value fails there, one that seeks reads the entries before it. When writing to the stream failed, `Dispose` writes no index.

**Appending**: `MsgPackStreamWriter.Append(stream, serializer, ...)` continues a stream (that seeks, reads and writes) after its last index. The new index points to the old one, so a reader finds the entries of both parts. It refuses a stream that continues after its last index (a crash: cut it off after that tail first) or whose last index is `Interrupted`. A stream without an index gets an index of the appended entries.

**Readers that don't seek** read the stream value by value as before: `MsgPackStreamReader` skips the index, its tail and the sidecars between the root values (extension types 3 and 4 at depth 0 are reserved for this in a stream). Other MsgPack readers see them as an extension value and an array of four values after each index.

**The layout** (a feature of this library, not of the MsgPack specification; the class `MsgPackStreamIndex` documents the positions of every field):

- The index is an extension value of type 3, whose data is one MsgPack array: version, status, writer, the previous index's tail, hash algorithm, schemas, entries, metadata. Each entry is an array too (offset, length, identifier, content type, modified, hash, element count, points, sidecar, metadata, incomplete), trailing nils left out, so fields can be added at the end. The content is always in the byte order of the specification, whatever `EndianAction` says.
- After it comes the tail, 25 bytes: `94 cf <offset of the index> ce <its length> ce <CRC-32 of the index> a4 "LSIX"`, big-endian. A reader reads the last 25 bytes of the stream and knows where the index is. The CRC-32 tells a tail apart from data that looks like one, and a damaged index from a valid one: a damaged index is not used.
- Offsets count from where the writer started. When the stream is moved (embedded in a file after other data), the reader takes the difference between where the tail is and where the index says it is, and moves every offset by it.
- The reader checks every index it reads before it uses it: offsets inside the stream and before the index, points inside their entry and in order, a version it knows. An index that matches its CRC-32 but isn't valid throws a `MsgPackException`.

## The format

MsgPack stores the length of an array, map, str or bin before its content. The values of unknown length use the escape sequences proposed in [msgpack/msgpack#347](https://github.com/msgpack/msgpack/pull/347), which is not part of the specification yet: the format may still change there. The byte `0xC1`, unused by the specification, is always followed by one code byte:

| Sequence | Meaning |
|---|---|
| `c1 90` | start of an array of unknown length |
| `c1 80` | start of a map of unknown length |
| `c1 a0` | start of a str of unknown length: str chunks follow |
| `c1 c4` | start of a bin of unknown length: bin chunks follow |
| `c1 c0` | end of the innermost one |
| other codes | reserved: an error |

```
[1, "a", [true]] as an array of unknown length:   c1 90  01  a1 61  91 c3  c1 c0
{"rows": [1, 2], "total": 2}:                     82  a4 72 6f 77 73  c1 90 01 02 c1 c0  a5 74 6f 74 61 6c  02
"abc" in two chunks:                              c1 a0  a2 61 62  a1 63  c1 c0
```

Only `MsgPackStreamReader` reads these. The serializers (LsMsgPack, LtMsgPack), the explorers, the TypeScript client and other MsgPack libraries refuse the data at the first `0xC1`. So write values of unknown length only for readers known to support them. Without them, the output of the writer is plain MsgPack that any reader reads value by value.
