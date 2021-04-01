using System;
using LsMsgPack;
using NUnit.Framework;
using System.IO;

namespace LsMsgPackUnitTests {
  public class MsgPackTests {

    public static bool DynamicallyCompactValue = true;

    public static MsgPackItem RoundTripTest<Typ, T>(T value, int expectedLength, MsgPackTypeId expectedMsgPackType, sbyte extensionType=0) where Typ : MsgPackItem {

      bool preservingType = !DynamicallyCompactValue;

      MsgPackItem item;
      if(typeof(Typ) == typeof(MpExt)) {
        item = new MpExt() {
          Value = value,
          TypeSpecifier = extensionType
        };
      } else
        item = MsgPackItem.Pack(value, new MsgPackSettings() {
          DynamicallyCompact = DynamicallyCompactValue,
          PreservePackages = false,
          ContinueProcessingOnBreakingError = false
        });
      byte[] buffer = item.ToBytes();
      Type expectedType = typeof(Typ);
      Type foundType = item.GetType();

      MsgPackTypeId resultType = preservingType && item is MpInt ? ((MpInt)item).PreservedType : item.TypeId;
      bool typesAreEqual = (expectedMsgPackType & resultType) == expectedMsgPackType;
      Assert.IsTrue(typesAreEqual, string.Concat("Expected packed type of ", expectedMsgPackType, " but received the type ", resultType));
      Assert.True(foundType == expectedType, string.Concat("Expected type of ", expectedType.ToString(), " but received the type ", foundType.ToString()));
      if(expectedLength>=0) Assert.AreEqual(expectedLength, buffer.Length, string.Concat("Expected a packed length of ", expectedLength.ToString(), " bytes but got ", buffer.Length.ToString(), " bytes."));

      MsgPackItem recreate = MsgPackItem.Unpack(new MemoryStream(buffer));

      MpError err = recreate as MpError;
      if (!(err is null))
        throw new Exception(err.ToString());

      resultType = preservingType && item is MpInt ? ((MpInt)recreate).PreservedType : recreate.TypeId;
      typesAreEqual = (expectedMsgPackType & resultType) == expectedMsgPackType;
      Assert.IsTrue(typesAreEqual, string.Concat("Expected unpacked type of ", expectedMsgPackType, " but received the type ", resultType));

      T ret = recreate.GetTypedValue<T>();

      // Correct Local / UTC differences before comparing final result
      if(ret is DateTime) {
        DateTime idt = (DateTime)(object)value;
        DateTime odt = (DateTime)(object)ret;
        if (idt.Kind != odt.Kind) {
          if (idt.Kind == DateTimeKind.Utc)
            ret = (T)(object)odt.ToUniversalTime();
          else
            ret= (T)(object)odt.ToLocalTime();
        }
      }

      Assert.AreEqual(value, ret, "The returned value ", ret, " differs from the input value ", value);

      return recreate;
    }

  }
}
