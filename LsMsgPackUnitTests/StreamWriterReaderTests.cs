using LsMsgPack;
using LsMsgPack.Streaming;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// MsgPackStreamWriter and MsgPackStreamReader: values one after another, arrays, maps, str and bin of unknown length (msgpack/msgpack#347), objects with their schema.
  /// </summary>
  [TestClass]
  public class StreamWriterReaderTests
  {
    public class SwLine
    {
      public string Product { get; set; }
      public decimal Price { get; set; }
      public int Quantity { get; set; }
    }

    public class SwOrder
    {
      public string Customer { get; set; }
      public DateTime Date { get; set; }
      public List<SwLine> Lines { get; set; } = new List<SwLine>();
    }

    public class SwShape { public string Name { get; set; } }
    public class SwCircle : SwShape { public double Radius { get; set; } }
    public class SwSquare : SwShape { public double Side { get; set; } }

    private static LsMsgPackSerializer Plain()
    {
      return new LsMsgPackSerializer(new LsMsgPackOptions { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map });
    }

    private static LsMsgPackSerializer WithReferences(SchemaStore store)
    {
      return new LsMsgPackSerializer(new LsMsgPackOptions { WriteSchemaReference = true, SchemaStore = store });
    }

    private static byte[] Write(Action<MsgPackStreamWriter> write, LsMsgPackSerializer serializer = null, MsgPackStreamSchemas schemas = MsgPackStreamSchemas.PerObject)
    {
      MemoryStream ms = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(ms, serializer ?? Plain(), schemas, true))
        write(writer);
      return ms.ToArray();
    }

    private static MsgPackStreamReader Reader(byte[] bytes, LsMsgPackSerializer serializer = null)
    {
      return new MsgPackStreamReader(new TrickleStream(bytes), serializer ?? Plain());
    }

    private static byte[] Hex(string hex)
    {
      string[] parts = hex.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
      return parts.Select(p => Convert.ToByte(p, 16)).ToArray();
    }

    private static string Hex(byte[] bytes)
    {
      return string.Join(" ", bytes.Select(b => b.ToString("x2")));
    }

    /// <summary>
    /// The tokens as text, e.g. "ArrayStart(-1) Integer:1 ArrayEnd".
    /// </summary>
    private static string Tokens(byte[] bytes, LsMsgPackSerializer serializer = null)
    {
      StringBuilder text = new StringBuilder();
      using (MsgPackStreamReader reader = Reader(bytes, serializer))
      {
        while (reader.Read())
        {
          if (text.Length > 0)
            text.Append(' ');
          text.Append(reader.TokenType);
          switch (reader.TokenType)
          {
            case MsgPackTokenType.Integer: text.Append(':').Append(reader.GetInt64()); break;
            case MsgPackTokenType.Boolean: text.Append(':').Append(reader.GetBoolean()); break;
            case MsgPackTokenType.String: text.Append(':').Append(reader.GetString()); break;
            case MsgPackTokenType.ArrayStart:
            case MsgPackTokenType.MapStart: text.Append('(').Append(reader.Length).Append(')'); break;
          }
        }
      }
      return text.ToString();
    }

    #region The examples of the proposal

    [TestMethod]
    public void ArrayOfUnknownLengthHoldingAnArrayOfKnownLength()
    {
      byte[] bytes = Write(w =>
      {
        w.WriteArrayStart();
        w.Write(1);
        w.Write("a");
        w.WriteArrayHeader(1);
        w.Write(true);
        w.WriteEnd();
      });
      Assert.AreEqual("c1 90 01 a1 61 91 c3 c1 c0", Hex(bytes));
      Assert.AreEqual("ArrayStart(-1) Integer:1 String:a ArrayStart(1) Boolean:True ArrayEnd ArrayEnd", Tokens(bytes));
    }

    [TestMethod]
    public void MapOfUnknownLength()
    {
      byte[] bytes = Write(w =>
      {
        w.WriteMapStart();
        w.Write("a");
        w.Write(1);
        w.WriteEnd();
      });
      Assert.AreEqual("c1 80 a1 61 01 c1 c0", Hex(bytes));
      Assert.AreEqual("MapStart(-1) String:a Integer:1 MapEnd", Tokens(bytes));
    }

    [TestMethod]
    public void NestedArraysOfUnknownLength()
    {
      byte[] bytes = Write(w =>
      {
        w.WriteArrayStart();
        w.WriteArrayStart();
        w.Write(1);
        w.WriteEnd();
        w.WriteEnd();
      });
      Assert.AreEqual("c1 90 c1 90 01 c1 c0 c1 c0", Hex(bytes));
      Assert.AreEqual("ArrayStart(-1) ArrayStart(-1) Integer:1 ArrayEnd ArrayEnd", Tokens(bytes));
    }

    [TestMethod]
    public void ArrayOfUnknownLengthInAMapOfKnownLength()
    {
      byte[] bytes = Write(w =>
      {
        w.WriteMapHeader(2);
        w.Write("rows");
        w.WriteArrayStart();
        w.Write(1);
        w.Write(2);
        w.WriteEnd();
        w.Write("total");
        w.Write(2);
      });
      Assert.AreEqual("82 a4 72 6f 77 73 c1 90 01 02 c1 c0 a5 74 6f 74 61 6c 02", Hex(bytes));
      Assert.AreEqual("MapStart(2) String:rows ArrayStart(-1) Integer:1 Integer:2 ArrayEnd String:total Integer:2 MapEnd", Tokens(bytes));
    }

    [TestMethod]
    public void StrOfUnknownLengthInTwoChunks()
    {
      byte[] bytes = Write(w =>
      {
        w.WriteStringStart();
        w.Write("ab");
        w.Write("c");
        w.WriteEnd();
      });
      Assert.AreEqual("c1 a0 a2 61 62 a1 63 c1 c0", Hex(bytes));
      Assert.AreEqual("StringStart String:ab String:c StringEnd", Tokens(bytes));

      using (MsgPackStreamReader reader = Reader(bytes))
      {
        Assert.IsTrue(reader.Read());
        Assert.AreEqual("abc", reader.GetString());
        Assert.AreEqual(MsgPackTokenType.StringEnd, reader.TokenType);
        Assert.IsFalse(reader.Read());
      }
    }

    #endregion

    #region Values

    [TestMethod]
    public void BinOfUnknownLength()
    {
      byte[] bytes = Write(w =>
      {
        w.WriteBinaryStart();
        w.WriteBinary(new byte[] { 1, 2 });
        w.WriteBinary(new byte[0]);
        w.WriteBinary(new byte[] { 3 });
        w.WriteEnd();
      });
      Assert.AreEqual("c1 c4 c4 02 01 02 c4 00 c4 01 03 c1 c0", Hex(bytes));
      using (MsgPackStreamReader reader = Reader(bytes))
      {
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(MsgPackTokenType.BinaryStart, reader.TokenType);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, reader.GetBytes());
        Assert.IsFalse(reader.Read());
      }
    }

    [TestMethod]
    public void ChunksKeepSurrogatePairsTogether()
    {
      byte[] bytes = Write(w =>
      {
        w.WriteStringStart();
        w.WriteStringChunk("ab\uD83D".AsSpan());
        w.WriteStringChunk("\uDE00cd".AsSpan());
        w.WriteEnd();
      });
      Assert.AreEqual("StringStart String:ab String:\U0001F600cd StringEnd", Tokens(bytes));
      using (MsgPackStreamReader reader = Reader(bytes))
      {
        reader.Read();
        Assert.AreEqual("ab\U0001F600cd", reader.GetString());
      }
    }

    [TestMethod]
    public void ValuesAsTheSerializerWritesThem()
    {
      DateTime date = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
      Guid guid = Guid.NewGuid();
      LsMsgPackSerializer serializer = Plain();
      byte[] bytes = Write(w =>
      {
        w.WriteArrayHeader(5);
        w.Write(date);
        w.Write(guid);
        w.Write(12.5m);
        w.Write(-3L);
        w.Write(ulong.MaxValue);
      }, serializer);
      byte[] expected = new byte[] { 0x95 }.Concat(serializer.Serialize(date)).Concat(serializer.Serialize(guid)).Concat(serializer.Serialize(12.5m)).Concat(serializer.Serialize(-3L)).Concat(serializer.Serialize(ulong.MaxValue)).ToArray();
      CollectionAssert.AreEqual(expected, bytes);

      using (MsgPackStreamReader reader = Reader(bytes, serializer))
      {
        reader.Read();
        reader.Read();
        Assert.AreEqual(date, reader.GetDateTime());
        reader.Read();
        CollectionAssert.AreEqual(guid.ToByteArray(), reader.GetBytes());
        reader.Read();
        Assert.AreEqual(12.5m, reader.GetValue());
        reader.Read();
        Assert.AreEqual(-3L, reader.GetInt64());
        Assert.ThrowsExactly<OverflowException>(() => reader.GetUInt64());
        reader.Read();
        Assert.AreEqual(ulong.MaxValue, reader.GetUInt64());
        Assert.ThrowsExactly<OverflowException>(() => reader.GetInt64());
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(MsgPackTokenType.ArrayEnd, reader.TokenType);
        Assert.IsFalse(reader.Read());
      }
    }

    [TestMethod]
    public void ValuesWithTypeIdsAlways()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions { UseInexedSchema = false, AddTypeIdOptions = AddTypeIdOption.Always });
      Guid guid = Guid.NewGuid();
      DateTime date = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
      CollectionAssert.AreEqual(serializer.Serialize(guid), Write(w => w.Write(guid), serializer));
      CollectionAssert.AreEqual(serializer.Serialize(12.5m), Write(w => w.Write(12.5m), serializer));
      CollectionAssert.AreEqual(serializer.Serialize(date), Write(w => w.Write(date), serializer));
    }

    [TestMethod]
    public void ValuesOfUnknownLengthReadWhole()
    {
      byte[] bytes = Hex("c1 90 01 c1 80 a1 61 c1 a0 a1 62 a1 63 c1 c0 c1 c0 92 c1 90 c1 c0 c3 c1 c0");
      using (MsgPackStreamReader reader = Reader(bytes))
      {
        Assert.IsTrue(reader.Read());
        object[] value = (object[])reader.GetValue();
        Assert.AreEqual(MsgPackTokenType.ArrayEnd, reader.TokenType);
        Assert.AreEqual(0, reader.Depth);
        Assert.AreEqual(bytes.Length, reader.Position);
        Assert.HasCount(3, value);
        Assert.AreEqual((byte)1, value[0]);
        KeyValuePair<object, object>[] map = (KeyValuePair<object, object>[])value[1];
        Assert.AreEqual("a", map[0].Key);
        Assert.AreEqual("bc", map[0].Value);
        object[] inner = (object[])value[2];
        Assert.HasCount(0, (object[])inner[0]);
        Assert.AreEqual(true, inner[1]);
        Assert.IsFalse(reader.Read());
      }
    }

    [TestMethod]
    public void KnownLengthHoldingUnknownLengthReadWhole()
    {
      using (MsgPackStreamReader reader = Reader(Hex("92 c1 90 01 c1 c0 02 c3")))
      {
        reader.Read();
        object[] value = (object[])reader.GetValue();
        Assert.AreEqual((byte)1, ((object[])value[0])[0]);
        Assert.AreEqual((byte)2, value[1]);
        Assert.AreEqual(MsgPackTokenType.ArrayEnd, reader.TokenType);
        reader.Read();
        Assert.IsTrue(reader.GetBoolean());
      }
    }

    [TestMethod]
    public void OtherByteOrder()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, EndianAction = EndianAction.NeverSwap });
      string text = new string('x', 300);
      byte[] bytes = Write(w =>
      {
        w.WriteArrayStart();
        w.WriteArrayHeader(300);
        for (int t = 0; t < 300; t++)
          w.Write(t);
        w.WriteStringStart();
        w.Write(text);
        w.WriteEnd();
        w.WriteEnd();
      }, serializer);

      using (MsgPackStreamReader reader = Reader(bytes, serializer))
      {
        reader.Read();
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(300, reader.Length);
        for (int t = 0; t < 300; t++)
        {
          reader.Read();
          Assert.AreEqual(t, reader.GetInt64());
        }
        reader.Read();
        Assert.AreEqual(MsgPackTokenType.ArrayEnd, reader.TokenType);
        reader.Read();
        Assert.AreEqual(text, reader.GetString());
      }

      using (MsgPackStreamReader reader = Reader(bytes, serializer)) // normalized: the headers in the same byte order
      {
        reader.Read();
        object[] value = (object[])reader.GetValue();
        Assert.HasCount(300, (object[])value[0]);
        Assert.AreEqual(text, value[1]);
      }
    }

    [TestMethod]
    public void SkipPassesValuesOfUnknownLength()
    {
      byte[] bytes = Write(w =>
      {
        w.WriteArrayStart();
        w.WriteMapStart();
        w.Write("k");
        w.WriteBinary(new byte[100000]);
        w.WriteEnd();
        w.Write(7);
        w.WriteEnd();
        w.Write(8);
      });
      using (MsgPackStreamReader reader = Reader(bytes))
      {
        reader.Read();
        reader.Read();
        Assert.AreEqual(MsgPackTokenType.MapStart, reader.TokenType);
        reader.Skip();
        Assert.AreEqual(MsgPackTokenType.MapEnd, reader.TokenType);
        reader.Read();
        Assert.AreEqual(7, reader.GetInt64());
        reader.Read();
        Assert.AreEqual(MsgPackTokenType.ArrayEnd, reader.TokenType);
        reader.Read();
        Assert.AreEqual(8, reader.GetInt64());
        Assert.IsTrue(reader.AtEnd);
        Assert.IsFalse(reader.Read());
      }
    }

    [TestMethod]
    public void MapKeys()
    {
      byte[] bytes = Hex("c1 80 a1 61 92 01 02 a1 62 03 c1 c0");
      List<bool> keys = new List<bool>();
      using (MsgPackStreamReader reader = Reader(bytes))
      {
        while (reader.Read())
          keys.Add(reader.IsKey);
      }
      CollectionAssert.AreEqual(new[] { false, true, false, false, false, false, true, false, false }, keys); // start, "a", [ 1 2 ] end, "b", 3, end
    }

    #endregion

    #region Positions and root values

    [TestMethod]
    public void PositionsOfTheRootValues()
    {
      List<long> positions = new List<long>();
      byte[] bytes = Write(w =>
      {
        for (int table = 0; table < 3; table++)
        {
          w.WriteArrayStart();
          for (int row = 0; row < 1000 * table; row++)
          {
            w.WriteArrayHeader(2);
            w.Write(row);
            w.Write("row " + row);
          }
          w.WriteEnd();
          Assert.AreEqual(0, w.Depth);
          positions.Add(w.Position);
        }
      });
      Assert.AreEqual(bytes.Length, positions[2]);

      using (MsgPackStreamReader reader = Reader(bytes))
      {
        for (int table = 0; table < 3; table++)
        {
          reader.Read();
          Assert.AreEqual(table == 0 ? 0 : positions[table - 1], reader.TokenStart);
          int rows = 0;
          while (!reader.AtEnd)
          {
            reader.Read();
            reader.Skip();
            rows++;
          }
          reader.Read();
          Assert.AreEqual(MsgPackTokenType.ArrayEnd, reader.TokenType);
          Assert.AreEqual(1000 * table, rows);
          Assert.AreEqual(positions[table], reader.Position);
        }
        Assert.IsTrue(reader.AtEnd);
      }
    }

    [TestMethod]
    public void FlushThreshold()
    {
      MemoryStream ms = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(ms, Plain(), MsgPackStreamSchemas.PerObject, true))
      {
        writer.WriteArrayStart();
        writer.Write("start");
        Assert.AreEqual(0, ms.Length);
        while (writer.Position < 70000)
          writer.Write("some text");
        Assert.AreNotEqual(0L, ms.Length);

        writer.FlushThreshold = 0;
        writer.Write(1);
        Assert.AreEqual(writer.Position, ms.Length);
        writer.WriteEnd();
        Assert.AreEqual(writer.Position, ms.Length);
      }
      Assert.IsTrue(ms.CanRead, "leaveOpen");
    }

    #endregion

    #region Objects

    private static SwOrder Order(int n)
    {
      SwOrder order = new SwOrder { Customer = "Customer " + n, Date = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc).AddSeconds(n) };
      for (int t = 0; t < n; t++)
        order.Lines.Add(new SwLine { Product = "P" + t, Price = 1.25m * t, Quantity = t });
      return order;
    }

    private static void AreEqual(SwOrder expected, SwOrder actual)
    {
      Assert.AreEqual(expected.Customer, actual.Customer);
      Assert.AreEqual(expected.Date, actual.Date.ToUniversalTime());
      Assert.HasCount(expected.Lines.Count, actual.Lines);
      for (int t = 0; t < expected.Lines.Count; t++)
      {
        Assert.AreEqual(expected.Lines[t].Product, actual.Lines[t].Product);
        Assert.AreEqual(expected.Lines[t].Price, actual.Lines[t].Price);
        Assert.AreEqual(expected.Lines[t].Quantity, actual.Lines[t].Quantity);
      }
    }

    [TestMethod]
    public void ObjectsWithTheirSchema()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions());
      List<long> positions = new List<long> { 0 };
      byte[] bytes = Write(w =>
      {
        for (int n = 1; n <= 3; n++)
          positions.Add(w.WriteObject(Order(n)));
        positions.Add(w.WriteObject<SwOrder>(null));
      }, serializer);

      for (int n = 1; n <= 3; n++) // every object can be read on its own: an index of the positions finds them
      {
        int start = (int)positions[n - 1];
        CollectionAssert.AreEqual(serializer.Serialize(Order(n)), bytes.Skip(start).Take((int)positions[n] - start).ToArray());
        AreEqual(Order(n), serializer.Deserialize<SwOrder>(bytes, start, (int)positions[n] - start));
      }

      using (MsgPackStreamReader reader = Reader(bytes, serializer))
      {
        for (int n = 1; n <= 3; n++)
        {
          AreEqual(Order(n), reader.ReadObject<SwOrder>());
          Assert.AreEqual(positions[n], reader.Position);
        }
        Assert.IsNull(reader.ReadObject<SwOrder>());
        Assert.IsTrue(reader.AtEnd);
        Assert.ThrowsExactly<MsgPackException>(() => reader.ReadObject<SwOrder>());
      }
    }

    [TestMethod]
    public void ObjectsLongerThanTheBuffer()
    {
      foreach (LsMsgPackSerializer serializer in new[] { new LsMsgPackSerializer(new LsMsgPackOptions()), Plain() })
      {
        int[] sizes = { 3, 2000, 10, 5000, 1 }; // the buffer starts with 16 KiB: compacted and grown while an object is read whole
        byte[] bytes = Write(w =>
        {
          foreach (int size in sizes)
            w.WriteObject(Order(size));
        }, serializer);

        using (MsgPackStreamReader reader = Reader(bytes, serializer))
        {
          foreach (int size in sizes)
            AreEqual(Order(size), reader.ReadObject<SwOrder>());
          Assert.IsTrue(reader.AtEnd);
        }
      }
    }

    [TestMethod]
    public void PerObjectWithASerializerThatRefersToSchemas()
    {
      LsMsgPackSerializer serializer = WithReferences(new SchemaStore());
      byte[] bytes = Write(w =>
      {
        w.WriteObject(Order(1));
        w.WriteObject(Order(2));
      }, serializer);

      using (MsgPackStreamReader reader = Reader(bytes, new LsMsgPackSerializer(new LsMsgPackOptions()))) // no store: every schema is inline
      {
        AreEqual(Order(1), reader.ReadObject<SwOrder>());
        AreEqual(Order(2), reader.ReadObject<SwOrder>());
        Assert.IsTrue(reader.AtEnd);
      }
    }

    [TestMethod]
    public void PerStreamWritesASchemaOnce()
    {
      SchemaStore store = new SchemaStore();
      LsMsgPackSerializer serializer = WithReferences(store);
      List<long> positions = new List<long> { 0 };
      MsgPackStreamWriter used = null;
      byte[] bytes = Write(w =>
      {
        used = w;
        for (int n = 1; n <= 3; n++)
          positions.Add(w.WriteObject(Order(n)));
      }, serializer, MsgPackStreamSchemas.PerStream);

      Assert.IsTrue(SchemaBytes.IsMap(bytes[0]), "the first object has its schema");
      for (int n = 2; n <= 3; n++)
      {
        int start = (int)positions[n - 1];
        Assert.AreEqual(0xD8, bytes[start], "a reference");
        Assert.AreEqual(serializer.Serialize(Order(n)).Length, (int)(positions[n] - positions[n - 1])); // as the serializer writes it with the reference
      }
      Assert.HasCount(1, used.ReferencedSchemas);

      using (MsgPackStreamReader reader = Reader(bytes, new LsMsgPackSerializer(new LsMsgPackOptions { SchemaStore = new SchemaStore() }))) // learns the schema from the stream
      {
        for (int n = 1; n <= 3; n++)
          AreEqual(Order(n), reader.ReadObject<SwOrder>());
        Assert.IsTrue(reader.AtEnd);
      }

      using (MsgPackStreamReader reader = Reader(bytes, new LsMsgPackSerializer(new LsMsgPackOptions())))
      {
        AreEqual(Order(1), reader.ReadObject<SwOrder>());
        Assert.ThrowsExactly<MsgPackException>(() => reader.ReadObject<SwOrder>()); // a reference needs a store
      }
    }

    [TestMethod]
    public void PerStreamWritesAGrownSchemaAgain()
    {
      LsMsgPackSerializer serializer = WithReferences(new SchemaStore());
      List<long> positions = new List<long> { 0 };
      MsgPackStreamWriter used = null;
      SwShape[] shapes = { new SwCircle { Name = "c", Radius = 1 }, new SwCircle { Name = "d", Radius = 2 }, new SwSquare { Name = "s", Side = 3 }, new SwSquare { Name = "t", Side = 4 } };
      byte[] bytes = Write(w =>
      {
        used = w;
        for (int t = 0; t < shapes.Length; t++)
          positions.Add(w.WriteObject<SwShape>(shapes[t]));
      }, serializer, MsgPackStreamSchemas.PerStream);

      bool[] inline = positions.Take(shapes.Length).Select(p => SchemaBytes.IsMap(bytes[p])).ToArray();
      CollectionAssert.AreEqual(new[] { true, false, true, false }, inline);
      Assert.HasCount(2, used.ReferencedSchemas);

      using (MsgPackStreamReader reader = Reader(bytes, new LsMsgPackSerializer(new LsMsgPackOptions { SchemaStore = new SchemaStore() })))
      {
        for (int t = 0; t < shapes.Length; t++)
        {
          SwShape shape = reader.ReadObject<SwShape>();
          Assert.AreEqual(shapes[t].GetType(), shape.GetType());
          Assert.AreEqual(shapes[t].Name, shape.Name);
        }
      }
    }

    [TestMethod]
    public void ExternalSchemas()
    {
      SchemaStore store = new SchemaStore();
      LsMsgPackSerializer serializer = WithReferences(store);
      MsgPackStreamWriter used = null;
      List<long> positions = new List<long> { 0 };
      byte[] bytes = Write(w =>
      {
        used = w;
        positions.Add(w.WriteObject(Order(1)));
        positions.Add(w.WriteObject(Order(2)));
      }, serializer, MsgPackStreamSchemas.External);

      Assert.AreEqual(0xD8, bytes[0]);
      Assert.AreEqual(0xD8, bytes[positions[1]]);
      SchemaId id = used.ReferencedSchemas.Single();

      SchemaStore readers = new SchemaStore();
      readers.Register(store.GetSchema(id)); // kept apart from the stream
      LsMsgPackSerializer reading = new LsMsgPackSerializer(new LsMsgPackOptions { SchemaStore = readers });
      AreEqual(Order(2), reading.Deserialize<SwOrder>(bytes, (int)positions[1], (int)(positions[2] - positions[1])));
      using (MsgPackStreamReader reader = Reader(bytes, reading))
      {
        AreEqual(Order(1), reader.ReadObject<SwOrder>());
        AreEqual(Order(2), reader.ReadObject<SwOrder>());
      }
    }

    [TestMethod]
    public void ObjectsInAnArrayOfUnknownLength()
    {
      LsMsgPackSerializer serializer = Plain();
      byte[] bytes = Write(w =>
      {
        w.WriteArrayStart();
        for (int n = 1; n <= 3; n++)
          w.WriteObject(Order(n));
        w.WriteEnd();
      }, serializer);

      using (MsgPackStreamReader reader = Reader(bytes, serializer))
      {
        reader.Read();
        int n = 0;
        while (!reader.AtEnd)
          AreEqual(Order(++n), reader.ReadObject<SwOrder>());
        Assert.AreEqual(3, n);
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.ReadObject<SwOrder>());
        reader.Read();
        Assert.AreEqual(MsgPackTokenType.ArrayEnd, reader.TokenType);
      }

      using (MsgPackStreamReader reader = Reader(bytes, serializer)) // the whole array, normalized for the serializer
        Assert.HasCount(3, reader.ReadObject<List<SwOrder>>());
    }

    /// <summary>
    /// ReadObject of a value that is all in the buffer finds its end from the headers alone: the reader is then where reading it token by token leaves it
    /// (a MemoryStream buffers the values, the TrickleStream gives 1-3 bytes per read). The counts of arrays and maps of known length, keys, a head's body, the next root value.
    /// </summary>
    [TestMethod]
    public void ObjectsFromTheBuffer_SameStateAsTokenByToken()
    {
      foreach (LsMsgPackSerializer serializer in new[] { new LsMsgPackSerializer(), Plain() })
      {
        byte[] bytes = Write(w =>
        {
          w.WriteHead<List<SwOrder>>();
          w.WriteArrayHeader(3);
          for (int n = 1; n <= 3; n++)
            w.WriteObject(Order(n));
          w.WriteHead<List<SwOrder>>();
          w.WriteArrayStart();
          w.WriteObject(Order(4));
          w.WriteEnd();
          w.WriteObject(Order(5)); // a root value (with the indexed schema: its schema, then the value)
          w.WriteObject(Order(6));
        }, serializer);

        string expected = Trace(new MsgPackStreamReader(new TrickleStream(bytes), serializer), serializer);
        Assert.AreEqual(expected, Trace(new MsgPackStreamReader(new MemoryStream(bytes), serializer), serializer));
        StringAssert.Contains(expected, "Customer 6");
      }

      LsMsgPackSerializer plain = Plain(); // without the indexed schema objects are read anywhere: keys and values of a map
      byte[] map = Write(w =>
      {
        w.WriteMapHeader(2);
        w.Write("a");
        w.WriteObject(Order(1));
        w.WriteObject(Order(2)); // a key
        w.Write(3);
        w.WriteMapStart();
        w.WriteObject(Order(3));
        w.WriteObject(Order(4));
        w.WriteEnd();
      }, plain);
      Assert.AreEqual(MapTrace(new MsgPackStreamReader(new TrickleStream(map), plain)), MapTrace(new MsgPackStreamReader(new MemoryStream(map), plain)));
    }

    /// <summary>
    /// GetValue at the start of an array or map of known length that is all in the buffer: the reader is then at its end, as reading it token by token leaves it (also the end of a head's body).
    /// </summary>
    [TestMethod]
    public void ValuesFromTheBuffer_SameStateAsTokenByToken()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer();
      byte[] bytes = Write(w =>
      {
        w.WriteArrayHeader(3);
        w.Write(1);
        w.WriteMapHeader(1);
        w.Write("k");
        w.WriteArrayHeader(0);
        w.WriteArrayHeader(2);
        w.Write("a");
        w.Write(2);
        w.WriteHead<List<SwOrder>>();
        w.WriteArrayHeader(2);
        w.WriteObject(Order(1));
        w.WriteObject(Order(2));
        w.WriteObject(Order(3));
      }, serializer);

      Func<MsgPackStreamReader, string> trace = reader =>
      {
        StringBuilder text = new StringBuilder();
        using (reader)
        {
          reader.Read(); // the outer array
          reader.Read(); // 1
          reader.Read(); // the map
          object value = reader.GetValue();
          text.Append(((KeyValuePair<object, object>[])value).Length).Append(' ').Append(reader.TokenType).Append(' ').Append(reader.Depth).Append(' ').Append(reader.Position).Append(' ').Append(reader.AtEnd).Append('|');
          reader.Read();
          value = reader.GetValue();
          text.Append(string.Join(",", (object[])value)).Append(' ').Append(reader.TokenType).Append(' ').Append(reader.Depth).Append(' ').Append(reader.Position).Append(' ').Append(reader.AtEnd).Append('|');
          reader.Read();
          text.Append(reader.TokenType).Append(' ').Append(reader.Depth).Append('|');
          reader.ReadHead<List<SwOrder>>();
          reader.Read();
          value = reader.GetValue(); // the whole body
          text.Append(((object[])value).Length).Append(' ').Append(reader.TokenType).Append(' ').Append(reader.Depth).Append(' ').Append(reader.Position).Append('|');
          text.Append(reader.ReadObject<SwOrder>().Customer).Append(' ').Append(reader.Read());
        }
        return text.ToString();
      };
      string expected = trace(new MsgPackStreamReader(new TrickleStream(bytes), serializer));
      Assert.AreEqual(expected, trace(new MsgPackStreamReader(new MemoryStream(bytes), serializer)));
      StringAssert.Contains(expected, "Customer 3 False");
    }

    private static string Trace(MsgPackStreamReader reader, LsMsgPackSerializer serializer)
    {
      StringBuilder text = new StringBuilder();
      using (reader)
      {
        for (int table = 0; table < 2; table++)
        {
          reader.ReadHead<List<SwOrder>>();
          reader.Read();
          while (!reader.AtEnd)
            State(text, reader, reader.ReadObject<SwOrder>());
          reader.Read();
          text.Append(reader.TokenType).Append(' ').Append(reader.Depth).Append('|');
        }
        State(text, reader, reader.ReadObject<SwOrder>());
        State(text, reader, reader.ReadObject<SwOrder>());
        text.Append(reader.Read());
      }
      return text.ToString();
    }

    private static string MapTrace(MsgPackStreamReader reader)
    {
      StringBuilder text = new StringBuilder();
      using (reader)
      {
        reader.Read();
        State(text, reader, reader.ReadObject<string>());
        State(text, reader, reader.ReadObject<SwOrder>());
        State(text, reader, reader.ReadObject<SwOrder>());
        State(text, reader, reader.ReadObject<int>());
        reader.Read();
        text.Append(reader.TokenType).Append(' ').Append(reader.Depth).Append('|');
        reader.Read();
        text.Append(reader.TokenType).Append(' ').Append(reader.IsKey).Append('|');
        State(text, reader, reader.ReadObject<SwOrder>());
        Assert.IsFalse(reader.AtEnd);
        State(text, reader, reader.ReadObject<SwOrder>());
        Assert.IsTrue(reader.AtEnd);
        reader.Read();
        text.Append(reader.TokenType).Append(' ').Append(reader.Depth).Append('|');
        reader.Read();
        text.Append(reader.TokenType).Append(' ').Append(reader.Depth).Append('|').Append(reader.Read());
      }
      return text.ToString();
    }

    private static void State(StringBuilder text, MsgPackStreamReader reader, object value)
    {
      string shown = value is SwOrder order ? order.Customer + " " + order.Lines.Count : Convert.ToString(value);
      text.Append(shown).Append(" depth ").Append(reader.Depth).Append(" at ").Append(reader.Position).Append(" end ").Append(reader.Depth > 0 && reader.AtEnd).Append('|');
    }

    [TestMethod]
    public void ObjectWrittenWithValuesOfUnknownLength()
    {
      foreach (EndianAction endian in new[] { EndianAction.SwapIfCurrentSystemIsLittleEndian, EndianAction.NeverSwap })
      {
        LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, EndianAction = endian });
        SwOrder expected = Order(20);
        expected.Customer = new string('c', 100);
        byte[] bytes = Write(w =>
        {
          w.WriteMapStart();
          w.Write("Customer");
          w.WriteStringStart();
          w.Write(new string('c', 40));
          w.Write(new string('c', 60));
          w.WriteEnd();
          w.Write("Date");
          w.Write(expected.Date);
          w.Write("Lines");
          w.WriteArrayStart();
          foreach (SwLine line in expected.Lines)
          {
            w.WriteMapStart();
            w.Write("Product");
            w.Write(line.Product);
            w.Write("Price");
            w.Write(line.Price);
            w.Write("Quantity");
            w.Write(line.Quantity);
            w.WriteEnd();
          }
          w.WriteEnd();
          w.WriteEnd();
        }, serializer);

        using (MsgPackStreamReader reader = Reader(bytes, serializer))
          AreEqual(expected, reader.ReadObject<SwOrder>());
      }
    }

    [TestMethod]
    public void ObjectsOnlyAtTheRootWithTheIndexedSchema()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions());
      MemoryStream ms = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(ms, serializer, MsgPackStreamSchemas.PerObject, true))
      {
        writer.WriteArrayStart();
        Assert.ThrowsExactly<InvalidOperationException>(() => writer.WriteObject(Order(1))); // without a head
        writer.WriteEnd();
      }

      using (MsgPackStreamReader reader = Reader(ms.ToArray(), serializer))
      {
        reader.Read();
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.ReadObject<SwOrder>());
      }
    }

    [TestMethod]
    public void ModesThatReferNeedReferences()
    {
      Assert.ThrowsExactly<ArgumentException>(() => new MsgPackStreamWriter(new MemoryStream(), new LsMsgPackSerializer(new LsMsgPackOptions()), MsgPackStreamSchemas.PerStream));
      Assert.ThrowsExactly<ArgumentException>(() => new MsgPackStreamWriter(new MemoryStream(), Plain(), MsgPackStreamSchemas.External));
    }

    #endregion

    #region Heads: one schema for a whole root value

    public class SwReport
    {
      public string Title { get; set; }
      public List<SwOrder> Orders { get; set; } = new List<SwOrder>();
      public int Total { get; set; }
    }

    public class SwBag
    {
      public string Name { get; set; }
      public object Content { get; set; }
    }

    private static LsMsgPackSerializer Complete(SchemaStore store, SchemaVersion version = SchemaVersion.Names)
    {
      return new LsMsgPackSerializer(new LsMsgPackOptions { WriteSchemaReference = true, SchemaStore = store, CompleteSchemas = true, SchemaVersion = version });
    }

    private static List<SwOrder> Orders(int count)
    {
      List<SwOrder> orders = new List<SwOrder>();
      for (int n = 1; n <= count; n++)
        orders.Add(Order(n));
      return orders;
    }

    /// <summary>
    /// A head and an array of known length: the payload of Serialize (with the complete schema), the schema inline or a reference.
    /// </summary>
    [TestMethod]
    public void HeadAndArrayOfKnownLengthAreTheSerializedList()
    {
      List<SwOrder> orders = Orders(3);
      foreach (MsgPackStreamSchemas mode in new[] { MsgPackStreamSchemas.PerObject, MsgPackStreamSchemas.PerStream, MsgPackStreamSchemas.External })
      {
        SchemaStore store = new SchemaStore();
        LsMsgPackSerializer serializer = Complete(store);
        byte[] bytes = Write(w =>
        {
          w.WriteHead<List<SwOrder>>();
          w.WriteArrayHeader(orders.Count);
          foreach (SwOrder order in orders)
            w.WriteObject(order);
          Assert.AreEqual(0, w.Depth);
        }, serializer, mode);

        byte[] expected = mode == MsgPackStreamSchemas.External
          ? serializer.Serialize(orders)
          : Write(w => w.WriteObject(orders), serializer, mode); // the schema inline in place of the reference
        Assert.AreEqual(Hex(expected), Hex(bytes), mode.ToString());

        LsMsgPackSerializer reading = new LsMsgPackSerializer(new LsMsgPackOptions { SchemaStore = store });
        List<SwOrder> read = reading.Deserialize<List<SwOrder>>(bytes);
        for (int t = 0; t < orders.Count; t++)
          AreEqual(orders[t], read[t]);
      }
    }

    [TestMethod]
    public void HeadWithoutAStoreWritesTheCompleteSchemaInline()
    {
      List<SwOrder> orders = Orders(2);
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions()); // no store, no complete schemas: the head makes them
      byte[] bytes = Write(w =>
      {
        w.WriteHead<List<SwOrder>>();
        w.WriteArrayHeader(orders.Count);
        foreach (SwOrder order in orders)
          w.WriteObject(order);
      }, serializer);

      Assert.AreEqual(Hex(Write(w => w.WriteObject(orders), Complete(new SchemaStore()))), Hex(bytes));
      List<SwOrder> read = serializer.Deserialize<List<SwOrder>>(bytes);
      AreEqual(orders[1], read[1]);
    }

    [TestMethod]
    public void HeadAndArrayOfUnknownLength()
    {
      List<SwOrder> orders = Orders(4);
      foreach (MsgPackStreamSchemas mode in new[] { MsgPackStreamSchemas.PerObject, MsgPackStreamSchemas.PerStream, MsgPackStreamSchemas.External })
      {
        SchemaStore store = new SchemaStore();
        byte[] bytes = Write(w =>
        {
          w.WriteHead<List<SwOrder>>();
          w.WriteArrayStart();
          foreach (SwOrder order in orders)
            w.WriteObject(order);
          w.WriteObject<SwOrder>(null);
          w.WriteEnd();
        }, Complete(store), mode);

        LsMsgPackSerializer reading = new LsMsgPackSerializer(new LsMsgPackOptions { SchemaStore = mode == MsgPackStreamSchemas.External ? store : new SchemaStore() });
        using (MsgPackStreamReader reader = Reader(bytes, reading)) // row by row
        {
          reader.ReadHead<List<SwOrder>>();
          Assert.IsTrue(reader.Read());
          Assert.AreEqual(MsgPackTokenType.ArrayStart, reader.TokenType);
          Assert.AreEqual(-1, reader.Length);
          int n = 0;
          while (!reader.AtEnd)
          {
            SwOrder order = reader.ReadObject<SwOrder>();
            if (n < orders.Count)
              AreEqual(orders[n], order);
            else
              Assert.IsNull(order);
            n++;
          }
          Assert.AreEqual(orders.Count + 1, n);
          Assert.IsTrue(reader.Read());
          Assert.AreEqual(MsgPackTokenType.ArrayEnd, reader.TokenType);
          Assert.IsFalse(reader.Read());
        }

        using (MsgPackStreamReader reader = Reader(bytes, reading)) // whole
        {
          List<SwOrder> read = reader.ReadObject<List<SwOrder>>();
          Assert.HasCount(orders.Count + 1, read);
          AreEqual(orders[3], read[3]);
          Assert.IsNull(read[4]);
        }
      }
    }

    [TestMethod]
    public void HeadForAMapOfTables()
    {
      Dictionary<string, List<SwOrder>> tables = new Dictionary<string, List<SwOrder>> { { "first", Orders(2) }, { "second", Orders(3) } };
      SchemaStore store = new SchemaStore();
      LsMsgPackSerializer serializer = Complete(store);
      byte[] known = Write(w =>
      {
        w.WriteHead<Dictionary<string, List<SwOrder>>>();
        w.WriteMapHeader(tables.Count);
        foreach (KeyValuePair<string, List<SwOrder>> table in tables)
        {
          w.Write(table.Key);
          w.WriteArrayHeader(table.Value.Count);
          foreach (SwOrder order in table.Value)
            w.WriteObject(order);
        }
      }, serializer, MsgPackStreamSchemas.External);
      Assert.AreEqual(Hex(serializer.Serialize(tables)), Hex(known));

      byte[] unknown = Write(w =>
      {
        w.WriteHead<Dictionary<string, List<SwOrder>>>();
        w.WriteMapStart();
        foreach (KeyValuePair<string, List<SwOrder>> table in tables)
        {
          w.Write(table.Key);
          w.WriteArrayStart();
          foreach (SwOrder order in table.Value)
            w.WriteObject(order);
          w.WriteEnd();
        }
        w.WriteEnd();
      }, serializer);

      using (MsgPackStreamReader reader = Reader(unknown, new LsMsgPackSerializer(new LsMsgPackOptions())))
      {
        reader.ReadHead<Dictionary<string, List<SwOrder>>>();
        reader.Read(); // MapStart
        while (!reader.AtEnd)
        {
          reader.Read();
          string name = reader.GetString();
          reader.Read(); // ArrayStart
          int n = 0;
          while (!reader.AtEnd)
            AreEqual(tables[name][n++], reader.ReadObject<SwOrder>());
          Assert.HasCount(n, tables[name]);
          reader.Read(); // ArrayEnd
        }
        reader.Read();
        Assert.AreEqual(MsgPackTokenType.MapEnd, reader.TokenType);
        Assert.AreEqual(0, reader.Depth);
      }
    }

    [TestMethod]
    public void HeadForAnObjectWrittenByHand()
    {
      SwReport report = new SwReport { Title = "October", Orders = Orders(3), Total = 3 };
      SchemaStore store = new SchemaStore();
      LsMsgPackSerializer serializer = Complete(store);
      byte[] bytes = Write(w =>
      {
        w.WriteHead<SwReport>();
        w.WriteArrayHeader(3); // the array layout: the values in the order of the properties
        w.Write(report.Title);
        w.WriteArrayHeader(report.Orders.Count);
        foreach (SwOrder order in report.Orders)
          w.WriteObject(order);
        w.Write(report.Total);
      }, serializer, MsgPackStreamSchemas.External);
      Assert.AreEqual(Hex(serializer.Serialize(report)), Hex(bytes));

      using (MsgPackStreamReader reader = Reader(bytes, new LsMsgPackSerializer(new LsMsgPackOptions { SchemaStore = store })))
      {
        reader.ReadHead<SwReport>();
        reader.Read(); // ArrayStart
        reader.Read();
        Assert.AreEqual("October", reader.GetString());
        reader.Read(); // ArrayStart of the orders
        int n = 0;
        while (!reader.AtEnd)
          AreEqual(report.Orders[n++], reader.ReadObject<SwOrder>());
        reader.Read(); // ArrayEnd
        reader.Read();
        Assert.AreEqual(3L, reader.GetInt64());
        reader.Read();
        Assert.AreEqual(MsgPackTokenType.ArrayEnd, reader.TokenType);
      }
    }

    [TestMethod]
    public void HeadWithDerivedClasses()
    {
      List<SwShape> shapes = new List<SwShape> { new SwCircle { Name = "c", Radius = 1 }, new SwSquare { Name = "s", Side = 2 }, new SwShape { Name = "plain" }, new SwCircle { Name = "d", Radius = 3 } };
      SchemaStore store = new SchemaStore();
      LsMsgPackSerializer serializer = Complete(store);
      byte[] bytes = Write(w =>
      {
        w.WriteHead<List<SwShape>>();
        w.WriteArrayHeader(shapes.Count);
        foreach (SwShape shape in shapes)
          w.WriteObject(shape); // declared as SwShape: type ids for the derived ones
      }, serializer, MsgPackStreamSchemas.External);
      Assert.AreEqual(Hex(serializer.Serialize(shapes)), Hex(bytes));

      using (MsgPackStreamReader reader = Reader(bytes, new LsMsgPackSerializer(new LsMsgPackOptions { SchemaStore = store })))
      {
        reader.ReadHead<List<SwShape>>();
        reader.Read();
        for (int t = 0; t < shapes.Count; t++)
        {
          SwShape shape = reader.ReadObject<SwShape>();
          Assert.AreEqual(shapes[t].GetType(), shape.GetType());
          Assert.AreEqual(shapes[t].Name, shape.Name);
        }
        Assert.IsTrue(reader.AtEnd);
      }
    }

    [TestMethod]
    public void HeadWithTheDescribedSchema()
    {
      List<SwOrder> orders = Orders(3);
      LsMsgPackSerializer serializer = Complete(new SchemaStore(), SchemaVersion.Described);
      byte[] known = Write(w =>
      {
        w.WriteHead<List<SwOrder>>();
        w.WriteArrayHeader(orders.Count);
        foreach (SwOrder order in orders)
          w.WriteObject(order);
      }, serializer);
      Assert.AreEqual(Hex(Write(w => w.WriteObject(orders), serializer)), Hex(known));

      byte[] unknown = Write(w =>
      {
        w.WriteHead<List<SwOrder>>();
        w.WriteArrayStart();
        foreach (SwOrder order in orders)
          w.WriteObject(order);
        w.WriteEnd();
      }, serializer);
      using (MsgPackStreamReader reader = Reader(unknown, new LsMsgPackSerializer(new LsMsgPackOptions())))
      {
        reader.ReadHead<List<SwOrder>>();
        reader.Read();
        int n = 0;
        while (!reader.AtEnd)
          AreEqual(orders[n++], reader.ReadObject<SwOrder>());
        Assert.AreEqual(orders.Count, n);
      }
    }

    [TestMethod]
    public void PerStreamWritesTheSchemaOfAHeadOnce()
    {
      LsMsgPackSerializer serializer = WithReferences(new SchemaStore());
      List<long> positions = new List<long> { 0 };
      MsgPackStreamWriter used = null;
      byte[] bytes = Write(w =>
      {
        used = w;
        for (int part = 0; part < 2; part++)
        {
          w.WriteHead<List<SwOrder>>();
          w.WriteArrayStart();
          for (int n = 1; n <= 3; n++)
            w.WriteObject(Order(n));
          w.WriteEnd();
          positions.Add(w.Position);
        }
        w.WriteObject(Order(5)); // a whole payload again, after the bodies
      }, serializer, MsgPackStreamSchemas.PerStream);

      Assert.IsTrue(SchemaBytes.IsMap(bytes[0]), "the first head has the schema");
      Assert.AreEqual(0xD8, bytes[positions[1]], "the second head refers to it");
      Assert.HasCount(1, used.ReferencedSchemas); // the schema of List<SwOrder> has the same entries as the one of SwOrder (version 1 does not name the root)
      Assert.AreEqual(0xD8, bytes[positions[2]], "the whole object refers to it too");

      using (MsgPackStreamReader reader = Reader(bytes, new LsMsgPackSerializer(new LsMsgPackOptions { SchemaStore = new SchemaStore() })))
      {
        for (int part = 0; part < 2; part++)
        {
          reader.ReadHead<List<SwOrder>>();
          reader.Read();
          int n = 0;
          while (!reader.AtEnd)
            AreEqual(Order(++n), reader.ReadObject<SwOrder>());
          Assert.AreEqual(3, n);
          reader.Read();
          Assert.AreEqual(positions[part + 1], reader.Position);
        }
        AreEqual(Order(5), reader.ReadObject<SwOrder>());
        Assert.IsTrue(reader.AtEnd);
      }
    }

    [TestMethod]
    public void HeadWithoutTheIndexedSchemaWritesNothing()
    {
      LsMsgPackSerializer serializer = Plain();
      byte[] withHead = Write(w =>
      {
        w.WriteHead<List<SwOrder>>();
        w.WriteArrayStart();
        w.WriteObject(Order(1));
        w.WriteObject(Order(2));
        w.WriteEnd();
      }, serializer);
      byte[] without = Write(w =>
      {
        w.WriteArrayStart();
        w.WriteObject(Order(1));
        w.WriteObject(Order(2));
        w.WriteEnd();
      }, serializer);
      Assert.AreEqual(Hex(without), Hex(withHead));

      using (MsgPackStreamReader reader = Reader(withHead, serializer))
      {
        reader.ReadHead<List<SwOrder>>();
        reader.Read();
        AreEqual(Order(1), reader.ReadObject<SwOrder>());
        AreEqual(Order(2), reader.ReadObject<SwOrder>());
        Assert.IsTrue(reader.AtEnd);
      }
    }

    [TestMethod]
    public void HeadRefusesAValueItsSchemaDoesNotHave()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions());
      byte[] bytes = Write(w =>
      {
        w.WriteHead<List<SwBag>>();
        w.WriteArrayStart();
        w.WriteObject(new SwBag { Name = "text", Content = "a string" });
        long before = w.Position;
        InvalidOperationException refused = Assert.ThrowsExactly<InvalidOperationException>(() => w.WriteObject(new SwBag { Name = "order", Content = Order(1) })); // SwOrder is not in the schema of List<SwBag>
        Assert.Contains("schema written in the head", refused.Message);
        Assert.AreEqual(before, w.Position, "nothing written");
        w.WriteObject(new SwBag { Name = "number", Content = 5 });
        w.WriteEnd();
        Assert.AreEqual(0, w.Depth);
      }, serializer);

      using (MsgPackStreamReader reader = Reader(bytes, serializer))
      {
        reader.ReadHead<List<SwBag>>();
        reader.Read();
        Assert.AreEqual("a string", reader.ReadObject<SwBag>().Content);
        Assert.AreEqual("number", reader.ReadObject<SwBag>().Name);
        Assert.IsTrue(reader.AtEnd);
      }
    }

    [TestMethod]
    public void HeadsOnlyBeforeARootValue()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions());
      MemoryStream ms = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(ms, serializer, MsgPackStreamSchemas.PerObject, true))
      {
        writer.WriteArrayStart();
        Assert.ThrowsExactly<InvalidOperationException>(() => writer.WriteHead<List<SwOrder>>());
        writer.WriteEnd();

        writer.WriteHead<List<SwOrder>>();
        Assert.ThrowsExactly<InvalidOperationException>(() => writer.WriteHead<List<SwOrder>>()); // the first one waits for its body
        writer.WriteArrayHeader(0); // the body: done
        writer.WriteHead<SwOrder>();
        writer.WriteObject(Order(2)); // a body that is one object
        writer.WriteObject(Order(3)); // no head: a whole payload
      }

      using (MsgPackStreamReader reader = Reader(ms.ToArray(), serializer))
      {
        reader.Read();
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.ReadHead<List<SwOrder>>());
        reader.Read();
        reader.ReadHead<List<SwOrder>>();
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.ReadHead<List<SwOrder>>());
        reader.Read();
        Assert.AreEqual(0L, reader.Length);
        reader.Read(); // its ArrayEnd
        reader.ReadHead<SwOrder>();
        AreEqual(Order(2), reader.ReadObject<SwOrder>());
        AreEqual(Order(3), reader.ReadObject<SwOrder>());
        Assert.IsTrue(reader.AtEnd);
      }
    }

    #endregion

    #region Errors

    [TestMethod]
    public void WriterRefusesWhatTheFormatDoesNotAllow()
    {
      void Refused(Action<MsgPackStreamWriter> write)
      {
        using (MsgPackStreamWriter writer = new MsgPackStreamWriter(new MemoryStream(), Plain()))
          Assert.ThrowsExactly<InvalidOperationException>(() => write(writer));
      }

      Refused(w => w.WriteEnd()); // nothing open
      Refused(w => { w.WriteArrayHeader(2); w.Write(1); w.WriteEnd(); }); // known length
      Refused(w => { w.WriteArrayStart(); w.WriteArrayHeader(2); w.Write(1); w.WriteEnd(); }); // the inner array is not complete
      Refused(w => { w.WriteMapStart(); w.Write("key"); w.WriteEnd(); }); // a key without its value
      Refused(w => { w.WriteStringStart(); w.Write(1); }); // only str chunks
      Refused(w => { w.WriteStringStart(); w.WriteBinary(new byte[1]); });
      Refused(w => { w.WriteStringStart(); w.WriteArrayStart(); });
      Refused(w => { w.WriteBinaryStart(); w.Write("text"); });
      Refused(w => w.WriteStringChunk("a".AsSpan())); // outside a str of unknown length
      Refused(w => { for (int t = 0; t <= MsgPackOptions.Default_MaxDepth; t++) w.WriteArrayStart(); });

      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(new MemoryStream(), Plain())) // a known array ends by itself
      {
        writer.WriteArrayHeader(1);
        writer.Write(1);
        Assert.AreEqual(0, writer.Depth);
      }
    }

    private static void Invalid(string hex, int maxDepth = 256)
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions { UseInexedSchema = false, MaxDepth = maxDepth });
      using (MsgPackStreamReader reader = Reader(Hex(hex), serializer))
        Assert.ThrowsExactly<MsgPackException>(() => { while (reader.Read()) { } }, hex);
      using (MsgPackStreamReader reader = Reader(Hex(hex), serializer))
        Assert.ThrowsExactly<MsgPackException>(() => reader.ReadObject<object>(), hex);
    }

    [TestMethod]
    public void ReaderRefusesWhatTheFormatDoesNotAllow()
    {
      Invalid("c1 91"); // reserved
      Invalid("c1 ff");
      Invalid("c1 90 01 c1"); // the escape byte at the end
      Invalid("c1 c0"); // an end outside
      Invalid("92 01 c1 c0"); // an end in an array of known length
      Invalid("c1 80 a1 61 c1 c0"); // an end where a value comes
      Invalid("c1 a0 01 c1 c0"); // only str chunks
      Invalid("c1 c4 a1 61 c1 c0"); // only bin chunks
      Invalid("c1 a0 c1 90 c1 c0 c1 c0"); // no escapes in chunks
      Invalid("c1 90 c1 90 c1 90 c1 c0 c1 c0 c1 c0", 2); // too deep
      Invalid("91 91 91 01", 2); // too deep, known lengths (all in the buffer)
      Invalid("92 01"); // cut off, known lengths
      Invalid("c1 90 01"); // cut off
      Invalid("db ff ff ff f0 61 62 63"); // claims more than there is
    }

    [TestMethod]
    public void HeadersClaimingMoreThanTheStreamHoldsAllocateNothingForIt()
    {
      byte[] bytes = Hex("c1 90 dd ff ff ff ff 01 02 c6 10 00 00 00 01 02 03");
      long before = GC.GetAllocatedBytesForCurrentThread();
      using (MsgPackStreamReader reader = Reader(bytes))
      {
        reader.Read();
        Assert.ThrowsExactly<MsgPackException>(() => reader.GetValue());
      }
      using (MsgPackStreamReader reader = Reader(Hex("db 10 00 00 00 61 62 63")))
      {
        reader.Read();
        Assert.ThrowsExactly<MsgPackException>(() => reader.GetString());
      }
      Assert.IsLessThan(10_000_000L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [TestMethod]
    public void SerializersPointToTheStreamReader()
    {
      MsgPackException error = Assert.ThrowsExactly<MsgPackException>(() => Plain().Deserialize<object>(Hex("c1 90 01 c1 c0")));
      StringAssert.Contains(error.Message, nameof(MsgPackStreamReader));
    }

    #endregion

    /// <summary>
    /// Cannot seek and returns 1 to 3 bytes per read, as a network stream may.
    /// </summary>
    private sealed class TrickleStream : Stream
    {
      private readonly MemoryStream _inner;
      private int _reads;
      public TrickleStream(byte[] bytes) { _inner = new MemoryStream(bytes); }
      public override bool CanRead { get { return true; } }
      public override bool CanSeek { get { return false; } }
      public override bool CanWrite { get { return false; } }
      public override long Length { get { throw new NotSupportedException(); } }
      public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
      public override void Flush() { }
      public override int Read(byte[] buffer, int offset, int count) { return _inner.Read(buffer, offset, Math.Min(count, 1 + _reads++ % 3)); }
      public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
      public override void SetLength(long value) { throw new NotSupportedException(); }
      public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }
  }
}
