// Byte and round trip equivalence of LtMsgPack (and its HTTP serializer): run against two builds and diff the output.
using LsMsgPack;
using LtMsgPack;
using LtMsgPack.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
public enum E8 : byte { A = 1, B = 200 } public enum E16 : short { A = -300, B = 3 } public enum E64 : long { A = long.MinValue, B = 5 }
public class Nested { public string Name { get; set; } public double D { get; set; } public float F { get; set; } public int? N { get; set; } public byte[] Blob { get; set; } public Guid G { get; set; } public DateTime When { get; set; } public DateTimeOffset Off { get; set; } public decimal M { get; set; } public E16 E { get; set; } public bool B { get; set; } public List<object> Objs { get; set; } public Dictionary<string, long> Map { get; set; } public ulong U { get; set; } public sbyte S { get; set; } }
public class Bcl { public char C { get; set; } public TimeSpan T { get; set; } public TimeSpan? N { get; set; } public DateOnly D { get; set; } public TimeOnly O { get; set; } public Uri U { get; set; } public List<object> Boxed { get; set; } }
public class WithGuid { public Guid G { get; set; } public byte[] B { get; set; } public object O { get; set; } }
public class Attributed { [System.Xml.Serialization.XmlElement("renamed")] public string A { get; set; } [System.Xml.Serialization.XmlIgnore] public string Ignored { get; set; } public int ReadOnly { get { return 5; } } public int B { get; set; } public Attributed Child { get; set; } public List<Attributed> Kids { get; set; } }
public class Poly { public object Any { get; set; } public Attributed Base { get; set; } public List<object> Items { get; set; } public int[] Ints { get; set; } public Dictionary<string, object> Bag { get; set; } }
public class WithDefault { [System.ComponentModel.DefaultValue("Woof")] public string Sound { get; set; } = "Woof"; public int Zero { get; set; } public string Last { get; set; } }
public struct EqVec { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } public float Sum => X + Y + Z; } // structs: by reference (a computed property), boxed in an object, by their constructor
public struct EqLine { public EqVec From { get; set; } public EqVec To { get; set; } public string Label { get; set; } public EqVec? Hint { get; set; } }
public struct EqInit { public EqInit() { N = 5; } public int N { get; set; } }
public class EqMover { public EqVec Position { get; set; } public EqVec Velocity { get; set; } public EqVec? Target { get; set; } public List<EqVec> Path { get; set; } public EqVec[] Points { get; set; } public object Boxed { get; set; } public EqLine Line { get; set; } public EqInit Init { get; set; } }
public class EqDerivedDict : Dictionary<string, int> { public int Extra { get; set; } }
public class EqDerivedSet : HashSet<int> { }
public class EqDerivedList : List<int> { }
public class EqColl {
  public Dictionary<string, string> DS { get; set; } public Dictionary<string, int> DI { get; set; } public Dictionary<int, Nested> DN { get; set; } public Dictionary<E8, string> DE { get; set; } public Dictionary<Guid, long?> DG { get; set; } public Dictionary<string, object> DO { get; set; } public Dictionary<object, object> DOO { get; set; } public Dictionary<string, EqVec> DV { get; set; } public Dictionary<string, List<int>> DL { get; set; }
  public HashSet<int> HI { get; set; } public HashSet<string> HS { get; set; } public HashSet<EqVec> HV { get; set; }
  public IList<int> IL { get; set; } public ICollection<string> IC { get; set; } public IEnumerable<Nested> IE { get; set; } public IReadOnlyList<EqVec> IR { get; set; } public IReadOnlyCollection<long> IRC { get; set; } public ISet<int> IS { get; set; }
  public IDictionary<string, int> ID { get; set; } public IReadOnlyDictionary<int, string> IRD { get; set; } public IEnumerable<KeyValuePair<string, int>> IKV { get; set; } public SortedDictionary<string, int> SD { get; set; } public object Any { get; set; } public IEnumerable<object> IO { get; set; } public IList<IList<int>> ILL { get; set; }
  [LsMsgPack.TypeResolving.Attributes.SerializeEnumerable(SerializeElements = true, SerializeProperties = true)] public Dictionary<string, int> AttrD { get; set; }
  [LsMsgPack.TypeResolving.Attributes.SerializeEnumerable(SerializeElements = false, SerializeProperties = true)] public Dictionary<string, int> AttrE { get; set; }
  [LsMsgPack.TypeResolving.Attributes.SerializeEnumerable(SerializeElements = true, SerializeProperties = true)] public HashSet<int> AttrH { get; set; }
  [LsMsgPack.TypeResolving.Attributes.SerializeEnumerable(SerializeElements = true, SerializeProperties = true)] public IList<int> AttrI { get; set; }
}
public class EqNumbers { public int[] I { get; set; } public long[] L { get; set; } public float[] F { get; set; } public double[] D { get; set; } public List<int> LI { get; set; } public List<long> LL { get; set; } public List<float> LF { get; set; } public List<double> LD { get; set; } public DateTime[] T { get; set; } public object Any { get; set; } public IList<int> IL { get; set; } public IEnumerable<double> IE { get; set; } }
public class EqDerivedDoubles : List<double> { }
public class EqSmall { public sbyte[] SB { get; set; } public short[] S { get; set; } public ushort[] US { get; set; } public uint[] U { get; set; } public ulong[] UL { get; set; } public bool[] B { get; set; } public Guid[] G { get; set; } public List<short> LS { get; set; } public List<uint> LU { get; set; } public List<bool> LB { get; set; } public List<Guid> LG { get; set; } public IList<bool> IB { get; set; } public object Any { get; set; } }
public class EqDerivedGuids : List<Guid> { }
public class EqDecimals { public decimal[] A { get; set; } public List<decimal> L { get; set; } public HashSet<decimal> S { get; set; } public object Any { get; set; } }
public sealed class EqNoGuard : LsMsgPack.TypeResolving.Interfaces.IMsgPackTypeGuard { public bool IsAllowed(Type type, Type assignedTo, LsMsgPack.Meta.FullPropertyInfo prop, MsgPackOptions settings) => false; }
public static class P {
  static string Hex(byte[] b) => BitConverter.ToString(b);
  static string H(string s) => BitConverter.ToString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(s))).Replace("-", "").Substring(0, 16);
  static string Dump(object o, int depth = 0) {
    if (depth > 6) return "...";
    if (o is null) return "null";
    Type t = o.GetType();
    if (o is string str) return "\"" + (str.Length > 40 ? str.Substring(0, 40) + "..(" + str.Length + ")" : str) + "\"";
    if (o is byte[] b) return "bin(" + b.Length + ":" + BitConverter.ToString(b, 0, Math.Min(b.Length, 20)) + ")";
    if (o is DateTime dt) return "DateTime(" + dt.ToUniversalTime().Ticks + "," + dt.Kind + ")";
    if (t.IsPrimitive || o is decimal || t.IsEnum || o is Guid || o is DateTimeOffset) return t.Name + ":" + Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture);
    if (o is MsgPackItem mi) return mi.GetType().Name + "{" + Dump(mi.Value, depth + 1) + "}";
    if (o is System.Collections.IEnumerable en) { var parts = new List<string>(); int c = 0; foreach (object x in en) { if (c++ > 30) { parts.Add("..."); break; } parts.Add(Dump(x, depth + 1)); } return t.Name + "[" + string.Join(",", parts) + "]"; }
    if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) return "(" + Dump(t.GetProperty("Key").GetValue(o), depth + 1) + "=" + Dump(t.GetProperty("Value").GetValue(o), depth + 1) + ")";
    var props = new List<string>(); foreach (var pi in t.GetProperties()) if (pi.GetIndexParameters().Length == 0) props.Add(pi.Name + "=" + Dump(pi.GetValue(o), depth + 1));
    return t.Name + "{" + string.Join(",", props) + "}";
  }
  static List<object> Values() {
    var values = new List<object> { null, true, false, "", "a", new string('x', 31), new string('y', 32), new string('z', 255), new string('w', 256), new string('v', 70000), "héllo wörld ✓ 😀",
      new byte[0], new byte[] { 1, 2, 3 }, new byte[300], new byte[70000], Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
      1.5f, -0.0f, float.NaN, float.MaxValue, 1.5, double.Epsilon, double.NegativeInfinity, 12.5m, -79228162514264337593543950335m, 0.0000001m,
      new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc), new DateTime(2026, 3, 4, 5, 6, 7, 123, DateTimeKind.Utc), new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2600, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero),
      E8.A, E8.B, E16.A, E16.B, E64.A, E64.B,
      new DateTime(1969, 12, 31, 23, 59, 58, 500, DateTimeKind.Utc), new DateTime(1601, 1, 1, 0, 0, 0, 250, DateTimeKind.Utc),
      'A', '\u00e9', TimeSpan.FromMinutes(90), TimeSpan.FromTicks(-12345), new DateOnly(2026, 9, 29), new TimeOnly(13, 45, 10, 5), new Uri("https://example.com/x?y=1") };
    long[] ints = { 0, 1, 31, 32, 127, 128, 255, 256, 32767, 32768, 65535, 65536, int.MaxValue, 2147483648L, uint.MaxValue, 4294967296L, long.MaxValue, -1, -31, -32, -33, -128, -129, -32768, -32769, int.MinValue, long.MinValue };
    foreach (long i in ints) {
      values.Add(i);
      if (i >= int.MinValue && i <= int.MaxValue) values.Add((int)i);
      if (i >= short.MinValue && i <= short.MaxValue) values.Add((short)i);
      if (i >= sbyte.MinValue && i <= sbyte.MaxValue) values.Add((sbyte)i);
      if (i >= 0) { values.Add((ulong)i); if (i <= uint.MaxValue) values.Add((uint)i); if (i <= ushort.MaxValue) values.Add((ushort)i); if (i <= byte.MaxValue) values.Add((byte)i); }
    }
    values.Add(ulong.MaxValue);
    values.Add(new object[] { 1, "two", 3.0, null, new object[] { true } });
    values.Add(new int[20]); values.Add(new List<string> { "a", "b" }); values.Add(new Dictionary<int, string> { { 1, "one" }, { 300, "three hundred" } });
    var arr = new object[70000]; for (int i = 0; i < arr.Length; i++) arr[i] = i; values.Add(arr);
    var bigMap = new Dictionary<int, int>(); for (int i = 0; i < 70000; i++) bigMap[i] = -i; values.Add(bigMap);
    var nested = new Nested { Name = "n", D = 2.25, F = -3.5f, N = 7, Blob = new byte[] { 9, 8 }, G = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), When = new DateTime(2026, 1, 1, 12, 0, 0, 500, DateTimeKind.Utc), Off = new DateTimeOffset(2021, 5, 6, 7, 8, 9, TimeSpan.Zero), M = 1.23m, E = E16.A, B = true, Objs = new List<object> { 1, "x", 2.5m, E8.B, new Nested { Name = "inner" } }, Map = new Dictionary<string, long> { { "k", long.MinValue } }, U = ulong.MaxValue, S = -5 };
    values.Add(nested);
    values.Add(new Bcl { C = 'Z', T = TimeSpan.FromSeconds(-1.5), N = TimeSpan.FromDays(3), D = new DateOnly(1999, 12, 31), O = new TimeOnly(23, 59), U = new Uri("https://example.com/a"), Boxed = new List<object> { TimeSpan.FromHours(1), 'q', new DateOnly(2000, 1, 1) } });
    values.Add(new Poly { Any = new Nested { Name = "p" }, Base = new Attributed { A = "x", Kids = new List<Attributed> { new Attributed() } }, Items = new List<object> { 1L, "s", new WithGuid { G = Guid.Empty, O = 3 }, null, new int[] { 1, 2 } }, Ints = new[] { 5, -5, 500 }, Bag = new Dictionary<string, object> { { "a", 1 }, { "b", new Nested() }, { "c", null } } });
    values.Add(new string('é', 16)); values.Add(new string('é', 30)); values.Add(new string('é', 200)); values.Add(new string('✓', 85)); values.Add(new string('✓', 40000)); values.Add(new string('é', 70000)); values.Add("\ud800x"); foreach (string shortText in new[] { "ab", "abc", "abcd", "abcde", "abcdefg", "abcdefgh", "abcdefghi", "aé", "éb", "abé", "abcé", "éabcdefg", "abcdéfgh", "abcdefgé", "\u0100bc", "ab\u8080", "\u0080", "\u007f\u007f\u007f\u007f" }) values.Add(shortText); values.Add(new string('x', 30) + "é"); values.Add(new string('x', 254) + "é"); values.Add(new string('x', 65534) + "é"); values.Add("abc\ud800"); values.Add("abc\udc00def"); values.Add("abc😀def" + new string('q', 40)); values.Add(new string('x', 17) + "ü" + new string('y', 17)); values.Add(new string('a', 65537));
    values.Add(new Nested { Name = "", Objs = new List<object> { "", null } }); // empty strings are written (FilterDefaultValues), null is left out
    values.Add(new WithDefault { Sound = null, Zero = 0, Last = null }); values.Add(new WithDefault { Sound = "Woof", Zero = 7, Last = "l" });
    values.Add(new Bench.Invoice[] { }); values.AddRange(Bench.Invoices.Take(3));
    values.Add(new EqMover { Position = new EqVec { X = 1.5f, Y = -0f, Z = float.NaN }, Target = new EqVec(), Path = new List<EqVec> { new EqVec { X = 1 }, new EqVec() }, Points = new[] { new EqVec { Z = -2 } }, Boxed = new EqVec { Y = 7 }, Line = new EqLine { To = new EqVec { X = 3 }, Label = "l", Hint = new EqVec { Z = 1 } }, Init = new EqInit() });
    values.Add(new EqMover()); values.Add(new EqVec { X = 0.5f, Z = -8 }); values.Add(new EqLine { From = new EqVec { Y = 2 } }); values.Add(new List<EqLine> { new EqLine { Label = "a" }, new EqLine() }); values.Add(new EqInit { N = 0 });
    values.AddRange(Collections());
    return values;
  }
  static List<object> Collections() {
    var values = new List<object>();
    var kids = new List<Nested> { new Nested { Name = "k" }, null, new Nested() };
    values.Add(new EqColl());
    values.Add(new EqColl { DS = new Dictionary<string, string> { { "a", "b" }, { "c", null }, { "", "empty" }, { "@", "content" }, { new string('k', 40), "long" }, { "é", "ü" } }, DI = new Dictionary<string, int> { { "x", 1 }, { "y", -300 }, { "z", 0 } }, DN = new Dictionary<int, Nested> { { 1, new Nested { Name = "n" } }, { -5, null }, { 70000, new Nested() } }, DE = new Dictionary<E8, string> { { E8.A, "a" }, { E8.B, "b" } }, DG = new Dictionary<Guid, long?> { { Guid.Empty, null }, { Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), 7 } }, DO = new Dictionary<string, object> { { "a", 1 }, { "b", "s" }, { "c", null }, { "d", new Nested { Name = "o" } }, { "e", new List<int> { 1 } }, { "f", new Dictionary<string, int> { { "q", 1 } } } }, DOO = new Dictionary<object, object> { { "s", 1 }, { 2, "two" }, { 2.5, null }, { E8.B, true } }, DV = new Dictionary<string, EqVec> { { "v", new EqVec { X = 1 } }, { "w", new EqVec() } }, DL = new Dictionary<string, List<int>> { { "l", new List<int> { 1, 2 } }, { "n", null } } });
    values.Add(new EqColl { HI = new HashSet<int> { 5, 1, 300, -7 }, HS = new HashSet<string> { "b", null, "a", "" }, HV = new HashSet<EqVec> { new EqVec { X = 1 }, new EqVec() } });
    values.Add(new EqColl { IL = new List<int> { 1, 2 }, IC = new List<string> { "a", null }, IE = kids, IR = new List<EqVec> { new EqVec { Y = 2 } }, IRC = new List<long> { long.MaxValue }, IS = new HashSet<int> { 3, 2 }, ID = new Dictionary<string, int> { { "a", 1 }, { "", 2 } }, IRD = new Dictionary<int, string> { { 1, "x" } }, IKV = new Dictionary<string, int> { { "kv", 1 } }, IO = new List<object> { 1, "x", null, new Nested() }, ILL = new List<IList<int>> { new List<int> { 1 }, new[] { 2, 3 }, null } });
    values.Add(new EqColl { IL = new[] { 1, 2 }, IC = new[] { "a" }, IE = kids.ToArray(), IR = new[] { new EqVec { Z = 3 } }, IRC = new long[0], IS = new SortedSet<int> { 9, 8 }, ID = new SortedDictionary<string, int> { { "b", 1 }, { "a", 2 } }, IRD = new System.Collections.ObjectModel.ReadOnlyDictionary<int, string>(new Dictionary<int, string> { { 2, "y" } }), IKV = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("p", 1), new KeyValuePair<string, int>("p", 2) }, IO = new object[] { 1, new object[] { 2 } }, ILL = new IList<int>[] { new EqDerivedList { 1 } } });
    values.Add(new EqColl { IL = new System.Collections.ObjectModel.Collection<int> { 1 }, IC = new HashSet<string> { "h" }, IE = kids.Where(x => x != null), IR = new System.Collections.ObjectModel.ReadOnlyCollection<EqVec>(new List<EqVec> { new EqVec() }), IRC = new HashSet<long> { 1 }, IS = new EqDerivedSet { 1 }, ID = new EqDerivedDict { { "d", 1 } }, IRD = System.Collections.Immutable.ImmutableDictionary<int, string>.Empty.Add(1, "i"), IKV = new KeyValuePair<string, int>[] { new KeyValuePair<string, int>("arr", 1) }, IO = Enumerable.Range(0, 3).Select(x => (object)x) });
    values.Add(new EqColl { IL = new List<int>(), IC = new string[0], IE = new List<Nested>(), IS = new HashSet<int>(), ID = new Dictionary<string, int>(), DS = new Dictionary<string, string>(), HI = new HashSet<int>(), SD = new SortedDictionary<string, int> { { "s", 1 } } });
    values.Add(new EqColl { Any = new List<int> { 1 } }); values.Add(new EqColl { Any = new Dictionary<string, int> { { "a", 1 } } }); values.Add(new EqColl { Any = new HashSet<int> { 1 } }); values.Add(new EqColl { Any = new[] { 1, 2 } }); values.Add(new EqColl { Any = new Dictionary<string, object> { { "", 1 }, { "@", 2 } } }); values.Add(new EqColl { Any = new EqDerivedDict { { "x", 1 } } }); values.Add(new EqColl { Any = new List<Nested> { new Nested() } });
    values.Add(new EqColl { AttrD = new Dictionary<string, int> { { "a", 1 } }, AttrE = new Dictionary<string, int> { { "b", 2 } }, AttrH = new HashSet<int> { 3 }, AttrI = new List<int> { 4 } });
    values.Add(new Dictionary<string, string> { { "root", "x" } }); values.Add(new HashSet<int> { 1, 2 }); values.Add(new Dictionary<string, Nested> { { "n", new Nested { Name = "z" } } }); values.Add(new EqDerivedDict { { "a", 1 } }); values.Add(new EqDerivedSet { 1 }); values.Add(new EqDerivedList { 1 });
    values.Add(new Dictionary<string, object> { { "", "only reserved" } }); values.Add(new Dictionary<string, int> { { "@", 1 }, { "", 2 } }); values.Add(new List<Dictionary<string, int>> { new Dictionary<string, int> { { "a", 1 } }, null }); values.Add(new Dictionary<string, Dictionary<string, int>> { { "a", new Dictionary<string, int> { { "b", 1 } } } }); values.Add(new HashSet<Nested> { new Nested() }); values.Add(new List<IList<string>> { new List<string> { "x" }, new string[] { "y" } });
    // Arrays and lists of numbers (written and read in bulk), dates
    int[] ei = { 0, 1, 31, 32, 127, 128, 255, 256, 32767, 32768, 65535, 65536, int.MaxValue, -1, -31, -32, -33, -128, -129, -32768, -32769, int.MinValue };
    long[] el = { 0, 1, 31, 32, 127, 128, 255, 256, 32767, 32768, 65535, 65536, int.MaxValue, 2147483648L, uint.MaxValue, 4294967296L, long.MaxValue, -1, -31, -32, -33, -128, -129, -32768, -32769, int.MinValue, -2147483649L, long.MinValue };
    float[] ef = { 0f, -0.0f, 1.5f, -2.25f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, float.MinValue, float.Epsilon };
    double[] ed = { 0, -0.0, 1.5, -2.25, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, double.MinValue, double.Epsilon, 1.5f, 0.1 };
    DateTime[] et = { new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc), new DateTime(2026, 3, 4, 5, 6, 7, 123, DateTimeKind.Utc).AddTicks(4567), new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(1969, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc), new DateTime(2106, 2, 7, 6, 28, 15, DateTimeKind.Utc), new DateTime(2106, 2, 7, 6, 28, 16, DateTimeKind.Utc),
      new DateTime(2514, 5, 30, 1, 53, 3, DateTimeKind.Utc).AddTicks(9999999), new DateTime(2514, 5, 30, 1, 53, 4, DateTimeKind.Utc), DateTime.MinValue, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc), new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Local), new DateTime(2026, 1, 1, 12, 0, 0, 5, DateTimeKind.Unspecified), default(DateTime) };
    int[] bigI = Enumerable.Range(0, 2500).Select(x => x * 7919 - 1000000).ToArray();
    values.Add(ei); values.Add(el); values.Add(ef); values.Add(ed); values.Add(et); values.Add(ei.ToList()); values.Add(el.ToList()); values.Add(ef.ToList()); values.Add(ed.ToList()); values.Add(et.ToList());
    values.Add(new int[0]); values.Add(new long[0]); values.Add(new float[0]); values.Add(new double[0]); values.Add(new List<int>()); values.Add(new List<double>());
    values.Add(bigI); values.Add(bigI.Select(x => x * 1000003L).ToArray()); values.Add(bigI.Select(x => x * 0.37f).ToArray()); values.Add(bigI.Select(x => x * 0.37).ToArray()); values.Add(bigI.ToList()); values.Add(bigI.Select(x => x * 0.37).ToList()); values.Add(bigI.Select(x => (long)x << 20).ToList()); values.Add(bigI.Select(x => x * 0.5f).ToList());
    values.Add(new uint[] { 1, uint.MaxValue }); values.Add(new ulong[] { 1, ulong.MaxValue }); values.Add(new short[] { 1, -300 }); values.Add(new EqDerivedDoubles { 1.5, 2 });
    // The other integer types, bools and Guids in bulk (bin 16; the presets write Guids as strings)
    sbyte[] esb = { 0, 1, 31, 32, 127, -1, -31, -32, -33, -128 };
    short[] es = { 0, 1, 31, 32, 127, 128, 255, 256, 32767, -1, -31, -32, -33, -128, -129, -32768 };
    ushort[] eus = { 0, 1, 31, 32, 127, 128, 255, 256, 32767, 32768, 65535 };
    uint[] eu = { 0, 1, 31, 32, 127, 128, 255, 256, 32767, 32768, 65535, 65536, int.MaxValue, 2147483648u, uint.MaxValue };
    ulong[] eul = { 0, 1, 31, 32, 127, 128, 255, 256, 65535, 65536, uint.MaxValue, 4294967296UL, long.MaxValue, 9223372036854775808UL, ulong.MaxValue };
    bool[] eb = { true, false, false, true };
    Guid[] eg = { Guid.Empty, Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff") };
    values.Add(esb); values.Add(es); values.Add(eus); values.Add(eu); values.Add(eul); values.Add(eb); values.Add(eg); values.Add(es.ToList()); values.Add(eu.ToList()); values.Add(eul.ToList()); values.Add(eb.ToList()); values.Add(eg.ToList()); values.Add(esb.ToList()); values.Add(eus.ToList());
    values.Add(new sbyte[0]); values.Add(new bool[0]); values.Add(new Guid[0]); values.Add(new List<short>()); values.Add(new List<Guid>()); values.Add(new EqDerivedGuids { Guid.Empty });
    values.Add(bigI.Select(x => (short)x).ToArray()); values.Add(bigI.Select(x => (uint)x).ToArray()); values.Add(bigI.Select(x => (ulong)(long)x << 9).ToArray()); values.Add(bigI.Select(x => (sbyte)x).ToList()); values.Add(bigI.Select(x => x % 3 == 0).ToArray()); values.Add(bigI.Select(x => new Guid(x, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10)).ToArray()); values.Add(bigI.Select(x => (ushort)x).ToList());
    values.Add(new HashSet<int>(bigI)); values.Add(new HashSet<long>(el)); values.Add(new HashSet<short>(es)); values.Add(new HashSet<Guid>(eg)); values.Add(new HashSet<ulong>(eul)); values.Add(new HashSet<double>(ed)); values.Add(new HashSet<bool> { true }); values.Add(new HashSet<Guid>());
    decimal[] edec = { 0m, -0m, 1.5m, -2.25m, decimal.MaxValue, decimal.MinValue, 0.0000000000000000000000000001m, 1.000m, 79228162514264337593543950335m / 3 };
    values.Add(edec); values.Add(edec.ToList()); values.Add(new HashSet<decimal>(edec)); values.Add(bigI.Select(x => x * 1.25m).ToArray()); values.Add(new decimal[0]); values.Add(new List<decimal> { 1m });
    values.Add(new EqDecimals { A = edec, L = edec.ToList(), S = new HashSet<decimal> { 2m }, Any = edec });
    values.Add(new EqSmall());
    values.Add(new EqSmall { SB = esb, S = es, US = eus, U = eu, UL = eul, B = eb, G = eg, LS = es.ToList(), LU = eu.ToList(), LB = eb.ToList(), LG = eg.ToList(), IB = eb, Any = eg });
    values.Add(new EqSmall { S = (short[])(object)new ushort[] { 1, 65535 }, SB = (sbyte[])(object)new byte[] { 1, 255 }, LG = new EqDerivedGuids { Guid.Empty }, Any = new List<bool> { true }, IB = new List<bool> { false } });
    values.Add(new EqNumbers());
    values.Add(new EqNumbers { I = ei, L = el, F = ef, D = ed, LI = ei.ToList(), LL = el.ToList(), LF = ef.ToList(), LD = ed.ToList(), T = et, Any = ed, IL = ei, IE = ed });
    values.Add(new EqNumbers { I = (int[])(object)new uint[] { 1, uint.MaxValue }, L = (long[])(object)new ulong[] { 1, ulong.MaxValue }, LD = new EqDerivedDoubles { 1.5 }, Any = new List<float> { 1.5f }, IL = new List<int> { 1, 2 }, IE = new List<double> { 0.5 } });
    values.Add(new EqNumbers { I = new int[0], L = new long[0], F = new float[0], D = new double[0], LI = new List<int>(), LL = new List<long>(), LF = new List<float>(), LD = new List<double>(), T = new DateTime[0], Any = new long[] { 5 } });
    return values;
  }
  // Data that is not what the typed readers expect (of other writers, or damaged): the same values, or the same errors
  static byte[] Raw(params object[] parts) { var ms = new System.IO.MemoryStream(); foreach (object p in parts) { switch (p) { case int b: ms.WriteByte((byte)b); break; case string t: byte[] u = Encoding.UTF8.GetBytes(t); if (u.Length < 32) ms.WriteByte((byte)(0xA0 + u.Length)); else { ms.WriteByte(0xD9); ms.WriteByte((byte)u.Length); } ms.Write(u); break; case byte[] r: ms.Write(r); break; } } return ms.ToArray(); }
  static void Odd(StringBuilder sb, LtMsgPackSerializer s, byte[] prefix) {
    var bodies = new (string, byte[])[] {
      ("number key", Raw(0x81, 1, 2)), ("nil key", Raw(0x81, 0xC0, 2)), ("nil value", Raw(0x81, "a", 0xC0)), ("dup key", Raw(0x82, "a", 1, "a", 2)), ("empty key", Raw(0x81, "", 1)), ("content key", Raw(0x81, "@", 1)), ("content array", Raw(0x81, "@", 0x92, 1, 2)), ("str8 empty key", Raw(0x81, 0xD9, 0, 1)), ("str8 content key", Raw(0x81, 0xD9, 1, 0x40, 1)), ("str16 key", Raw(0x81, 0xDA, 0, 1, 0x61, 1)), ("long key", Raw(0x81, new string('k', 40), 1)),
      ("text number key", Raw(0x81, "5", 1)), ("text value", Raw(0x81, "a", "7")), ("bad text value", Raw(0x81, "a", "x")), ("array value", Raw(0x81, "a", 0x91, 1)), ("map value", Raw(0x81, "a", 0x81, "b", 1)), ("float value", Raw(0x81, "a", 0xCA, 0x40, 0x20, 0, 0)), ("bool key", Raw(0x81, 0xC3, 1)), ("bin key", Raw(0x81, 0xC4, 1, 65, 1)), ("float key", Raw(0x81, 0xCA, 0x40, 0x20, 0, 0, 1)), ("array key", Raw(0x81, 0x91, 1, 1)),
      ("array", Raw(0x92, 1, 2)), ("array nil", Raw(0x92, 1, 0xC0)), ("array dup", Raw(0x93, 1, 1, 2)), ("array text", Raw(0x92, "1", "2")), ("array pairs", Raw(0x91, 0x92, "a", 1)), ("array odd pairs", Raw(0x91, 0x93, "a", 1, 2)), ("empty array", Raw(0x90)), ("empty map", Raw(0x80)), ("array16", Raw(0xDC, 0, 1, 1)), ("map16", Raw(0xDE, 0, 1, "a", 1)), ("number", Raw(5)), ("text", Raw("x")), ("nil", Raw(0xC0)),
      ("wrapped list", Raw(0x82, "", "List<Int32>", "@", 0x92, 1, 2)), ("wrapped array", Raw(0x82, "", "Int32[]", "@", 0x92, 1, 2)), ("wrapped set", Raw(0x82, "", "HashSet<Int32>", "@", 0x92, 1, 2)), ("wrapped dict", Raw(0x82, "", "Dictionary<String, Int32>", "@", 0x81, "a", 1)), ("wrapped dict reserved", Raw(0x82, "", "Dictionary<String, Int32>", "@", 0x82, "", 1, "@", 2)), ("wrapped dict number key", Raw(0x82, "", "Dictionary<String, Int32>", "@", 0x81, 1, 1)), ("wrapped sorted", Raw(0x82, "", "SortedDictionary<String, Int32>", "@", 0x81, "a", 1)), ("wrapped unknown", Raw(0x82, "", "NoSuchType", "@", 0x91, 1)), ("wrapped not assignable", Raw(0x82, "", "Nested", "@", 0x91, 1)), ("wrapped number id", Raw(0x82, "", 0, "@", 0x91, 1)), ("wrapped nil content", Raw(0x82, "", "List<Int32>", "@", 0xC0)), ("wrapped wrong content", Raw(0x82, "", "List<Int32>", "@", 5)), ("wrapped map content", Raw(0x82, "", "List<Int32>", "@", 0x81, "a", 1)), ("wrapped third key", Raw(0x83, "", "List<Int32>", "@", 0x91, 1, "x", 1)), ("wrapped content first", Raw(0x82, "@", 0x91, 1, "", "List<Int32>")), ("id only", Raw(0x81, "", "List<Int32>")), ("cut map", Raw(0x82, "a", 1)), ("cut wrap", Raw(0x82, "")), ("cut key", Raw(0x81, 0xA5, 0x61)),
      ("mixed ints", Raw(0x9F, 1, 0xCC, 200, 0xCD, 1, 2, 0xCE, 0, 1, 2, 3, 0xD0, 0x80, 0xD1, 0x80, 0, 0xD2, 0x80, 0, 0, 0, 0xD3, 0, 0, 0, 0, 0, 0, 0, 9, 0xCF, 0, 0, 0, 0, 0, 0, 0, 8, 0xE0, 0xFF, 0x7F, 0xC0, 0xCA, 0x40, 0x20, 0, 0, 0xCB, 0x40, 0x04, 0, 0, 0, 0, 0, 0)),
      ("uint32 max", Raw(0x92, 1, 0xCE, 0xFF, 0xFF, 0xFF, 0xFF)), ("uint64 max", Raw(0x92, 1, 0xCF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF)), ("int64 min", Raw(0x92, 1, 0xD3, 0x80, 0, 0, 0, 0, 0, 0, 0)), ("big int64", Raw(0x91, 0xD3, 0, 0, 0, 1, 0, 0, 0, 0)),
      ("floats", Raw(0x93, 0xCA, 0x40, 0x20, 0, 0, 0xCA, 0x7F, 0xC0, 0, 0, 0xCA, 0x80, 0, 0, 0)), ("doubles", Raw(0x93, 0xCB, 0x40, 0x04, 0, 0, 0, 0, 0, 0, 0xCB, 0x7F, 0xF8, 0, 0, 0, 0, 0, 0, 0xCA, 0x40, 0x20, 0, 0)), ("fraction", Raw(0x92, 1, 0xCB, 0x40, 0x04, 0, 0, 0, 0, 0, 0)),
      ("ints then text", Raw(0x93, 1, 2, "x")), ("ints then true", Raw(0x93, 1, 2, 0xC3)), ("ints then array", Raw(0x93, 1, 2, 0x91, 3)), ("nils", Raw(0x93, 0xC0, 1, 0xC0)),
      ("cut uint16", Raw(0x92, 1, 0xCD, 0)), ("cut uint32", Raw(0x93, 1, 2, 0xCE, 0, 0, 0)), ("cut int64", Raw(0x92, 1, 0xD3, 0, 0, 0)), ("cut float", Raw(0x92, 0xCA, 0x40, 0x20, 0, 0, 0xCA, 0x40)), ("cut double", Raw(0x92, 0xCB, 0x40, 0x04, 0, 0, 0, 0, 0, 0, 0xCB, 0x40, 0x04)), ("cut format", Raw(0x93, 1, 2)), ("too many", Raw(0xDC, 0x10, 0, 1, 2)),
      ("timestamps", Raw(0x93, 0xD6, 0xFF, 0x60, 0, 0, 0, 0xD7, 0xFF, 0x77, 0x35, 0x94, 0, 0x60, 0, 0, 0, 0xC7, 12, 0xFF, 0, 0, 0, 100, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE)), ("timestamp max nanos", Raw(0x91, 0xD7, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF)), ("cut timestamp32", Raw(0x91, 0xD6, 0xFF, 0x60, 0)), ("cut timestamp64", Raw(0x91, 0xD7, 0xFF, 0x60, 0, 0, 0)), ("cut timestamp header", Raw(0x91, 0xD7)), ("timestamp96 far", Raw(0x91, 0xC7, 12, 0xFF, 0, 0, 0, 0, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF)), ("ext8 timestamp 4", Raw(0x91, 0xC7, 4, 0xFF, 0x60, 0, 0, 0)),
      ("bools", Raw(0x94, 0xC3, 0xC2, 0xC0, 0xC3)), ("bools then int", Raw(0x93, 0xC3, 1, 0xC2)), ("bool text", Raw(0x92, 0xC3, "true")), ("small ints", Raw(0x96, 0x7F, 0xCC, 0x80, 0xD0, 0x80, 0xD1, 0x7F, 0xFF, 0xCD, 0xFF, 0xFF, 0xE0)), ("int8 overflow", Raw(0x92, 1, 0xCC, 200)), ("int16 overflow", Raw(0x92, 1, 0xCD, 0x80, 0)), ("negative unsigned", Raw(0x92, 1, 0xD0, 0xFF)), ("uint64 in int64", Raw(0x92, 0xD3, 0, 0, 0, 0, 0, 0, 0, 5, 0xCF, 0x80, 0, 0, 0, 0, 0, 0, 0)),
      ("guids", Raw(0x93, 0xC4, 16, new byte[] { 0x5B, 0xAD, 0x8F, 0x0F, 0xCB, 0xD9, 0x9F, 0x46, 0xA1, 0x65, 0x70, 0x86, 0x77, 0x28, 0x95, 0x0E }, 0xC0, 0xC4, 16, new byte[16])), ("guid text", Raw(0x92, 0xC4, 16, new byte[16], "0f8fad5b-d9cb-469f-a165-70867728950e")), ("bin 15", Raw(0x92, 0xC4, 16, new byte[16], 0xC4, 15, new byte[15])), ("cut guid", Raw(0x92, 0xC4, 16, new byte[16], 0xC4, 16, new byte[9])), ("cut bin header", Raw(0x92, 0xC4, 16, new byte[16], 0xC4)),
      ("decimals", Raw(0x93, 0xD8, 1, new byte[16], 0xC0, 0xD8, 1, new byte[] { 0, 0, 2, 0, 0, 0, 0, 0, 15, 0, 0, 0, 0, 0, 0, 0 })), ("decimal other code", Raw(0x92, 0xD8, 1, new byte[16], 0xD8, 2, new byte[16])), ("decimal text", Raw(0x92, 0xD8, 1, new byte[16], "1.5")), ("decimal int", Raw(0x92, 0xD8, 1, new byte[16], 7)), ("cut decimal", Raw(0x92, 0xD8, 1, new byte[16], 0xD8, 1, new byte[5])), ("decimal ext8", Raw(0x91, 0xC7, 16, 1, new byte[16])),
    };
    foreach (var (name, body) in bodies) {
      byte[] data = new byte[prefix.Length + body.Length]; prefix.CopyTo(data, 0); body.CopyTo(data, prefix.Length);
      foreach (Type t in new[] { typeof(Dictionary<string, int>), typeof(Dictionary<int, int>), typeof(Dictionary<string, object>), typeof(Dictionary<object, object>), typeof(Dictionary<string, int?>), typeof(HashSet<int>), typeof(HashSet<string>), typeof(IList<int>), typeof(IEnumerable<int>), typeof(ISet<int>), typeof(IDictionary<string, int>), typeof(IReadOnlyDictionary<string, int>), typeof(IEnumerable<KeyValuePair<string, int>>),
        typeof(int[]), typeof(long[]), typeof(float[]), typeof(double[]), typeof(List<int>), typeof(List<long>), typeof(List<float>), typeof(List<double>), typeof(DateTime[]), typeof(short[]), typeof(ulong[]), typeof(EqNumbers),
        typeof(sbyte[]), typeof(ushort[]), typeof(uint[]), typeof(bool[]), typeof(Guid[]), typeof(List<short>), typeof(List<ulong>), typeof(List<bool>), typeof(List<Guid>), typeof(IList<bool>), typeof(EqSmall), typeof(HashSet<short>), typeof(HashSet<Guid>), typeof(HashSet<long>), typeof(decimal[]), typeof(List<decimal>), typeof(HashSet<decimal>), typeof(EqDecimals) }) {
        sb.AppendLine($"odd {name} {t.Name} {string.Join(",", t.GenericTypeArguments.Select(x => x.Name))}: " + Run(() => Dump(s.Deserialize(t, data))));
        if (t == typeof(Dictionary<string, int>) || t == typeof(IList<int>)) sb.AppendLine("  report: " + Run(() => { object r = s.Deserialize(t, data, out LsMsgPack.ReadDifferences d); return Dump(r) + " | " + (d?.GenerateReport() ?? "none").Replace("\r", "").Replace("\n", " "); }));
      }
    }
  }
  static string Run(Func<string> f) { try { return f(); } catch (Exception ex) { return "EX " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]; } }
  static void One(StringBuilder sb, LtMsgPackSerializer s, object v, Type t) {
    string ser = Run(() => Hex(s.Serialize(v, t)));
    sb.AppendLine($"  {t.Name}: {H(ser)}/{ser.Length}" + (ser.Length < 400 ? " " + ser : ""));
    string back = Run(() => Dump(s.Deserialize(t, s.Serialize(v, t))));
    sb.AppendLine($"     back={H(back)}/{back.Length}" + (back.Length < 300 ? " " + back : ""));
    if (t != typeof(object)) {
      string asObj = Run(() => Dump(s.Deserialize(typeof(object), s.Serialize(v, t))));
      sb.AppendLine($"     obj={H(asObj)}/{asObj.Length}");
    }
  }

  /// <summary>LT_COMPILE_AFTER=n sets LtMsgPackOptions.Default_CompileWritersAfter (compiled writers after n objects per type, 0 at once, -1 never); ignored by a base without the option.</summary>
  private static void CompileAfterFromEnvironment()
  {
    string after = System.Environment.GetEnvironmentVariable("LT_COMPILE_AFTER");
    if (after != null)
      {
      typeof(LtMsgPack.LtMsgPackOptions).GetField("Default_CompileWritersAfter")?.SetValue(null, int.Parse(after));
      typeof(LtMsgPack.LtMsgPackOptions).GetField("Default_CompileReadersAfter")?.SetValue(null, int.Parse(after)); // the compiled readers too
    }
  }
  public static void Main() {
    CompileAfterFromEnvironment();
    var sb = new StringBuilder();
    List<object> values = Values();
    var configs = new List<(string, Func<LtMsgPackOptions>)>();
    foreach (EndianAction endian in Enum.GetValues(typeof(EndianAction)))
    foreach (bool compact in new[] { true, false })
    foreach (ObjectLayout layout in new[] { ObjectLayout.Map, ObjectLayout.Array })
    foreach (string ids in new[] { "names", "inline", "reference" })
    foreach (bool trim in layout == ObjectLayout.Array ? new[] { false, true } : new[] { false }) {
      EndianAction e = endian; bool c = compact; ObjectLayout l = layout; string i = ids; bool tr = trim;
      configs.Add(($"{e} compact={c} {l} {i} trim={tr}", () => new LtMsgPackOptions { EndianAction = e, DynamicallyCompact = c, ObjectLayout = l, TrimTrailingNulls = tr,
        UseInexedSchema = i != "names", SchemaStore = i == "reference" ? new SchemaStore() : null, WriteSchemaReference = i == "reference" }));
    }
    configs.Add(("mpcsharp", () => LtMsgPackPresets.MessagePackCSharp()));
    configs.Add(("nerdbank", () => LtMsgPackPresets.Nerdbank()));
    configs.Add(("generic", () => LtMsgPackPresets.Generic()));
    configs.Add(("declaration-noschema", () => new LtMsgPackOptions { UseInexedSchema = false, PropertyOrder = PropertyOrder.Declaration }));
    configs.Add(("always-typeid", () => new LtMsgPackOptions { AddTypeIdOptions = AddTypeIdOption.Always | AddTypeIdOption.FullName }));
    configs.Add(("attr-resolver", () => new LtMsgPackOptions { ObjectLayout = ObjectLayout.Map, PropertyNameResolvers = new LsMsgPack.TypeResolving.Interfaces.IMsgPackPropertyIdResolver[] { new LsMsgPack.TypeResolving.Names.AttributePropertyNameResolver() } }));
    configs.Add(("attr-resolver-names", () => new LtMsgPackOptions { ObjectLayout = ObjectLayout.Map, UseInexedSchema = false, PropertyNameResolvers = new LsMsgPack.TypeResolving.Interfaces.IMsgPackPropertyIdResolver[] { new LsMsgPack.TypeResolving.Names.AttributePropertyNameResolver() } }));
    foreach (var (name, make) in configs) {
      LtMsgPackSerializer s = new LtMsgPackSerializer(make());
      sb.AppendLine("### " + name);
      int n = 0;
      foreach (object v in values) {
        n++;
        sb.AppendLine($"{n} {v?.GetType().Name}");
        One(sb, s, v, v?.GetType() ?? typeof(object));
        if (v != null && !(v is Array a && a.Length > 1000) && !(v is System.Collections.IDictionary d && d.Count > 1000)) One(sb, s, v, typeof(object));
      }
      string typed = Run(() => Hex(s.Serialize(Bench.Invoices[0])) + "|" + Hex(s.Serialize(Bench.Invoices.ToList())) + "|" + Hex(s.Serialize(new Poly { Any = 5 })));
      sb.AppendLine($"typed {H(typed)}/{typed.Length}");
      string typedBack = Run(() => Dump(s.Deserialize<List<Bench.Invoice>>(s.Serialize(Bench.Invoices.ToList()))) + Dump(s.Deserialize<Bench.Invoice[]>(s.Serialize(Bench.Invoices))));
      sb.AppendLine($"typedBack {H(typedBack)}/{typedBack.Length}");
      string stream = Run(() => { var ms = new System.IO.MemoryStream(); s.Serialize(Bench.Invoices[1], ms); s.Serialize(Bench.Invoices[2], ms); ms.Position = 0; var x = Dump(s.Deserialize<Bench.Invoice>(ms)) + Dump(s.Deserialize<Bench.Invoice>(ms)); return Hex(ms.ToArray()) + x; });
      sb.AppendLine($"stream {H(stream)}/{stream.Length}");
    }
    foreach (var (name, make, prefix) in new (string, Func<LtMsgPackOptions>, byte[])[] {
      ("names", () => new LtMsgPackOptions { UseInexedSchema = false }, new byte[0]),
      ("names map", () => new LtMsgPackOptions { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map }, new byte[0]),
      ("schema", () => new LtMsgPackOptions(), new byte[] { 0x80 }),
      ("depth 1", () => new LtMsgPackOptions { UseInexedSchema = false, MaxDepth = 1 }, new byte[0]),
      ("guard", () => new LtMsgPackOptions { UseInexedSchema = false, TypeGuard = new EqNoGuard() }, new byte[0]),
      ("collect", () => new LtMsgPackOptions { UseInexedSchema = false, ReadErrors = ReadErrorHandling.FailDeferred }, new byte[0]),
    }) {
      sb.AppendLine("### odd data " + name);
      Odd(sb, new LtMsgPackSerializer(make()), prefix);
    }
    foreach (string mt in new[] { "application/x-lsmsgpack", "application/msgpack" })
    foreach (bool negotiate in new[] { true, false }) {
      var h = new LtMsgPackHttpSerializer(new LtMsgPackHttpOptions { NegotiateSchemas = negotiate });
      foreach (object v in new object[] { Bench.Invoices[0], Bench.Invoices[1], null, 5, new Nested { Name = "h" } }) {
        Type t = v?.GetType() ?? typeof(Bench.Invoice);
        string first = Run(() => { var p = h.Serialize(v, t, mt, null); return Hex(p.ToArray()) + " id=" + p.SchemaId + " ref=" + p.IsReference; });
        string second = Run(() => { var p = h.Serialize(v, t, mt, h.Serialize(v, t, mt, null).SchemaId); byte[] all = p.ToArray(); return Hex(all) + " id=" + p.SchemaId + " ref=" + p.IsReference + " back=" + Dump(h.Deserialize(t, all, 0, all.Length, mt)); });
        sb.AppendLine($"http {mt} {negotiate} {t.Name}: {H(first)}/{first.Length} {H(second)}/{second.Length}");
      }
    }
    Console.Write(sb.ToString());
  }
}
