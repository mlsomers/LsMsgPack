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
    values.Add(new string('é', 16)); values.Add(new string('é', 30)); values.Add(new string('é', 200)); values.Add(new string('✓', 85)); values.Add(new string('✓', 40000)); values.Add(new string('é', 70000)); values.Add("\ud800x"); values.Add(new string('a', 65537));
    values.Add(new Nested { Name = "", Objs = new List<object> { "", null } }); // empty strings are written (FilterDefaultValues), null is left out
    values.Add(new WithDefault { Sound = null, Zero = 0, Last = null }); values.Add(new WithDefault { Sound = "Woof", Zero = 7, Last = "l" });
    values.Add(new Bench.Invoice[] { }); values.AddRange(Bench.Invoices.Take(3));
    return values;
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
  public static void Main() {
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
