// Other shapes of data than the invoices: dictionaries, interface typed collections, polymorphic lists, object properties, primitive collections, large values, struct roots.
// Usage: dotnet shapes-new.dll [seconds per pass=0.5] [name filter] [passes=5]
// Prints write and read in ns per call (fastest pass), allocated bytes per call and the payload size, System.Text.Json next to it where it can do the same.
using LtMsgPack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

public class DictStrStr { public Dictionary<string, string> Values { get; set; } }
public class DictStrInt { public Dictionary<string, int> Values { get; set; } }
public class DictStrItem { public Dictionary<string, Item> Values { get; set; } }
public class DictIntItem { public Dictionary<int, Item> Values { get; set; } }
public class IListItems { public IList<Item> Values { get; set; } }
public class ICollectionItems { public ICollection<Item> Values { get; set; } }
public class IEnumerableItems { public IEnumerable<Item> Values { get; set; } }
public class IReadOnlyListItems { public IReadOnlyList<Item> Values { get; set; } }
public class ListItems { public List<Item> Values { get; set; } }
public class HashSetInts { public HashSet<int> Values { get; set; } }
public class ListInts { public List<int> Values { get; set; } }
public class IntArray { public int[] Values { get; set; } }
public class DoubleArray { public double[] Values { get; set; } }
public class LongArray { public long[] Values { get; set; } }
public class FloatArray { public float[] Values { get; set; } }
public class ListDoubles { public List<double> Values { get; set; } }
public class DateTimeArray { public DateTime[] Values { get; set; } }
public class GuidArray { public Guid[] Values { get; set; } }
public class DecimalArray { public decimal[] Values { get; set; } }
public class StringArray { public string[] Values { get; set; } }
public class Blob { public byte[] Value { get; set; } }
public class LongText { public string Value { get; set; } }
public class ObjectProps { public object A { get; set; } public object B { get; set; } public object C { get; set; } public object D { get; set; } }
public class Node { public int Id { get; set; } public string Name { get; set; } public List<Node> Children { get; set; } }

public class Item
{
  public int Id { get; set; }
  public string Name { get; set; }
  public double Price { get; set; }
}

public abstract class Shape { public int Id { get; set; } public string Color { get; set; } }
public class Circle : Shape { public double Radius { get; set; } }
public class Square : Shape { public double Side { get; set; } }
public class Triangle : Shape { public double A { get; set; } public double B { get; set; } public double C { get; set; } }
public class Drawing { public string Title { get; set; } public List<Shape> Shapes { get; set; } }

public struct Point3 { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } }

public static class LtShapes
{
  private static List<Item> Items(int count)
  {
    List<Item> items = new List<Item>(count);
    for (int t = 0; t < count; t++)
      items.Add(new Item { Id = t + 1, Name = "Item " + t, Price = t * 1.25 + 0.5 });
    return items;
  }

  private static Node Tree(int depth, int width, ref int id)
  {
    Node node = new Node { Id = ++id, Name = "Node " + id };
    if (depth > 0)
    {
      node.Children = new List<Node>(width);
      for (int t = 0; t < width; t++)
        node.Children.Add(Tree(depth - 1, width, ref id));
    }
    return node;
  }

  private sealed class Case
  {
    public string Name;
    public Func<LtMsgPackSerializer, byte[]> Write;
    public Func<LtMsgPackSerializer, byte[], object> Read;
    public Func<byte[]> WriteJson;
    public Func<byte[], object> ReadJson;
  }

  private static Case Make<T>(string name, T value, bool json = true)
  {
    Case c = new Case
    {
      Name = name,
      Write = s => s.Serialize<T>(value),
      Read = (s, data) => s.Deserialize<T>(data),
    };
    if (json)
    {
      c.WriteJson = () => JsonSerializer.SerializeToUtf8Bytes<T>(value);
      c.ReadJson = data => JsonSerializer.Deserialize<T>(data);
    }
    return c;
  }

  private static List<Case> Cases()
  {
    List<Item> items = Items(20);
    Dictionary<string, string> strings = new Dictionary<string, string>();
    Dictionary<string, int> ints = new Dictionary<string, int>();
    Dictionary<string, Item> byName = new Dictionary<string, Item>();
    Dictionary<int, Item> byId = new Dictionary<int, Item>();
    for (int t = 0; t < 20; t++)
    {
      strings["key" + t] = "value number " + t;
      ints["key" + t] = t * 37;
      byName["key" + t] = items[t];
      byId[t * 3] = items[t];
    }
    List<Shape> shapes = new List<Shape>();
    for (int t = 0; t < 30; t++)
    {
      switch (t % 3)
      {
        case 0: shapes.Add(new Circle { Id = t, Color = "red", Radius = t + 0.5 }); break;
        case 1: shapes.Add(new Square { Id = t, Color = "green", Side = t + 0.25 }); break;
        default: shapes.Add(new Triangle { Id = t, Color = "blue", A = t + 1.5, B = t + 2.5, C = t + 3.5 }); break;
      }
    }
    int[] numbers = Enumerable.Range(0, 1000).Select(x => x * 13).ToArray();
    double[] doubles = numbers.Select(x => x * 0.37).ToArray();
    string[] texts = Enumerable.Range(0, 100).Select(x => "text number " + x).ToArray();
    byte[] blob = new byte[64 * 1024];
    new Random(1).NextBytes(blob);
    string ascii = new string('x', 10000);
    string unicode = string.Concat(Enumerable.Repeat("héllo wörld ✓ ", 700));
    int id = 0;
    Node tree = Tree(4, 3, ref id);
    return new List<Case>
    {
      Make("list-of-objects", new ListItems { Values = items }),
      Make("ilist-of-objects", new IListItems { Values = items }),
      Make("icollection-of-objects", new ICollectionItems { Values = items }),
      Make("ienumerable-of-objects", new IEnumerableItems { Values = items }),
      Make("ireadonlylist-of-objects", new IReadOnlyListItems { Values = items }),
      Make("dict-str-str", new DictStrStr { Values = strings }),
      Make("dict-str-int", new DictStrInt { Values = ints }),
      Make("dict-str-obj", new DictStrItem { Values = byName }),
      Make("dict-int-obj", new DictIntItem { Values = byId }),
      Make("hashset-int", new HashSetInts { Values = new HashSet<int>(numbers) }),
      Make("list-int", new ListInts { Values = numbers.ToList() }),
      Make("int-array", new IntArray { Values = numbers }),
      Make("double-array", new DoubleArray { Values = doubles }),
      Make("long-array", new LongArray { Values = numbers.Select(x => x * 1000003L - 500000).ToArray() }),
      Make("float-array", new FloatArray { Values = numbers.Select(x => x * 0.37f).ToArray() }),
      Make("list-double", new ListDoubles { Values = doubles.ToList() }),
      Make("datetime-array", new DateTimeArray { Values = numbers.Select(x => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(x * 7).AddMilliseconds(x % 3 == 0 ? x : 0)).ToArray() }),
      Make("guid-array", new GuidArray { Values = numbers.Select(x => new Guid(x, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)).ToArray() }),
      Make("decimal-array", new DecimalArray { Values = numbers.Select(x => x * 1.25m).ToArray() }),
      Make("string-array", new StringArray { Values = texts }),
      Make("bytes-64k", new Blob { Value = blob }),
      Make("ascii-10k", new LongText { Value = ascii }),
      Make("unicode-10k", new LongText { Value = unicode }),
      Make("polymorphic-list", new Drawing { Title = "drawing", Shapes = shapes }, false),
      Make("object-props", new ObjectProps { A = 12, B = "text", C = 1.5, D = new Item { Id = 1, Name = "x", Price = 2 } }, false),
      Make("tree-depth-4", tree),
      Make("struct-root", new Point3 { X = 1.5f, Y = 2.5f, Z = 3.5f }),
      Make("int-root", 123456),
    };
  }

  private static double Pass(Func<int> action, double seconds)
  {
    int calls = 0;
    Stopwatch watch = Stopwatch.StartNew();
    while (watch.Elapsed.TotalSeconds < seconds)
    {
      for (int t = 0; t < 16; t++)
        calls += action();
    }
    return watch.Elapsed.TotalMilliseconds * 1e6 / calls;
  }

  private static double Allocated(Func<int> action)
  {
    action();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int t = 0; t < 64; t++)
      action();
    return (GC.GetAllocatedBytesForCurrentThread() - before) / 64.0;
  }

  private static (double ns, double bytes) Measure(Func<int> action, double seconds, int passes)
  {
    Pass(action, Math.Max(seconds * 2, 1)); // past tiering
    double best = double.MaxValue;
    for (int t = 0; t < passes; t++)
      best = Math.Min(best, Pass(action, seconds));
    return (best, Allocated(action));
  }

  private static string Text(double ns)
  {
    return ns >= 1e6 ? (ns / 1e6).ToString("0.00", CultureInfo.InvariantCulture) + " ms"
      : ns >= 1e4 ? (ns / 1e3).ToString("0.0", CultureInfo.InvariantCulture) + " us"
      : ns.ToString("0", CultureInfo.InvariantCulture) + " ns";
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
  public static void Main(string[] args)
  {
    CompileAfterFromEnvironment();
    double seconds = args.Length > 0 ? double.Parse(args[0], CultureInfo.InvariantCulture) : 0.5;
    string filter = args.Length > 1 ? args[1] : "";
    int passes = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 5;

    LtMsgPackSerializer defaults = new LtMsgPackSerializer();
    Console.WriteLine($"{"shape",-26} {"size",8} {"write",10} {"read",10} {"w alloc",9} {"r alloc",9} | {"json size",9} {"write",10} {"read",10} {"w alloc",9} {"r alloc",9}");
    foreach (Case c in Cases())
    {
      if (filter.Length > 0 && !c.Name.Contains(filter, StringComparison.Ordinal))
        continue;
      byte[] data = c.Write(defaults);
      (double wn, double wb) = Measure(() => { c.Write(defaults); return 1; }, seconds, passes);
      (double rn, double rb) = Measure(() => { c.Read(defaults, data); return 1; }, seconds, passes);
      string json = "";
      if (c.WriteJson != null)
      {
        byte[] text = c.WriteJson();
        (double jwn, double jwb) = Measure(() => { c.WriteJson(); return 1; }, seconds, passes);
        (double jrn, double jrb) = Measure(() => { c.ReadJson(text); return 1; }, seconds, passes);
        json = $"{text.Length,9} {Text(jwn),10} {Text(jrn),10} {jwb,9:0} {jrb,9:0}";
      }
      Console.WriteLine($"{c.Name,-26} {data.Length,8} {Text(wn),10} {Text(rn),10} {wb,9:0} {rb,9:0} | {json}");
    }
  }
}
