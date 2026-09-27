using LsMsgPack;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Property values are read and written by reflection at first, and by compiled delegates once a property has been used a number of times.
  /// These tests use every property far more often, so both ways are covered and must give the same results.
  /// </summary>
  [TestClass]
  public class CompiledPropertyAccessors
  {
    private const int Instances = 250; // well over the number of calls after which the accessors are compiled (PropertyAccessor.CompileAfterCalls)

    public CompiledPropertyAccessors()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(IIPet));
    }

    public enum AccessorSize { Small, Medium, Large }

    public class AccessorBase
    {
      public string FromBase { get; set; }
      public virtual int Overridden { get; set; }
    }

    public class AccessorSample : AccessorBase
    {
      private int _overridden;
      public override int Overridden { get { return _overridden; } set { _overridden = value * 2; } } // the override must be used, also when compiled

      public int Int { get; set; }
      public long Long { get; set; }
      public int? NullableInt { get; set; }
      public string Text { get; set; }
      public AccessorSize Size { get; set; }
      public decimal Amount { get; set; }
      public DateTime When { get; set; }
      public IIPet Pet { get; set; }
      public object Anything { get; set; }
      public List<int> Numbers { get; set; }
    }

    public struct AccessorPoint
    {
      public int X { get; set; }
      public int Y { get; set; }
    }

    public class AccessorNullable { public int? Value { get; set; } }

    public class AccessorValue { public int Value { get; set; } }

    public class AccessorPrivateSetter
    {
      public AccessorPrivateSetter() { }
      public AccessorPrivateSetter(string name) { Name = name; }
      public string Name { get; private set; }
    }

    public class AccessorThrowingGetter
    {
      public int Get { get { throw new InvalidOperationException("getter"); } set { } }
    }

    public class AccessorThrowingSetter
    {
      public int Set { get { return 1; } set { throw new InvalidOperationException("setter"); } }
    }

    private static List<AccessorSample> CreateAssorted()
    {
      List<AccessorSample> list = new List<AccessorSample>();
      for (int t = 0; t < Instances; t++)
      {
        list.Add(new AccessorSample()
        {
          FromBase = "base " + t,
          Overridden = t,
          Int = t - 25,
          Long = t * 100000000000L,
          NullableInt = t % 3 == 0 ? (int?)null : t,
          Text = t % 4 == 0 ? null : "text " + t,
          Size = (AccessorSize)(t % 3),
          Amount = t * 1.25m,
          When = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(t),
          Pet = t % 2 == 0 ? (IIPet)new Cat() { Name = "cat " + t, NotNullable = t } : new Dog() { Name = "dog " + t, BarkingDecibels = t },
          Anything = t % 2 == 0 ? (object)("thing " + t) : t,
          Numbers = new List<int>() { t, t + 1 }
        });
      }
      return list;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RoundTripGivesTheSameValues(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      List<AccessorSample> org = CreateAssorted();

      for (int round = 0; round < 3; round++) // one list per round, the accessors are compiled somewhere along the way
      {
        List<AccessorSample> ret = MsgPackSerializer.Deserialize<List<AccessorSample>>(MsgPackSerializer.Serialize(org, settings), settings);

        Assert.HasCount(org.Count, ret);
        for (int t = 0; t < org.Count; t++)
        {
          AccessorSample o = org[t], r = ret[t];
          Assert.AreEqual(o.FromBase, r.FromBase);
          Assert.AreEqual(o.Overridden * 2, r.Overridden, "The value is doubled by the overridden setter");
          Assert.AreEqual(o.Int, r.Int);
          Assert.AreEqual(o.Long, r.Long);
          Assert.AreEqual(o.NullableInt, r.NullableInt);
          Assert.AreEqual(o.Text, r.Text);
          Assert.AreEqual(o.Size, r.Size);
          Assert.AreEqual(o.Amount, r.Amount);
          Assert.AreEqual(o.When, r.When.ToUniversalTime());
          Assert.AreEqual(o.Pet.GetType(), r.Pet.GetType());
          Assert.AreEqual(o.Pet.Name, r.Pet.Name);
          Assert.AreEqual(o.Anything, r.Anything is byte ? (object)Convert.ToInt32(r.Anything) : r.Anything); // an object gets the smallest integer type
          CollectionAssert.AreEqual(o.Numbers, r.Numbers);
        }
      }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void StructProperties(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      List<AccessorPoint> org = new List<AccessorPoint>();
      for (int t = 0; t < Instances; t++)
        org.Add(new AccessorPoint() { X = t, Y = -t });

      List<AccessorPoint> ret = MsgPackSerializer.Deserialize<List<AccessorPoint>>(MsgPackSerializer.Serialize(org, settings), settings);
      CollectionAssert.AreEqual(org, ret);
    }

    [TestMethod]
    public void NullForAValueTypeSetsTheDefault()
    {
      // Without filters null is serialized, and read into a property that cannot be null (the property names match)
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false, DynamicFilters = new IMsgPackPropertyIncludeDynamically[0] };
      List<AccessorNullable> org = new List<AccessorNullable>();
      for (int t = 0; t < Instances; t++)
        org.Add(new AccessorNullable() { Value = t % 2 == 0 ? (int?)null : t });

      List<AccessorValue> ret = MsgPackSerializer.Deserialize<List<AccessorValue>>(MsgPackSerializer.Serialize(org, settings), settings);
      for (int t = 0; t < Instances; t++)
        Assert.AreEqual(org[t].Value ?? 0, ret[t].Value);
    }

    [TestMethod]
    public void NonPublicSetter()
    {
      MsgPackSettings settings = new MsgPackSettings()
      {
        UseInexedSchema = false,
        StaticFilters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(false), new FilterIgnoredAttribute() }
      };
      List<AccessorPrivateSetter> org = new List<AccessorPrivateSetter>();
      for (int t = 0; t < Instances; t++)
        org.Add(new AccessorPrivateSetter("name " + t));

      List<AccessorPrivateSetter> ret = MsgPackSerializer.Deserialize<List<AccessorPrivateSetter>>(MsgPackSerializer.Serialize(org, settings), settings);
      for (int t = 0; t < Instances; t++)
        Assert.AreEqual(org[t].Name, ret[t].Name);
    }

    [TestMethod]
    public void ExceptionsOfGettersAreNotWrapped()
    {
      for (int t = 0; t < Instances; t++)
      {
        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>(() => MsgPackSerializer.Serialize(new AccessorThrowingGetter()), $"call {t}");
        Assert.AreEqual("getter", ex.Message);
      }
    }

    [TestMethod]
    public void ExceptionsOfSettersAreNotWrapped()
    {
      byte[] buffer = MsgPackSerializer.Serialize(new AccessorThrowingSetter(), new MsgPackSettings() { UseInexedSchema = false });
      for (int t = 0; t < Instances; t++)
      {
        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>(() => MsgPackSerializer.Deserialize<AccessorThrowingSetter>(buffer, new MsgPackSettings() { UseInexedSchema = false }), $"call {t}");
        Assert.AreEqual("setter", ex.Message);
      }
    }
  }
}
