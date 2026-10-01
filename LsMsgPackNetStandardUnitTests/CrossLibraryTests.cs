using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Attributes;
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

    public class XlTaggedList : List<string> { public string Tag { get; set; } }

    [SerializeEnumerable(SerializeProperties = true)]
    public class XlNamedMap : Dictionary<string, int> { public string Name { get; set; } }

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
      values.Add(new XlTaggedList { "only" });
      values.Add(new XlStruct { X = 1, Y = "struct" });
      values.Add(new int[,] { { 1, 2 }, { 3, 4 } }); // written flat, cannot be read back (by either)
      values.Add(new XlCircle { Name = "root", Radius = 1 });
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
      CompareCorpus(ls, new LtMsgPackSerializer(ltOptions));
    }

    [TestMethod]
    [DynamicData(nameof(Orders))]
    public void SameBytesAndValuesPerOrder(PropertyOrder order, SchemaMode mode, ObjectLayout layout)
    {
      MsgPackSettings ls = new MsgPackSettings();
      Configure(ls, EndianAction.SwapIfCurrentSystemIsLittleEndian, true, mode, layout, order);
      LtMsgPackOptions ltOptions = new LtMsgPackOptions();
      Configure(ltOptions, EndianAction.SwapIfCurrentSystemIsLittleEndian, true, mode, layout, order);
      CompareCorpus(ls, new LtMsgPackSerializer(ltOptions));
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
      CompareCorpus(ls, new LtMsgPackSerializer(ltOptions));
    }

    private void CompareCorpus(MsgPackSettings ls, LtMsgPackSerializer lt)
    {
      List<string> differences = new List<string>();
      int n = 0;
      foreach (object value in Corpus())
      {
        n++;
        foreach (Type declared in new[] { value?.GetType() ?? typeof(object), typeof(object) })
          Compare($"#{n} {value?.GetType().Name ?? "null"} as {declared.Name}", value, declared, ls, lt, differences);
      }

      Assert.IsEmpty(differences, string.Join("\n", differences.Take(40)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CustomPropertyIds(bool schema)
    {
      MsgPackSettings ls = new MsgPackSettings { UseInexedSchema = schema, PropertyNameResolvers = new IMsgPackPropertyIdResolver[] { new AttributePropertyNameResolver() } };
      LtMsgPackSerializer lt = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = schema, PropertyNameResolvers = ls.PropertyNameResolvers });
      XlRenamed value = new XlRenamed { A = "a", Ignored = "x", B = 3, Child = new XlRenamed { A = "c", B = 4 }, Kids = new List<XlRenamed> { new XlRenamed { B = 9 }, new XlRenamed { A = "k" } } };

      List<string> differences = new List<string>();
      Compare("renamed", value, typeof(XlRenamed), ls, lt, differences);
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
        LtMsgPackSerializer lt = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = schema, AddTypeIdOptions = option });
        List<string> differences = new List<string>();
        int n = 0;
        foreach (object value in Corpus().Where(v => !(v is Array a && a.Length > 1000) && !(v is IDictionary d && d.Count > 1000)))
        {
          n++;
          Compare($"{option} #{n} {value?.GetType().Name ?? "null"}", value, value?.GetType() ?? typeof(object), ls, lt, differences);
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
        LtMsgPackSerializer lt = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = schema, DynamicFilters = new IMsgPackPropertyIncludeDynamically[0] });
        List<string> differences = new List<string>();
        int n = 0;
        foreach (object value in Corpus().Where(v => !(v is Array a && a.Length > 1000) && !(v is IDictionary d && d.Count > 1000)))
          Compare($"#{++n}", value, value?.GetType() ?? typeof(object), ls, lt, differences);
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
      LtMsgPackSerializer lt = new LtMsgPackSerializer(new LtMsgPackOptions { SchemaStore = store, WriteSchemaReference = true });
      XlShapes shapes = (XlShapes)Corpus().First(v => v is XlShapes);

      byte[] fromLs = MsgPackSerializer.Serialize(shapes, ls);
      byte[] fromLt = lt.Serialize(shapes);
      CollectionAssert.AreEqual(fromLs, fromLt);
      Assert.AreEqual(Dump(MsgPackSerializer.Deserialize<XlShapes>(fromLt, ls)), Dump(lt.Deserialize<XlShapes>(fromLs)));
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
      if (o is DateTimeOffset dto) return "DateTimeOffset(" + dto.UtcTicks + ")";
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
