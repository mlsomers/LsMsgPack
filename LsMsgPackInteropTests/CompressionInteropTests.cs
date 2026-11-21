using LsMsgPack;
using LsMsgPack.Compression;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace LsMsgPackInteropTests
{
  /// <summary>
  /// LZ4 compression (MsgPackOptions.Compression, the package LsMsgPack.Lz4) against MessagePack-CSharp's <c>Lz4Block</c> and <c>Lz4BlockArray</c>: each library reads what the other writes,
  /// in both layouts. The compressed bytes themselves may differ: K4os and MessagePack-CSharp's own LZ4 code find other matches (K4os often compresses better), the decompressed bodies are the same.
  /// </summary>
  [TestClass]
  public class CompressionInteropTests
  {
    private static readonly MessagePackSerializerOptions Contractless = ContractlessStandardResolver.Options;

    private static readonly MessagePackCompression[] TheirModes = new[] { MessagePackCompression.Lz4Block, MessagePackCompression.Lz4BlockArray };

    private static LsMsgPackSerializer Ours(MsgPackCompression compression)
    {
      LsMsgPackOptions options = LsMsgPackPresets.MessagePackCSharp();
      options.Compression = compression;
      return new LsMsgPackSerializer(options);
    }

    private static void AreEqual(List<Invoice> expected, List<Invoice> actual)
    {
      Assert.HasCount(expected.Count, actual);
      for (int t = 0; t < expected.Count; t++)
        Same.AssertEqual(expected[t], actual[t], expected[t].InvoiceNumber);
    }

    /// <param name="count">1 invoice is a few hundred bytes (one block), 1000 several hundred KB: MessagePack-CSharp splits those in blocks where its buffers' segments end</param>
    [TestMethod]
    [DataRow(1)]
    [DataRow(1000)]
    public void MessagePackCSharp_ReadsOurs(int count)
    {
      List<Invoice> invoices = Invoices.Create(count).ToList();
      foreach (MsgPackCompression compression in new[] { Lz4Compression.Block, Lz4Compression.BlockArray })
      {
        byte[] bytes = Ours(compression).Serialize(invoices);
        Assert.IsLessThan(Ours(null).Serialize(invoices).Length, bytes.Length);
        foreach (MessagePackCompression theirs in TheirModes)
          AreEqual(invoices, MessagePackSerializer.Deserialize<List<Invoice>>(bytes, Contractless.WithCompression(theirs)));
      }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(1000)]
    public void LsMsgPack_ReadsTheirs(int count)
    {
      List<Invoice> invoices = Invoices.Create(count).ToList();
      foreach (MessagePackCompression theirs in TheirModes)
      {
        byte[] bytes = MessagePackSerializer.Serialize(invoices, Contractless.WithCompression(theirs));
        foreach (MsgPackCompression compression in new[] { Lz4Compression.Block, Lz4Compression.BlockArray })
          AreEqual(invoices, Ours(compression).Deserialize<List<Invoice>>(bytes));
      }
    }

    /// <summary>
    /// The headers are MessagePack-CSharp's: ext 99 with the length as int32, or an array of ext 98 with the block lengths and bin 32 blocks; bodies under 64 bytes are not compressed.
    /// With one block of the same LZ4 bytes the payloads are equal byte for byte.
    /// </summary>
    [TestMethod]
    public void Headers_AsMessagePackCSharp()
    {
      List<Invoice> one = Invoices.Create(1).ToList();
      byte[] plain = Ours(null).Serialize(one);
      CollectionAssert.AreEqual(MessagePackSerializer.Serialize(one, Contractless), plain);

      byte[] block = Ours(Lz4Compression.Block).Serialize(one);
      byte[] theirBlock = MessagePackSerializer.Serialize(one, Contractless.WithCompression(MessagePackCompression.Lz4Block));
      byte[] array = Ours(Lz4Compression.BlockArray).Serialize(one);
      byte[] theirArray = MessagePackSerializer.Serialize(one, Contractless.WithCompression(MessagePackCompression.Lz4BlockArray));
      if (block.Length == theirBlock.Length) // the same matches found
      {
        CollectionAssert.AreEqual(theirBlock, block);
        CollectionAssert.AreEqual(theirArray, array);
      }
      else
      {
        CollectionAssert.AreEqual(theirBlock.Take(3).ToArray(), block.Take(3).ToArray()); // the ext header up to its type, if the lengths have the same header size
        CollectionAssert.AreEqual(theirArray.Take(5).ToArray(), array.Take(5).ToArray()); // [ext 98 of one length...
      }

      List<int> small = new List<int> { 1, 2, 3 };
      CollectionAssert.AreEqual(MessagePackSerializer.Serialize(small, Contractless.WithCompression(MessagePackCompression.Lz4Block)), Ours(Lz4Compression.Block).Serialize(small));
    }
  }
}
