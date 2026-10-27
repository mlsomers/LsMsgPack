using LsMsgPack;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// An opt-in attribute of the application's own (recognized by "Include" in its name, see <see cref="NonPublicProperties.Attributed"/>).
  /// </summary>
  [AttributeUsage(AttributeTargets.Property)]
  public sealed class NppIncludeAttribute : Attribute
  {
  }

  public interface INppFace
  {
    int Face { get; set; }
  }

  /// <summary>
  /// Non-public properties (<see cref="NonPublicProperties"/>, an option of FilterNonSettable since October 2026, none by default):
  /// the ones with an opt-in attribute of another serializer, or all of them.
  /// </summary>
  public abstract class SerializingNonPublicProperties
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    public class NppBase
    {
      [JsonInclude]
      private string BaseSecret { get; set; }

      public string GetBaseSecret() { return BaseSecret; }
      public void SetBaseSecret(string value) { BaseSecret = value; }
    }

    public class NppAccount : NppBase, INppFace
    {
      public string Name { get; set; }

      [JsonInclude]
      private int Pin { get; set; }

      [DataMember]
      internal string Note { get; set; }

      [Newtonsoft.Json.JsonProperty]
      protected decimal Balance { get; set; }

      [JsonInclude]
      public string Owner { get; private set; }

      [NppInclude]
      private Guid Token { get; set; }

      [JsonInclude]
      private int Doubled { get { return Pin * 2; } } // computed: written, skipped when read

      [JsonInclude, IgnoreDataMember] // the ignore attribute wins (FilterIgnoredAttribute)
      private int Ignored { get; set; }

      private string Unmarked { get; set; } // NonPublicProperties.All only

      int INppFace.Face { get; set; } // an explicit interface implementation: never

      public static NppAccount Sample()
      {
        NppAccount account = new NppAccount() { Name = "n", Pin = 1234, Note = "note", Balance = 12.5m, Owner = "o", Token = new Guid("11111111-2222-3333-4444-555555555555"), Ignored = 9, Unmarked = "u" };
        ((INppFace)account).Face = 3;
        account.SetBaseSecret("base");
        return account;
      }

      /// <summary>
      /// The non-public values, as text (the test cannot reach them otherwise).
      /// </summary>
      public string Secrets()
      {
        return string.Join("|", GetBaseSecret(), Pin, Note, Balance, Owner, Token, Ignored, Unmarked, ((INppFace)this).Face);
      }
    }

    /// <summary>
    /// A record has a protected EqualityContract (a Type, which would be refused) that the compiler generates: never serialized.
    /// </summary>
    public record NppRecord(string Name)
    {
      private int Hidden { get; set; }

      public int GetHidden() { return Hidden; }
      public void SetHidden(int value) { Hidden = value; }
    }

    public class NppHolder
    {
      public List<NppAccount> Accounts { get; set; }

      [JsonInclude]
      private NppAccount Main { get; set; }

      public NppAccount GetMain() { return Main; }
      public void SetMain(NppAccount value) { Main = value; }
    }

    private static MsgPackSettings Settings(bool schema, ObjectLayout layout, NonPublicProperties nonPublic)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = schema, ObjectLayout = layout };
      if (nonPublic != NonPublicProperties.None)
        settings.StaticFilters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(GetOnlyProperties.All, nonPublic), new FilterIgnoredAttribute(), new FilterStatic() };
      return settings;
    }

    private static IEnumerable<object[]> AllSettings()
    {
      foreach (bool schema in new[] { true, false })
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
          yield return new object[] { schema, layout };
    }

    private T RoundTrip<T>(T value, MsgPackSettings settings)
    {
      byte[] bytes = Serializer.Serialize(value, settings);
      return Serializer.Deserialize<T>(bytes, settings);
    }

    /// <summary>
    /// The names of the properties written (the map layout without the schema).
    /// </summary>
    private string WrittenNames<T>(T value, NonPublicProperties nonPublic)
    {
      byte[] bytes = Serializer.Serialize(value, Settings(false, ObjectLayout.Map, nonPublic));
      Dictionary<string, object> map = Serializer.Deserialize<Dictionary<string, object>>(bytes, Settings(false, ObjectLayout.Map, NonPublicProperties.None));
      return string.Join(", ", map.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [TestMethod]
    public void Default_OnlyPublic()
    {
      Assert.AreEqual("Name", WrittenNames(NppAccount.Sample(), NonPublicProperties.None), "as before: Owner has a private setter");
    }

    [TestMethod]
    public void Attributed_Written()
    {
      Assert.AreEqual("Balance, BaseSecret, Doubled, Name, Note, Owner, Pin, Token", WrittenNames(NppAccount.Sample(), NonPublicProperties.Attributed));
    }

    [TestMethod]
    public void All_Written()
    {
      Assert.AreEqual("Balance, BaseSecret, Doubled, Name, Note, Owner, Pin, Token, Unmarked", WrittenNames(NppAccount.Sample(), NonPublicProperties.All), "not the ignored one nor the explicit implementation");
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Default_NotRead(bool schema, ObjectLayout layout)
    {
      NppAccount read = RoundTrip(NppAccount.Sample(), Settings(schema, layout, NonPublicProperties.None));
      Assert.AreEqual("n", read.Name);
      Assert.AreEqual("|0||0||00000000-0000-0000-0000-000000000000|0||0", read.Secrets());
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Attributed_RoundTrip(bool schema, ObjectLayout layout)
    {
      NppAccount read = RoundTrip(NppAccount.Sample(), Settings(schema, layout, NonPublicProperties.Attributed));
      Assert.AreEqual("n", read.Name);
      Assert.AreEqual("base|1234|note|12.5|o|11111111-2222-3333-4444-555555555555|0||0", read.Secrets());
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void All_RoundTrip(bool schema, ObjectLayout layout)
    {
      NppAccount read = RoundTrip(NppAccount.Sample(), Settings(schema, layout, NonPublicProperties.All));
      Assert.AreEqual("n", read.Name);
      Assert.AreEqual("base|1234|note|12.5|o|11111111-2222-3333-4444-555555555555|0|u|0", read.Secrets());
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Nested_RoundTrip(bool schema, ObjectLayout layout)
    {
      NppHolder holder = new NppHolder() { Accounts = new List<NppAccount>() { NppAccount.Sample() } };
      holder.SetMain(NppAccount.Sample());
      NppHolder read = RoundTrip(holder, Settings(schema, layout, NonPublicProperties.Attributed));
      Assert.AreEqual("base|1234|note|12.5|o|11111111-2222-3333-4444-555555555555|0||0", read.Accounts.Single().Secrets());
      Assert.AreEqual("base|1234|note|12.5|o|11111111-2222-3333-4444-555555555555|0||0", read.GetMain().Secrets());
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Record_All(bool schema, ObjectLayout layout)
    {
      NppRecord record = new NppRecord("r");
      record.SetHidden(5);
      NppRecord read = RoundTrip(record, Settings(schema, layout, NonPublicProperties.All));
      Assert.AreEqual("r", read.Name);
      Assert.AreEqual(5, read.GetHidden());
      Assert.AreEqual("Hidden, Name", WrittenNames(record, NonPublicProperties.All), "not EqualityContract");
    }

    [TestMethod]
    public void Filter_RefusesUnknownValues()
    {
      Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new FilterNonSettable(GetOnlyProperties.All, (NonPublicProperties)3));
    }
  }

  [TestClass]
  public class LsSerializingNonPublicProperties : SerializingNonPublicProperties
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtSerializingNonPublicProperties : SerializingNonPublicProperties
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }

  [TestClass]
  public class GenSerializingNonPublicProperties : SerializingNonPublicProperties
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
