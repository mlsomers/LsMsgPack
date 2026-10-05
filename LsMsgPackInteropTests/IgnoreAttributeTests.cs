using LsMsgPack;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using NB = Nerdbank.MessagePack;

namespace LsMsgPackInteropTests
{
  public class IgnoreInteropProbe
  {
    public string Plain { get; set; }
    [XmlIgnore] public string Xml { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string Stj { get; set; }
    [Newtonsoft.Json.JsonIgnore] public string Newtonsoft { get; set; }
    [IgnoreDataMember] public string DataMember { get; set; }
    [IgnoreMember] public string MessagePackCSharp { get; set; }
    [PropertyShape(Ignore = true)] public string PolyType { get; set; }

    public static IgnoreInteropProbe Filled()
    {
      return new IgnoreInteropProbe() { Plain = "p", Xml = "x", Stj = "s", Newtonsoft = "n", DataMember = "d", MessagePackCSharp = "m", PolyType = "t" };
    }
  }

  [GenerateShapeFor<IgnoreInteropProbe>]
  public partial class IgnoreInteropShapes
  {
  }

  /// <summary>
  /// The presets of <see cref="FilterIgnoredAttribute"/> leave out the same properties as the libraries they are named after.
  /// </summary>
  public abstract class IgnoreAttributeTests
  {
    protected abstract LsMsgPackUnitTests.ISerializerUnderTest Serializer { get; }

    private static MsgPackSettings Named(IMsgPackPropertyIncludeStatically ignoreFilter)
    {
      return new MsgPackSettings()
      {
        UseInexedSchema = false,
        ObjectLayout = ObjectLayout.Map,
        StaticFilters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(), ignoreFilter, new FilterStatic() }
      };
    }

    private static string[] Sorted(IEnumerable<string> keys)
    {
      return keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    private static string[] MsgPackKeys(byte[] bytes)
    {
      return Sorted(((KeyValuePair<object, object>[])((MpMap)MsgPackItem.Unpack(bytes)).Value).Select(kv => (string)kv.Key));
    }

    private string[] LsMsgPackKeys(FilterIgnoredAttribute filter)
    {
      return MsgPackKeys(Serializer.Serialize(IgnoreInteropProbe.Filled(), Named(filter)));
    }

    [TestMethod]
    public void LikeNewtonsoft()
    {
      string[] expected = Sorted(JObject.FromObject(IgnoreInteropProbe.Filled()).Properties().Select(p => p.Name));
      CollectionAssert.AreEqual(expected, LsMsgPackKeys(FilterIgnoredAttribute.LikeNewtonsoft), string.Join(", ", expected));
    }

    [TestMethod]
    public void LikeSystemTextJson()
    {
      string json = System.Text.Json.JsonSerializer.Serialize(IgnoreInteropProbe.Filled());
      string[] expected = Sorted(System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name));
      CollectionAssert.AreEqual(expected, LsMsgPackKeys(FilterIgnoredAttribute.LikeSystemTextJson), string.Join(", ", expected));
    }

    [TestMethod]
    public void LikeMessagePackCSharp()
    {
      string[] expected = MsgPackKeys(MessagePackSerializer.Serialize(IgnoreInteropProbe.Filled(), ContractlessStandardResolver.Options));
      CollectionAssert.AreEqual(expected, LsMsgPackKeys(FilterIgnoredAttribute.LikeMessagePackCSharp), string.Join(", ", expected));
    }

    [TestMethod]
    public void LikeNerdbank()
    {
      string[] expected = MsgPackKeys(new NB.MessagePackSerializer().Serialize<IgnoreInteropProbe, IgnoreInteropShapes>(IgnoreInteropProbe.Filled()));
      CollectionAssert.AreEqual(expected, LsMsgPackKeys(FilterIgnoredAttribute.LikeNerdbank), string.Join(", ", expected));
    }
  }

  [TestClass]
  public class LsIgnoreAttributeTests : IgnoreAttributeTests
  {
    protected override LsMsgPackUnitTests.ISerializerUnderTest Serializer { get { return LsMsgPackUnitTests.Serializers.Ls; } }
  }

  [TestClass]
  public class LtIgnoreAttributeTests : IgnoreAttributeTests
  {
    protected override LsMsgPackUnitTests.ISerializerUnderTest Serializer { get { return LsMsgPackUnitTests.Serializers.Lt; } }
  }
}
