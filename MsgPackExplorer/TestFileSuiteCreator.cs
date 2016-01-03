using LsMsgPack;
using System.Collections.Generic;
using System.IO;

namespace MsgPackExplorer {
  public class TestFileSuiteCreator {

    public void CreateSuite(string directory) {
      if(!Directory.Exists(directory)) Directory.CreateDirectory(directory);
      AllSmallTypes(directory);
      SomeBadChoices(directory);
      SlidingTackle(directory);
    }

    public void AllSmallTypes(string directory) {
      Dictionary<string, int> simpleMap = new Dictionary<string, int>();
      simpleMap.Add("Black", 0);
      simpleMap.Add("Brown", 1);
      simpleMap.Add("Red", 2);
      simpleMap.Add("Orange", 3);
      simpleMap.Add("Yellow", 4);
      simpleMap.Add("Green", 5);
      simpleMap.Add("Blue", 6);
      simpleMap.Add("Violet", 7);
      simpleMap.Add("Gray", 8);
      simpleMap.Add("White", 9);

      object[] items = new object[] {
        "Null type:",
        null,
        "Bool types:",
        true,
        false,
        "Float types:",
        float.Epsilon,
        float.MaxValue,
        float.MinValue,
        float.NaN,
        float.NegativeInfinity,
        float.PositiveInfinity,
        double.Epsilon,
        double.MaxValue,
        double.MinValue,
        double.NaN,
        double.NegativeInfinity,
        double.PositiveInfinity,
        "Integer types:",
        0,
        1,
        65,
        127,
        -1,
        -15,
        -31,
        128,
        200,
        255,
        -32,
        -100,
        -128,
        256,
        1000,
        ushort.MaxValue,
        -129,
        -1000,
        short.MinValue,
        ushort.MaxValue+1,
        int.MaxValue,
        uint.MaxValue,
        short.MinValue-1,
        short.MinValue - 1000,
        int.MinValue,
        ((ulong)uint.MaxValue) + 1,
        long.MaxValue,
        ulong.MaxValue,
        ((long)int.MinValue) -1,
        ((long)int.MinValue) - 1000,
        long.MinValue,
        "Map:",
        simpleMap,
        "Binary bytes blob:",
        new byte[] {1,2,3,4,5,6,7,8,9,0},
        "Extension (type 5)",
        new MpExt() {
          Value =new byte[] {0,9,8,7,6,5,4,3,2,1},
         TypeSpecifier = 5
        }
      };

      File.WriteAllBytes(Path.Combine(directory, "AllSmallTypes.MsgPack"), MsgPackItem.Pack(items, true).ToBytes());
    }

    public void SomeBadChoices(string directory) {
      object[] items = new object[] {
        "Wrongfully signed types",
        (sbyte)50,
        (int)128,
        (short)129,
        (int)129,
        (long)129,
        (int)1234,
        short.MaxValue,
        int.MaxValue,
        long.MaxValue,
        "Uncompressed integers",

        (ulong)1,
        (ulong)127,
        (ulong)128,
        (ulong)255,
        (ulong)256,
        (ulong)ushort.MaxValue,
        (ulong)ushort.MaxValue + 1,
        (ulong)uint.MaxValue,

        (uint)1,
        (uint)127,
        (uint)128,
        (uint)255,
        (uint)256,
        (uint)ushort.MaxValue,

        (ushort)1,
        (ushort)127,
        (ushort)128,
        (ushort)255,

        (byte)1,
        (byte)127,

        (long)-1,
        (long)-31,
        (long)-32,
        (long)-128,
        (long)-129,
        (long)short.MinValue,
        ((long)short.MinValue) - 1,
        (long)int.MinValue,

        (int)-1,
        (int)-31,
        (int)-32,
        (int)-128,
        (int)-129,
        (int)short.MinValue,

        (short)-1,
        (short)-31,
        (short)-32,
        (short)-128,
      };
      
      File.WriteAllBytes(Path.Combine(directory, "SomeBadChoices.MsgPack"), MsgPackItem.Pack(items, false).ToBytes());
    }

    public void SlidingTackle(string directory) {
      KeyValuePair<object, object>[] items = new KeyValuePair<object, object>[] {
        new KeyValuePair<object, object>(true,false),
        new KeyValuePair<object, object>(null,"Maps are quite flexible!"),
        new KeyValuePair<object, object>(true,"Double!"),
        new KeyValuePair<object, object>(new KeyValuePair<object,object>[0],new object[0]),
        new KeyValuePair<object, object>(new object[0],new KeyValuePair<object,object>[0])
      };
      File.WriteAllBytes(Path.Combine(directory, "SlidingTackle.MsgPack"), MsgPackItem.Pack(items).ToBytes());
    }
  }
}
