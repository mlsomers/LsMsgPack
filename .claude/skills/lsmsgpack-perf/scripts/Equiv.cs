// Byte and round trip equivalence check: run the same program against two builds of LsMsgPack and diff the output.
// Covers integer sizes and boundaries, strings, bin, floats, decimals, DateTimes, enums, collections, dictionaries,
// nested objects and a custom property id resolver, for all EndianAction, DynamicallyCompact and UseInexedSchema settings.
// Per value: serialized bytes (as item, via the serializer and as root), the typed round trip and the raw MsgPackItem tree.
// Large payloads are compared by hash (SHA-256 prefix, deterministic), small ones are printed.
// A warm-up first uses every property more than PropertyAccessor.CompileAfterCalls times, so the bound delegates are compared as well.
using LsMsgPack;
using System;
using System.Collections.Generic;
using System.Text;
public enum E8 : byte { A = 1, B = 200 } public enum E16 : short { A = -300, B = 3 } public enum E64 : long { A = long.MinValue, B = 5 }
public class Nested { public string Name { get; set; } public double D { get; set; } public float F { get; set; } public int? N { get; set; } public byte[] Blob { get; set; } public Guid G { get; set; } public DateTime When { get; set; } public DateTimeOffset Off { get; set; } public decimal M { get; set; } public E16 E { get; set; } public bool B { get; set; } public List<object> Objs { get; set; } public Dictionary<string, long> Map { get; set; } public ulong U { get; set; } public sbyte S { get; set; } }
public class Bcl { public char C { get; set; } public TimeSpan T { get; set; } public TimeSpan? N { get; set; } public DateOnly D { get; set; } public TimeOnly O { get; set; } public Uri U { get; set; } public List<object> Boxed { get; set; } }
public class WithGuid { public Guid G { get; set; } public byte[] B { get; set; } public object O { get; set; } }
public class Attributed { [System.Xml.Serialization.XmlElement("renamed")] public string A { get; set; } [System.Xml.Serialization.XmlIgnore] public string Ignored { get; set; } public int ReadOnly { get { return 5; } } public int B { get; set; } public Attributed Child { get; set; } public List<Attributed> Kids { get; set; } }
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
  public static void Main() {
    var sb = new StringBuilder();
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
    // Warm-up with the small values only (the property accessors switch to bound delegates after 100 calls)
    for (int warm = 0; warm < 10; warm++)
      foreach (EndianAction endian in Enum.GetValues(typeof(EndianAction)))
        foreach (bool compact in new[] { true, false })
          foreach (bool schema in new[] { false, true })
          {
            var s = new MsgPackSettings { EndianAction = endian, DynamicallyCompact = compact, UseInexedSchema = schema };
            foreach (object v in values)
            {
              if (v is Array a && a.Length > 1000 || v is System.Collections.IDictionary d && d.Count > 1000 || v is string str && str.Length > 1000)
                continue;
              try { MsgPackSerializer.Deserialize(v?.GetType() ?? typeof(object), MsgPackSerializer.Serialize(v, v?.GetType() ?? typeof(object), s), s); } catch (Exception) { }
            }
          }

    foreach (EndianAction endian in Enum.GetValues(typeof(EndianAction)))
    foreach (bool compact in new[] { true, false })
    foreach (bool schema in new[] { false, true })
    {
      var s = new MsgPackSettings { EndianAction = endian, DynamicallyCompact = compact, UseInexedSchema = schema };
      sb.AppendLine($"### {endian} compact={compact} schema={schema}");
      int n = 0;
      foreach (object v in values) {
        n++;
        string item, ser;
        try { MsgPackItem it = MsgPackItem.Pack(v, s); item = it is null ? "null" : Hex(it.ToBytes()); } catch (Exception ex) { item = "EX " + ex.GetType().Name; }
        try { ser = Hex(MsgPackSerializer.Serialize(v, v?.GetType() ?? typeof(object), s)); } catch (Exception ex) { ser = "EX " + ex.GetType().Name; }
        string root;
        try { root = Hex(MsgPackItem.PackMultiple(s, v, v).ToBytes()); } catch (Exception ex) { root = "EX " + ex.GetType().Name; }
        sb.AppendLine($"{n} {v?.GetType().Name}: item={H(item)}/{item.Length} ser={H(ser)}/{ser.Length} root={H(root)}/{root.Length}");
        string back, raw;
        try { back = Dump(MsgPackSerializer.Deserialize(v?.GetType() ?? typeof(object), MsgPackSerializer.Serialize(v, v?.GetType() ?? typeof(object), s), s)); } catch (Exception ex) { back = "EX " + ex.GetType().Name; }
        try { raw = Dump(MsgPackItem.UnpackMultiple(MsgPackSerializer.Serialize(v, v?.GetType() ?? typeof(object), s), s)); } catch (Exception ex) { raw = "EX " + ex.GetType().Name; }
        sb.AppendLine($"   back={H(back)}/{back.Length} raw={H(raw)}/{raw.Length}");
        if (back.Length < 300) sb.AppendLine("   " + back);
        if (item.Length < 200) sb.AppendLine("   " + item);
        if (ser.Length < 400) sb.AppendLine("   " + ser);
      }
    }
    foreach (bool schema in new[] { false, true }) {
      var s2 = new MsgPackSettings { UseInexedSchema = schema, PropertyNameResolvers = new LsMsgPack.TypeResolving.Interfaces.IMsgPackPropertyIdResolver[] { new LsMsgPack.TypeResolving.Names.AttributePropertyNameResolver() } };
      var a = new Attributed { A = "a", Ignored = "x", B = 3, Child = new Attributed { A = "c", B = 4 }, Kids = new List<Attributed> { new Attributed { B = 9 }, new Attributed { A = "k" } } };
      byte[] ab = MsgPackSerializer.Serialize(a, s2);
      sb.AppendLine($"### attributes schema={schema}: {Hex(ab)}");
      sb.AppendLine("   " + Dump(MsgPackSerializer.Deserialize<Attributed>(ab, s2)));
    }
    // An extension that is not registered (here a big-endian Guid as Nerdbank.MessagePack writes it, ext type 2), read into Guid, byte[] and object members
    byte[] guidBigEndian = { 0x0f, 0x8f, 0xad, 0x5b, 0xd9, 0xcb, 0x46, 0x9f, 0xa1, 0x65, 0x70, 0x86, 0x77, 0x28, 0x95, 0x0e };
    foreach (string member in new[] { "G", "B", "O" }) {
      var ext = new MpExt { TypeSpecifier = 2, Value = guidBigEndian };
      byte[] extMap = new MpMap(new[] { new KeyValuePair<object, object>(member, ext) }, new MsgPackSettings()).ToBytes();
      string res;
      try { res = Dump(MsgPackSerializer.Deserialize<WithGuid>(extMap, new MsgPackSettings { UseInexedSchema = false })); } catch (Exception ex) { res = "EX " + ex.GetType().Name; }
      sb.AppendLine($"### unknown extension in {member}: {Hex(extMap)}");
      sb.AppendLine("   " + res);
    }
    Console.Write(sb.ToString());
  }
}
