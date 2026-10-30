using LsMsgPack;
using LtMsgPack;
using LtMsgPack.Streaming;
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

    private static LtMsgPackSerializer Plain()
    {
      return new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map });
    }

    private static LtMsgPackSerializer WithReferences(SchemaStore store)
    {
      return new LtMsgPackSerializer(new LtMsgPackOptions { WriteSchemaReference = true, SchemaStore = store });
    }

    private static byte[] Write(Action<MsgPackStreamWriter> write, LtMsgPackSerializer serializer = null, MsgPackStreamSchemas schemas = MsgPackStreamSchemas.PerObject)
    {
      MemoryStream ms = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(ms, serializer ?? Plain(), schemas, true))
        write(writer);
      return ms.ToArray();
    }

    private static MsgPackStreamReader Reader(byte[] bytes, LtMsgPackSerializer serializer = null)
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
    private static string Tokens(byte[] bytes, LtMsgPackSerializer serializer = null)
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
      LtMsgPackSerializer serializer = Plain();
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
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false, AddTypeIdOptions = AddTypeIdOption.Always });
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
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, EndianAction = EndianAction.NeverSwap });
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
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions());
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
      foreach (LtMsgPackSerializer serializer in new[] { new LtMsgPackSerializer(new LtMsgPackOptions()), Plain() })
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
      LtMsgPackSerializer serializer = WithReferences(new SchemaStore());
      byte[] bytes = Write(w =>
      {
        w.WriteObject(Order(1));
        w.WriteObject(Order(2));
      }, serializer);

      using (MsgPackStreamReader reader = Reader(bytes, new LtMsgPackSerializer(new LtMsgPackOptions()))) // no store: every schema is inline
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
      LtMsgPackSerializer serializer = WithReferences(store);
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

      using (MsgPackStreamReader reader = Reader(bytes, new LtMsgPackSerializer(new LtMsgPackOptions { SchemaStore = new SchemaStore() }))) // learns the schema from the stream
      {
        for (int n = 1; n <= 3; n++)
          AreEqual(Order(n), reader.ReadObject<SwOrder>());
        Assert.IsTrue(reader.AtEnd);
      }

      using (MsgPackStreamReader reader = Reader(bytes, new LtMsgPackSerializer(new LtMsgPackOptions())))
      {
        AreEqual(Order(1), reader.ReadObject<SwOrder>());
        Assert.ThrowsExactly<MsgPackException>(() => reader.ReadObject<SwOrder>()); // a reference needs a store
      }
    }

    [TestMethod]
    public void PerStreamWritesAGrownSchemaAgain()
    {
      LtMsgPackSerializer serializer = WithReferences(new SchemaStore());
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

      using (MsgPackStreamReader reader = Reader(bytes, new LtMsgPackSerializer(new LtMsgPackOptions { SchemaStore = new SchemaStore() })))
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
      LtMsgPackSerializer serializer = WithReferences(store);
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
      LtMsgPackSerializer reading = new LtMsgPackSerializer(new LtMsgPackOptions { SchemaStore = readers });
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
      LtMsgPackSerializer serializer = Plain();
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

    [TestMethod]
    public void ObjectWrittenWithValuesOfUnknownLength()
    {
      foreach (EndianAction endian in new[] { EndianAction.SwapIfCurrentSystemIsLittleEndian, EndianAction.NeverSwap })
      {
        LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, EndianAction = endian });
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
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions());
      MemoryStream ms = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(ms, serializer, MsgPackStreamSchemas.PerObject, true))
      {
        writer.WriteArrayStart();
        Assert.ThrowsExactly<InvalidOperationException>(() => writer.WriteObject(Order(1)));
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
      Assert.ThrowsExactly<ArgumentException>(() => new MsgPackStreamWriter(new MemoryStream(), new LtMsgPackSerializer(new LtMsgPackOptions()), MsgPackStreamSchemas.PerStream));
      Assert.ThrowsExactly<ArgumentException>(() => new MsgPackStreamWriter(new MemoryStream(), Plain(), MsgPackStreamSchemas.External));
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
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false, MaxDepth = maxDepth });
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
