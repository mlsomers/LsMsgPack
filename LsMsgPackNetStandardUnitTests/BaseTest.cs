using System;
using LsMsgPack;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections;

namespace LsMsgPackUnitTests
{
  public class MsgPackTests
  {
    public static MsgPackItem RoundTripTest<Typ, T>(T value, int expectedLength, MsgPackTypeId expectedMsgPackType, bool dynamicallyCompact = true, sbyte extensionType = 0) where Typ : MsgPackItem
    {
      bool preservingType = !dynamicallyCompact;

      MsgPackItem item;
      if (typeof(Typ) == typeof(MpExt))
      {
        item = new MpExt()
        {
          Value = value,
          TypeSpecifier = extensionType
        };
      }
      else
        item = MsgPackItem.Pack(value, new MsgPackSettings()
        {
          DynamicallyCompact = dynamicallyCompact
        });
      byte[] buffer = item.ToBytes();
      Type expectedType = typeof(Typ);
      Type foundType = item.GetType();

      MsgPackTypeId resultType = preservingType && item is MpInt ? ((MpInt)item).PreservedType : item.TypeId;
      bool typesAreEqual = (expectedMsgPackType & resultType) == expectedMsgPackType;
      Assert.IsTrue(typesAreEqual, string.Concat("Expected packed type of ", expectedMsgPackType, " but received the type ", resultType));
      Assert.IsTrue(foundType == expectedType, string.Concat("Expected type of ", expectedType.ToString(), " but received the type ", foundType.ToString()));
      if (expectedLength >= 0) Assert.AreEqual(expectedLength, buffer.Length, string.Concat("Expected a packed length of ", expectedLength.ToString(), " bytes but got ", buffer.Length.ToString(), " bytes."));

      MsgPackItem recreate = MsgPackItem.Unpack(new MemoryStream(buffer));

      resultType = preservingType && item is MpInt ? ((MpInt)recreate).PreservedType : recreate.TypeId;
      typesAreEqual = (expectedMsgPackType & resultType) == expectedMsgPackType;
      Assert.IsTrue(typesAreEqual, string.Concat("Expected unpacked type of ", expectedMsgPackType, " but received the type ", resultType));

      T ret = recreate.GetTypedValue<T>();

      // Correct Local / UTC differences before comparing final result
      if (ret is DateTime)
      {
        DateTime idt = (DateTime)(object)value;
        DateTime odt = (DateTime)(object)ret;
        if (idt.Kind != odt.Kind)
        {
          if (idt.Kind == DateTimeKind.Utc)
            ret = (T)(object)odt.ToUniversalTime();
          else
            ret = (T)(object)odt.ToLocalTime();
        }
      }

      ICollection collVal=value as ICollection;
      if (collVal != null)
      {
        ICollection retColl = collVal as ICollection;
        CollectionAssert.AreEqual(collVal, retColl, string.Concat("The returned value ", ret, " differs from the input value ", value));
      }
      else
      {
        Assert.AreEqual<T>(value, ret, string.Concat("The returned value ", ret, " differs from the input value ", value));
      }

      return recreate;
    }

    public static bool AreEqualish(object a, object b)
    {
      if (ReferenceEquals(a, b)) return true;
      if (a == null) return b == null;
      if (a.Equals(b) || a == b || b.Equals(a) || b == a) return true;
      ICollection acoll = a as ICollection;
      if (acoll != null)
      {
        ICollection bcoll = b as ICollection;
        if (acoll.Count != bcoll.Count) return false;
        object[] aarr = new object[acoll.Count];
        acoll.CopyTo(aarr, 0);
        object[] barr = new object[bcoll.Count];
        bcoll.CopyTo(barr, 0);

        for (int t = aarr.Length - 1; t >= 0; t--)
        {
          if (!AreEqualish(aarr[t], barr[t]))
            return false;
        }
        return true;
      }
      return Convert.ToDecimal(a) == Convert.ToDecimal(b);
    }
  }
}
