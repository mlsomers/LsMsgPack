﻿using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace LsMsgPackUnitTests
{
  [TestClass]
  public class MpMapTest
  {
    [TestMethod]
    [DataRow(0, 1, MsgPackTypeId.MpMap4)]
    [DataRow(1, 2, MsgPackTypeId.MpMap4)]
    [DataRow(15, 16, MsgPackTypeId.MpMap4)]
    [DataRow(200, 203, MsgPackTypeId.MpMap16)]
    [DataRow(256, 259, MsgPackTypeId.MpMap16)]
    [DataRow(ushort.MaxValue, ushort.MaxValue + 3, MsgPackTypeId.MpMap16)]
    [DataRow(ushort.MaxValue + 1, ushort.MaxValue + 6, MsgPackTypeId.MpMap32)]
    // [DataRow(0x7FEFFFF9, 0x7FEFFFF9 + 6, MsgPackTypeId.MpMap32)] // Out of memory on my machine
    public void MapLengths(int length, int expectedBytes, MsgPackTypeId expedctedType)
    {
      KeyValuePair<object, object>[] test = new KeyValuePair<object, object>[length];
      for (int t = test.Length - 1; t >= 0; t--) test[t] = new KeyValuePair<object, object>(null, null);
      int additionalBytes = test.Length;
      MsgPackItem item = MsgPackTests.RoundTripTest<MpMap, KeyValuePair<object, object>[]>(test, expectedBytes + additionalBytes, expedctedType);

      KeyValuePair<object, object>[] ret = item.GetTypedValue<KeyValuePair<object, object>[]>();

      Assert.HasCount(length, ret, string.Concat("Expected ", length, " items but got ", ret.Length, " items in the map."));
      for (int t = ret.Length - 1; t >= 0; t--)
      {
        Assert.AreEqual(test[t], ret[t], string.Concat("Expected ", test[t], " but got ", ret[t], " at index ", t));
      }
    }

    [TestMethod]
    [DataRow(false, 47)]
    [DataRow(true, 47)]
    public void AssortedMix(bool preserveTypes, int expectedLength)
    {
      KeyValuePair<object, object>[] items = new KeyValuePair<object, object>[] {
        new KeyValuePair<object, object>(true, true),
        new KeyValuePair<object, object>(false, false),
        new KeyValuePair<object, object>(null, null),
        new KeyValuePair<object, object>(int.MinValue, uint.MaxValue),
        new KeyValuePair<object, object>((byte)1, 5.2d),
        new KeyValuePair<object, object>((byte)2, 900.1f),
        new KeyValuePair<object, object>("Hallo!", "Hallo!"),
      };
      MsgPackItem item = MsgPackTests.RoundTripTest<MpMap, KeyValuePair<object, object>[]>(items, expectedLength, MsgPackTypeId.MpMap4);

      KeyValuePair<object, object>[] ret = item.GetTypedValue<KeyValuePair<object, object>[]>();

      Assert.HasCount(items.Length, ret, string.Concat("Expected ", items.Length, " items but got ", ret.Length, " items in the array."));
      for (int t = ret.Length - 1; t >= 0; t--)
      {
        Assert.AreEqual(items[t], ret[t], string.Concat("Expected ", items[t], " but got ", ret[t], " at index ", t));
      }
    }

    [TestMethod]
    public void TypicalDictionaryUse()
    {
      Dictionary<string, object> objectProps = new Dictionary<string, object>();
      objectProps.Add("string", "Hallo!");
      objectProps.Add("int", 5);
      objectProps.Add("bool", true);
      objectProps.Add("nothing", null);
      objectProps.Add("float", 3.5f);

      MsgPackItem item = MsgPackTests.RoundTripTest<MpMap, Dictionary<string, object>>(objectProps, 46, MsgPackTypeId.MpMap4);
    }

    [TestMethod]
    public void EnumDictionaryUse()
    {
      Dictionary<myEnum, object> objectProps = new Dictionary<myEnum, object>();
      objectProps.Add(myEnum.SomeString, "Hallo!");
      objectProps.Add(myEnum.SomeInt, 5);
      objectProps.Add(myEnum.SomeBool, true);
      objectProps.Add(myEnum.SomeNothing, null);
      objectProps.Add(myEnum.SomeFloat, 3.5f);

      MsgPackItem item = MsgPackTests.RoundTripTest<MpMap, Dictionary<myEnum, object>>(objectProps, 21, MsgPackTypeId.MpMap4);
    }

    public enum myEnum : byte
    {
      SomeString,
      SomeInt,
      SomeBool,
      SomeNothing,
      SomeFloat
    }
  }
}
