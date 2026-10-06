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

  public interface IIgnoreInteropProbe
  {
    [Newtonsoft.Json.JsonIgnore] string INewtonsoft { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] string IStj { get; set; }
    [XmlIgnore] string IXml { get; set; }
    [IgnoreDataMember] string IDataMember { get; set; }
    [IgnoreMember] string IMessagePackCSharp { get; set; }
    [PropertyShape(Ignore = true)] string IPolyType { get; set; }
  }

  public class IgnoreInteropProbeBase
  {
    [Newtonsoft.Json.JsonIgnore] public virtual string ONewtonsoft { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public virtual string OStj { get; set; }
    [XmlIgnore] public virtual string OXml { get; set; }
    [IgnoreDataMember] public virtual string ODataMember { get; set; }
    [IgnoreMember] public virtual string OMessagePackCSharp { get; set; }
    [PropertyShape(Ignore = true)] public virtual string OPolyType { get; set; }
  }

  /// <summary>
  /// The ignore attributes on the properties that the properties override (O...) or implement (I...): each library looks elsewhere.
  /// </summary>
  public class InheritedIgnoreInteropProbe : IgnoreInteropProbeBase, IIgnoreInteropProbe
  {
    public string Plain { get; set; } = "p";
    public override string ONewtonsoft { get; set; } = "o";
    public override string OStj { get; set; } = "o";
    public override string OXml { get; set; } = "o";
    public override string ODataMember { get; set; } = "o";
    public override string OMessagePackCSharp { get; set; } = "o";
    public override string OPolyType { get; set; } = "o";
    public string INewtonsoft { get; set; } = "i";
    public string IStj { get; set; } = "i";
    public string IXml { get; set; } = "i";
    public string IDataMember { get; set; } = "i";
    public string IMessagePackCSharp { get; set; } = "i";
    public string IPolyType { get; set; } = "i";
  }

  [GenerateShapeFor<IgnoreInteropProbe>]
  [GenerateShapeFor<InheritedIgnoreInteropProbe>]
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

    private string[] LsMsgPackKeys<T>(T value, FilterIgnoredAttribute filter)
    {
      return MsgPackKeys(Serializer.Serialize(value, Named(filter)));
    }

    private void AssertSameKeys<T>(T value, FilterIgnoredAttribute filter, string[] expected)
    {
      CollectionAssert.AreEqual(expected, LsMsgPackKeys(value, filter), $"{typeof(T).Name}: {string.Join(", ", expected)}");
    }

    private static string[] Newtonsoft<T>(T value)
    {
      return Sorted(JObject.FromObject(value).Properties().Select(p => p.Name));
    }

    [TestMethod]
    public void LikeNewtonsoft()
    {
      AssertSameKeys(IgnoreInteropProbe.Filled(), FilterIgnoredAttribute.LikeNewtonsoft, Newtonsoft(IgnoreInteropProbe.Filled()));
      AssertSameKeys(new InheritedIgnoreInteropProbe(), FilterIgnoredAttribute.LikeNewtonsoft, Newtonsoft(new InheritedIgnoreInteropProbe()));
    }

    private static string[] SystemTextJson<T>(T value)
    {
      string json = System.Text.Json.JsonSerializer.Serialize(value);
      return Sorted(System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name));
    }

    [TestMethod]
    public void LikeSystemTextJson()
    {
      AssertSameKeys(IgnoreInteropProbe.Filled(), FilterIgnoredAttribute.LikeSystemTextJson, SystemTextJson(IgnoreInteropProbe.Filled()));
      AssertSameKeys(new InheritedIgnoreInteropProbe(), FilterIgnoredAttribute.LikeSystemTextJson, SystemTextJson(new InheritedIgnoreInteropProbe()));
    }

    private static string[] MessagePackCSharp<T>(T value)
    {
      return MsgPackKeys(MessagePackSerializer.Serialize(value, ContractlessStandardResolver.Options));
    }

    [TestMethod]
    public void LikeMessagePackCSharp()
    {
      AssertSameKeys(IgnoreInteropProbe.Filled(), FilterIgnoredAttribute.LikeMessagePackCSharp, MessagePackCSharp(IgnoreInteropProbe.Filled()));
      AssertSameKeys(new InheritedIgnoreInteropProbe(), FilterIgnoredAttribute.LikeMessagePackCSharp, MessagePackCSharp(new InheritedIgnoreInteropProbe()));
    }

    [TestMethod]
    public void LikeNerdbank()
    {
      NB.MessagePackSerializer nerdbank = new NB.MessagePackSerializer();
      AssertSameKeys(IgnoreInteropProbe.Filled(), FilterIgnoredAttribute.LikeNerdbank, MsgPackKeys(nerdbank.Serialize<IgnoreInteropProbe, IgnoreInteropShapes>(IgnoreInteropProbe.Filled())));
      AssertSameKeys(new InheritedIgnoreInteropProbe(), FilterIgnoredAttribute.LikeNerdbank, MsgPackKeys(nerdbank.Serialize<InheritedIgnoreInteropProbe, IgnoreInteropShapes>(new InheritedIgnoreInteropProbe())));
    }

    private static string[] ElementNames(string xml)
    {
      return Sorted(System.Xml.Linq.XDocument.Parse(xml).Root.Elements().Where(e => !e.IsEmpty || e.HasAttributes == false).Select(e => e.Name.LocalName));
    }

    private static string[] XmlSerializerKeys<T>(T value)
    {
      System.IO.StringWriter writer = new System.IO.StringWriter();
      new XmlSerializer(typeof(T)).Serialize(writer, value);
      return ElementNames(writer.ToString());
    }

    [TestMethod]
    public void LikeXmlSerializer()
    {
      AssertSameKeys(IgnoreInteropProbe.Filled(), FilterIgnoredAttribute.LikeXmlSerializer, XmlSerializerKeys(IgnoreInteropProbe.Filled()));
      AssertSameKeys(new InheritedIgnoreInteropProbe(), FilterIgnoredAttribute.LikeXmlSerializer, XmlSerializerKeys(new InheritedIgnoreInteropProbe()));
    }

    private static string[] DataContractKeys<T>(T value)
    {
      System.IO.MemoryStream stream = new System.IO.MemoryStream();
      new DataContractSerializer(typeof(T)).WriteObject(stream, value);
      return ElementNames(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    [TestMethod]
    public void LikeDataContract()
    {
      AssertSameKeys(IgnoreInteropProbe.Filled(), FilterIgnoredAttribute.LikeDataContract, DataContractKeys(IgnoreInteropProbe.Filled()));
      AssertSameKeys(new InheritedIgnoreInteropProbe(), FilterIgnoredAttribute.LikeDataContract, DataContractKeys(new InheritedIgnoreInteropProbe()));
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
