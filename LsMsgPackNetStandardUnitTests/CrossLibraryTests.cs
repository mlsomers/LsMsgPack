using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Names;
using LtMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// LtMsgPack writes the same bytes as LsMsgPack and reads the same values, for a corpus of values in all settings.
  /// </summary>
  [TestClass]
  public class CrossLibraryTests
  {
    public enum XlE8 : byte { A = 1, B = 200 }
    public enum XlE16 : short { A = -300, B = 3 }
    public enum XlE64 : long { A = long.MinValue, B = 5 }

    public interface IXlShape { string Name { get; set; } }
    public abstract class XlShapeBase : IXlShape { public string Name { get; set; } public int? Sides { get; set; } }
    public class XlCircle : XlShapeBase { public double Radius { get; set; } }
    public class XlSquare : XlShapeBase { public float Side { get; set; } [DefaultValue(7)] public int Standard { get; set; } = 7; }
    public class XlGeneric<T> { public T Value { get; set; } public int Count { get; set; } = 1; }
    public class XlMoney { public XlMoney(decimal amount, string currency) { Amount = amount; Currency = currency; } public decimal Amount { get; } public string Currency { get; } public string Tag { get; set; } }
    public readonly struct XlPoint { public XlPoint(int x, int y) { X = x; Y = y; } public int X { get; } public int Y { get; } }
    public class XlCart { public List<XlShapeBase> Lines { get; } = new List<XlShapeBase>(); public int Count { get { return Lines.Count; } } public Dictionary<string, int> Totals { get; } = new Dictionary<string, int>(); public string Name { get; set; } }

    public class XlNested
    {
      public string Name { get; set; }
      public double D { get; set; }
      public float F { get; set; }
      public int? N { get; set; }
      public byte[] Blob { get; set; }
      public Guid G { get; set; }
      public DateTime When { get; set; }
      public DateTimeOffset Off { get; set; }
      public decimal M { get; set; }
      public XlE16 E { get; set; }
      public XlE8? NE { get; set; }
      public bool B { get; set; }
      public List<object> Objs { get; set; }
      public Dictionary<string, long> Map { get; set; }
      public ulong U { get; set; }
      public sbyte S { get; set; }
      public char C { get; set; }
      public TimeSpan T { get; set; }
      public Uri Link { get; set; }
      public object Anything { get; set; }
      public int[] Ints { get; set; }
      public string[] Strings { get; set; }
      public List<XlNested> Children { get; set; }
      public XlNested Child { get; set; }
    }

    public class XlShapes
    {
      public IXlShape Main { get; set; }
      public XlShapeBase Base { get; set; }
      public XlCircle Circle { get; set; }
      public IXlShape[] Array { get; set; }
      public List<XlShapeBase> List { get; set; }
      public IList<IXlShape> Interface { get; set; }
      public Dictionary<string, IXlShape> ByName { get; set; }
      public Dictionary<IXlShape, int> ByShape { get; set; }
      public object Boxed { get; set; }
      public IEnumerable<int> Numbers { get; set; }
      public KeyValuePair<string, int>[] Pairs { get; set; }
      public Hashtable Table { get; set; }
      public HashSet<string> Set { get; set; }
      public Stack<int> Stack { get; set; }
      public XlStruct Struct { get; set; }
    }

    public struct XlStruct
    {
      public int X { get; set; }
      public string Y { get; set; }
    }

    // Structs as games use them (LtMsgPack writes and reads them with the object plans, boxed once): floats, nesting, defaults, nullables, collections, type ids

    public struct XlVec { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } }

    // Fields (SerializedFields): attributed ones by default, all public ones with Public; framework structs by their fields always
    public struct XlFieldVec { public float X; public float Y; [System.Runtime.Serialization.DataMember] public float Z; }

    public class XlFieldBase
    {
      public int BaseCount;
      [System.Text.Json.Serialization.JsonInclude]
      private string _baseSecret;
      public void SetBaseSecret(string value) { _baseSecret = value; }
    }

    public class XlFields : XlFieldBase
    {
      public string Name { get; set; }
      public int Count;
      [System.Runtime.Serialization.DataMember]
      public string Marked;
      [System.Text.Json.Serialization.JsonInclude]
      private int _pin;
      public System.Numerics.Vector3 Position { get; set; }
      public System.Numerics.Quaternion Rotation;
      [System.Runtime.Serialization.DataMember]
      public System.Numerics.Matrix4x4 Transform;
      public XlFieldVec Velocity;
      [System.Runtime.Serialization.DataMember]
      public List<XlFieldVec> Path;
      public readonly List<int> Items = new List<int>(); // filled
      public readonly int Computed = 4; // written, skipped
      [DefaultValue(3)]
      public int Level = 3;
      public void SetPin(int pin) { _pin = pin; }
    }
    public struct XlLine { public XlVec From { get; set; } public XlVec To { get; set; } public string Label { get; set; } public XlVec? Hint { get; set; } public double Length { get; set; } }

    /// <summary>Equals of its own: 10 equals the default (0), so the default filter leaves it out</summary>
    public struct XlTens { public int A { get; set; } public override bool Equals(object obj) { return obj is XlTens other && other.A % 10 == A % 10; } public override int GetHashCode() { return A % 10; } }

    public record struct XlRecordStruct(int Id, string Name);
    public interface IXlPositioned { XlVec Position { get; set; } }
    public struct XlPositioned : IXlPositioned { public XlVec Position { get; set; } public int Id { get; set; } }

    public class XlStructs
    {
      public XlVec Position { get; set; }
      public XlVec Velocity { get; set; }
      public XlVec? Maybe { get; set; }
      public XlVec? None { get; set; }
      public XlLine Line { get; set; }
      public List<XlVec> Path { get; set; }
      public XlVec[] Points { get; set; }
      public object Boxed { get; set; }
      public IXlPositioned Positioned { get; set; }
      public XlTens Tens { get; set; }
      public XlTens Ones { get; set; }
      public XlRecordStruct Record { get; set; }
    }

    public class XlFramework
    {
      public Half H { get; set; }
      public Version V { get; set; }
      public StringBuilder Sb { get; set; }
      public CultureInfo Culture { get; set; }
      public Rune R { get; set; }
      public nint P { get; set; }
      public nuint UP { get; set; }
      public Memory<byte> Mem { get; set; }
      public ArraySegment<byte> Seg { get; set; }
      public System.Numerics.Complex Cx { get; set; }
      public (int, string) Vt { get; set; }
      public Tuple<int, object> T { get; set; }
      public List<object> Objs { get; set; }
      public System.Numerics.BigInteger Big { get; set; }
      public Int128 I128 { get; set; }
      public UInt128 U128 { get; set; }
      public DateTimeOffset Off { get; set; }
    }

    public class XlTaggedList : List<string> { public string Tag { get; set; } }

    [SerializeEnumerable(SerializeProperties = true)]
    public class XlNamedMap : Dictionary<string, int> { public string Name { get; set; } }
    public class XlTypedCollections
    {
      public Dictionary<string, string> Strings { get; set; }
      public Dictionary<string, int> Numbers { get; set; }
      public Dictionary<int, XlSquare> Objects { get; set; }
      public Dictionary<XlE8, string> Enums { get; set; }
      public Dictionary<Guid, long?> Nullables { get; set; }
      public Dictionary<string, object> Bag { get; set; }
      public HashSet<int> Ints { get; set; }
      public HashSet<string> Texts { get; set; }
      public IList<int> List { get; set; }
      public ICollection<string> Collection { get; set; }
      public IEnumerable<XlSquare> Enumerable { get; set; }
      public IReadOnlyList<long> ReadOnlyList { get; set; }
      public ISet<int> Set { get; set; }
      public IDictionary<string, int> Dictionary { get; set; }
      public IReadOnlyDictionary<int, string> ReadOnlyDictionary { get; set; }
      public IEnumerable<KeyValuePair<string, int>> Pairs { get; set; }
      public IList<IList<int>> Lists { get; set; }
      public object Any { get; set; }
    }

    [SerializeEnumerable(SerializeElements = false, SerializeProperties = true)]
    public class XlBag : IEnumerable<string>
    {
      public string Label { get; set; }
      public IEnumerator<string> GetEnumerator() { yield return "x"; }
      IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public class XlCollections
    {
      [SerializeEnumerable(SerializeProperties = true)]
      public XlTaggedList Tagged { get; set; }
      public XlTaggedList NotTagged { get; set; }
      public IList<string> Interface { get; set; }
      public XlNamedMap Named { get; set; }
      public XlBag Bag { get; set; }
      [SerializeEnumerable(typeof(XlCircle))]
      public List<XlShapeBase> Circles { get; set; }
      public List<KeyValuePair<string, int>> PairList { get; set; }
    }

    public class XlRenamed
    {
      [System.Xml.Serialization.XmlElement("renamed")] public string A { get; set; }
      [System.Xml.Serialization.XmlIgnore] public string Ignored { get; set; }
      public int ReadOnly { get { return 5; } }
      public int B { get; set; }
      public XlRenamed Child { get; set; }
      public List<XlRenamed> Kids { get; set; }
    }

    public CrossLibraryTests()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(XlCircle));
    }

    private static List<object> Corpus()
    {
      List<object> values = new List<object> { null, true, false, "", "a", new string('x', 31), new string('y', 32), new string('z', 255), new string('w', 256), new string('v', 70000), "héllo wörld ✓ 😀",
        new byte[0], new byte[] { 1, 2, 3 }, new byte[300], new byte[70000], new sbyte[] { -1, 2 }, Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
        1.5f, -0.0f, float.NaN, float.MaxValue, 1.5, double.Epsilon, double.NegativeInfinity, 12.5m, -79228162514264337593543950335m, 0.0000001m,
        new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc), new DateTime(2026, 3, 4, 5, 6, 7, 123, DateTimeKind.Utc), new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2600, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero),
        new DateTime(1969, 12, 31, 23, 59, 58, 500, DateTimeKind.Utc), new DateTime(1601, 1, 1, 0, 0, 0, 250, DateTimeKind.Utc),
        XlE8.A, XlE8.B, XlE16.A, XlE16.B, XlE64.A, XlE64.B,
        'A', 'é', TimeSpan.FromMinutes(90), TimeSpan.FromTicks(-12345), new DateOnly(2026, 9, 29), new TimeOnly(13, 45, 10, 5), new Uri("https://example.com/x?y=1") };

      long[] ints = { 0, 1, 31, 32, 127, 128, 255, 256, 32767, 32768, 65535, 65536, int.MaxValue, 2147483648L, uint.MaxValue, 4294967296L, long.MaxValue, -1, -31, -32, -33, -128, -129, -32768, -32769, int.MinValue, long.MinValue };
      foreach (long i in ints)
      {
        values.Add(i);
        if (i >= int.MinValue && i <= int.MaxValue) values.Add((int)i);
        if (i >= short.MinValue && i <= short.MaxValue) values.Add((short)i);
        if (i >= sbyte.MinValue && i <= sbyte.MaxValue) values.Add((sbyte)i);
        if (i >= 0) { values.Add((ulong)i); if (i <= uint.MaxValue) values.Add((uint)i); if (i <= ushort.MaxValue) values.Add((ushort)i); if (i <= byte.MaxValue) values.Add((byte)i); }
      }
      values.Add(ulong.MaxValue);

      values.Add(new object[] { 1, "two", 3.0, null, new object[] { true }, XlE8.B, 2.5m, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), new XlCircle { Name = "c" } });
      values.Add(new int[20]);
      values.Add(new List<string> { "a", null, "b" });
      values.Add(new Dictionary<int, string> { { 1, "one" }, { 300, "three hundred" } });
      values.Add(new XlTypedCollections());
      values.Add(new XlTypedCollections { Strings = new Dictionary<string, string> { { "a", "b" }, { "c", null }, { "", "empty" }, { "@", "content" }, { new string('k', 40), "long" } }, Numbers = new Dictionary<string, int> { { "x", 1 }, { "y", -300 } }, Objects = new Dictionary<int, XlSquare> { { 1, new XlSquare { Side = 2 } }, { -5, null } }, Enums = new Dictionary<XlE8, string> { { XlE8.B, "b" } }, Nullables = new Dictionary<Guid, long?> { { Guid.Empty, null } }, Bag = new Dictionary<string, object> { { "a", 1 }, { "b", null }, { "c", new XlSquare() }, { "d", new List<int> { 1 } } }, Ints = new HashSet<int> { 5, 1, -7 }, Texts = new HashSet<string> { "b", null, "" } });
      values.Add(new XlTypedCollections { List = new List<int> { 1, 2 }, Collection = new List<string> { "a", null }, Enumerable = new List<XlSquare> { new XlSquare(), null }, ReadOnlyList = new List<long> { long.MaxValue }, Set = new HashSet<int> { 3, 2 }, Dictionary = new Dictionary<string, int> { { "a", 1 }, { "", 2 } }, ReadOnlyDictionary = new Dictionary<int, string> { { 1, "x" } }, Pairs = new Dictionary<string, int> { { "kv", 1 } }, Lists = new List<IList<int>> { new List<int> { 1 }, new[] { 2 }, null } });
      values.Add(new XlTypedCollections { List = new[] { 1, 2 }, Collection = new[] { "a" }, Enumerable = new[] { new XlSquare() }, ReadOnlyList = new long[0], Set = new SortedSet<int> { 9, 8 }, Dictionary = new SortedDictionary<string, int> { { "b", 1 }, { "a", 2 } }, Pairs = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("p", 1) }, Any = new Dictionary<string, int> { { "any", 1 } } });
      values.Add(new XlTypedCollections { List = new System.Collections.ObjectModel.Collection<int> { 1 }, Collection = new HashSet<string> { "h" }, Enumerable = new List<XlSquare>().Where(x => x != null), Set = new HashSet<int>(), Dictionary = new XlNamedMap { { "d", 1 } }, Any = new HashSet<int> { 1 } });
      values.Add(new Dictionary<string, string> { { "root", "x" } });
      values.Add(new Dictionary<string, object> { { "", 1 }, { "@", 2 } });
      values.Add(new HashSet<int> { 1, 2 });
      values.Add(new List<Dictionary<string, int>> { new Dictionary<string, int> { { "a", 1 } }, null });
      values.Add(new Dictionary<object, object> { { "k", 1 }, { 2, new XlSquare { Side = 2 } } });
      object[] big = new object[70000];
      for (int i = 0; i < big.Length; i++) big[i] = i;
      values.Add(big);
      Dictionary<int, int> bigMap = new Dictionary<int, int>();
      for (int i = 0; i < 70000; i++) bigMap[i] = -i;
      values.Add(bigMap);

      XlNested nested = new XlNested
      {
        Name = "n", D = 2.25, F = -3.5f, N = 7, Blob = new byte[] { 9, 8 }, G = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
        When = new DateTime(2026, 1, 1, 12, 0, 0, 500, DateTimeKind.Utc), Off = new DateTimeOffset(2021, 5, 6, 7, 8, 9, TimeSpan.Zero), M = 1.23m, E = XlE16.A, NE = XlE8.B, B = true,
        Objs = new List<object> { 1, "x", 2.5m, XlE8.B, new XlNested { Name = "inner" }, TimeSpan.FromHours(1), 'q', new DateOnly(2000, 1, 1), null, new List<int> { 1 } },
        Map = new Dictionary<string, long> { { "k", long.MinValue } }, U = ulong.MaxValue, S = -5, C = 'Z', T = TimeSpan.FromSeconds(-1.5), Link = new Uri("https://example.com/a"),
        Anything = new XlSquare { Name = "sq", Side = 1.5f, Standard = 3 }, Ints = new[] { 1, -1, 300 }, Strings = new[] { "a", null },
        Children = new List<XlNested> { new XlNested { Name = "c1", N = 0 }, null, new XlNested { Name = "c2", Anything = 5 } },
        Child = new XlNested { Name = "child", Anything = "text", Objs = new List<object>() }
      };
      values.Add(nested);
      values.Add(new XlNested()); // all default: an empty map

      values.Add(new XlShapes
      {
        Main = new XlCircle { Name = "main", Radius = 2 },
        Base = new XlSquare { Name = "base", Side = 3, Sides = 4 },
        Circle = new XlCircle { Name = "circle", Radius = 1 },
        Array = new IXlShape[] { new XlCircle { Name = "a1" }, null, new XlSquare { Name = "a2", Standard = 0 } },
        List = new List<XlShapeBase> { new XlSquare { Name = "l1" }, new XlCircle { Name = "l2" } },
        Interface = new List<IXlShape> { new XlCircle { Name = "i1" } },
        ByName = new Dictionary<string, IXlShape> { { "x", new XlCircle { Name = "x" } } },
        ByShape = new Dictionary<IXlShape, int> { { new XlSquare { Name = "key" }, 3 } },
        Boxed = new DateTime(2020, 2, 2, 0, 0, 0, DateTimeKind.Utc),
        Numbers = new List<int> { 1, 2 },
        Pairs = new[] { new KeyValuePair<string, int>("p", 1) },
        Table = new Hashtable { { "h", 1 } },
        Set = new HashSet<string> { "s" },
        Stack = new Stack<int>(new[] { 1, 2, 3 }),
        Struct = new XlStruct { X = 4, Y = "y" }
      });

      values.Add(new XlCollections
      {
        Tagged = new XlTaggedList { "t1", "t2" },
        NotTagged = new XlTaggedList { "n1" },
        Interface = new List<string> { "i" },
        Named = new XlNamedMap { { "a", 1 } },
        Bag = new XlBag { Label = "bag" },
        Circles = new List<XlShapeBase> { new XlCircle { Name = "c" }, new XlSquare { Name = "s" } },
        PairList = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("k", 1) }
      });
      values.Add(new XlFramework
      {
        H = (Half)(-0.5), V = new Version(2, 1), Sb = new StringBuilder("sb"), Culture = CultureInfo.GetCultureInfo("de-DE"), R = new Rune(0x10FFFF), P = -1, UP = nuint.MaxValue,
        Mem = new byte[] { 1, 2 }, Seg = new ArraySegment<byte>(new byte[] { 3, 4, 5 }, 1, 1), Cx = new System.Numerics.Complex(-1, 0.25), Vt = (7, "seven"), T = Tuple.Create(8, (object)new XlCircle { Name = "t" }),
        Objs = new List<object> { (Half)1, new Version(1, 0), new System.Numerics.Complex(1, 1), (1, "a"), new Memory<byte>(new byte[] { 6 }), CultureInfo.InvariantCulture, new Rune('r'),
          System.Numerics.BigInteger.Pow(7, 50), (Int128)(-5), UInt128.MaxValue, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromMinutes(-90)) },
        Big = -System.Numerics.BigInteger.Pow(2, 64), I128 = Int128.MinValue, U128 = 7, Off = new DateTimeOffset(2026, 10, 10, 1, 2, 3, 4, TimeSpan.FromHours(9))
      });
      values.Add(new XlFramework());
      values.Add((1, "a", 2.5, (object)XlE8.B, 5, 6, 7, 8, 9));
      values.Add(new XlTaggedList { "only" });
      values.Add(new XlStruct { X = 1, Y = "struct" });
      values.Add(new XlStructs
      {
        Position = new XlVec { X = 1.5f, Y = -0f, Z = float.NaN },
        Maybe = new XlVec { Z = 2 },
        Line = new XlLine { From = new XlVec { X = -1 }, To = new XlVec { X = 3.25f, Y = 4, Z = 5 }, Label = "l", Hint = new XlVec(), Length = 4.5 },
        Path = new List<XlVec> { new XlVec { X = 1 }, new XlVec(), new XlVec { Y = float.MaxValue } },
        Points = new[] { new XlVec { Z = -2 }, new XlVec() },
        Boxed = new XlVec { X = 7 },
        Positioned = new XlPositioned { Position = new XlVec { Y = 1 }, Id = 3 },
        Tens = new XlTens { A = 10 },
        Ones = new XlTens { A = 11 },
        Record = new XlRecordStruct(5, "five")
      });
      values.Add(new XlStructs()); // all default
      values.Add(new XlVec { X = 0.5f, Z = -8 });
      values.Add(new XlLine { To = new XlVec { Y = 1 } });
      values.Add(new List<XlLine> { new XlLine { Label = "a" }, new XlLine() });
      values.Add(new XlRecordStruct(1, null));
      values.Add(new int[,] { { 1, 2 }, { 3, 4 } }); // [2, 2, [1, 2, 3, 4]]
      values.Add(new object[] { new string[2, 1, 1], System.Collections.Immutable.ImmutableArray.Create(1), System.Collections.Immutable.ImmutableDictionary.CreateRange(new[] { new KeyValuePair<string, object>("k", 'c') }), new BitArray(3) });
      values.Add(new XlCircle { Name = "root", Radius = 1 });

      // Anonymous types: in the schema where declared, maps of names below another one and where declared as object (SerializationRules.WritesByNames)
      values.Add(new
      {
        Id = 7, Name = "anon", When = new DateTime(2026, 10, 27, 1, 2, 3, DateTimeKind.Utc), Empty = (string)null, Zero = 0,
        Inner = new { A = 1, B = new[] { new { C = "c" }, null } },
        Items = new List<object> { new { D = 2.5m }, 3, new XlCircle { Name = "c" } },
        Page = new XlGeneric<object> { Value = new { E = 'e' } }
      });
      values.Add(new[] { 1, 2 }.Select(i => new { Id = i, Name = "n" + i }));
      values.Add(new[] { 1, 2 }.Select(i => new { Id = i }).ToList());
      values.Add(new XlGeneric<object> { Value = new { Id = 1, Kids = new[] { new { Id = 2 } } } });
      values.Add(new XlGeneric<XlGeneric<object>> { Value = new XlGeneric<object> { Value = 5 } });
      values.Add(new XlNested { Name = "holder", Anything = new { Id = 1, Shape = new XlSquare { Name = "sq" } }, Objs = new List<object> { new { X = 1 }, new[] { new { Y = "y" } }.ToList() } });

      // Immutable classes and structs: the properties a constructor takes are written, readers call it (ObjectConstructor)
      values.Add(new XlMoney(12.5m, "EUR") { Tag = "t" });
      values.Add(new XlGeneric<object> { Value = new XlMoney(0m, null) });
      values.Add(new List<XlMoney> { new XlMoney(1m, "USD"), null });
      values.Add(new XlGeneric<XlPoint?> { Value = new XlPoint(1, -2) });

      // Properties without a setter (GetOnlyProperties): computed ones are written and skipped, get-only collections filled
      XlCart cart = new XlCart { Name = "cart" };
      cart.Lines.Add(new XlCircle { Name = "c", Radius = 1 });
      cart.Lines.Add(new XlSquare { Name = "s", Side = 2 });
      cart.Totals.Add("a", 3);
      values.Add(cart);
      values.Add(new XlGeneric<object> { Value = new XlCart() });

      // Fields: what each SerializedFields mode serializes of them (SameBytesAndValuesFields runs the corpus in the other modes)
      XlFields fields = new XlFields { Name = "f", Count = 2, Marked = "m", Position = new System.Numerics.Vector3(1, -0f, float.NaN), Rotation = System.Numerics.Quaternion.Identity,
        Transform = System.Numerics.Matrix4x4.CreateTranslation(1, 2, 3), Velocity = new XlFieldVec { X = 1, Y = 2, Z = 3 }, Path = new List<XlFieldVec> { new XlFieldVec { Z = 1 }, default(XlFieldVec) }, Level = 5, BaseCount = 6 };
      fields.SetPin(1234);
      fields.SetBaseSecret("s");
      fields.Items.Add(9);
      values.Add(fields);
      values.Add(new XlFields());
      values.Add(new XlGeneric<object> { Value = new XlFields { Count = 1, Marked = "x" } });
      values.Add(new List<XlFieldVec> { new XlFieldVec { X = 1, Z = 2 }, default(XlFieldVec) });
      values.Add(new System.Numerics.Vector3(1, 2, 3));
      values.Add(new object[] { System.Numerics.Vector2.One, new System.Numerics.Vector4(1, 2, 3, 4), new System.Numerics.Plane(1, 0, 0, 2), System.Numerics.Matrix3x2.CreateRotation(1) });
      return values;
    }

    public enum SchemaMode { Names, Inline, Reference }

    private static IEnumerable<object[]> Settings()
    {
      foreach (EndianAction endian in Enum.GetValues(typeof(EndianAction)))
        foreach (bool compact in new[] { true, false })
          foreach (SchemaMode mode in Enum.GetValues(typeof(SchemaMode)))
            foreach (ObjectLayout layout in Enum.GetValues(typeof(ObjectLayout)))
              yield return new object[] { endian, compact, mode, layout };
    }

    private static IEnumerable<object[]> Orders()
    {
      foreach (PropertyOrder order in Enum.GetValues(typeof(PropertyOrder)))
        foreach (SchemaMode mode in Enum.GetValues(typeof(SchemaMode)))
          foreach (ObjectLayout layout in Enum.GetValues(typeof(ObjectLayout)))
            yield return new object[] { order, mode, layout };
    }

    private static void Configure(MsgPackOptions options, EndianAction endian, bool compact, SchemaMode mode, ObjectLayout layout = ObjectLayout.Map, PropertyOrder order = PropertyOrder.Reflection)
    {
      options.ObjectLayout = layout;
      options.PropertyOrder = order;
      options.EndianAction = endian;
      options.DynamicallyCompact = compact;
      options.UseInexedSchema = mode != SchemaMode.Names;
      options.WriteSchemaReference = mode == SchemaMode.Reference;
      options.SchemaStore = mode == SchemaMode.Reference ? new SchemaStore() : null;
    }

    [TestMethod]
    [DynamicData(nameof(Settings))]
    public void SameBytesAndValues(EndianAction endian, bool compact, SchemaMode mode, ObjectLayout layout)
    {
      MsgPackSettings ls = new MsgPackSettings();
      Configure(ls, endian, compact, mode, layout);
      LtMsgPackOptions ltOptions = new LtMsgPackOptions();
      Configure(ltOptions, endian, compact, mode, layout);
      CompareCorpus(ls, ltOptions);
    }

    [TestMethod]
    [DynamicData(nameof(Orders))]
    public void SameBytesAndValuesPerOrder(PropertyOrder order, SchemaMode mode, ObjectLayout layout)
    {
      MsgPackSettings ls = new MsgPackSettings();
      Configure(ls, EndianAction.SwapIfCurrentSystemIsLittleEndian, true, mode, layout, order);
      LtMsgPackOptions ltOptions = new LtMsgPackOptions();
      Configure(ltOptions, EndianAction.SwapIfCurrentSystemIsLittleEndian, true, mode, layout, order);
      CompareCorpus(ls, ltOptions);
    }

    [TestMethod]
    [DataRow(DateTimeOffsetFormat.Timestamp)]
    [DataRow(DateTimeOffsetFormat.ClockTimeAndOffset)]
    public void SameBytesAndValuesPerDateTimeOffsetFormat(DateTimeOffsetFormat format)
    {
      foreach (SchemaMode mode in new[] { SchemaMode.Names, SchemaMode.Inline })
      {
        MsgPackSettings ls = new MsgPackSettings() { DateTimeOffsetFormat = format };
        Configure(ls, EndianAction.SwapIfCurrentSystemIsLittleEndian, true, mode);
        LtMsgPackOptions ltOptions = new LtMsgPackOptions() { DateTimeOffsetFormat = format };
        Configure(ltOptions, EndianAction.SwapIfCurrentSystemIsLittleEndian, true, mode);
        CompareCorpus(ls, ltOptions);
      }
    }

    [TestMethod]
    [DataRow(SchemaMode.Names)]
    [DataRow(SchemaMode.Inline)]
    [DataRow(SchemaMode.Reference)]
    public void SameBytesAndValuesTrimmed(SchemaMode mode)
    {
      MsgPackSettings ls = new MsgPackSettings() { TrimTrailingNulls = true };
      Configure(ls, EndianAction.SwapIfCurrentSystemIsLittleEndian, true, mode, ObjectLayout.Array);
      LtMsgPackOptions ltOptions = new LtMsgPackOptions() { TrimTrailingNulls = true };
      Configure(ltOptions, EndianAction.SwapIfCurrentSystemIsLittleEndian, true, mode, ObjectLayout.Array);
      CompareCorpus(ls, ltOptions);
    }

    private static IEnumerable<object[]> DescribedSettings()
    {
      foreach (EndianAction endian in Enum.GetValues(typeof(EndianAction)))
        foreach (bool compact in new[] { true, false })
          foreach (SchemaMode mode in new[] { SchemaMode.Inline, SchemaMode.Reference })
            foreach (ObjectLayout layout in Enum.GetValues(typeof(ObjectLayout)))
              yield return new object[] { endian, compact, mode, layout };
    }

    /// <summary>
    /// Version 2 of the schema (SchemaVersion.Described): the same bytes, the schema included (a reference is the hash of its bytes).
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(DescribedSettings))]
    public void SameBytesAndValuesDescribed(EndianAction endian, bool compact, SchemaMode mode, ObjectLayout layout)
    {
      MsgPackSettings ls = new MsgPackSettings() { SchemaVersion = SchemaVersion.Described };
      Configure(ls, endian, compact, mode, layout);
      LtMsgPackOptions ltOptions = new LtMsgPackOptions() { SchemaVersion = SchemaVersion.Described };
      Configure(ltOptions, endian, compact, mode, layout);
      CompareCorpus(ls, ltOptions);
    }

    private static IEnumerable<object[]> NonPublicSettings()
    {
      foreach (NonPublicProperties nonPublic in new[] { NonPublicProperties.Attributed, NonPublicProperties.All })
        foreach (SchemaMode mode in Enum.GetValues(typeof(SchemaMode)))
          foreach (ObjectLayout layout in Enum.GetValues(typeof(ObjectLayout)))
            yield return new object[] { nonPublic, mode, layout };
    }

    /// <summary>
    /// Non-public properties (FilterNonSettable's NonPublicProperties): the corpus, and a class with non-public properties of several kinds (also in its base class).
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(NonPublicSettings))]
    public void SameBytesAndValuesNonPublic(NonPublicProperties nonPublic, SchemaMode mode, ObjectLayout layout)
    {
      IMsgPackPropertyIncludeStatically[] filters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(GetOnlyProperties.All, nonPublic), new FilterIgnoredAttribute(), new FilterStatic() };
      MsgPackSettings ls = new MsgPackSettings() { StaticFilters = filters };
      Configure(ls, EndianAction.SwapIfCurrentSystemIsLittleEndian, true, mode, layout);
      LtMsgPackOptions ltOptions = new LtMsgPackOptions() { StaticFilters = filters };
      Configure(ltOptions, EndianAction.SwapIfCurrentSystemIsLittleEndian, true, mode, layout);
      CompareCorpus(ls, ltOptions);

      List<string> differences = new List<string>();
      foreach ((string which, LtMsgPackSerializer lt) in LtAndGenerated(ltOptions))
      {
        Compare(which + " account", SerializingNonPublicProperties.NppAccount.Sample(), typeof(SerializingNonPublicProperties.NppAccount), ls, lt, differences);
        Compare(which + " private", GeneratedCodeTests.GcPrivate.Sample(), typeof(GeneratedCodeTests.GcPrivate), ls, lt, differences);
      }
      Assert.IsEmpty(differences, string.Join("\n", differences));
    }

    private static IEnumerable<object[]> FieldSettings()
    {
      foreach (SerializedFields fields in new[] { SerializedFields.None, SerializedFields.Public, SerializedFields.All })
        foreach (SchemaMode mode in Enum.GetValues(typeof(SchemaMode)))
          foreach (ObjectLayout layout in Enum.GetValues(typeof(ObjectLayout)))
            yield return new object[] { fields, mode, layout };
    }

    /// <summary>
    /// Fields (FilterNonSettable's SerializedFields, the default Attributed runs in the other tests): the corpus (its field classes), and the field classes of SerializingFields.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(FieldSettings))]
    public void SameBytesAndValuesFields(SerializedFields fields, SchemaMode mode, ObjectLayout layout)
    {
      IMsgPackPropertyIncludeStatically[] filters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(GetOnlyProperties.All, NonPublicProperties.None, fields), new FilterIgnoredAttribute(), new FilterStatic() };
      MsgPackSettings ls = new MsgPackSettings() { StaticFilters = filters };
      Configure(ls, EndianAction.SwapIfCurrentSystemIsLittleEndian, true, mode, layout);
      LtMsgPackOptions ltOptions = new LtMsgPackOptions() { StaticFilters = filters };
      Configure(ltOptions, EndianAction.SwapIfCurrentSystemIsLittleEndian, true, mode, layout);
      CompareCorpus(ls, ltOptions);

      List<string> differences = new List<string>();
      foreach ((string which, LtMsgPackSerializer lt) in LtAndGenerated(ltOptions))
      {
        Compare(which + " account", SerializingFields.FldAccount.Sample(), typeof(SerializingFields.FldAccount), ls, lt, differences);
        Compare(which + " point", new SerializingFields.FldPoint() { X = 1, Y = -2, Label = "p" }, typeof(SerializingFields.FldPoint), ls, lt, differences);
        SerializingFields.FldReadOnly readOnly = new SerializingFields.FldReadOnly(7) { Name = "r" };
        readOnly.Items.Add(1);
        Compare(which + " readonly", readOnly, typeof(SerializingFields.FldReadOnly), ls, lt, differences);
      }
      Assert.IsEmpty(differences, string.Join("\n", differences));
    }

    /// <summary>
    /// LtMsgPack with the options, as it builds its plans at runtime, with writers compiled at runtime from the first object (CompileWritersAfter = 0)
    /// and with the code of the source generator (TestMsgPackContext).
    /// </summary>
    private static IEnumerable<(string Name, LtMsgPackSerializer Serializer)> LtAndGenerated(LtMsgPackOptions options)
    {
      LtMsgPackOptions runtime = options.Clone();
      runtime.UseRegisteredContexts = false;
      runtime.CompileWritersAfter = -1;
      yield return ("Lt", new LtMsgPackSerializer(runtime));
      LtMsgPackOptions compiled = options.Clone();
      compiled.UseRegisteredContexts = false;
      compiled.CompileWritersAfter = 0;
      yield return ("Compiled", new LtMsgPackSerializer(compiled));
      LtMsgPackOptions generated = options.Clone();
      generated.Contexts = new LtMsgPackContext[] { TestMsgPackContext.Default };
      generated.UseRegisteredContexts = false;
      yield return ("Gen", new LtMsgPackSerializer(generated));
    }

    private void CompareCorpus(MsgPackSettings ls, LtMsgPackOptions ltOptions)
    {
      List<string> differences = new List<string>();
      foreach ((string which, LtMsgPackSerializer lt) in LtAndGenerated(ltOptions))
      {
        int n = 0;
        foreach (object value in Corpus())
        {
          n++;
          foreach (Type declared in new[] { value?.GetType() ?? typeof(object), typeof(object) })
            Compare($"{which} #{n} {value?.GetType().Name ?? "null"} as {declared.Name}", value, declared, ls, lt, differences);
        }
      }

      Assert.IsEmpty(differences, string.Join("\n", differences.Take(40)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CustomPropertyIds(bool schema)
    {
      MsgPackSettings ls = new MsgPackSettings { UseInexedSchema = schema, PropertyNameResolvers = new IMsgPackPropertyIdResolver[] { new AttributePropertyNameResolver() } };
      XlRenamed value = new XlRenamed { A = "a", Ignored = "x", B = 3, Child = new XlRenamed { A = "c", B = 4 }, Kids = new List<XlRenamed> { new XlRenamed { B = 9 }, new XlRenamed { A = "k" } } };

      List<string> differences = new List<string>();
      foreach ((string which, LtMsgPackSerializer lt) in LtAndGenerated(new LtMsgPackOptions { UseInexedSchema = schema, PropertyNameResolvers = ls.PropertyNameResolvers }))
        Compare(which + " renamed", value, typeof(XlRenamed), ls, lt, differences);
      Assert.IsEmpty(differences, string.Join("\n", differences));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AlwaysTypeIdsAndFullNames(bool schema)
    {
      foreach (AddTypeIdOption option in new[] { AddTypeIdOption.Always, AddTypeIdOption.IfAmbiguious | AddTypeIdOption.FullName, AddTypeIdOption.Never })
      {
        MsgPackSettings ls = new MsgPackSettings { UseInexedSchema = schema, AddTypeIdOptions = option };
        List<string> differences = new List<string>();
        foreach ((string which, LtMsgPackSerializer lt) in LtAndGenerated(new LtMsgPackOptions { UseInexedSchema = schema, AddTypeIdOptions = option }))
        {
          int n = 0;
          foreach (object value in Corpus().Where(v => !(v is Array a && a.Length > 1000) && !(v is IDictionary d && d.Count > 1000)))
          {
            n++;
            Compare($"{which} {option} #{n} {value?.GetType().Name ?? "null"}", value, value?.GetType() ?? typeof(object), ls, lt, differences);
          }
        }
        Assert.IsEmpty(differences, string.Join("\n", differences.Take(40)));
      }
    }

    [TestMethod]
    public void NoDynamicFilters()
    {
      foreach (bool schema in new[] { false, true })
      {
        MsgPackSettings ls = new MsgPackSettings { UseInexedSchema = schema, DynamicFilters = new IMsgPackPropertyIncludeDynamically[0] };
        List<string> differences = new List<string>();
        foreach ((string which, LtMsgPackSerializer lt) in LtAndGenerated(new LtMsgPackOptions { UseInexedSchema = schema, DynamicFilters = new IMsgPackPropertyIncludeDynamically[0] }))
        {
          int n = 0;
          foreach (object value in Corpus().Where(v => !(v is Array a && a.Length > 1000) && !(v is IDictionary d && d.Count > 1000)))
            Compare($"{which} #{++n}", value, value?.GetType() ?? typeof(object), ls, lt, differences);
        }
        Assert.IsEmpty(differences, string.Join("\n", differences.Take(40)));
      }
    }

    [TestMethod]
    public void StreamsAreLeftAfterThePayload()
    {
      foreach (bool schema in new[] { false, true })
      {
        LtMsgPackSerializer lt = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = schema });
        MemoryStream ms = new MemoryStream();
        lt.Serialize(new XlCircle { Name = "one" }, ms);
        lt.Serialize(new XlCircle { Name = "two" }, ms);
        byte[] bytes = ms.ToArray();

        ms.Position = 0;
        Assert.AreEqual("one", lt.Deserialize<XlCircle>(ms).Name);
        Assert.AreEqual("two", lt.Deserialize<XlCircle>(ms).Name);
        Assert.AreEqual(ms.Length, ms.Position);

        using (NonSeekable stream = new NonSeekable(bytes)) // read value by value, not to the end
        {
          Assert.AreEqual("one", lt.Deserialize<XlCircle>(stream).Name);
          Assert.AreEqual("two", lt.Deserialize<XlCircle>(stream).Name);
        }
      }
    }

    private sealed class NonSeekable : Stream
    {
      private readonly MemoryStream _inner;
      public NonSeekable(byte[] bytes) { _inner = new MemoryStream(bytes); }
      public override bool CanRead { get { return true; } }
      public override bool CanSeek { get { return false; } }
      public override bool CanWrite { get { return false; } }
      public override long Length { get { throw new NotSupportedException(); } }
      public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
      public override void Flush() { }
      public override int Read(byte[] buffer, int offset, int count) { return _inner.Read(buffer, offset, Math.Min(count, 3)); } // short reads
      public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
      public override void SetLength(long value) { throw new NotSupportedException(); }
      public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }

    [TestMethod]
    public void SharedSchemaStore()
    {
      SchemaStore store = new SchemaStore();
      MsgPackSettings ls = new MsgPackSettings { SchemaStore = store, WriteSchemaReference = true };
      XlShapes shapes = (XlShapes)Corpus().First(v => v is XlShapes);
      foreach ((string which, LtMsgPackSerializer lt) in LtAndGenerated(new LtMsgPackOptions { SchemaStore = store, WriteSchemaReference = true }))
      {
        byte[] fromLs = MsgPackSerializer.Serialize(shapes, ls);
        byte[] fromLt = lt.Serialize(shapes);
        CollectionAssert.AreEqual(fromLs, fromLt, which);
        Assert.AreEqual(Dump(MsgPackSerializer.Deserialize<XlShapes>(fromLt, ls)), Dump(lt.Deserialize<XlShapes>(fromLs)), which);
      }
    }

    private static void Compare(string name, object value, Type declared, MsgPackSettings ls, LtMsgPackSerializer lt, List<string> differences)
    {
      string lsBytes = Try(() => MsgPackSerializer.Serialize(value, declared, ls), out byte[] lsData);
      string ltBytes = Try(() => lt.Serialize(value, declared), out byte[] ltData);
      if (lsBytes != ltBytes)
      {
        differences.Add($"{name}: bytes differ\n  Ls {Short(lsBytes)}\n  Lt {Short(ltBytes)}");
        return;
      }
      if (lsData is null)
        return;

      string lsRead = TryRead(() => MsgPackSerializer.Deserialize(declared, lsData, ls));
      string ltRead = TryRead(() => lt.Deserialize(declared, lsData));
      if (lsRead.StartsWith("EX ", StringComparison.Ordinal) && ltRead.StartsWith("EX ", StringComparison.Ordinal))
        return; // both refuse the data (they may stop at another property: LsMsgPack sets the properties from last to first)
      if (lsRead != ltRead)
        differences.Add($"{name}: read differs\n  Ls {Short(lsRead)}\n  Lt {Short(ltRead)}");
    }

    private static string Short(string text)
    {
      return text.Length > 600 ? text.Substring(0, 600) + "..." : text;
    }

    private static string Try(Func<byte[]> serialize, out byte[] data)
    {
      try
      {
        data = serialize();
        return data.Length > 2000 ? "len " + data.Length + " sha " + Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(data)) : BitConverter.ToString(data);
      }
      catch (Exception ex)
      {
        data = null;
        return "EX " + ex.GetType().Name;
      }
    }

    private static string TryRead(Func<object> read)
    {
      try
      {
        return Dump(read());
      }
      catch (Exception ex)
      {
        return "EX " + ex.GetType().Name + ": " + ex.Message;
      }
    }

    internal static string Dump(object o, int depth = 0)
    {
      if (depth > 8) return "...";
      if (o is null) return "null";
      Type t = o.GetType();
      if (o is string str) return "\"" + (str.Length > 40 ? str.Substring(0, 40) + "..(" + str.Length + ")" : str) + "\"";
      if (o is byte[] b) return "bin(" + b.Length + ":" + BitConverter.ToString(b, 0, Math.Min(b.Length, 20)) + ")";
      if (o is DateTime dt) return "DateTime(" + dt.ToUniversalTime().Ticks + "," + dt.Kind + ")";
      if (o is DateTimeOffset dto) return "DateTimeOffset(" + dto.UtcTicks + "," + dto.Offset + ")";
      if (t.IsPrimitive || o is decimal || t.IsEnum || o is Guid || o is TimeSpan || o is Uri) return t.Name + ":" + Convert.ToString(o, CultureInfo.InvariantCulture);
      if (t.FullName.StartsWith("System.DateOnly", StringComparison.Ordinal) || t.FullName.StartsWith("System.TimeOnly", StringComparison.Ordinal)) return t.Name + ":" + o;
      if (o is MpExt ext) return "ext(" + ext.TypeSpecifier + ":" + BitConverter.ToString((byte[])ext.Value) + ")";
      if (o is LtMsgPack.Extensions.MsgPackExtension lext) return "ext(" + lext.TypeCode + ":" + BitConverter.ToString(lext.Data) + ")";
      if (o is IEnumerable en)
      {
        List<string> parts = new List<string>();
        int c = 0;
        foreach (object x in en)
        {
          if (c++ > 30) { parts.Add("..."); break; }
          parts.Add(Dump(x, depth + 1));
        }
        string extra = "";
        if (!(o is Array) && !t.Namespace.StartsWith("System", StringComparison.Ordinal))
          extra = "+" + DumpProps(o, t, depth);
        return t.Name + "[" + string.Join(",", parts) + "]" + extra;
      }
      if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) return "(" + Dump(t.GetProperty("Key").GetValue(o), depth + 1) + "=" + Dump(t.GetProperty("Value").GetValue(o), depth + 1) + ")";
      if (o is Memory<byte> memory) return "Memory:" + Dump(memory.ToArray());
      if (o is ReadOnlyMemory<byte> readOnly) return "ReadOnlyMemory:" + Dump(readOnly.ToArray());
      if (t.Namespace?.StartsWith("System", StringComparison.Ordinal) == true) return t.Name + ":" + Convert.ToString(o, CultureInfo.InvariantCulture); // Half, Version, tuples...
      return t.Name + DumpProps(o, t, depth);
    }

    private static string DumpProps(object o, Type t, int depth)
    {
      List<string> props = new List<string>();
      foreach (PropertyInfo pi in t.GetProperties(BindingFlags.Instance | BindingFlags.Public).OrderBy(p => p.Name, StringComparer.Ordinal))
        if (pi.GetIndexParameters().Length == 0 && pi.DeclaringType.Namespace?.StartsWith("System", StringComparison.Ordinal) != true)
          props.Add(pi.Name + "=" + Dump(pi.GetValue(o), depth + 1));
      return "{" + string.Join(",", props) + "}";
    }

  }
}
