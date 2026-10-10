#if KEEPTRACK
using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ObjectDebugger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static LsMsgPack.MsgPackValidation;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// The validation of the explorers: <see cref="MsgPackValidation"/> per item, <see cref="PayloadValidation"/> for the schema, the type ids and the places declared as object.
  /// </summary>
  [TestClass]
  public class ValidationTests
  {
    public class VtAddress
    {
      public string Street { get; set; }
      public int Number { get; set; }
    }

    public class VtHolder
    {
      public string Name { get; set; }
      public object Anything { get; set; }
      public VtAddress Address { get; set; }
    }

    public class VtBase
    {
      public string Name { get; set; }
    }

    public class VtDerived : VtBase
    {
      public object Extra { get; set; }
    }

    public class VtBag
    {
      public List<object> Items { get; set; }
      public Dictionary<string, object> Values { get; set; }
    }

    public class VtTyped
    {
      public DateTime When { get; set; }
      public DateTimeOffset Offset { get; set; }
      public decimal Amount { get; set; }
      public BigInteger Big { get; set; }
      public Guid Id { get; set; }
      public int[,] Grid { get; set; }
      public Tuple<int, string> Pair { get; set; }
      public List<VtAddress> Addresses { get; set; }
      public Dictionary<string, int> Counts { get; set; }
    }

    private static List<ValidationItem> Validate(byte[] bytes)
    {
      MsgPackSettings settings = new MsgPackSettings() { PreservePackages = true, ContinueProcessingOnBreakingError = true, DynamicallyCompact = false };
      MpRoot root = MsgPackItem.UnpackMultiple(bytes, settings);
      MsgPackItem item = root.Count == 1 ? root[0] : root;

      List<ValidationItem> issues = new List<ValidationItem>();
      AddItemIssues(item, issues);
      RootObject objects = new RootObject();
      objects.Reconstruct(item);
      issues.AddRange(PayloadValidation.Validate(objects));
      for (int t = 0; t < issues.Count; t++)
        Assert.IsNotNull(issues[t].Item, issues[t].Message);
      return issues;
    }

    private static void AddItemIssues(MsgPackItem item, List<ValidationItem> issues)
    {
      if (item is null)
        return;
      if (item is MpRoot root)
        foreach (MsgPackItem child in root)
          AddItemIssues(child, issues);
      else if (item is MpArray array)
        foreach (MsgPackItem child in array.PackedValues)
          AddItemIssues(child, issues);
      else if (item is MpMap map)
        foreach (KeyValuePair<MsgPackItem, MsgPackItem> entry in map.PackedValues)
        {
          AddItemIssues(entry.Key, issues);
          AddItemIssues(entry.Value, issues);
        }
      issues.AddRange(MsgPackValidation.ValidateItem(item, 1000));
    }

    private static string Describe(List<ValidationItem> issues)
    {
      return string.Join(Environment.NewLine, issues.Select(i => string.Concat(i.Severity, ": ", i.Message)));
    }

    private static List<ValidationItem> Of(List<ValidationItem> issues, ValidationSeverity severity)
    {
      return issues.Where(i => i.Severity == severity).ToList();
    }

    private static MsgPackSettings Settings(bool schema, ObjectLayout layout = ObjectLayout.Array)
    {
      return new MsgPackSettings() { UseInexedSchema = schema, ObjectLayout = layout };
    }

    #region Items

    [TestMethod]
    public void WrittenFrameworkTypes_NoIssues()
    {
      VtTyped value = new VtTyped()
      {
        When = new DateTime(2026, 10, 10, 12, 30, 15, 123, DateTimeKind.Utc),
        Offset = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.FromHours(2)),
        Amount = 12.345m,
        Big = BigInteger.Parse("123456789012345678901234567890"),
        Id = Guid.NewGuid(),
        Grid = new int[,] { { 1, 2, 3 }, { 4, 5, 6 } },
        Pair = Tuple.Create(1, "one"),
        Addresses = new List<VtAddress>() { new VtAddress() { Street = "Main", Number = 1 } },
        Counts = new Dictionary<string, int>() { { "a", 1 }, { "b", 2 } }
      };
      foreach (bool schema in new[] { true, false })
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
        {
          List<ValidationItem> issues = Validate(MsgPackSerializer.Serialize(value, Settings(schema, layout)));
          Assert.AreEqual(0, issues.Count(i => i.Severity != ValidationSeverity.Comment), $"schema {schema}, {layout}:{Environment.NewLine}{Describe(issues)}");
        }
    }

    [TestMethod]
    public void Timestamp96_ThatFitsIn64_Wastes5Bytes()
    {
      // ext 8, 12 bytes, type -1: nanoseconds 500, seconds 1000
      byte[] bytes = { 0xC7, 12, 0xFF, 0, 0, 0x01, 0xF4, 0, 0, 0, 0, 0, 0, 0x03, 0xE8 };
      List<ValidationItem> issues = Validate(bytes);
      ValidationItem issue = issues.Single(i => i.Message.Contains("timestamp 64"));
      Assert.AreEqual(ValidationSeverity.Warning, issue.Severity);
      Assert.AreEqual(5, issue.WaistedBytes);
    }

    [TestMethod]
    public void Timestamp64_WithoutNanoseconds_Wastes4Bytes()
    {
      byte[] bytes = { 0xD7, 0xFF, 0, 0, 0, 0, 0, 0, 0x03, 0xE8 };
      ValidationItem issue = Validate(bytes).Single(i => i.Message.Contains("timestamp 32"));
      Assert.AreEqual(4, issue.WaistedBytes);
    }

    [TestMethod]
    public void Timestamp_NanosecondsBeyondASecond_IsAnError()
    {
      // timestamp 64: nanoseconds 1000000000 (30 bits) followed by 34 bits of seconds
      ulong both = (1000000000UL << 34) | 1000UL;
      byte[] value = BitConverter.GetBytes(both);
      if (BitConverter.IsLittleEndian)
        Array.Reverse(value);
      byte[] bytes = new byte[] { 0xD7, 0xFF }.Concat(value).ToArray();
      List<ValidationItem> issues = Validate(bytes);
      Assert.IsTrue(Of(issues, ValidationSeverity.Error).Any(i => i.Message.Contains("nanoseconds")), Describe(issues));
    }

    [TestMethod]
    public void BigInteger_ThatFitsIn64Bits_ShouldBeAnInteger()
    {
      // ext -2 holding 1000 (big-endian 0x03E8) in fixext 2: 4 bytes, an integer takes 3
      ValidationItem issue = Validate(new byte[] { 0xD5, 0xFE, 0x03, 0xE8 }).Single(i => i.Message.Contains("fits in 64 bits"));
      Assert.AreEqual(ValidationSeverity.Warning, issue.Severity);
      Assert.AreEqual(1, issue.WaistedBytes);
    }

    [TestMethod]
    public void BigInteger_WithRedundantSignBytes_IsNotMinimal()
    {
      // ext 8, 10 bytes, type -2: 0x00 0x00 followed by 8 bytes starting below 0x80 (2 redundant bytes, the rest does not fit in 64 bits... it does: 8 bytes)
      // 11 bytes: 0xFF 0xFF and 9 bytes from 0x80: -2^71 is 9 bytes minimal, 2 redundant
      byte[] content = { 0xFF, 0xFF, 0x80, 0, 0, 0, 0, 0, 0, 0, 0 };
      byte[] bytes = new byte[] { 0xC7, (byte)content.Length, 0xFE }.Concat(content).ToArray();
      ValidationItem issue = Validate(bytes).Single(i => i.Message.Contains("repeat the sign"));
      Assert.AreEqual(2, issue.WaistedBytes);
    }

    [TestMethod]
    public void Decimal_WithAScaleBeyond28_IsAnError()
    {
      byte[] bytes = MsgPackSerializer.Serialize(1.5m, Settings(false));
      // the flags are the first 4 bytes of the content (after fixext 16 and the type): the scale is their third byte
      Assert.AreEqual(0xD8, bytes[0]);
      bytes[2 + 2] = 29;
      List<ValidationItem> issues = Validate(bytes);
      Assert.IsTrue(Of(issues, ValidationSeverity.Error).Any(i => i.Message.Contains("not a valid decimal")), Describe(issues));
    }

    [TestMethod]
    public void TypeIdKey_InAMapWithIndexes_NotInconsistent()
    {
      // { "": 0, 0: "a", 1: 2 }: an object with its type id, the property ids are indexes of the schema
      MpMap body = new MpMap(new KeyValuePair<object, object>[]
      {
        new KeyValuePair<object, object>("", 0),
        new KeyValuePair<object, object>(0, "a"),
        new KeyValuePair<object, object>(1, 2)
      }, new MsgPackSettings());
      List<ValidationItem> issues = Validate(body.ToBytes());
      Assert.IsFalse(issues.Any(i => i.Message.Contains("not appear to be consistent")), Describe(issues));

      // Other string keys still are
      MpMap mixed = new MpMap(new KeyValuePair<object, object>[]
      {
        new KeyValuePair<object, object>("", 0),
        new KeyValuePair<object, object>(0, "a"),
        new KeyValuePair<object, object>("b", 2)
      }, new MsgPackSettings());
      issues = Validate(mixed.ToBytes());
      Assert.IsTrue(issues.Any(i => i.Message.Contains("Item 1 has a key of type positive fixint while item 2 has a key of type fixstr")), Describe(issues));
    }

    [TestMethod]
    public void ObjectWithTypeIds_MapLayoutWithSchema_NoWarnings()
    {
      List<VtBase> values = new List<VtBase>() { new VtDerived() { Name = "a", Extra = new VtAddress() { Street = "Main", Number = 1 } } };
      List<ValidationItem> issues = Validate(MsgPackSerializer.Serialize(values, Settings(true, ObjectLayout.Map)));
      Assert.AreEqual(0, Of(issues, ValidationSeverity.Warning).Count, Describe(issues));
    }

    [TestMethod]
    public void ReservedExtensionType_IsAWarning()
    {
      List<ValidationItem> issues = Validate(new byte[] { 0xD4, 0xFB, 0x01 }); // fixext 1, type -5
      Assert.IsTrue(Of(issues, ValidationSeverity.Warning).Any(i => i.Message.Contains("reserved")), Describe(issues));
    }

    #endregion

    #region Schema

    [TestMethod]
    public void PlainModel_NoSecurityIssues()
    {
      VtHolder holder = new VtHolder() { Name = "a", Address = new VtAddress() { Street = "Main", Number = 3 } };
      foreach (bool schema in new[] { true, false })
      {
        List<ValidationItem> issues = Validate(MsgPackSerializer.Serialize(holder, Settings(schema)));
        Assert.AreEqual(0, Of(issues, ValidationSeverity.SecurityVulnerability).Count, Describe(issues));
      }
    }

    [TestMethod]
    public void ObjectProperty_HoldingAWrappedValue_IsASecurityVulnerability()
    {
      VtHolder holder = new VtHolder() { Name = "a", Anything = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
      foreach (bool schema in new[] { true, false })
      {
        List<ValidationItem> issues = Validate(MsgPackSerializer.Serialize(holder, Settings(schema)));
        ValidationItem issue = Of(issues, ValidationSeverity.SecurityVulnerability).Single();
        if (schema) // without it the array of values is not known to be an object (path [1])
          StringAssert.Contains(issue.Message, "Anything");
        StringAssert.Contains(issue.Message, "DateTime");
      }
    }

    [TestMethod]
    public void ObjectProperty_HoldingAFrameworkCollection_IsASecurityVulnerability()
    {
      VtHolder holder = new VtHolder() { Name = "a", Anything = new List<object>() { 1, "two" } };
      List<ValidationItem> issues = Validate(MsgPackSerializer.Serialize(holder, Settings(true)));
      ValidationItem issue = Of(issues, ValidationSeverity.SecurityVulnerability).Single();
      StringAssert.Contains(issue.Message, "List<Object>");
      StringAssert.Contains(issue.Message, "elements are declared as object");
    }

    [TestMethod]
    public void ObjectProperty_HoldingDifferentKinds_IsASecurityVulnerability()
    {
      // Integers and strings are never wrapped: objects with a type id show the same property holding both
      List<VtBase> values = new List<VtBase>() { new VtDerived() { Name = "a", Extra = 1 }, new VtDerived() { Name = "b", Extra = "text" } };
      foreach (bool schema in new[] { true, false })
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
        {
          if (!schema && layout == ObjectLayout.Array)
            continue; // the values of a type that is not in a schema have no names, nothing tells which ones belong together
          List<ValidationItem> issues = Validate(MsgPackSerializer.Serialize(values, Settings(schema, layout)));
          Assert.AreEqual(1, Of(issues, ValidationSeverity.SecurityVulnerability).Count, $"schema {schema}, {layout}:{Environment.NewLine}{Describe(issues)}");
          ValidationItem issue = Of(issues, ValidationSeverity.SecurityVulnerability).Single();
          StringAssert.Contains(issue.Message, "Extra");
          StringAssert.Contains(issue.Message, "holds both");
        }
    }

    [TestMethod]
    public void InferredObjects_DifferentKinds_NotReported()
    {
      // Without type ids the types are inferred, which may be wrong: not a reason to report
      List<VtHolder> holders = new List<VtHolder>() { new VtHolder() { Name = "a", Anything = 1 }, new VtHolder() { Name = "b", Anything = "text" } };
      List<ValidationItem> issues = Validate(MsgPackSerializer.Serialize(holders, Settings(true)));
      Assert.AreEqual(0, Of(issues, ValidationSeverity.SecurityVulnerability).Count, Describe(issues));
    }

    [TestMethod]
    public void DictionaryOfObjects_HoldingDifferentKinds_IsASecurityVulnerability()
    {
      VtBag bag = new VtBag() { Values = new Dictionary<string, object>() { { "a", 1 }, { "b", "two" }, { "c", 3.5 } } };
      List<ValidationItem> issues = Validate(MsgPackSerializer.Serialize(bag, Settings(true)));
      Assert.AreEqual(1, Of(issues, ValidationSeverity.SecurityVulnerability).Count, Describe(issues));
    }

    [TestMethod]
    public void TypeIds_PerPlace_ReportedOnce()
    {
      VtBag bag = new VtBag() { Items = new List<object>() { Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow } };
      List<ValidationItem> issues = Validate(MsgPackSerializer.Serialize(bag, Settings(true)));
      ValidationItem issue = Of(issues, ValidationSeverity.SecurityVulnerability).Single();
      StringAssert.Contains(issue.Message, "3 such values");
    }

    /// <summary>
    /// { "Process": ["FileName"] } followed by [ { "": 0, ... } ]: a schema made to attack a reader.
    /// </summary>
    [TestMethod]
    public void Schema_NamingAGadget_IsASecurityVulnerability()
    {
      MpMap schema = new MpMap(new KeyValuePair<object, object>[]
      {
        new KeyValuePair<object, object>("Process", new object[] { "FileName" })
      }, new MsgPackSettings());
      MpMap body = new MpMap(new KeyValuePair<object, object>[]
      {
        new KeyValuePair<object, object>("", 0),
        new KeyValuePair<object, object>(0, "calc.exe")
      }, new MsgPackSettings());
      byte[] bytes = schema.ToBytes().Concat(body.ToBytes()).ToArray();
      List<ValidationItem> issues = Validate(bytes);
      Assert.IsTrue(Of(issues, ValidationSeverity.SecurityVulnerability).Any(i => i.Message.Contains("gadget")), Describe(issues));
    }

    [TestMethod]
    public void TypeId_AssemblyQualified_IsASecurityVulnerability()
    {
      MpMap body = new MpMap(new KeyValuePair<object, object>[]
      {
        new KeyValuePair<object, object>("", "MyApp.Foo, MyApp"),
        new KeyValuePair<object, object>("Name", "x")
      }, new MsgPackSettings());
      List<ValidationItem> issues = Validate(body.ToBytes());
      Assert.IsTrue(Of(issues, ValidationSeverity.SecurityVulnerability).Any(i => i.Message.Contains("assembly name")), Describe(issues));
    }

    [TestMethod]
    public void Schema_DuplicatePropertyNames_IsAnError()
    {
      MpMap schema = new MpMap(new KeyValuePair<object, object>[]
      {
        new KeyValuePair<object, object>("Foo", new object[] { "A", "B", "A" })
      }, new MsgPackSettings());
      MpArray body = new MpArray(new MsgPackSettings()) { Value = new object[] { 1, 2, 3 } };
      List<ValidationItem> issues = Validate(schema.ToBytes().Concat(body.ToBytes()).ToArray());
      Assert.IsTrue(Of(issues, ValidationSeverity.Error).Any(i => i.Message.Contains("twice")), Describe(issues));
    }

    [TestMethod]
    public void TypeId_NotInTheSchema_IsAnError()
    {
      MpMap schema = new MpMap(new KeyValuePair<object, object>[]
      {
        new KeyValuePair<object, object>("Foo", new object[] { "A" })
      }, new MsgPackSettings());
      MpMap body = new MpMap(new KeyValuePair<object, object>[]
      {
        new KeyValuePair<object, object>("", 5),
        new KeyValuePair<object, object>(0, "x")
      }, new MsgPackSettings());
      List<ValidationItem> issues = Validate(schema.ToBytes().Concat(body.ToBytes()).ToArray());
      Assert.IsTrue(Of(issues, ValidationSeverity.Error).Any(i => i.Message.Contains("is not in the schema")), Describe(issues));
    }

    [TestMethod]
    public void MultidimensionalArray_WithTooFewItems_IsAnError()
    {
      MpMap body = new MpMap(new KeyValuePair<object, object>[]
      {
        new KeyValuePair<object, object>("", "Int32[,]"),
        new KeyValuePair<object, object>("@", new object[] { 2, 3, new object[] { 1, 2, 3 } })
      }, new MsgPackSettings());
      List<ValidationItem> issues = Validate(body.ToBytes());
      Assert.IsTrue(Of(issues, ValidationSeverity.Error).Any(i => i.Message.Contains("make 6 items")), Describe(issues));
    }

    [TestMethod]
    public void DateTimeOffset_WithAnOffsetBeyond14Hours_IsAnError()
    {
      MpMap body = new MpMap(new KeyValuePair<object, object>[]
      {
        new KeyValuePair<object, object>("", "DateTimeOffset"),
        new KeyValuePair<object, object>("@", new object[] { new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 900 })
      }, new MsgPackSettings());
      List<ValidationItem> issues = Validate(body.ToBytes());
      Assert.IsTrue(Of(issues, ValidationSeverity.Error).Any(i => i.Message.Contains("14 hours")), Describe(issues));
    }

    #endregion
  }
}
#endif
