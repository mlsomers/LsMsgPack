# Streaming: MsgPackStreamWriter and MsgPackStreamReader

`MsgPackStreamWriter` and `MsgPackStreamReader` (LtMsgPack, namespace `LtMsgPack.Streaming`) write and read MsgPack values one after another on a `Stream`. Use them when the data is too large to buffer, when it's produced while it's written (rows of a database query), or when one stream holds several parts (the result sets of a stored procedure, the tables of a backup).

- **Values**: integers, floats, strings, bins, timestamps, Guids, decimals, extensions, and arrays and maps of a known length.
- **Values of unknown length**: arrays, maps, str and bin that are written before their length is known (see [the format](#the-format)).
- **Objects**: whole objects, written and read by the `LtMsgPackSerializer`, with their schema.
- **Positions**: the writer reports the position after each value, so an index of the values can be kept, or appended to the stream.

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

**Objects.** `ReadObject<T>()` reads the next value as `LtMsgPackSerializer.Deserialize<T>` does, with its schema. Values of unknown length inside it are read too: the reader replaces them by ones of known length before the serializer reads them.

**The buffer.** The reader reads the stream in blocks into a buffer, so the stream doesn't have to seek (network streams work). Values that are read whole are buffered whole: objects, `GetValue`, `GetString`... The buffer only grows as far as the data that arrives, so a header that claims more than the stream holds costs no more memory than the stream holds.

**Errors.** Data that isn't valid throws a `MsgPackException` with its position:
- a reserved escape code;
- an escape byte at the end of the data;
- an end sequence where no value of unknown length can end (outside of one, directly inside an array or map of known length, where the value of a key comes);
- a chunk of a str or bin that isn't a str or bin;
- nesting deeper than `MaxDepth`;
- data that ends inside a value.

## Objects and their schema

With the indexed schema (`UseInexedSchema`, the default) an object is written as its schema, then the object. That's two MsgPack values, so `WriteObject` and `ReadObject` only take root values of the stream (`Depth` 0). Without the indexed schema an object is one value: it can also be an element of an array, e.g. of an array of unknown length:

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
