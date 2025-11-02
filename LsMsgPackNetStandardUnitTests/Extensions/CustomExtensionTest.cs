using LsMsgPack;
using LsMsgPack.Types.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace LsMsgPackUnitTests.Extensions
{
  [TestFixture]
  [TestClass]
  public class CustomExtensionTest
  {
    [TestMethod]
    public void RoundTripMpGuid()
    {
      MsgPackSettings.Default_CustomExtentionTypes = new ICustomExt[]
      {
        new MpDecimal((MsgPackSettings)null),
        new MyGuidExtension((MsgPackSettings)null)
      };

      KeyValuePair<Guid, Guid> tst = new KeyValuePair<Guid, Guid>(Guid.NewGuid(), Guid.NewGuid());
      MsgPackTests.RoundTripTest<MyGuidExtension, KeyValuePair<Guid, Guid>>(tst, 35, MsgPackTypeId.MpExt8, true, 2);

      MsgPackSettings.Default_CustomExtentionTypes = new ICustomExt[]
      {
        new MpDecimal((MsgPackSettings)null)
      };
    }

    [TestMethod]
    public void RoundTripMpGuidNonCached()
    {
      MsgPackSettings.Default_CustomExtentionTypes = new ICustomExt[]
      {
        new MpDecimal((MsgPackSettings)null),
        new MyGuidExtensionNonCached((MsgPackSettings)null)
      };

      KeyValuePair<Guid, Guid> tst = new KeyValuePair<Guid, Guid>(Guid.NewGuid(), Guid.NewGuid());
      MsgPackTests.RoundTripTest<MyGuidExtensionNonCached, KeyValuePair<Guid, Guid>>(tst, 35, MsgPackTypeId.MpExt8, true, 2);

      MsgPackSettings.Default_CustomExtentionTypes = new ICustomExt[]
      {
        new MpDecimal((MsgPackSettings)null)
      };
    }
  }

  public class MyGuidExtension : BaseCustomExt<MyGuidExtension, KeyValuePair<Guid, Guid>>
  {
    public MyGuidExtension() : base() { }

    public MyGuidExtension(MsgPackSettings settings) : base(settings) { }
    protected override sbyte DefaultTypeSpecifier { get { return 2; } }

    public override KeyValuePair<Guid, Guid> FromBytes(byte[] bytes)
    {
      return new KeyValuePair<Guid, Guid>(new Guid(new Span<byte>(bytes, 0, 16)), new Guid(new Span<byte>(bytes, 16, 16)));
    }

    public override byte[] GetBytes(KeyValuePair<Guid, Guid> item)
    {
      byte[] ret = new byte[32];
      item.Key.ToByteArray().CopyTo(ret, 0);
      item.Value.ToByteArray().CopyTo(ret, 16);
      return ret;
    }
  }

  /// <summary>
  /// Probably is slower due to multiple decoding, need to profile
  /// </summary>
  public class MyGuidExtensionNonCached : BaseCustomExtNonCached<MyGuidExtensionNonCached, KeyValuePair<Guid, Guid>>
  {
    public MyGuidExtensionNonCached() : base() { }

    public MyGuidExtensionNonCached(MsgPackSettings settings) : base(settings) { }
    protected override sbyte DefaultTypeSpecifier { get { return 2; } }

    public override KeyValuePair<Guid, Guid> FromBytes(byte[] bytes)
    {
      return new KeyValuePair<Guid, Guid>(new Guid(new Span<byte>(bytes, 0, 16)), new Guid(new Span<byte>(bytes, 16, 16)));
    }

    public override byte[] GetBytes(KeyValuePair<Guid, Guid> item)
    {
      byte[] ret = new byte[32];
      item.Key.ToByteArray().CopyTo(ret, 0);
      item.Value.ToByteArray().CopyTo(ret, 16);
      return ret;
    }
  }
}
