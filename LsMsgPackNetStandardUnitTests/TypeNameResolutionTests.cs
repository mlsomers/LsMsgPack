using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace LsMsgPackUnitTests
{
  namespace TwinsA
  {
    public class TwinBaseA { public string Tag { get; set; } }
    public class ResolutionTwin : TwinBaseA { public int A { get; set; } }
  }

  namespace TwinsB
  {
    public class TwinBaseB { public string Tag { get; set; } }
    public class ResolutionTwin : TwinBaseB { public int B { get; set; } }
  }

  namespace Shadowing
  {
    /// <summary>
    /// A class with the short name of an abstract framework class (System.Attribute).
    /// </summary>
    public class Attribute
    {
      public string Name { get; set; }
    }
  }

  /// <summary>
  /// Classes nested in a generic class have its generic arguments (like the reply class of a generic API controller).
  /// </summary>
  public class NestingGeneric<T>
  {
    public class Reply
    {
      public T Value { get; set; }
      public int Count { get; set; }
    }

    public class Pair<U>
    {
      public T First { get; set; }
      public U Second { get; set; }
    }

    public class Middle
    {
      public class Deep<U>
      {
        public List<U> Items { get; set; }
      }
    }
  }

  public class TwinHolder
  {
    public TwinsA.TwinBaseA First { get; set; }
    public TwinsB.TwinBaseB Second { get; set; }
    public object Anything { get; set; }
  }

  /// <summary>
  /// How the names of types (type ids and the names in the indexed schema) are resolved when reading.
  /// </summary>
  public abstract class TypeNameResolutionTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    private static int _assemblies;

    /// <summary>
    /// A class in an assembly that no cache has seen yet (as in a fresh process): public string Name and int Number.
    /// </summary>
    private static Type NewAssemblyType()
    {
      int n = System.Threading.Interlocked.Increment(ref _assemblies);
      AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("FreshModel" + n), AssemblyBuilderAccess.Run);
      TypeBuilder type = assembly.DefineDynamicModule("FreshModel" + n).DefineType("FreshModel" + n + ".FreshEntity" + n, TypeAttributes.Public | TypeAttributes.Class);
      type.DefineDefaultConstructor(MethodAttributes.Public);
      AddProperty(type, "Name", typeof(string));
      AddProperty(type, "Number", typeof(int));
      return type.CreateTypeInfo().AsType();
    }

    private static void AddProperty(TypeBuilder type, string name, Type propertyType)
    {
      FieldBuilder field = type.DefineField("_" + name, propertyType, FieldAttributes.Private);
      PropertyBuilder property = type.DefineProperty(name, PropertyAttributes.None, propertyType, null);
      MethodAttributes accessor = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

      MethodBuilder getter = type.DefineMethod("get_" + name, accessor, propertyType, Type.EmptyTypes);
      ILGenerator il = getter.GetILGenerator();
      il.Emit(OpCodes.Ldarg_0);
      il.Emit(OpCodes.Ldfld, field);
      il.Emit(OpCodes.Ret);
      property.SetGetMethod(getter);

      MethodBuilder setter = type.DefineMethod("set_" + name, accessor, null, new[] { propertyType });
      il = setter.GetILGenerator();
      il.Emit(OpCodes.Ldarg_0);
      il.Emit(OpCodes.Ldarg_1);
      il.Emit(OpCodes.Stfld, field);
      il.Emit(OpCodes.Ret);
      property.SetSetMethod(setter);
    }

    /// <summary>
    /// The names of the schema were resolved when it was read, before anything registered the assembly of the T of List&lt;T&gt; ("Unable to resolve type").
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map, false)]
    [DataRow(ObjectLayout.Array, false)]
    [DataRow(ObjectLayout.Map, true)]
    [DataRow(ObjectLayout.Array, true)]
    public void SchemaNamesOfAnotherAssembly(ObjectLayout layout, bool fullNames)
    {
      Type entity = NewAssemblyType();
      Type listType = typeof(List<>).MakeGenericType(entity);
      IList list = (IList)Activator.CreateInstance(listType);
      object item = Activator.CreateInstance(entity);
      entity.GetProperty("Name").SetValue(item, "fresh");
      entity.GetProperty("Number").SetValue(item, 42);
      list.Add(item);

      MsgPackSettings settings = new MsgPackSettings() { ObjectLayout = layout };
      if (fullNames)
        settings.AddTypeIdOptions |= AddTypeIdOption.FullName;
      byte[] bytes = Serializer.Serialize(list, listType, settings);

      IList read = (IList)Serializer.Deserialize(listType, bytes, settings);
      Assert.AreEqual(1, read.Count);
      Assert.AreEqual("fresh", entity.GetProperty("Name").GetValue(read[0]));
      Assert.AreEqual(42, entity.GetProperty("Number").GetValue(read[0]));
    }

    [TestMethod]
    public void NamesOfClassesNestedInGenericClasses()
    {
      Assert.AreEqual("NestingGeneric<Int32>+Reply", LsMsgPack.Meta.TypeResolver.GetTypeName(typeof(NestingGeneric<int>.Reply), false));
      Assert.AreEqual("LsMsgPackUnitTests.NestingGeneric<System.Int32>+Reply", LsMsgPack.Meta.TypeResolver.GetTypeName(typeof(NestingGeneric<int>.Reply), true));
      Assert.AreEqual("NestingGeneric<Int32>+Pair<String>", LsMsgPack.Meta.TypeResolver.GetTypeName(typeof(NestingGeneric<int>.Pair<string>), false));
      Assert.AreEqual("NestingGeneric<Int32>+Middle+Deep<String>[]", LsMsgPack.Meta.TypeResolver.GetTypeName(typeof(NestingGeneric<int>.Middle.Deep<string>[]), false));
      Assert.AreEqual("List<NestingGeneric<NestingGeneric<Int32>+Reply>+Reply>", LsMsgPack.Meta.TypeResolver.GetTypeName(typeof(List<NestingGeneric<NestingGeneric<int>.Reply>.Reply>), false));
    }

    /// <summary>
    /// The short name of a class nested in a generic class threw (ArgumentOutOfRangeException), the full name was the outer class's.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map, false, false)]
    [DataRow(ObjectLayout.Array, false, false)]
    [DataRow(ObjectLayout.Array, true, false)]
    [DataRow(ObjectLayout.Map, false, true)]
    [DataRow(ObjectLayout.Array, false, true)]
    [DataRow(ObjectLayout.Array, true, true)]
    public void ClassesNestedInGenericClasses(ObjectLayout layout, bool fullNames, bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { ObjectLayout = layout, UseInexedSchema = useSchema };
      if (fullNames)
        settings.AddTypeIdOptions |= AddTypeIdOption.FullName;

      NestingGeneric<string>.Reply root = new NestingGeneric<string>.Reply() { Value = "root", Count = 1 };
      NestingGeneric<string>.Reply readRoot = Serializer.Deserialize<NestingGeneric<string>.Reply>(Serializer.Serialize(root, settings), settings);
      Assert.AreEqual("root", readRoot.Value);
      Assert.AreEqual(1, readRoot.Count);

      object[] values =
      {
        new NestingGeneric<int>.Reply() { Value = 7, Count = 2 },
        new NestingGeneric<int>.Pair<string>() { First = 3, Second = "three" },
        new NestingGeneric<int>.Middle.Deep<string>() { Items = new List<string>() { "deep" } },
        new List<NestingGeneric<int>.Reply[]>() { new[] { new NestingGeneric<int>.Reply() { Value = 8 } } }
      };
      object[] read = Serializer.Deserialize<object[]>(Serializer.Serialize(values, settings), settings);
      Assert.AreEqual(7, ((NestingGeneric<int>.Reply)read[0]).Value);
      Assert.AreEqual(2, ((NestingGeneric<int>.Reply)read[0]).Count);
      Assert.AreEqual("three", ((NestingGeneric<int>.Pair<string>)read[1]).Second);
      Assert.AreEqual(3, ((NestingGeneric<int>.Pair<string>)read[1]).First);
      Assert.AreEqual("deep", ((NestingGeneric<int>.Middle.Deep<string>)read[2]).Items[0]);
      Assert.AreEqual(8, ((List<NestingGeneric<int>.Reply[]>)read[3])[0][0].Value);
    }

    /// <summary>
    /// A short name of several types: the one that fits the declared type (type ids name runtime types, so abstract classes like System.Attribute do not count).
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ShortNameOfSeveralTypes(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema, ObjectLayout = ObjectLayout.Map };
      TwinHolder value = new TwinHolder()
      {
        First = new TwinsA.ResolutionTwin() { Tag = "a", A = 1 },
        Second = new TwinsB.ResolutionTwin() { Tag = "b", B = 2 },
        Anything = new Shadowing.Attribute() { Name = "mine" }
      };

      TwinHolder read = Serializer.Deserialize<TwinHolder>(Serializer.Serialize(value, settings), settings);
      Assert.AreEqual(1, ((TwinsA.ResolutionTwin)read.First).A);
      Assert.AreEqual(2, ((TwinsB.ResolutionTwin)read.Second).B);
      Assert.AreEqual("mine", ((Shadowing.Attribute)read.Anything).Name);
    }

    /// <summary>
    /// Where the declared type does not decide, the name stays ambiguous.
    /// </summary>
    [TestMethod]
    public void ShortNameThatFitsSeveralTypes()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map };
      object[] values = { new TwinsA.ResolutionTwin() { A = 1 } };
      byte[] bytes = Serializer.Serialize(values, settings);
      Exception ex = Assert.Throws<Exception>(() => Serializer.Deserialize<object[]>(bytes, settings));
      StringAssert.Contains(ex.ToString(), "ResolutionTwin");
    }
  }

  [TestClass]
  public class LsTypeNameResolutionTests : TypeNameResolutionTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtTypeNameResolutionTests : TypeNameResolutionTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }

  [TestClass]
  public class GenTypeNameResolutionTests : TypeNameResolutionTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
