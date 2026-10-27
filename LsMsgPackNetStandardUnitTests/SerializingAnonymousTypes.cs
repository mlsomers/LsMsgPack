using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Anonymous types (<c>return Ok(new { Id = 1, Name = "x" })</c>): their properties have no setters but are written (they used to be an empty map), and read into classes with the same names.
  /// <para>Where the type is declared (the root, the elements of a root collection) an anonymous object is written like any class, with an entry in the schema under its name without the compiler's prefix.
  /// In a place declared as object, below another anonymous object, and below a value with an anonymous type that is not written as its declared type, it is a map of names: no type id, not in the schema.</para>
  /// </summary>
  public abstract class SerializingAnonymousTypes
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    public class AnonDto
    {
      public int Id { get; set; }
      public string Name { get; set; }
    }

    public class AnonWrapper
    {
      public object Data { get; set; }
    }

    public class AnonDtoWrapper
    {
      public AnonDto Data { get; set; }
    }

    public class AnonListWrapper
    {
      public List<AnonDto> Data { get; set; }
    }

    public class AnonOrder
    {
      public int Total { get; set; }
      public AnonDto Customer { get; set; }
      public List<AnonDto> Lines { get; set; }
    }

    public class AnonPage<T>
    {
      public T Data { get; set; }
      public int Count { get; set; }
    }

    private static AnonPage<T> PageOf<T>(T data)
    {
      return new AnonPage<T>() { Data = data, Count = 1 };
    }

    private static MsgPackSettings Settings(bool schema, ObjectLayout layout)
    {
      return new MsgPackSettings() { UseInexedSchema = schema, ObjectLayout = layout };
    }

    private static IEnumerable<object[]> AllSettings()
    {
      foreach (bool schema in new[] { true, false })
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
          yield return new object[] { schema, layout };
    }

    private static string Text(AnonDto dto)
    {
      return dto is null ? "null" : $"{dto.Id} {dto.Name}";
    }

    private static string Text(object value)
    {
      if (value is KeyValuePair<object, object>[] map)
        return "{" + string.Join(", ", map.Select(p => $"{p.Key}: {Text(p.Value)}")) + "}";
      if (value is object[] items)
        return "[" + string.Join(", ", items.Select(Text)) + "]";
      return value?.ToString() ?? "null";
    }

    private static bool Contains(byte[] bytes, string text)
    {
      return Encoding.UTF8.GetString(bytes).Contains(text, StringComparison.Ordinal);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Root_ReadAsClass(bool schema, ObjectLayout layout)
    {
      var value = new { Id = 1, Name = "x" };
      byte[] bytes = Serializer.Serialize(value, Settings(schema, layout));
      Assert.AreEqual("1 x", Text(Serializer.Deserialize<AnonDto>(bytes, Settings(schema, layout))));
      Assert.IsFalse(Contains(bytes, "<>"), "the compiler's prefix is not written");
      if (schema) // the compiler numbers the anonymous types (AnonymousType0...), differently per configuration
      {
        Assert.AreEqual(0x81, bytes[0], "one entry");
        Assert.IsTrue(Contains(bytes, "<Int32, String>") && Contains(bytes, "AnonymousType"), "the root is in the schema, as any class");
      }
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void DeclaredAsObject_IsAMapOfNames(bool schema, ObjectLayout layout)
    {
      AnonWrapper wrapper = new AnonWrapper() { Data = new { Id = 1, Name = "x" } };
      byte[] bytes = Serializer.Serialize(wrapper, Settings(schema, layout));
      Assert.IsFalse(Contains(bytes, "AnonymousType"), "no type id, not in the schema");
      Assert.AreEqual("{Id: 1, Name: x}", Text(Serializer.Deserialize<AnonWrapper>(bytes, Settings(schema, layout)).Data));
      Assert.AreEqual("1 x", Text(Serializer.Deserialize<AnonDtoWrapper>(bytes, Settings(schema, layout)).Data));

      byte[] root = Serializer.Serialize(wrapper.Data, typeof(object), Settings(schema, layout));
      Assert.AreEqual("{Id: 1, Name: x}", Text(Serializer.Deserialize<object>(root, Settings(schema, layout))));
      Assert.AreEqual("1 x", Text(Serializer.Deserialize<AnonDto>(root, Settings(schema, layout))));
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Nested_ReadAsClasses(bool schema, ObjectLayout layout)
    {
      var order = new { Total = 5, Customer = new { Id = 2, Name = "c" }, Lines = new[] { new { Id = 3, Name = "l" }, new { Id = 4, Name = "m" } } };
      byte[] bytes = Serializer.Serialize(order, Settings(schema, layout));
      AnonOrder read = Serializer.Deserialize<AnonOrder>(bytes, Settings(schema, layout));
      Assert.AreEqual(5, read.Total);
      Assert.AreEqual("2 c", Text(read.Customer));
      Assert.AreEqual("3 l, 4 m", string.Join(", ", read.Lines.Select(Text)));
      if (schema) // the root has an entry, the anonymous objects below it are maps of names
        Assert.AreEqual(0x81, bytes[0], "one entry");
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void NestedInObjectProperty_AllByNames(bool schema, ObjectLayout layout)
    {
      AnonWrapper wrapper = new AnonWrapper() { Data = new { Total = 5, Customer = new { Id = 2, Name = "c" } } };
      byte[] bytes = Serializer.Serialize(wrapper, Settings(schema, layout));
      Assert.AreEqual("{Total: 5, Customer: {Id: 2, Name: c}}", Text(Serializer.Deserialize<AnonWrapper>(bytes, Settings(schema, layout)).Data));
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void RootCollection_ReadAsListOfClasses(bool schema, ObjectLayout layout)
    {
      var items = new[] { 1, 2 }.Select(i => new { Id = i, Name = "n" + i });
      byte[] bytes = Serializer.Serialize(items, items.GetType(), Settings(schema, layout));
      Assert.AreEqual("1 n1, 2 n2", string.Join(", ", Serializer.Deserialize<List<AnonDto>>(bytes, Settings(schema, layout)).Select(Text)));
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void ListInObjectProperty_NoTypeId_ElementsByNames(bool schema, ObjectLayout layout)
    {
      AnonWrapper wrapper = new AnonWrapper() { Data = new[] { 1, 2 }.Select(i => new { Id = i, Name = "n" + i }).ToList() };
      byte[] bytes = Serializer.Serialize(wrapper, Settings(schema, layout));
      Assert.IsFalse(Contains(bytes, "AnonymousType"), "no type id, not in the schema");
      Assert.AreEqual("[{Id: 1, Name: n1}, {Id: 2, Name: n2}]", Text(Serializer.Deserialize<AnonWrapper>(bytes, Settings(schema, layout)).Data));
      Assert.AreEqual("1 n1, 2 n2", string.Join(", ", Serializer.Deserialize<AnonListWrapper>(bytes, Settings(schema, layout)).Data.Select(Text)));
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void GenericClassOfAnonymousType(bool schema, ObjectLayout layout)
    {
      var page = PageOf(new { Id = 1, Name = "x" });
      byte[] bytes = Serializer.Serialize(page, Settings(schema, layout));
      AnonPage<AnonDto> read = Serializer.Deserialize<AnonPage<AnonDto>>(bytes, Settings(schema, layout));
      Assert.AreEqual(1, read.Count);
      Assert.AreEqual("1 x", Text(read.Data));

      // Declared as object: no type id (no reader resolves the name), a map of names
      byte[] wrapped = Serializer.Serialize(new AnonWrapper() { Data = page }, Settings(schema, layout));
      Assert.IsFalse(Contains(wrapped, "AnonymousType"), "no type id");
      Assert.AreEqual("{Data: {Id: 1, Name: x}, Count: 1}", Text(Serializer.Deserialize<AnonWrapper>(wrapped, Settings(schema, layout)).Data));
    }

    [TestMethod]
    public void TypeIdsAlways_NoneForAnonymousTypes()
    {
      MsgPackSettings settings = new MsgPackSettings() { AddTypeIdOptions = AddTypeIdOption.Always | AddTypeIdOption.IfAmbiguious };
      byte[] bytes = Serializer.Serialize(new { Id = 1, Name = "x" }, settings);
      Assert.AreEqual("1 x", Text(Serializer.Deserialize<AnonDto>(bytes, settings)));
      byte[] wrapped = Serializer.Serialize(new AnonWrapper() { Data = new { Id = 1, Name = "x" } }, settings);
      Assert.AreEqual("{Id: 1, Name: x}", Text(Serializer.Deserialize<AnonWrapper>(wrapped, settings).Data));
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void DefaultValues_LeftOutOfTheMap(bool schema, ObjectLayout layout)
    {
      AnonWrapper wrapper = new AnonWrapper() { Data = new { Id = 0, Name = (string)null, Other = 3 } };
      byte[] bytes = Serializer.Serialize(wrapper, Settings(schema, layout));
      Assert.AreEqual("{Other: 3}", Text(Serializer.Deserialize<AnonWrapper>(bytes, Settings(schema, layout)).Data));
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void ReadIntoAnonymousType(bool schema, ObjectLayout layout)
    {
      var value = new { Id = 1, Name = "x", Customer = new { Id = 2, Name = "c" } };
      byte[] bytes = Serializer.Serialize(value, Settings(schema, layout));
      Assert.AreEqual(value, ReadAs(value, bytes, Settings(schema, layout)), "the fields are set as the constructor sets them");
    }

    private T ReadAs<T>(T template, byte[] bytes, MsgPackSettings settings)
    {
      return Serializer.Deserialize<T>(bytes, settings);
    }

    [TestMethod]
    public void CaseInsensitiveNames()
    {
      MsgPackSettings settings = new MsgPackSettings() { PropertyNameCaseInsensitive = true };
      byte[] bytes = Serializer.Serialize(new AnonWrapper() { Data = new { id = 1, name = "x" } }, settings);
      Assert.AreEqual("1 x", Text(Serializer.Deserialize<AnonDtoWrapper>(bytes, settings).Data));
    }

    [TestMethod]
    public void UnknownNames_Reported()
    {
      byte[] bytes = Serializer.Serialize(new AnonWrapper() { Data = new { Id = 1, Name = "x", Extra = true } }, (MsgPackSettings)null);
      AnonDtoWrapper read = Serializer.Deserialize<AnonDtoWrapper>(bytes, null, out ReadDifferences differences);
      Assert.AreEqual("1 x", Text(read.Data));
      StringAssert.Contains(differences.GenerateReport(), "Extra");
    }
  }

  [TestClass]
  public class LsSerializingAnonymousTypes : SerializingAnonymousTypes
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtSerializingAnonymousTypes : SerializingAnonymousTypes
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }

  [TestClass]
  public class GenSerializingAnonymousTypes : SerializingAnonymousTypes
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
