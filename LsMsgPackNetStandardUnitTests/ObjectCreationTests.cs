using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// No parameterless constructor, counts its constructions and finalizations.
  /// </summary>
  public class CreationProbe
  {
    public static int Constructed;
    public static int Finalized;

    public CreationProbe(string name)
    {
      Name = name;
      Constructed++;
    }

    public string Name { get; set; }
    public string Initialized { get; set; } = "initial";

    ~CreationProbe()
    {
      Interlocked.Increment(ref Finalized);
    }
  }

  public class CreationProbeDefault
  {
    public static int Constructed;
    public static int Finalized;

    public CreationProbeDefault()
    {
      Constructed++;
    }

    public string Name { get; set; }
    public string Initialized { get; set; } = "initial";

    ~CreationProbeDefault()
    {
      Interlocked.Increment(ref Finalized);
    }
  }

  public class CreationRefused
  {
    public CreationRefused()
    {
      throw new InvalidOperationException("refused by the constructor");
    }

    public string Name { get; set; }
  }

  public class CreationCollections
  {
    public string Name { get; set; }
    public List<string> Items { get; set; }
    public Dictionary<string, int> Counts { get; set; }
  }

  /// <summary>
  /// <see cref="MsgPackOptions.ObjectCreation"/>: constructors, initializers and finalizers per choice (docs/security.md).
  /// </summary>
  public abstract class ObjectCreationTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    private static MsgPackSettings Settings(ObjectCreation creation)
    {
      return new MsgPackSettings() { UseInexedSchema = false, ObjectCreation = creation };
    }

    /// <summary>
    /// { "Name": "x" }
    /// </summary>
    private static readonly byte[] NameOnly = new MpMap(new[] { new KeyValuePair<object, object>("Name", "x") }, new MsgPackSettings()).ToBytes();

    [TestInitialize]
    public void Reset()
    {
      CollectGarbage(); // finalize what earlier tests left behind
      CreationProbe.Constructed = CreationProbe.Finalized = 0;
      CreationProbeDefault.Constructed = CreationProbeDefault.Finalized = 0;
    }

    private static void CollectGarbage()
    {
      for (int t = 0; t < 3; t++)
      {
        GC.Collect();
        GC.WaitForPendingFinalizers();
      }
    }

    /// <summary>
    /// In a method of its own, so the instance is garbage once it returns.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private (string Name, string Initialized) Read<T>(ObjectCreation creation)
    {
      object read = Serializer.Deserialize<T>(NameOnly, Settings(creation));
      dynamic probe = read;
      return (probe.Name, probe.Initialized);
    }

    [TestMethod]
    public void Default_IsConstructorOrUninitialized()
    {
      Assert.AreEqual(ObjectCreation.ConstructorOrUninitialized, new MsgPackSettings().ObjectCreation);
    }

    [TestMethod]
    [DataRow(ObjectCreation.Constructor)]
    [DataRow(ObjectCreation.ConstructorOrUninitialized)]
    public void WithConstructor(ObjectCreation creation)
    {
      Assert.AreEqual(("x", "initial"), Read<CreationProbeDefault>(creation));
      Assert.AreEqual(1, CreationProbeDefault.Constructed);
      CollectGarbage();
      Assert.AreEqual(1, CreationProbeDefault.Finalized, "a constructed object keeps its finalizer");
    }

    [TestMethod]
    public void WithoutConstructor_UninitializedWithoutFinalizer()
    {
      Assert.AreEqual(("x", (string)null), Read<CreationProbe>(ObjectCreation.ConstructorOrUninitialized), "no constructor, no initializers");
      Assert.AreEqual(0, CreationProbe.Constructed);
      CollectGarbage();
      Assert.AreEqual(0, CreationProbe.Finalized);
    }

    [TestMethod]
    public void WithoutConstructor_Refused()
    {
      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<CreationProbe>(NameOnly, Settings(ObjectCreation.Constructor)));
      StringAssert.Contains(ex.Message, "no parameterless constructor");
    }

    /// <summary>
    /// Used to fall back to an uninitialized instance. Repeated past the calls after which LtMsgPack compiles the constructor call (PropertyAccessor.CompileAfterCalls).
    /// </summary>
    [TestMethod]
    [DataRow(ObjectCreation.Constructor)]
    [DataRow(ObjectCreation.ConstructorOrUninitialized)]
    public void ConstructorExceptions_PassedOn(ObjectCreation creation)
    {
      for (int t = 0; t < 150; t++)
      {
        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>(() => Serializer.Deserialize<CreationRefused>(NameOnly, Settings(creation)));
        Assert.AreEqual("refused by the constructor", ex.Message);
      }
    }

    [TestMethod]
    public void Uninitialized()
    {
      Assert.AreEqual(("x", (string)null), Read<CreationProbeDefault>(ObjectCreation.Uninitialized));
      Assert.AreEqual(("x", (string)null), Read<CreationProbe>(ObjectCreation.Uninitialized));
      Assert.AreEqual("x", Serializer.Deserialize<CreationRefused>(NameOnly, Settings(ObjectCreation.Uninitialized)).Name);
      Assert.AreEqual(0, CreationProbeDefault.Constructed);
      CollectGarbage();
      Assert.AreEqual(0, CreationProbeDefault.Finalized);
      Assert.AreEqual(0, CreationProbe.Finalized);
    }

    /// <summary>
    /// { "Shape": { "": typeName } } for a property declared as object.
    /// </summary>
    private static byte[] ObjectWithTypeId(string typeName)
    {
      MsgPackSettings settings = new MsgPackSettings();
      return new MpMap(new[] { new KeyValuePair<object, object>("Shape", new MpMap(new[] { new KeyValuePair<object, object>("", typeName) }, settings)) }, settings).ToBytes();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private string ReadShape(string typeName, ObjectCreation creation)
    {
      return Serializer.Deserialize<InjectionHolderLoose>(ObjectWithTypeId(typeName), Settings(creation)).Shape?.GetType().FullName;
    }

    /// <summary>
    /// The finalizer of an uninitialized PeriodicTimer throws (a NullReferenceException on the finalizer thread ends the process): suppressed.
    /// If this test fails, the test host crashes.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectCreation.ConstructorOrUninitialized)]
    [DataRow(ObjectCreation.Uninitialized)]
    public void FinalizerOfAFrameworkType_Suppressed(ObjectCreation creation)
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(InjectionHolderLoose));
      Assert.AreEqual(typeof(PeriodicTimer).FullName, ReadShape(nameof(PeriodicTimer), creation));
      CollectGarbage();
    }

    /// <summary>
    /// The garbage collector crashes on an uninitialized weak reference whether its finalizer is suppressed or not (.NET 8 to 10): never created without the constructor.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectCreation.ConstructorOrUninitialized)]
    [DataRow(ObjectCreation.Uninitialized)]
    public void WeakReferences_Refused(ObjectCreation creation)
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(InjectionHolderLoose));
      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => ReadShape(nameof(WeakReference), creation));
      StringAssert.Contains(ex.Message, "weak reference");
      CollectGarbage();
    }

    /// <summary>
    /// Collections and dictionaries keep their constructor, an uninitialized one would not work.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectCreation.Uninitialized)]
    public void Uninitialized_CollectionsAreConstructed(ObjectCreation creation)
    {
      CreationCollections value = new CreationCollections() { Name = "x", Items = new List<string>() { "a", "b" }, Counts = new Dictionary<string, int>() { { "a", 1 } } };
      foreach (bool schema in new[] { true, false })
      {
        MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = schema, ObjectCreation = creation };
        CreationCollections read = Serializer.Deserialize<CreationCollections>(Serializer.Serialize(value, settings), settings);
        CollectionAssert.AreEqual(value.Items, read.Items);
        Assert.AreEqual(1, read.Counts["a"]);
        read.Items.Add("c"); // usable
        read.Counts.Add("b", 2);
        Assert.AreEqual("c", read.Items.Last());
      }
    }
  }

  [TestClass]
  public class LsObjectCreationTests : ObjectCreationTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtObjectCreationTests : ObjectCreationTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }
}
