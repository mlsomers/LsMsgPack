using LsMsgPack;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace LsMsgPackUnitTests
{
  [AttributeUsage(AttributeTargets.Property)]
  public class SkipInMsgPackAttribute : Attribute { }

  [AttributeUsage(AttributeTargets.Property)]
  public class CustomIgnoreMeAttribute : Attribute { }

  /// <summary>
  /// The static filter for "ignore" attributes (FilterIgnoredAttribute and its options), the static filters of different settings, and empty strings in FilterDefaultValues.
  /// </summary>
  public abstract class FilterTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    public class IgnoreProbe
    {
      public string Plain { get; set; }
      [XmlIgnore] public string Xml { get; set; }
      [System.Text.Json.Serialization.JsonIgnore] public string Stj { get; set; }
      [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string StjNever { get; set; }
      [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string StjWhenNull { get; set; }
      [Newtonsoft.Json.JsonIgnore] public string Newtonsoft { get; set; }
      [IgnoreDataMember] public string DataMember { get; set; }
      [CustomIgnoreMe] public string Custom { get; set; }
      [SkipInMsgPack] public string Skip { get; set; }

      public static IgnoreProbe Filled()
      {
        return new IgnoreProbe() { Plain = "p", Xml = "x", Stj = "s", StjNever = "sn", StjWhenNull = "sw", Newtonsoft = "n", DataMember = "d", Custom = "c", Skip = "k" };
      }
    }

    public class BothJsonIgnores
    {
      public string Plain { get; set; }
      [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.Never), Newtonsoft.Json.JsonIgnore] public string Both { get; set; }
    }

    // Only used by FiltersOfOtherSettingsDoNotLeak, so the first settings that see it are the ones of that test
    public class FilterCacheProbe
    {
      public string Plain { get; set; }
      [XmlIgnore] public string Xml { get; set; }
      [Newtonsoft.Json.JsonIgnore] public string Newtonsoft { get; set; }
    }

    public class TextProbe
    {
      public string Text { get; set; }
      public string Initialized { get; set; } = "init";
      public object Boxed { get; set; }
      public int Number { get; set; }
    }

    private static MsgPackSettings Settings(IMsgPackPropertyIncludeStatically ignoreFilter, bool useSchema = false)
    {
      return new MsgPackSettings()
      {
        UseInexedSchema = useSchema,
        ObjectLayout = ObjectLayout.Map, // keys show what was written
        StaticFilters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(), ignoreFilter, new FilterStatic() }
      };
    }

    private static string[] Keys(byte[] bytes)
    {
      MpMap map = (MpMap)MsgPackItem.Unpack(bytes);
      return ((KeyValuePair<object, object>[])map.Value).Select(kv => (string)kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    private static string[] Sorted(params string[] keys)
    {
      return keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    [TestMethod]
    public void DefaultSettingsIgnoreAllKnownAttributes()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map };
      CollectionAssert.AreEqual(Sorted("Plain", "StjNever", "StjWhenNull", "Skip"), Keys(Serializer.Serialize(IgnoreProbe.Filled(), settings)));
    }

    // The rows only carry the name: Visual Studio serializes the arguments of each row (DataContractJsonSerializer), a filter does not round trip
    private static readonly Dictionary<string, (FilterIgnoredAttribute Filter, string[] Expected)> Presets = new Dictionary<string, (FilterIgnoredAttribute, string[])>()
    {
      { "All", (new FilterIgnoredAttribute(), Sorted("Plain", "StjNever", "StjWhenNull", "Skip")) },
      { "Newtonsoft", (FilterIgnoredAttribute.LikeNewtonsoft, Sorted("Plain", "Xml", "Stj", "StjNever", "StjWhenNull", "Custom", "Skip")) },
      { "SystemTextJson", (FilterIgnoredAttribute.LikeSystemTextJson, Sorted("Plain", "Xml", "StjNever", "StjWhenNull", "Newtonsoft", "DataMember", "Custom", "Skip")) },
      { "XmlSerializer", (FilterIgnoredAttribute.LikeXmlSerializer, Sorted("Plain", "Stj", "StjNever", "StjWhenNull", "Newtonsoft", "DataMember", "Custom", "Skip")) },
      { "DataContract", (FilterIgnoredAttribute.LikeDataContract, Sorted("Plain", "Xml", "Stj", "StjNever", "StjWhenNull", "Newtonsoft", "Custom", "Skip")) },
      { "OtherIgnore", (new FilterIgnoredAttribute(IgnoreAttributes.OtherIgnore), Sorted("Plain", "Xml", "Stj", "StjNever", "StjWhenNull", "Newtonsoft", "DataMember", "Skip")) },
      { "None", (new FilterIgnoredAttribute(IgnoreAttributes.None), Sorted("Plain", "Xml", "Stj", "StjNever", "StjWhenNull", "Newtonsoft", "DataMember", "Custom", "Skip")) },
      { "Name", (new FilterIgnoredAttribute(IgnoreAttributes.None, "SkipInMsgPack"), Sorted("Plain", "Xml", "Stj", "StjNever", "StjWhenNull", "Newtonsoft", "DataMember", "Custom")) },
      { "ClassName", (new FilterIgnoredAttribute(IgnoreAttributes.XmlIgnore, nameof(SkipInMsgPackAttribute)), Sorted("Plain", "Stj", "StjNever", "StjWhenNull", "Newtonsoft", "DataMember", "Custom")) },
      { "FullName", (new FilterIgnoredAttribute(IgnoreAttributes.None, typeof(SkipInMsgPackAttribute).FullName, typeof(CustomIgnoreMeAttribute).FullName), Sorted("Plain", "Xml", "Stj", "StjNever", "StjWhenNull", "Newtonsoft", "DataMember")) },
    };

    [TestMethod]
    [DataRow("All")]
    [DataRow("Newtonsoft")]
    [DataRow("SystemTextJson")]
    [DataRow("XmlSerializer")]
    [DataRow("DataContract")]
    [DataRow("OtherIgnore")]
    [DataRow("None")]
    [DataRow("Name")]
    [DataRow("ClassName")]
    [DataRow("FullName")]
    public void IgnoredAttributesAreConfigurable(string name)
    {
      FilterIgnoredAttribute filter = Presets[name].Filter;
      string[] expected = Presets[name].Expected;
      foreach (bool useSchema in new[] { false, true })
      {
        MsgPackSettings settings = Settings(filter, useSchema);
        byte[] bytes = Serializer.Serialize(IgnoreProbe.Filled(), settings);
        if (!useSchema)
          CollectionAssert.AreEqual(expected, Keys(bytes), name);

        // Reading uses the same filters: what was written comes back, the rest keeps the constructor's value
        IgnoreProbe read = Serializer.Deserialize<IgnoreProbe>(bytes, settings);
        IgnoreProbe filled = IgnoreProbe.Filled();
        foreach (System.Reflection.PropertyInfo prop in typeof(IgnoreProbe).GetProperties())
          Assert.AreEqual(expected.Contains(prop.Name) ? prop.GetValue(filled) : null, prop.GetValue(read), $"{name}, {prop.Name}, schema: {useSchema}");
      }
    }

    [TestMethod]
    public void BothJsonIgnoresOnOneProperty()
    {
      BothJsonIgnores value = new BothJsonIgnores() { Plain = "p", Both = "b" };
      // Json.NET ignores it, System.Text.Json writes it (Condition = Never)
      CollectionAssert.AreEqual(Sorted("Plain"), Keys(Serializer.Serialize(value, Settings(FilterIgnoredAttribute.LikeNewtonsoft))));
      CollectionAssert.AreEqual(Sorted("Both", "Plain"), Keys(Serializer.Serialize(value, Settings(FilterIgnoredAttribute.LikeSystemTextJson))));
      CollectionAssert.AreEqual(Sorted("Plain"), Keys(Serializer.Serialize(value, Settings(new FilterIgnoredAttribute()))));
    }

    /// <summary>
    /// Without a session (no indexed schema) the properties of a type are cached for all settings: per array of static filters, so other filters decide again.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FiltersOfOtherSettingsDoNotLeak(bool useSchema)
    {
      MsgPackSettings newtonsoft = Settings(FilterIgnoredAttribute.LikeNewtonsoft, useSchema);
      MsgPackSettings xml = Settings(FilterIgnoredAttribute.LikeXmlSerializer, useSchema);
      MsgPackSettings all = Settings(new FilterIgnoredAttribute(IgnoreAttributes.None), useSchema);
      MsgPackSettings defaults = new MsgPackSettings() { UseInexedSchema = useSchema, ObjectLayout = ObjectLayout.Map };
      FilterCacheProbe value = new FilterCacheProbe() { Plain = "p", Xml = "x", Newtonsoft = "n" };

      for (int round = 0; round < 2; round++)
      {
        Assert.AreEqual("x", Serializer.Deserialize<FilterCacheProbe>(Serializer.Serialize(value, newtonsoft), newtonsoft).Xml);
        Assert.IsNull(Serializer.Deserialize<FilterCacheProbe>(Serializer.Serialize(value, newtonsoft), newtonsoft).Newtonsoft);
        Assert.IsNull(Serializer.Deserialize<FilterCacheProbe>(Serializer.Serialize(value, xml), xml).Xml);
        Assert.AreEqual("n", Serializer.Deserialize<FilterCacheProbe>(Serializer.Serialize(value, xml), xml).Newtonsoft);

        // Written with every property, read with the filters of the reader
        byte[] everything = Serializer.Serialize(value, all);
        FilterCacheProbe readByDefaults = Serializer.Deserialize<FilterCacheProbe>(everything, defaults);
        Assert.IsNull(readByDefaults.Xml);
        Assert.IsNull(readByDefaults.Newtonsoft);
        FilterCacheProbe readByNewtonsoft = Serializer.Deserialize<FilterCacheProbe>(everything, newtonsoft);
        Assert.AreEqual("x", readByNewtonsoft.Xml);
        Assert.IsNull(readByNewtonsoft.Newtonsoft);

        if (!useSchema)
        {
          CollectionAssert.AreEqual(Sorted("Newtonsoft", "Plain", "Xml"), Keys(everything));
          CollectionAssert.AreEqual(Sorted("Plain", "Xml"), Keys(Serializer.Serialize(value, newtonsoft)));
          CollectionAssert.AreEqual(Sorted("Plain"), Keys(Serializer.Serialize(value, defaults)));
          CollectionAssert.AreEqual(Sorted("Newtonsoft", "Plain"), Keys(Serializer.Serialize(value, xml)));
        }
      }
    }

    /// <summary>
    /// An empty string is not the default value of a string (null), the JSON serializers write it too.
    /// </summary>
    [TestMethod]
    [DataRow(false, ObjectLayout.Map)]
    [DataRow(true, ObjectLayout.Map)]
    [DataRow(false, ObjectLayout.Array)]
    [DataRow(true, ObjectLayout.Array)]
    public void EmptyStringsAreWritten(bool useSchema, ObjectLayout layout)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema, ObjectLayout = layout };
      TextProbe value = new TextProbe() { Text = "", Initialized = "", Boxed = "" };

      byte[] bytes = Serializer.Serialize(value, settings);
      if (!useSchema && layout == ObjectLayout.Map)
        CollectionAssert.AreEqual(Sorted("Boxed", "Initialized", "Text"), Keys(bytes));

      TextProbe read = Serializer.Deserialize<TextProbe>(bytes, settings);
      Assert.AreEqual("", read.Text);
      Assert.AreEqual("", read.Initialized);
      Assert.AreEqual("", read.Boxed);
      Assert.AreEqual(0, read.Number);
    }

    [TestMethod]
    [DataRow(false, ObjectLayout.Map)]
    [DataRow(true, ObjectLayout.Map)]
    [DataRow(false, ObjectLayout.Array)]
    [DataRow(true, ObjectLayout.Array)]
    public void EmptyStringsAreOmittedWhenAsked(bool useSchema, ObjectLayout layout)
    {
      MsgPackSettings settings = new MsgPackSettings()
      {
        UseInexedSchema = useSchema,
        ObjectLayout = layout,
        DynamicFilters = new IMsgPackPropertyIncludeDynamically[] { new FilterDefaultValues(omitEmptyStrings: true) }
      };
      TextProbe value = new TextProbe() { Text = "", Initialized = "", Boxed = "", Number = 1 };

      byte[] bytes = Serializer.Serialize(value, settings);
      if (!useSchema && layout == ObjectLayout.Map)
        CollectionAssert.AreEqual(Sorted("Number"), Keys(bytes));

      // Left out, so the reader keeps what the constructor set
      TextProbe read = Serializer.Deserialize<TextProbe>(bytes, settings);
      Assert.IsNull(read.Text);
      Assert.AreEqual("init", read.Initialized);
      Assert.IsNull(read.Boxed);
      Assert.AreEqual(1, read.Number);
    }
  }

  [TestClass]
  public class LsFilterTests : FilterTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtFilterTests : FilterTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }
}
