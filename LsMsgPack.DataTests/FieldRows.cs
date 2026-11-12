using LsMsgPack;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace LsMsgPack.DataTests.FieldRows
{
  /// <summary>
  /// A user's own attributes, found by their class names as Entity Framework's are (these target fields too, EF's do not).
  /// </summary>
  [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
  public sealed class ColumnAttribute : Attribute
  {
    public ColumnAttribute(string name) { Name = name; }
    public string Name { get; set; }
  }

  [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
  public sealed class NotMappedAttribute : Attribute { }

  public class PublicFieldsRow
  {
    public int Id;
    public string Name;
    public long Big;
    public int? Count = 5;
    public DateTime? When;
    public string Comment = "none";
  }

  public class AttributedFieldsRow
  {
    [DataMember] public int Id;
    [DataMember] public string Name;
    [DataMember] private string _secret = null;
    public int NotSerialized = 1;

    public string Secret { get { return _secret; } }
  }

  public class FieldNamesRow
  {
    public string FirstName;
    public int Order_Number;
    [Column("customer_name")] public string Customer;
    [NotMapped] public string Extra;
    public int Plain;
  }

  public class ReadonlyFieldRow
  {
    public readonly int Fixed = 5;
    public int Id;
  }

  public class ReadonlyFieldCtorRow
  {
    public readonly int Fixed;
    public int Id;

    public ReadonlyFieldCtorRow(int @fixed)
    {
      Fixed = @fixed;
    }
  }

  /// <summary>
  /// Counts the boxed reads: a column of the member's type is read with the typed getter, GetValue is the boxed path.
  /// </summary>
  public sealed class CountingReader : ColumnarReader
  {
    public CountingReader(string[] names, Column[] columns, int rows) : base(names, columns, rows) { }

    public int GetValueCalls { get; private set; }

    public override object GetValue(int ordinal)
    {
      GetValueCalls++;
      return base.GetValue(ordinal);
    }
  }

  [TestClass]
  public class FieldRowsTests
  {
    private static LsMsgPackSerializer Public()
    {
      LsMsgPackOptions options = new LsMsgPackOptions();
      options.StaticFilters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(GetOnlyProperties.All, NonPublicProperties.None, SerializedFields.Public), new FilterIgnoredAttribute(), new FilterStatic() };
      return new LsMsgPackSerializer(options);
    }

    private static ColumnarReader.Column<T> Col<T>(T[] values, bool[] nulls = null) { return new ColumnarReader.Column<T>(values, nulls); }

    private static PublicFieldsRow[] ReadPublic(LsMsgPackSerializer serializer, bool enumerate, ColumnarReader reader)
    {
      return enumerate ? serializer.EnumerateRows<PublicFieldsRow>(reader).ToArray() : serializer.ReadRows<PublicFieldsRow>(reader).ToArray();
    }

    private static ColumnarReader PublicReader(bool counting, out CountingReader counter)
    {
      string[] names = { "Id", "Name", "Big", "Count", "When", "Comment" };
      ColumnarReader.Column[] columns =
      {
        Col(new[] { 1, 2 }),
        Col(new[] { "Ann", "Bob" }),
        Col(new[] { 10L, 20L }),
        Col(new[] { 0, 3 }, new[] { false, true }),
        Col(new[] { new DateTime(2026, 1, 2), default(DateTime) }, new[] { false, true }),
        Col(new[] { "set", null }),
      };
      counter = counting ? new CountingReader(names, columns, 2) : null;
      return counting ? counter : new ColumnarReader(names, columns, 2);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PublicFields_Mode_Public(bool enumerate)
    {
      PublicFieldsRow[] read = ReadPublic(Public(), enumerate, PublicReader(false, out _));
      Assert.HasCount(2, read);
      Assert.AreEqual(1, read[0].Id);
      Assert.AreEqual("Ann", read[0].Name);
      Assert.AreEqual(10L, read[0].Big);
      Assert.AreEqual(0, read[0].Count, "a 0 is a value");
      Assert.AreEqual(new DateTime(2026, 1, 2), read[0].When);
      Assert.AreEqual("set", read[0].Comment);
      Assert.AreEqual(20L, read[1].Big);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NullableFields_DBNull_KeepTheInitialValue(bool enumerate)
    {
      PublicFieldsRow[] read = ReadPublic(Public(), enumerate, PublicReader(false, out _));
      Assert.AreEqual(5, read[1].Count, "DBNull leaves the initializer of the field, as for a property");
      Assert.IsNull(read[1].When);
      Assert.AreEqual("none", read[1].Comment);
      Assert.AreEqual("Bob", read[1].Name);
    }

    [TestMethod]
    public void PublicFields_DefaultMode_AreNotSet()
    {
      PublicFieldsRow[] read = new LsMsgPackSerializer().ReadRows<PublicFieldsRow>(PublicReader(false, out _)).ToArray();
      Assert.HasCount(2, read);
      Assert.AreEqual(0, read[0].Id, "an unattributed public field is not serialized by default: no column sets it");
      Assert.IsNull(read[0].Name);
      Assert.AreEqual(5, read[0].Count);
    }

    [TestMethod]
    public void AttributedFields_DefaultOptions_IncludingPrivate()
    {
      string[] names = { "Id", "Name", "_secret", "NotSerialized" };
      ColumnarReader.Column[] columns = { Col(new[] { 4 }), Col(new[] { "Cy" }), Col(new[] { "s3" }), Col(new[] { 99 }) };
      AttributedFieldsRow[] read = new LsMsgPackSerializer().ReadRows<AttributedFieldsRow>(new ColumnarReader(names, columns, 1)).ToArray();
      Assert.AreEqual(4, read[0].Id);
      Assert.AreEqual("Cy", read[0].Name);
      Assert.AreEqual("s3", read[0].Secret, "a private [DataMember] field");
      Assert.AreEqual(1, read[0].NotSerialized, "public without attribute: not mapped");
    }

    [TestMethod]
    public void ColumnNames_IgnoreCaseAndUnderscores_ColumnAttribute_NotMapped()
    {
      string[] names = { "first_name", "ORDERNUMBER", "customer_name", "Extra", "plain" };
      ColumnarReader.Column[] columns = { Col(new[] { "A" }), Col(new[] { 12 }), Col(new[] { "Ann" }), Col(new[] { "x" }), Col(new[] { 3 }) };
      FieldNamesRow[] read = Public().ReadRows<FieldNamesRow>(new ColumnarReader(names, columns, 1)).ToArray();
      Assert.AreEqual("A", read[0].FirstName, "first_name: ignoring case and underscores");
      Assert.AreEqual(12, read[0].Order_Number, "the underscore is in the field name");
      Assert.AreEqual("Ann", read[0].Customer, "an own [Column] on a field");
      Assert.IsNull(read[0].Extra, "an own [NotMapped] on a field");
      Assert.AreEqual(3, read[0].Plain, "plain: ignoring case");
    }

    [TestMethod]
    public void ColumnAttribute_OnField_ReplacesTheFieldName()
    {
      // the field's own name no longer matches
      string[] names = { "Customer" };
      ColumnarReader.Column[] columns = { Col(new[] { "Ann" }) };
      FieldNamesRow[] read = Public().ReadRows<FieldNamesRow>(new ColumnarReader(names, columns, 1)).ToArray();
      Assert.IsNull(read[0].Customer, "[Column(\"customer_name\")] names the column; \"Customer\" is another one");
    }

    [TestMethod]
    public void TypedSetter_ForAFieldOfTheColumnType()
    {
      PublicReader(true, out CountingReader counter);
      PublicFieldsRow[] read = Public().ReadRows<PublicFieldsRow>(counter).ToArray();
      Assert.AreEqual(0, counter.GetValueCalls, "columns of the types of the fields: the typed getters, no boxed GetValue");
      Assert.AreEqual(20L, read[1].Big);
      Assert.AreEqual("Bob", read[1].Name);
    }

    [TestMethod]
    public void BoxedSetter_ForAColumnOfAnotherType()
    {
      string[] names = { "Id", "Big" };
      ColumnarReader.Column[] columns = { Col(new[] { 5L }), Col(new[] { 7 }) }; // long into an int field, int into a long field
      CountingReader counter = new CountingReader(names, columns, 1);
      PublicFieldsRow[] read = Public().ReadRows<PublicFieldsRow>(counter).ToArray();
      Assert.IsGreaterThan(0, counter.GetValueCalls, "converted boxed");
      Assert.AreEqual(5, read[0].Id);
      Assert.AreEqual(7L, read[0].Big);
    }

    [TestMethod]
    public void ReadonlyField_IsNotSet_ByAColumn()
    {
      string[] names = { "Fixed", "Id" };
      ColumnarReader.Column[] columns = { Col(new[] { 99 }), Col(new[] { 1 }) };
      ReadonlyFieldRow[] read = Public().ReadRows<ReadonlyFieldRow>(new ColumnarReader(names, columns, 1)).ToArray();
      Assert.AreEqual(1, read[0].Id);
      Assert.AreEqual(5, read[0].Fixed, "a readonly field is never set behind the type's back (like a get-only property)");
    }

    [TestMethod]
    public void ReadonlyField_SetByTheConstructor()
    {
      string[] names = { "Fixed", "Id" };
      ColumnarReader.Column[] columns = { Col(new[] { 99 }), Col(new[] { 1 }) };
      ReadonlyFieldCtorRow[] read = Public().ReadRows<ReadonlyFieldCtorRow>(new ColumnarReader(names, columns, 1)).ToArray();
      Assert.AreEqual(1, read[0].Id);
      Assert.AreEqual(99, read[0].Fixed, "the constructor parameter binds to the public field");
    }

    [TestMethod]
    public void Fields_WithSqlite()
    {
      using (Microsoft.Data.Sqlite.SqliteConnection connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"))
      {
        connection.Open();
        using (System.Data.IDbCommand command = connection.CreateCommand())
        {
          command.CommandText = "create table t (Id integer, Name text, Big integer, Count integer, When_ text, Comment text); insert into t values (1, 'Ann', 10, null, null, 'x'), (2, 'Bob', 20, 4, null, null);";
          command.ExecuteNonQuery();
          command.CommandText = "select Id, Name, Big, Count, Comment from t order by Id";
          using (System.Data.IDataReader reader = command.ExecuteReader())
          {
            PublicFieldsRow[] read = Public().ReadRows<PublicFieldsRow>(reader).ToArray();
            Assert.AreEqual(2, read.Length);
            Assert.AreEqual(1, read[0].Id);
            Assert.AreEqual(10L, read[0].Big);
            Assert.AreEqual(5, read[0].Count, "NULL: the initializer");
            Assert.AreEqual(4, read[1].Count);
            Assert.AreEqual("none", read[1].Comment);
          }
        }
      }
    }
  }
}
