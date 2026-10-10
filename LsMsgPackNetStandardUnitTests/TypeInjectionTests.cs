using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace LsMsgPackUnitTests
{
  public interface IInjectionShape
  {
    string Label { get; set; }
  }

  public class InjectionSquare : IInjectionShape
  {
    public string Label { get; set; }
    public int Side { get; set; }
  }

  /// <summary>
  /// Stands in for a dangerous type: counts its instances and the calls of its setter (a gadget does its harm in the constructor or a setter).
  /// </summary>
  public class InjectionCanary
  {
    public static int Created;
    public static int Set;

    private string _label;

    public InjectionCanary()
    {
      Created++;
    }

    public string Label
    {
      get { return _label; }
      set { _label = value; Set++; }
    }

    public static void Reset()
    {
      Created = 0;
      Set = 0;
    }
  }

  public class InjectionHolderLoose
  {
    public object Shape { get; set; }
  }

  /// <summary>
  /// Has the same property name as <see cref="InjectionHolderLoose"/>, so the data of one is read as the other.
  /// </summary>
  public class InjectionHolderTyped
  {
    public IInjectionShape Shape { get; set; }
  }

  public class InjectionHolderExact
  {
    public InjectionSquare Shape { get; set; }
  }

  /// <summary>
  /// The data must not create instances of types it picks that do not fit where they go, nor load assemblies, and a <see cref="MsgPackOptions.TypeGuard"/> limits what an object, interface or base class accepts (docs/security.md).
  /// </summary>
  public abstract class TypeInjectionTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    [TestInitialize]
    public void CacheTheTestTypes()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(InjectionCanary)); // an attacker names types the resolvers can find
    }

    private static MsgPackSettings Settings(bool schema, IMsgPackTypeGuard guard = null)
    {
      return new MsgPackSettings() { UseInexedSchema = schema, TypeGuard = guard };
    }

    /// <summary>
    /// { "Shape": { "": typeId, "Label": "x" } } without a schema.
    /// </summary>
    private static byte[] HolderWithTypeId(string typeId)
    {
      MsgPackSettings settings = new MsgPackSettings();
      MpMap shape = new MpMap(new[]
      {
        new KeyValuePair<object, object>(new MpString(settings) { Value = "" }, new MpString(settings) { Value = typeId }),
        new KeyValuePair<object, object>(new MpString(settings) { Value = "Label" }, new MpString(settings) { Value = "x" })
      }, settings);
      return new MpMap(new[] { new KeyValuePair<object, object>(new MpString(settings) { Value = "Shape" }, shape) }, settings).ToBytes();
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void NotAssignable_RefusedBeforeCreating(bool schema)
    {
      byte[] bytes = Serializer.Serialize(new InjectionHolderLoose() { Shape = new InjectionCanary() { Label = "x" } }, Settings(schema));
      InjectionCanary.Reset();

      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<InjectionHolderTyped>(bytes, Settings(schema)));
      StringAssert.Contains(ex.Message, "cannot be assigned to");
      Assert.AreEqual(0, InjectionCanary.Created);
      Assert.AreEqual(0, InjectionCanary.Set);
    }

    [TestMethod]
    public void NotAssignable_ByName()
    {
      InjectionCanary.Reset();
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<InjectionHolderTyped>(HolderWithTypeId(nameof(InjectionCanary)), Settings(false)));
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<InjectionHolderTyped>(HolderWithTypeId(typeof(InjectionCanary).FullName), Settings(false)));
      Assert.AreEqual(0, InjectionCanary.Created);
    }

    [TestMethod]
    public void AssemblyQualifiedNames_NotResolved()
    {
      InjectionCanary.Reset();
      string[] names =
      {
        typeof(InjectionCanary).AssemblyQualifiedName,
        typeof(List<InjectionCanary>).FullName, // List`1[[LsMsgPackUnitTests.InjectionCanary, LsMsgPackUnitTests, ...]]
        "NoSuchType, NoSuchAssembly"
      };
      foreach (string name in names)
      {
        MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<InjectionHolderLoose>(HolderWithTypeId(name), Settings(false)), name);
        StringAssert.Contains(ex.Message, "could load assemblies");
      }
      Assert.AreEqual(0, InjectionCanary.Created);
    }

    /// <summary>
    /// The framework's internal types are not found by name (the short name "Complex" used to resolve to an internal struct of System.Private.CoreLib): the value stays the map it is.
    /// </summary>
    [TestMethod]
    public void InternalFrameworkTypes_NotResolved()
    {
      Type internalType = typeof(object).GetType(); // System.RuntimeType
      Assert.IsFalse(internalType.IsVisible);
      string[] names = { internalType.Name, internalType.FullName };
      foreach (string name in names)
      {
        InjectionHolderLoose read = Serializer.Deserialize<InjectionHolderLoose>(HolderWithTypeId(name), Settings(false));
        Assert.IsNotNull(read.Shape, name);
        Assert.IsFalse(read.Shape is Type, name);
      }
    }

    [TestMethod]
    public void AssemblyQualifiedNames_NotResolvedFromTheSchema()
    {
      byte[] bytes = Serializer.Serialize(new InjectionHolderLoose() { Shape = new InjectionCanary() { Label = "x" } }, Settings(true));
      string name = nameof(InjectionCanary);
      string replacement = typeof(InjectionCanary).AssemblyQualifiedName;

      // The schema starts with the type names: replace the name by an assembly-qualified one (a str8 header, the schema is read as it is)
      MsgPackSettings settings = new MsgPackSettings();
      IndexedSchemaTypeResolver schema = IndexedSchemaTypeResolver.Unpack(new System.IO.MemoryStream(bytes), settings);
      byte[] schemaBytes = schema.Pack();
      foreach (ComplexTypeDef def in schema.ByTypeId)
      {
        if (def.TypeName == name)
          def.TypeName = replacement;
      }
      byte[] poisoned = Concat(schema.Pack(), bytes, schemaBytes.Length);

      InjectionCanary.Reset();
      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<InjectionHolderLoose>(poisoned, Settings(true)));
      StringAssert.Contains(ex.Message, "could load assemblies");
      Assert.AreEqual(0, InjectionCanary.Created);
    }

    private static byte[] Concat(byte[] head, byte[] source, int skip)
    {
      byte[] result = new byte[head.Length + source.Length - skip];
      Buffer.BlockCopy(head, 0, result, 0, head.Length);
      Buffer.BlockCopy(source, skip, result, head.Length, source.Length - skip);
      return result;
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Guard_RefusesTypesForObject(bool schema)
    {
      byte[] bytes = Serializer.Serialize(new InjectionHolderLoose() { Shape = new InjectionCanary() { Label = "x" } }, Settings(schema));

      InjectionCanary.Reset();
      Assert.IsInstanceOfType<InjectionCanary>(Serializer.Deserialize<InjectionHolderLoose>(bytes, Settings(schema)).Shape); // without a guard object accepts anything the resolvers find
      Assert.AreEqual(1, InjectionCanary.Created);

      InjectionCanary.Reset();
      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<InjectionHolderLoose>(bytes, Settings(schema, new AllowedTypesGuard(typeof(InjectionSquare)))));
      StringAssert.Contains(ex.Message, nameof(MsgPackOptions.TypeGuard));
      StringAssert.Contains(ex.Message, typeof(InjectionCanary).FullName);
      Assert.AreEqual(0, InjectionCanary.Created);
      Assert.AreEqual(0, InjectionCanary.Set);

      InjectionHolderLoose allowed = Serializer.Deserialize<InjectionHolderLoose>(bytes, Settings(schema, new AllowedTypesGuard(typeof(InjectionCanary))));
      Assert.AreEqual("x", ((InjectionCanary)allowed.Shape).Label);
    }

    /// <summary>
    /// The elements of a <c>List&lt;InjectionCanary&gt;</c> are created as the declared element type without a type id, so the guard checks the generic arguments.
    /// </summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Guard_ChecksGenericArguments(bool schema)
    {
      byte[] bytes = Serializer.Serialize(new InjectionHolderLoose() { Shape = new List<InjectionCanary>() { new InjectionCanary() { Label = "x" } } }, Settings(schema));

      InjectionCanary.Reset();
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<InjectionHolderLoose>(bytes, Settings(schema, new AllowedTypesGuard())));
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<InjectionHolderLoose>(bytes, Settings(schema, new AllowedTypesGuard(typeof(List<>)))));
      Assert.AreEqual(0, InjectionCanary.Created);

      InjectionHolderLoose allowed = Serializer.Deserialize<InjectionHolderLoose>(bytes, Settings(schema, new AllowedTypesGuard(typeof(InjectionCanary))));
      Assert.AreEqual("x", ((List<InjectionCanary>)allowed.Shape)[0].Label);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Guard_AllowsTheModel(bool schema)
    {
      byte[] bytes = Serializer.Serialize(new InjectionHolderTyped() { Shape = new InjectionSquare() { Label = "x", Side = 3 } }, Settings(schema));

      InjectionHolderTyped allowed = Serializer.Deserialize<InjectionHolderTyped>(bytes, Settings(schema, new AllowedTypesGuard().AllowAssemblyOf(typeof(IInjectionShape))));
      Assert.AreEqual(3, ((InjectionSquare)allowed.Shape).Side);

      // Assignable, but not allowed (LtMsgPack reads this one without LsMsgPack's way)
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<InjectionHolderTyped>(bytes, Settings(schema, new AllowedTypesGuard())));
    }

    private sealed class CountingGuard : IMsgPackTypeGuard
    {
      internal int Calls;

      public bool IsAllowed(Type type, Type assignedTo, FullPropertyInfo assignedToProp, MsgPackOptions settings)
      {
        Calls++;
        return true;
      }
    }

    /// <summary>
    /// The guard costs nothing for values of their declared type, also when the type id is written (AddTypeIdOption.Always).
    /// </summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Guard_NotAskedForDeclaredTypes(bool schema)
    {
      MsgPackSettings writing = Settings(schema);
      writing.AddTypeIdOptions = AddTypeIdOption.Always;
      byte[] bytes = Serializer.Serialize(new InjectionHolderExact() { Shape = new InjectionSquare() { Label = "x", Side = 3 } }, writing);

      CountingGuard guard = new CountingGuard();
      Assert.AreEqual(3, Serializer.Deserialize<InjectionHolderExact>(bytes, Settings(schema, guard)).Shape.Side);
      Assert.AreEqual(0, guard.Calls);

      byte[] polymorphic = Serializer.Serialize(new InjectionHolderTyped() { Shape = new InjectionSquare() { Label = "x", Side = 3 } }, Settings(schema));
      Serializer.Deserialize<InjectionHolderTyped>(polymorphic, Settings(schema, guard));
      Assert.AreEqual(1, guard.Calls);
    }

    /// <summary>
    /// A class in an assembly of its own (with a Label property), which the readers do not find by themselves: nothing they read reaches it.
    /// </summary>
    private static Type EmitPluginClass(string name)
    {
      AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name + "Assembly"), AssemblyBuilderAccess.Run);
      TypeBuilder type = assembly.DefineDynamicModule(name + "Module").DefineType("Plugins." + name, TypeAttributes.Public | TypeAttributes.Class);
      FieldBuilder field = type.DefineField("_label", typeof(string), FieldAttributes.Private);
      MethodAttributes accessor = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
      MethodBuilder get = type.DefineMethod("get_Label", accessor, typeof(string), Type.EmptyTypes);
      ILGenerator il = get.GetILGenerator();
      il.Emit(OpCodes.Ldarg_0);
      il.Emit(OpCodes.Ldfld, field);
      il.Emit(OpCodes.Ret);
      MethodBuilder set = type.DefineMethod("set_Label", accessor, null, new[] { typeof(string) });
      il = set.GetILGenerator();
      il.Emit(OpCodes.Ldarg_0);
      il.Emit(OpCodes.Ldarg_1);
      il.Emit(OpCodes.Stfld, field);
      il.Emit(OpCodes.Ret);
      PropertyBuilder label = type.DefineProperty("Label", PropertyAttributes.None, typeof(string), null);
      label.SetGetMethod(get);
      label.SetSetMethod(set);
      return type.CreateTypeInfo().AsType();
    }

    private static bool ReadsAs(Type expected, Func<InjectionHolderLoose> read)
    {
      try
      {
        object shape = read().Shape;
        return shape != null && shape.GetType() == expected;
      }
      catch (MsgPackException)
      {
        return false;
      }
    }

    /// <summary>
    /// What an <see cref="AllowedTypesGuard"/> allows becomes known by name: its assemblies, and the types it allows by themselves. Without it, the type of a value declared as object is not found.
    /// </summary>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void AllowedTypesBecomeResolvable(bool schema, bool wholeAssembly)
    {
      Type plugin = EmitPluginClass($"PluginShape{Serializer.Name}{schema}{wholeAssembly}");
      object shape = Activator.CreateInstance(plugin);
      plugin.GetProperty("Label").SetValue(shape, "x");
      byte[] bytes = Serializer.Serialize(new InjectionHolderLoose() { Shape = shape }, Settings(schema));

      Assert.IsFalse(ReadsAs(plugin, () => Serializer.Deserialize<InjectionHolderLoose>(bytes, Settings(schema))), "not registered yet");

      AllowedTypesGuard guard = wholeAssembly ? new AllowedTypesGuard().AllowAssembly(plugin.Assembly) : new AllowedTypesGuard(plugin);
      InjectionHolderLoose read = Serializer.Deserialize<InjectionHolderLoose>(bytes, Settings(schema, guard));
      Assert.AreEqual(plugin, read.Shape.GetType());
      Assert.AreEqual("x", plugin.GetProperty("Label").GetValue(read.Shape));
    }

    [TestMethod]
    public void MsgPackTypesRegistersAnAssembly()
    {
      Type plugin = EmitPluginClass($"PluginRegistered{Serializer.Name}");
      byte[] bytes = Serializer.Serialize(new InjectionHolderLoose() { Shape = Activator.CreateInstance(plugin) }, Settings(true));
      Assert.IsFalse(ReadsAs(plugin, () => Serializer.Deserialize<InjectionHolderLoose>(bytes, Settings(true))), "not registered yet");

      MsgPackTypes.CacheAssemblyTypes(plugin);
      Assert.IsTrue(ReadsAs(plugin, () => Serializer.Deserialize<InjectionHolderLoose>(bytes, Settings(true))));
    }
  }

  [TestClass]
  public class LsTypeInjectionTests : TypeInjectionTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtTypeInjectionTests : TypeInjectionTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }

  [TestClass]
  public class AllowedTypesGuardTests
  {
    [TestMethod]
    public void FrameworkValuesAndCollections()
    {
      AllowedTypesGuard guard = new AllowedTypesGuard();
      Type[] allowed =
      {
        typeof(int), typeof(int?), typeof(string), typeof(decimal), typeof(DateTime), typeof(Guid), typeof(Uri), typeof(object), typeof(DayOfWeek),
        typeof(byte[]), typeof(List<string>), typeof(Dictionary<string, int[]>), typeof(KeyValuePair<int, string>[]), typeof(List<List<int>>)
      };
      foreach (Type type in allowed)
        Assert.IsTrue(guard.IsAllowed(type), type.FullName);

      Type[] refused =
      {
        typeof(StringBuilder), typeof(InjectionCanary), typeof(List<InjectionCanary>), typeof(InjectionCanary[]), typeof(Dictionary<string, InjectionCanary>),
        typeof(int[,]), typeof(List<>), typeof(Lazy<int>)
      };
      foreach (Type type in refused)
        Assert.IsFalse(guard.IsAllowed(type), type.FullName);
    }

    [TestMethod]
    public void TypesAndAssemblies()
    {
      AllowedTypesGuard guard = new AllowedTypesGuard(typeof(InjectionSquare));
      Assert.IsTrue(guard.IsAllowed(typeof(InjectionSquare)));
      Assert.IsTrue(guard.IsAllowed(typeof(List<InjectionSquare>)));
      Assert.IsFalse(guard.IsAllowed(typeof(InjectionCanary)));

      guard.AllowAssemblyOf(typeof(InjectionCanary)); // clears the decisions made so far
      Assert.IsTrue(guard.IsAllowed(typeof(InjectionCanary)));
      Assert.IsFalse(guard.IsAllowed(typeof(Lazy<InjectionCanary>)), "a generic definition of another assembly");

      guard.Allow(typeof(Lazy<>));
      Assert.IsTrue(guard.IsAllowed(typeof(Lazy<InjectionCanary>)));
      Assert.IsFalse(guard.IsAllowed(typeof(Lazy<StringBuilder>)));

      guard.Allow(typeof(Lazy<StringBuilder>)); // a constructed type is allowed as it is
      Assert.IsTrue(guard.IsAllowed(typeof(Lazy<StringBuilder>)));
    }
  }
}
