using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Framework types without settable properties (they used to be serialized as an empty map and read as their default value).
  /// <para>They are written with the same encodings as MessagePack-CSharp: char as an integer, TimeSpan and TimeOnly as ticks, DateOnly as its day number and Uri as its original string.</para>
  /// </summary>
  public abstract class SerializingFrameworkTypes
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    public SerializingFrameworkTypes()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(FrameworkTypesHolder));
    }

    public class FrameworkTypesHolder
    {
      public char Letter { get; set; }
      public char? MaybeLetter { get; set; }
      public TimeSpan Duration { get; set; }
      public TimeSpan? MaybeDuration { get; set; }
      public DateOnly Day { get; set; }
      public TimeOnly Time { get; set; }
      public Uri Link { get; set; }
      public Uri RelativeLink { get; set; }
      public List<TimeSpan> Durations { get; set; }
      public Dictionary<DateOnly, Uri> Links { get; set; }
    }

    private static FrameworkTypesHolder CreateHolder()
    {
      return new FrameworkTypesHolder()
      {
        Letter = 'é',
        MaybeLetter = 'A',
        Duration = TimeSpan.FromMinutes(90),
        MaybeDuration = TimeSpan.FromTicks(-12345),
        Day = new DateOnly(2026, 9, 29),
        Time = new TimeOnly(13, 45, 10, 5),
        Link = new Uri("https://example.com/path?q=1"),
        RelativeLink = new Uri("../relative/path", UriKind.Relative),
        Durations = new List<TimeSpan>() { TimeSpan.MaxValue, TimeSpan.MinValue, TimeSpan.FromDays(1) },
        Links = new Dictionary<DateOnly, Uri>() { { DateOnly.MinValue, new Uri("https://example.com/min") }, { DateOnly.MaxValue, new Uri("urn:isbn:0451450523") } }
      };
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PropertiesRoundTrip(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      FrameworkTypesHolder holder = CreateHolder();

      FrameworkTypesHolder ret = Serializer.Deserialize<FrameworkTypesHolder>(Serializer.Serialize(holder, settings), settings);

      Assert.AreEqual(holder.Letter, ret.Letter);
      Assert.AreEqual(holder.MaybeLetter, ret.MaybeLetter);
      Assert.AreEqual(holder.Duration, ret.Duration);
      Assert.AreEqual(holder.MaybeDuration, ret.MaybeDuration);
      Assert.AreEqual(holder.Day, ret.Day);
      Assert.AreEqual(holder.Time, ret.Time);
      Assert.AreEqual(holder.Link, ret.Link);
      Assert.AreEqual(holder.Link.OriginalString, ret.Link.OriginalString);
      Assert.AreEqual(holder.RelativeLink.OriginalString, ret.RelativeLink.OriginalString);
      Assert.IsFalse(ret.RelativeLink.IsAbsoluteUri);
      CollectionAssert.AreEqual(holder.Durations, ret.Durations);
      CollectionAssert.AreEqual(holder.Links, ret.Links);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DefaultValuesAreOmitted(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      FrameworkTypesHolder empty = new FrameworkTypesHolder();

      FrameworkTypesHolder ret = Serializer.Deserialize<FrameworkTypesHolder>(Serializer.Serialize(empty, settings), settings);

      Assert.AreEqual(default(char), ret.Letter);
      Assert.IsNull(ret.MaybeLetter);
      Assert.AreEqual(TimeSpan.Zero, ret.Duration);
      Assert.IsNull(ret.MaybeDuration);
      Assert.AreEqual(default(DateOnly), ret.Day);
      Assert.IsNull(ret.Link);
    }

    /// <summary>
    /// The same bytes as MessagePack-CSharp writes (checked in LsMsgPackInteropTests).
    /// </summary>
    [TestMethod]
    public void Encodings()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false };

      CollectionAssert.AreEqual(new byte[] { 0x41 }, Serializer.Serialize('A', settings));
      CollectionAssert.AreEqual(new byte[] { 0xCD, 0x20, 0xAC }, Serializer.Serialize('€', settings));
      CollectionAssert.AreEqual(new byte[] { 0xCF, 0x00, 0x00, 0x00, 0x0C, 0x92, 0xA6, 0x9C, 0x00 }, Serializer.Serialize(TimeSpan.FromMinutes(90), settings));
      CollectionAssert.AreEqual(new byte[] { 0xD1, 0xCF, 0xC7 }, Serializer.Serialize(TimeSpan.FromTicks(-12345), settings));
      CollectionAssert.AreEqual(new byte[] { 0xCE, 0x00, 0x0B, 0x4A, 0x2F }, Serializer.Serialize(new DateOnly(2026, 9, 29), settings));
      CollectionAssert.AreEqual(new byte[] { 0xCF, 0x00, 0x00, 0x00, 0x73, 0x46, 0x43, 0x3A, 0x50 }, Serializer.Serialize(new TimeOnly(13, 45, 10, 5), settings));
      CollectionAssert.AreEqual(new byte[] { 0xA5, (byte)'a', (byte)'/', (byte)'b', (byte)'?', (byte)'c' }, Serializer.Serialize(new Uri("a/b?c", UriKind.Relative), settings));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RootValues(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };

      Assert.AreEqual('x', Serializer.Deserialize<char>(Serializer.Serialize('x', settings), settings));
      Assert.AreEqual(TimeSpan.FromSeconds(-1.5), Serializer.Deserialize<TimeSpan>(Serializer.Serialize(TimeSpan.FromSeconds(-1.5), settings), settings));
      Assert.AreEqual(new DateOnly(1999, 12, 31), Serializer.Deserialize<DateOnly>(Serializer.Serialize(new DateOnly(1999, 12, 31), settings), settings));
      Assert.AreEqual(TimeOnly.MaxValue, Serializer.Deserialize<TimeOnly>(Serializer.Serialize(TimeOnly.MaxValue, settings), settings));
      Assert.AreEqual(new Uri("https://example.com/"), Serializer.Deserialize<Uri>(Serializer.Serialize(new Uri("https://example.com/"), settings), settings));
    }

    public class FrameworkTypesBoxed
    {
      public object Duration { get; set; }
      public List<object> Values { get; set; }
    }

    /// <summary>
    /// Assigned to object they get a type id (TimeSpan and DateOnly are found by their short name).
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void BoxedValues(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      FrameworkTypesBoxed boxed = new FrameworkTypesBoxed()
      {
        Duration = TimeSpan.FromHours(1),
        Values = new List<object>() { TimeSpan.FromMinutes(1), new DateOnly(2000, 1, 1), new TimeOnly(12, 0) }
      };

      FrameworkTypesBoxed ret = Serializer.Deserialize<FrameworkTypesBoxed>(Serializer.Serialize(boxed, settings), settings);

      Assert.AreEqual(boxed.Duration, ret.Duration);
      CollectionAssert.AreEqual(boxed.Values, ret.Values);
    }

    public class FrameworkValuesHolder
    {
      public Half Small { get; set; }
      public Half? MaybeSmall { get; set; }
      public Version Release { get; set; }
      public StringBuilder Text { get; set; }
      public CultureInfo Culture { get; set; }
      public Rune Letter { get; set; }
      public nint Pointer { get; set; }
      public nuint UnsignedPointer { get; set; }
      public Memory<byte> Memory { get; set; }
      public ReadOnlyMemory<byte> ReadOnly { get; set; }
      public ArraySegment<byte> Segment { get; set; }
      public Complex Number { get; set; }
      public Tuple<int, string> Pair { get; set; }
      public (int Id, string Name) Named { get; set; }
      public (int, string, int, string, int, string, int, string, int) Long { get; set; }
      public List<(string, double)> Points { get; set; }
      public (int, object) WithObject { get; set; }
    }

    private static FrameworkValuesHolder CreateValues()
    {
      return new FrameworkValuesHolder()
      {
        Small = (Half)1.5,
        MaybeSmall = Half.MinValue,
        Release = new Version(1, 2, 3, 4),
        Text = new StringBuilder("built"),
        Culture = CultureInfo.GetCultureInfo("nl-NL"),
        Letter = new Rune(0x1F600),
        Pointer = -42,
        UnsignedPointer = 42,
        Memory = new byte[] { 1, 2, 3 },
        ReadOnly = new byte[] { 4, 5 },
        Segment = new ArraySegment<byte>(new byte[] { 0, 6, 7, 8, 0 }, 1, 3),
        Number = new Complex(1.5, -2),
        Pair = Tuple.Create(1, "one"),
        Named = (2, "two"),
        Long = (1, "a", 2, "b", 3, "c", 4, "d", 5),
        Points = new List<(string, double)>() { ("x", 1.5), ("y", -2) },
        WithObject = (3, new Version(3, 0))
      };
    }

    private static void AssertValues(FrameworkValuesHolder expected, FrameworkValuesHolder ret)
    {
      Assert.AreEqual(expected.Small, ret.Small);
      Assert.AreEqual(expected.MaybeSmall, ret.MaybeSmall);
      Assert.AreEqual(expected.Release, ret.Release);
      Assert.AreEqual(expected.Text.ToString(), ret.Text.ToString());
      Assert.AreEqual(expected.Culture, ret.Culture);
      Assert.AreEqual(expected.Letter, ret.Letter);
      Assert.AreEqual(expected.Pointer, ret.Pointer);
      Assert.AreEqual(expected.UnsignedPointer, ret.UnsignedPointer);
      CollectionAssert.AreEqual(expected.Memory.ToArray(), ret.Memory.ToArray());
      CollectionAssert.AreEqual(expected.ReadOnly.ToArray(), ret.ReadOnly.ToArray());
      CollectionAssert.AreEqual(expected.Segment.ToArray(), ret.Segment.ToArray());
      Assert.AreEqual(expected.Number, ret.Number);
      Assert.AreEqual(expected.Pair, ret.Pair);
      Assert.AreEqual(expected.Named, ret.Named);
      Assert.AreEqual(expected.Long, ret.Long);
      CollectionAssert.AreEqual(expected.Points, ret.Points);
      Assert.AreEqual(expected.WithObject, ret.WithObject); // the Version in the object item has a type id
    }

    [TestMethod]
    [DataRow(false, ObjectLayout.Array)]
    [DataRow(true, ObjectLayout.Array)]
    [DataRow(false, ObjectLayout.Map)]
    [DataRow(true, ObjectLayout.Map)]
    public void MoreFrameworkValuesRoundTrip(bool useSchema, ObjectLayout layout)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema, ObjectLayout = layout };
      FrameworkValuesHolder holder = CreateValues();

      FrameworkValuesHolder ret = Serializer.Deserialize<FrameworkValuesHolder>(Serializer.Serialize(holder, settings), settings);

      AssertValues(holder, ret);
    }

    /// <summary>
    /// The same bytes as MessagePack-CSharp writes (checked in LsMsgPackInteropTests).
    /// </summary>
    [TestMethod]
    public void MoreEncodings()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false };

      CollectionAssert.AreEqual(new byte[] { 0xCA, 0x3F, 0xC0, 0x00, 0x00 }, Serializer.Serialize((Half)1.5, settings));
      CollectionAssert.AreEqual(new byte[] { 0xA5, (byte)'1', (byte)'.', (byte)'2', (byte)'.', (byte)'3' }, Serializer.Serialize(new Version(1, 2, 3), settings));
      CollectionAssert.AreEqual(new byte[] { 0xA2, (byte)'h', (byte)'i' }, Serializer.Serialize(new StringBuilder("hi"), settings));
      CollectionAssert.AreEqual(new byte[] { 0xA5, (byte)'n', (byte)'l', (byte)'-', (byte)'N', (byte)'L' }, Serializer.Serialize(CultureInfo.GetCultureInfo("nl-NL"), settings));
      CollectionAssert.AreEqual(new byte[] { 0x41 }, Serializer.Serialize(new Rune('A'), settings));
      CollectionAssert.AreEqual(new byte[] { 0xCE, 0x00, 0x01, 0xF6, 0x00 }, Serializer.Serialize(new Rune(0x1F600), settings));
      CollectionAssert.AreEqual(new byte[] { 0xD0, 0xD6 }, Serializer.Serialize((nint)(-42), settings));
      CollectionAssert.AreEqual(new byte[] { 0x2A }, Serializer.Serialize((nuint)42, settings));
      CollectionAssert.AreEqual(new byte[] { 0xC4, 0x03, 0x01, 0x02, 0x03 }, Serializer.Serialize(new Memory<byte>(new byte[] { 1, 2, 3 }), settings));
      CollectionAssert.AreEqual(new byte[] { 0xC4, 0x03, 0x01, 0x02, 0x03 }, Serializer.Serialize(new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }), settings));
      CollectionAssert.AreEqual(new byte[] { 0xC4, 0x02, 0x02, 0x03 }, Serializer.Serialize(new ArraySegment<byte>(new byte[] { 1, 2, 3 }, 1, 2), settings));
      CollectionAssert.AreEqual(new byte[] { 0x92, 0xCB, 0x3F, 0xF8, 0, 0, 0, 0, 0, 0, 0xCB, 0xC0, 0, 0, 0, 0, 0, 0, 0 }, Serializer.Serialize(new Complex(1.5, -2), settings));
      CollectionAssert.AreEqual(new byte[] { 0x92, 0x01, 0xA1, (byte)'a' }, Serializer.Serialize((1, "a"), settings));
      CollectionAssert.AreEqual(new byte[] { 0x92, 0x01, 0xA1, (byte)'a' }, Serializer.Serialize(Tuple.Create(1, "a"), settings));
      CollectionAssert.AreEqual(new byte[] { 0x98, 1, 2, 3, 4, 5, 6, 7, 0x92, 8, 9 }, Serializer.Serialize((1, 2, 3, 4, 5, 6, 7, 8, 9), settings)); // the rest is a tuple of its own
    }

    public class FrameworkValuesBoxed
    {
      public List<object> Values { get; set; }
    }

    /// <summary>
    /// Assigned to object they get a type id, found by its short name (Complex is System.Numerics.Complex, not an internal type of System.Private.CoreLib).
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MoreBoxedValues(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      FrameworkValuesBoxed boxed = new FrameworkValuesBoxed()
      {
        Values = new List<object>() { (Half)2.5, new Version(1, 0), new Complex(0, 1), (1, "a"), Tuple.Create("b", 2.5), new Rune('x'), CultureInfo.InvariantCulture }
      };

      FrameworkValuesBoxed ret = Serializer.Deserialize<FrameworkValuesBoxed>(Serializer.Serialize(boxed, settings), settings);

      CollectionAssert.AreEqual(boxed.Values, ret.Values);

      boxed.Values = new List<object>() { new Memory<byte>(new byte[] { 1 }), new ArraySegment<byte>(new byte[] { 2 }) };
      ret = Serializer.Deserialize<FrameworkValuesBoxed>(Serializer.Serialize(boxed, settings), settings);
      CollectionAssert.AreEqual(new byte[] { 1 }, ((Memory<byte>)ret.Values[0]).ToArray());
      CollectionAssert.AreEqual(new byte[] { 2 }, ((ArraySegment<byte>)ret.Values[1]).ToArray());
    }

    /// <summary>
    /// ArraySegment&lt;byte&gt; used to be written as an array of numbers, which still reads (and so do arrays and bins of other libraries).
    /// </summary>
    [TestMethod]
    public void ArraySegmentFromAnArray()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false };
      byte[] array = Serializer.Serialize(new List<byte>() { 1, 2, 3 }, settings);
      CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, Serializer.Deserialize<ArraySegment<byte>>(array, settings).ToArray());
    }

    /// <summary>
    /// Tuples with fewer or more items than the reader's: the missing ones are the default, the extra ones are skipped.
    /// </summary>
    [TestMethod]
    public void TuplesOfOtherLengths()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false };
      Assert.AreEqual((1, "a", 0), Serializer.Deserialize<(int, string, int)>(Serializer.Serialize((1, "a"), settings), settings));
      Assert.AreEqual(Tuple.Create(1), Serializer.Deserialize<Tuple<int>>(Serializer.Serialize((1, "a"), settings), settings));
    }

    public class FrameworkCollectionsHolder
    {
      public ImmutableArray<int> Array { get; set; }
      public ImmutableList<string> List { get; set; }
      public ImmutableHashSet<int> HashSet { get; set; }
      public ImmutableSortedSet<string> SortedSet { get; set; }
      public ImmutableQueue<int> Queue { get; set; }
      public ImmutableStack<int> Stack { get; set; }
      public ImmutableDictionary<string, int> Dictionary { get; set; }
      public ImmutableSortedDictionary<int, string> SortedDictionary { get; set; }
      public IImmutableList<int> ListInterface { get; set; }
      public IImmutableSet<string> SetInterface { get; set; }
      public IImmutableDictionary<string, string> DictionaryInterface { get; set; }
      public FrozenSet<int> Frozen { get; set; }
      public FrozenDictionary<string, int> FrozenMap { get; set; }
      public BitArray Bits { get; set; }
      public int[,] Grid { get; set; }
      public string[,,] Cube { get; set; }
      public object[,] Mixed { get; set; }
    }

    private static FrameworkCollectionsHolder CreateCollections()
    {
      return new FrameworkCollectionsHolder()
      {
        Array = ImmutableArray.Create(1, 2, 3),
        List = ImmutableList.Create("a", "b"),
        HashSet = ImmutableHashSet.Create(4, 5),
        SortedSet = ImmutableSortedSet.Create("z", "y"),
        Queue = ImmutableQueue.Create(1, 2, 3),
        Stack = ImmutableStack.Create(1, 2, 3),
        Dictionary = ImmutableDictionary.CreateRange(new[] { new KeyValuePair<string, int>("a", 1) }),
        SortedDictionary = ImmutableSortedDictionary.CreateRange(new[] { new KeyValuePair<int, string>(2, "two"), new KeyValuePair<int, string>(1, "one") }),
        ListInterface = ImmutableList.Create(7),
        SetInterface = ImmutableHashSet.Create("s"),
        DictionaryInterface = ImmutableDictionary.CreateRange(new[] { new KeyValuePair<string, string>("k", "v") }),
        Frozen = new[] { 8, 9 }.ToFrozenSet(),
        FrozenMap = new Dictionary<string, int>() { { "f", 1 } }.ToFrozenDictionary(),
        Bits = new BitArray(new[] { true, false, true, true }),
        Grid = new int[,] { { 1, 2, 3 }, { 4, 5, 6 } },
        Cube = new string[,,] { { { "a", null }, { "c", "d" } } },
        Mixed = new object[,] { { "one", "two" }, { new Version(3, 0), null } }
      };
    }

    private static void AssertCollections(FrameworkCollectionsHolder expected, FrameworkCollectionsHolder ret)
    {
      CollectionAssert.AreEqual(expected.Array.ToArray(), ret.Array.ToArray());
      CollectionAssert.AreEqual(expected.List.ToArray(), ret.List.ToArray());
      Assert.IsTrue(expected.HashSet.SetEquals(ret.HashSet));
      CollectionAssert.AreEqual(expected.SortedSet.ToArray(), ret.SortedSet.ToArray());
      CollectionAssert.AreEqual(expected.Queue.ToArray(), ret.Queue.ToArray());
      CollectionAssert.AreEqual(expected.Stack.ToArray(), ret.Stack.ToArray()); // the same order (top first)
      CollectionAssert.AreEquivalent(expected.Dictionary.ToArray(), ret.Dictionary.ToArray());
      CollectionAssert.AreEqual(expected.SortedDictionary.ToArray(), ret.SortedDictionary.ToArray());
      Assert.IsInstanceOfType<ImmutableList<int>>(ret.ListInterface);
      CollectionAssert.AreEqual(expected.ListInterface.ToArray(), ret.ListInterface.ToArray());
      Assert.IsInstanceOfType<ImmutableHashSet<string>>(ret.SetInterface);
      CollectionAssert.AreEquivalent(expected.DictionaryInterface.ToArray(), ret.DictionaryInterface.ToArray());
      Assert.IsTrue(expected.Frozen.SetEquals(ret.Frozen));
      CollectionAssert.AreEquivalent(expected.FrozenMap.ToArray(), ret.FrozenMap.ToArray());
      CollectionAssert.AreEqual(expected.Bits, ret.Bits);
      CollectionAssert.AreEqual(expected.Grid, ret.Grid);
      Assert.AreEqual(2, ret.Grid.GetLength(0));
      CollectionAssert.AreEqual(expected.Cube, ret.Cube);
      Assert.AreEqual(2, ret.Cube.GetLength(1));
      CollectionAssert.AreEqual(expected.Mixed, ret.Mixed); // a type id for the Version
    }

    [TestMethod]
    [DataRow(false, ObjectLayout.Array)]
    [DataRow(true, ObjectLayout.Array)]
    [DataRow(false, ObjectLayout.Map)]
    [DataRow(true, ObjectLayout.Map)]
    public void FrameworkCollectionsRoundTrip(bool useSchema, ObjectLayout layout)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema, ObjectLayout = layout };
      FrameworkCollectionsHolder holder = CreateCollections();

      FrameworkCollectionsHolder ret = Serializer.Deserialize<FrameworkCollectionsHolder>(Serializer.Serialize(holder, settings), settings);

      AssertCollections(holder, ret);
    }

    /// <summary>
    /// Assigned to object they get a type id: "Int32[,]" for a multidimensional array.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FrameworkCollectionsBoxed(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      FrameworkValuesBoxed boxed = new FrameworkValuesBoxed()
      {
        Values = new List<object>() { new int[,] { { 1 }, { 2 } }, ImmutableArray.Create("a"), ImmutableDictionary.CreateRange(new[] { new KeyValuePair<string, int>("b", 2) }), new BitArray(new[] { true }), new List<int[,]>() { new int[0, 3] } }
      };

      FrameworkValuesBoxed ret = Serializer.Deserialize<FrameworkValuesBoxed>(Serializer.Serialize(boxed, settings), settings);

      CollectionAssert.AreEqual((int[,])boxed.Values[0], (int[,])ret.Values[0]);
      CollectionAssert.AreEqual(new[] { "a" }, ((ImmutableArray<string>)ret.Values[1]).ToArray());
      Assert.AreEqual(2, ((ImmutableDictionary<string, int>)ret.Values[2])["b"]);
      CollectionAssert.AreEqual((BitArray)boxed.Values[3], (BitArray)ret.Values[3]);
      Assert.AreEqual(3, ((List<int[,]>)ret.Values[4])[0].GetLength(1));
    }

    /// <summary>
    /// The same bytes as MessagePack-CSharp writes (checked in LsMsgPackInteropTests): the lengths, then the items.
    /// </summary>
    [TestMethod]
    public void MultidimensionalEncoding()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false };
      CollectionAssert.AreEqual(new byte[] { 0x93, 0x02, 0x02, 0x94, 1, 2, 3, 4 }, Serializer.Serialize(new int[,] { { 1, 2 }, { 3, 4 } }, settings));
      CollectionAssert.AreEqual(new byte[] { 0x94, 0x01, 0x02, 0x00, 0x90 }, Serializer.Serialize(new int[1, 2, 0], settings));

      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<int[,]>(Serializer.Serialize(new[] { 1, 2, 3, 4 }, settings), settings)); // how they were written before
      StringAssert.Contains(ex.Message, "multidimensional");
    }

    public class FrameworkHiddenHolder
    {
      public IEnumerable<int> Numbers { get; set; }
      public object Set { get; set; }
    }

    /// <summary>
    /// The framework's types that are not public (a LINQ iterator, the FrozenSet that ToFrozenSet returns) get no type id: no reader resolves their names.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HiddenFrameworkTypesGetNoTypeId(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema, ObjectLayout = ObjectLayout.Map };
      FrameworkHiddenHolder holder = new FrameworkHiddenHolder() { Numbers = new[] { 1, 2, 3 }.Select(n => n * 2), Set = new[] { 7 }.ToFrozenSet() };

      byte[] bytes = Serializer.Serialize(holder, settings);
      string text = Encoding.UTF8.GetString(bytes);
      Assert.DoesNotContain("Iterator", text);
      Assert.DoesNotContain("Frozen", text);

      FrameworkHiddenHolder ret = Serializer.Deserialize<FrameworkHiddenHolder>(bytes, settings);
      CollectionAssert.AreEqual(new[] { 2, 4, 6 }, ret.Numbers.ToArray());
      Assert.AreEqual(1, ((object[])ret.Set).Length); // an array without a type id
    }

    public class FrameworkBigIntegers
    {
      public BigInteger Big { get; set; }
      public BigInteger Small { get; set; }
      public Int128 Signed { get; set; }
      public Int128? MaybeSigned { get; set; }
      public UInt128 Unsigned { get; set; }
      public List<BigInteger> Many { get; set; }
      public object Boxed { get; set; }
      public object BoxedWide { get; set; }
    }

    [TestMethod]
    [DataRow(false, ObjectLayout.Array)]
    [DataRow(true, ObjectLayout.Array)]
    [DataRow(false, ObjectLayout.Map)]
    [DataRow(true, ObjectLayout.Map)]
    public void BigIntegersRoundTrip(bool useSchema, ObjectLayout layout)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema, ObjectLayout = layout };
      FrameworkBigIntegers numbers = new FrameworkBigIntegers()
      {
        Big = BigInteger.Pow(-3, 201), // 319 bits
        Small = -7,
        Signed = Int128.MinValue,
        MaybeSigned = Int128.MaxValue,
        Unsigned = UInt128.MaxValue,
        Many = new List<BigInteger>() { 0, long.MinValue, ulong.MaxValue, (BigInteger)ulong.MaxValue + 1, -(BigInteger)ulong.MaxValue - 1, BigInteger.Pow(2, 1000) },
        Boxed = BigInteger.Pow(10, 30),
        BoxedWide = (Int128)(-1)
      };

      FrameworkBigIntegers ret = Serializer.Deserialize<FrameworkBigIntegers>(Serializer.Serialize(numbers, settings), settings);

      Assert.AreEqual(numbers.Big, ret.Big);
      Assert.AreEqual(numbers.Small, ret.Small);
      Assert.AreEqual(numbers.Signed, ret.Signed);
      Assert.AreEqual(numbers.MaybeSigned, ret.MaybeSigned);
      Assert.AreEqual(numbers.Unsigned, ret.Unsigned);
      CollectionAssert.AreEqual(numbers.Many, ret.Many);
      Assert.AreEqual(numbers.Boxed, ret.Boxed); // with a type id
      Assert.AreEqual(numbers.BoxedWide, ret.BoxedWide);
    }

    /// <summary>
    /// An integer when the value fits in 64 bits, otherwise extension type -2 with the value in big-endian two's complement, as short as possible (msgpack/msgpack#206).
    /// </summary>
    [TestMethod]
    public void BigIntegerEncodings()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false };

      CollectionAssert.AreEqual(new byte[] { 0x05 }, Serializer.Serialize(new BigInteger(5), settings));
      CollectionAssert.AreEqual(new byte[] { 0xFF }, Serializer.Serialize(new BigInteger(-1), settings));
      CollectionAssert.AreEqual(new byte[] { 0xCF, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, Serializer.Serialize((Int128)long.MaxValue, settings));
      CollectionAssert.AreEqual(new byte[] { 0xCF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, Serializer.Serialize((UInt128)ulong.MaxValue, settings));
      CollectionAssert.AreEqual(new byte[] { 0xD3, 0x80, 0, 0, 0, 0, 0, 0, 0 }, Serializer.Serialize((BigInteger)long.MinValue, settings));
      CollectionAssert.AreEqual(new byte[] { 0xC7, 0x09, 0xFE, 0x01, 0, 0, 0, 0, 0, 0, 0, 0 }, Serializer.Serialize((BigInteger)ulong.MaxValue + 1, settings)); // 2^64
      CollectionAssert.AreEqual(new byte[] { 0xC7, 0x09, 0xFE, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0 }, Serializer.Serialize(-((BigInteger)ulong.MaxValue + 1), settings)); // -2^64
      CollectionAssert.AreEqual(new byte[] { 0xD8, 0xFE, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, Serializer.Serialize(Int128.MinValue, settings)); // fixext 16
      byte[] max = Serializer.Serialize(UInt128.MaxValue, settings);
      CollectionAssert.AreEqual(new byte[] { 0xC7, 0x11, 0xFE, 0x00, 0xFF }, max.Take(5).ToArray()); // 17 bytes: a 0 for the sign
      Assert.AreEqual(UInt128.MaxValue, Serializer.Deserialize<UInt128>(max, settings));

      MsgPackSettings uncompact = new MsgPackSettings() { UseInexedSchema = false, DynamicallyCompact = false };
      CollectionAssert.AreEqual(new byte[] { 0xD3, 0, 0, 0, 0, 0, 0, 0, 5 }, Serializer.Serialize(new BigInteger(5), uncompact));
      CollectionAssert.AreEqual(new byte[] { 0xCF, 0, 0, 0, 0, 0, 0, 0, 5 }, Serializer.Serialize((UInt128)5, uncompact));
    }

    /// <summary>
    /// Other integers read into big integers, and values that do not fit.
    /// </summary>
    [TestMethod]
    public void BigIntegersFromOtherValues()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false };

      Assert.AreEqual((Int128)300, Serializer.Deserialize<Int128>(Serializer.Serialize((short)300, settings), settings));
      Assert.AreEqual((BigInteger)ulong.MaxValue, Serializer.Deserialize<BigInteger>(Serializer.Serialize(ulong.MaxValue, settings), settings));
      Assert.AreEqual(BigInteger.Pow(2, 100), Serializer.Deserialize<BigInteger>(Serializer.Serialize((Int128)BigInteger.Pow(2, 100), settings), settings));
      Assert.ThrowsExactly<OverflowException>(() => Serializer.Deserialize<Int128>(Serializer.Serialize(BigInteger.Pow(2, 127), settings), settings));
      Assert.ThrowsExactly<OverflowException>(() => Serializer.Deserialize<UInt128>(Serializer.Serialize(new BigInteger(-1), settings), settings));
    }

    public class FrameworkOffsets
    {
      public DateTimeOffset When { get; set; }
      public DateTimeOffset? Maybe { get; set; }
      public object Boxed { get; set; }
      public List<DateTimeOffset> Many { get; set; }
    }

    /// <summary>
    /// DateTimeOffset keeps its offset by default: [the moment as a timestamp, the offset in minutes].
    /// </summary>
    [TestMethod]
    [DataRow(false, ObjectLayout.Array, DateTimeOffsetFormat.TimestampAndOffset)]
    [DataRow(true, ObjectLayout.Array, DateTimeOffsetFormat.TimestampAndOffset)]
    [DataRow(false, ObjectLayout.Map, DateTimeOffsetFormat.TimestampAndOffset)]
    [DataRow(true, ObjectLayout.Map, DateTimeOffsetFormat.ClockTimeAndOffset)]
    [DataRow(false, ObjectLayout.Array, DateTimeOffsetFormat.ClockTimeAndOffset)]
    public void DateTimeOffsetsKeepTheirOffset(bool useSchema, ObjectLayout layout, DateTimeOffsetFormat format)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema, ObjectLayout = layout, DateTimeOffsetFormat = format };
      FrameworkOffsets offsets = new FrameworkOffsets()
      {
        When = new DateTimeOffset(2026, 10, 10, 12, 0, 0, 125, TimeSpan.FromMinutes(330)),
        Maybe = new DateTimeOffset(1950, 1, 1, 0, 0, 0, TimeSpan.FromHours(-8)),
        Boxed = new DateTimeOffset(2026, 3, 1, 23, 59, 59, TimeSpan.FromHours(14)),
        Many = new List<DateTimeOffset>() { DateTimeOffset.MinValue.ToOffset(TimeSpan.Zero), new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.FromMinutes(-150)) }
      };

      FrameworkOffsets ret = Serializer.Deserialize<FrameworkOffsets>(Serializer.Serialize(offsets, settings), settings);

      AssertSameOffset(offsets.When, ret.When);
      AssertSameOffset(offsets.Maybe.Value, ret.Maybe.Value);
      AssertSameOffset((DateTimeOffset)offsets.Boxed, (DateTimeOffset)ret.Boxed);
      for (int t = 0; t < offsets.Many.Count; t++)
        AssertSameOffset(offsets.Many[t], ret.Many[t]);
    }

    private static void AssertSameOffset(DateTimeOffset expected, DateTimeOffset actual)
    {
      Assert.AreEqual(expected, actual);
      Assert.AreEqual(expected.Offset, actual.Offset);
    }

    [TestMethod]
    public void DateTimeOffsetEncodings()
    {
      DateTimeOffset when = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.FromHours(2)); // 10:00 UTC
      byte[] moment = { 0xD6, 0xFF, 0x6A, 0x1D, 0x58, 0x20 }; // 2026-06-01 10:00:00 UTC
      byte[] clock = { 0xD6, 0xFF, 0x6A, 0x1D, 0x74, 0x40 }; // 2026-06-01 12:00:00 "UTC"

      CollectionAssert.AreEqual(new byte[] { 0x92 }.Concat(moment).Concat(new byte[] { 0x78 }).ToArray(), Serializer.Serialize(when, new MsgPackSettings() { UseInexedSchema = false }));
      CollectionAssert.AreEqual(moment, Serializer.Serialize(when, new MsgPackSettings() { UseInexedSchema = false, DateTimeOffsetFormat = DateTimeOffsetFormat.Timestamp }));
      CollectionAssert.AreEqual(new byte[] { 0x92 }.Concat(clock).Concat(new byte[] { 0x78 }).ToArray(), Serializer.Serialize(when, new MsgPackSettings() { UseInexedSchema = false, DateTimeOffsetFormat = DateTimeOffsetFormat.ClockTimeAndOffset }));
      byte[] uncompact = Serializer.Serialize(when.ToOffset(TimeSpan.FromMinutes(-300)), new MsgPackSettings() { UseInexedSchema = false, DynamicallyCompact = false });
      CollectionAssert.AreEqual(new byte[] { 0xD1, 0xFE, 0xD4 }, uncompact.Skip(uncompact.Length - 3).ToArray()); // the minutes are a short (int16)

      // A timestamp (as DateTimeOffset values used to be written) is read in any format
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false, ReadDateTimeKind = DateTimeKind.Utc };
      Assert.AreEqual(when, Serializer.Deserialize<DateTimeOffset>(moment, settings));
      Assert.AreEqual(TimeSpan.Zero, Serializer.Deserialize<DateTimeOffset>(moment, settings).Offset);
    }

    public class FrameworkLazyHolder
    {
      public Lazy<int> Later { get; set; }
    }

    public class FrameworkLazyWriter
    {
      public Dictionary<string, int> Later { get; set; }
    }

    public class FrameworkTypeHolder
    {
      public Type Kind { get; set; }
    }

    /// <summary>
    /// Framework types the serializers do not know and that have no settable properties are refused, instead of being written as an empty map and read back as their default value.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnsupportedFrameworkTypesAreRefused(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };

      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(new FrameworkLazyHolder() { Later = new Lazy<int>(() => 7) }, settings));
      StringAssert.Contains(ex.Message, "no settable properties");
      StringAssert.Contains(ex.Message, "Lazy");
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(new Lazy<int>(() => 7), settings));
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(new List<object>() { Index.FromEnd(1) }, settings));

      MsgPackSettings map = new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map };
      byte[] bytes = Serializer.Serialize(new FrameworkLazyWriter() { Later = new Dictionary<string, int>() }, map);
      ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<FrameworkLazyHolder>(bytes, map));
      StringAssert.Contains(ex.Message, "no settable properties");
    }

    /// <summary>
    /// A Type would let the data pick types (see docs/security.md): refused with a reason of its own.
    /// </summary>
    [TestMethod]
    public void TypesAreRefused()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false };
      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(new FrameworkTypeHolder() { Kind = typeof(string) }, settings));
      StringAssert.Contains(ex.Message, "the data would pick the type");
    }
  }

  [TestClass]
  public class LsSerializingFrameworkTypes : SerializingFrameworkTypes
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtSerializingFrameworkTypes : SerializingFrameworkTypes
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }
}
