using LsMsgPack;
using LsMsgPackDebug;
using LsMsgPack.Generated;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LtMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.Serialization;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// The source generator's code for fields (contract version 4): its members mirror the runtime's (FullPropertyInfo.GetMembers), and its field accessors are used by LtMsgPack and LsMsgPack.
  /// The test assembly's context is generated with SerializedFields.All (GeneratedContext.cs): the options decide which fields are serialized.
  /// </summary>
  [TestClass]
  public partial class GeneratedFieldsTests // partial: the contexts are nested in it
  {
    public class GfBase
    {
      public int BaseField;
      [DataMember] private string _baseSecret = "b";
      public string BaseProperty { get; set; }

      public string GetBaseSecret() { return _baseSecret; }
      public void SetBaseSecret(string value) { _baseSecret = value; }
    }

    public class GfItem : GfBase
    {
      public string Name;
      public double Amount;
      public DateTime When;
      public GfKind Kind;
      public int? Maybe;
      public List<string> Tags;
      public readonly List<int> Filled = new List<int>();
      public readonly int Computed = 5;
      [DataMember] private int _secret;
      [DataMember] public Guid Opted;
      [NonSerialized] public int Skipped;
      public string Title { get; set; }

      public int GetSecret() { return _secret; }
      public void SetSecret(int value) { _secret = value; }
    }

    public enum GfKind { None, Some, Other }

    /// <summary>
    /// A field hiding a property of the base class, and a property hiding a field: the runtime keeps the derived member, whatever its kind.
    /// </summary>
    public class GfHidingBase
    {
      [DataMember] public int Shared { get; set; }
      [DataMember] public string Other;
    }

    public class GfHiding : GfHidingBase
    {
      [DataMember] public new string Shared;
      [DataMember] public new int Other { get; set; }
    }

    public struct GfPoint
    {
      public float X;
      public float Y;
      [DataMember] private int _tag;
      public int Weight { get; set; }

      public int GetTag() { return _tag; }
      public void SetTag(int value) { _tag = value; }
    }

    public class GfHolder
    {
      public GfPoint Point { get; set; }
      public GfPoint[] Path { get; set; }
      public Vector3 Position { get; set; }
      public Quaternion Rotation { get; set; }
      public Plane Ground { get; set; }
      public Vector3 Velocity;
    }

    private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;

    private static object Inner(LtMsgPackSerializer serializer)
    {
      return typeof(LtMsgPackSerializer).GetField("_serializer", Internal).GetValue(serializer);
    }

    private static object Code(LtMsgPackSerializer serializer, Type type)
    {
      object inner = Inner(serializer);
      return inner.GetType().GetMethod("GeneratedCode", Internal).Invoke(inner, new object[] { type });
    }

    private static (Type Declaring, string Name)[] Members(object code)
    {
      Array members = (Array)code.GetType().GetField("Members", Internal | BindingFlags.Public).GetValue(code);
      (Type, string)[] result = new (Type, string)[members.Length];
      for (int t = 0; t < members.Length; t++)
      {
        object member = members.GetValue(t);
        result[t] = ((Type)member.GetType().GetField("DeclaringType", Internal).GetValue(member), (string)member.GetType().GetField("Name", Internal).GetValue(member));
      }
      return result;
    }

    private static int[] UnrolledIds(object code)
    {
      return (int[])code.GetType().GetField("UnrolledIds", Internal | BindingFlags.Public).GetValue(code);
    }

    private static IMsgPackPropertyIncludeStatically[] Filters(SerializedFields fields, NonPublicProperties nonPublic = NonPublicProperties.None)
    {
      return new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(GetOnlyProperties.All, nonPublic, fields), new FilterIgnoredAttribute(), new FilterStatic() };
    }

    private static LtMsgPackSerializer Serializer(bool generated, SerializedFields fields, bool schema = false, ObjectLayout layout = ObjectLayout.Array)
    {
      return new LtMsgPackSerializer(new LtMsgPackOptions()
      {
        Contexts = generated ? new LtMsgPackContext[] { TestMsgPackContext.Default } : null,
        UseRegisteredContexts = false,
        UseInexedSchema = schema,
        ObjectLayout = layout,
        StaticFilters = Filters(fields)
      });
    }

    /// <summary>
    /// The generator mirrors FullPropertyInfo.GetMembers (it cannot call it): for every class and struct of this assembly with generated code, its members are the runtime's candidates
    /// (non-public properties and fields All, as the context is generated) in the same order, the same hiding over properties and fields; and its unrolled members are what the default static filters keep.
    /// </summary>
    [TestMethod]
    public void Members_AsTheRuntimeSeesThem()
    {
      LtMsgPackSerializer serializer = Serializer(true, SerializedFields.Attributed);
      MemberSelection all = new MemberSelection(NonPublicProperties.All, SerializedFields.All);
      LtMsgPackOptions defaults = new LtMsgPackOptions();
      List<string> differences = new List<string>();
      List<string> unrolledDifferences = new List<string>();
      int compared = 0, withFields = 0;
      foreach (Type type in TestMsgPackContext.Default.Types.Types.ToArray())
      {
        object code = Code(serializer, type);
        if (code is null || type == typeof(GfRegistered)) // not its properties here: PropertyAccessor would keep accessors made before Ls_UsesRegisteredFieldAccessors registers its context
          continue;
        compared++;
        (Type Declaring, string Name)[] generated = Members(code);
        MemberInfo[] runtime = FullPropertyInfo.GetMembers(type, all).Where(Describable).ToArray();
        withFields += runtime.Count(m => m is FieldInfo);
        string expected = string.Join(", ", runtime.Select(m => m.DeclaringType.Name + "." + m.Name));
        string actual = string.Join(", ", generated.Select(m => m.Declaring.Name + "." + m.Name));
        if (expected != actual)
          differences.Add(type.FullName + "\n  runtime:   " + expected + "\n  generated: " + actual);

        // The default static filters (Attributed fields, no non-public properties): what the unrolled writer writes, when the generator predicts it
        string kept = string.Join(", ", FullPropertyInfo.GetStaticallyIncludedProps(type, defaults.Clone()).Select(p => p.Member.DeclaringType.Name + "." + p.Name));
        string unrolled = string.Join(", ", UnrolledIds(code).Select(id => generated[id].Declaring.Name + "." + generated[id].Name));
        if (kept != unrolled && (type.Namespace == typeof(GeneratedFieldsTests).Namespace) && Fields(type))
          unrolledDifferences.Add(type.FullName + "\n  kept:     " + kept + "\n  unrolled: " + unrolled);
      }
      Assert.IsGreaterThan(50, compared);
      Assert.IsGreaterThan(20, withFields, "the test assembly has classes with fields");
      Assert.IsEmpty(differences, string.Join("\n", differences));
      Assert.IsEmpty(unrolledDifferences, string.Join("\n", unrolledDifferences));
    }

    /// <summary>
    /// What the generator can describe at all (others are left to the runtime): instance members it sees (Roslyn imports no private members of other assemblies, internal ones through InternalsVisibleTo)
    /// whose type the generated code can name.
    /// </summary>
    private static bool Describable(MemberInfo member)
    {
      if (member is PropertyInfo property && (property.GetIndexParameters().Length > 0 || property.PropertyType.IsByRef || (property.GetMethod ?? property.SetMethod).IsStatic))
        return false;
      ObsoleteAttribute obsolete = (ObsoleteAttribute)member.GetCustomAttribute(typeof(ObsoleteAttribute));
      if (obsolete != null && obsolete.IsError)
        return false;
      if (member.DeclaringType.Assembly != typeof(GeneratedFieldsTests).Assembly)
      {
        bool isPublic, isInternal;
        if (member is FieldInfo field)
        {
          isPublic = field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;
          isInternal = field.IsAssembly || field.IsFamilyAndAssembly;
        }
        else
        {
          MethodInfo[] accessors = ((PropertyInfo)member).GetAccessors(true);
          isPublic = accessors.Any(a => a.IsPublic || a.IsFamily || a.IsFamilyOrAssembly);
          isInternal = accessors.Any(a => a.IsAssembly || a.IsFamilyAndAssembly);
        }
        if (!isPublic && !(isInternal && GrantsInternals(member.DeclaringType.Assembly)))
          return false;
      }
      return Visible(FullPropertyInfo.TypeOf(member));
    }

    private static bool GrantsInternals(Assembly assembly)
    {
      string name = typeof(GeneratedFieldsTests).Assembly.GetName().Name;
      return assembly == typeof(GeneratedFieldsTests).Assembly || assembly.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>().Any(a => a.AssemblyName.Split(',')[0].Trim() == name);
    }

    private static bool Visible(Type type)
    {
      if (type.IsArray || type.IsByRef || type.IsPointer)
        return Visible(type.GetElementType());
      if (type.IsGenericParameter)
        return true;
      bool visible = type.IsNested
        ? (type.IsNestedPublic || ((type.IsNestedAssembly || type.IsNestedFamORAssem) && GrantsInternals(type.Assembly))) && Visible(type.DeclaringType)
        : type.IsPublic || GrantsInternals(type.Assembly);
      if (!visible)
        return false;
      foreach (Type argument in type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes)
        if (!Visible(argument))
          return false;
      return true;
    }

    /// <summary>
    /// The type or a base class declares a field (the unrolled members of properties are checked by the generator's own tests).
    /// </summary>
    private static bool Fields(Type type)
    {
      for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
        if (t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Any(f => !f.Name.Contains('<')))
          return true;
      return false;
    }

    private static GfItem Item()
    {
      GfItem item = new GfItem() { Name = "n", Amount = 2.5, When = new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc), Kind = GfKind.Other, Maybe = 4, Tags = new List<string>() { "a" }, Opted = new Guid("11111111-2222-3333-4444-555555555555"),
        Skipped = 9, Title = "t", BaseField = 3, BaseProperty = "p" };
      item.Filled.Add(8);
      item.SetSecret(6);
      item.SetBaseSecret("s");
      return item;
    }

    private static GfHolder Holder()
    {
      GfPoint point = new GfPoint() { X = 1.5f, Y = -2, Weight = 3 };
      point.SetTag(7);
      return new GfHolder() { Point = point, Path = new[] { point, default(GfPoint) }, Position = new Vector3(1, 2, 3), Rotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f), Ground = new Plane(0, 1, 0, -4), Velocity = new Vector3(-1, 0, 0.5f) };
    }

    private static string Values(GfItem item)
    {
      // Invariant: string.Join formats with the current culture ("2,5" on a Dutch Windows)
      return string.Join("|", item.Name, item.Amount.ToString(CultureInfo.InvariantCulture), item.When.ToUniversalTime().Ticks, item.Kind, item.Maybe, item.Tags is null ? "-" : string.Join(",", item.Tags), string.Join(",", item.Filled), item.Computed, item.GetSecret(), item.Opted,
        item.Skipped, item.Title, item.BaseField, item.GetBaseSecret(), item.BaseProperty);
    }

    [TestMethod]
    public void Fields_SameBytesAndValuesAsTheRuntime()
    {
      foreach (SerializedFields fields in new[] { SerializedFields.None, SerializedFields.Attributed, SerializedFields.Public, SerializedFields.All })
        foreach (bool schema in new[] { false, true })
          foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
          {
            string what = $"{fields} schema={schema} {layout}";
            LtMsgPackSerializer runtime = Serializer(false, fields, schema, layout);
            LtMsgPackSerializer generated = Serializer(true, fields, schema, layout);
            byte[] expected = runtime.Serialize(Item());
            CollectionAssert.AreEqual(expected, generated.Serialize(Item()), what + ": bytes");
            Assert.AreEqual(Values(runtime.Deserialize<GfItem>(expected)), Values(generated.Deserialize<GfItem>(expected)), what + ": values");

            expected = runtime.Serialize(Holder());
            CollectionAssert.AreEqual(expected, generated.Serialize(Holder()), what + ": struct bytes");
            CollectionAssert.AreEqual(runtime.Serialize(runtime.Deserialize<GfHolder>(expected)), runtime.Serialize(generated.Deserialize<GfHolder>(expected)), what + ": struct values");

            GfHiding hiding = new GfHiding() { Shared = "derived", Other = 5 };
            ((GfHidingBase)hiding).Shared = 1;
            ((GfHidingBase)hiding).Other = "base";
            expected = runtime.Serialize(hiding);
            CollectionAssert.AreEqual(expected, generated.Serialize(hiding), what + ": hiding bytes");
          }
    }

    /// <summary>
    /// The generated code writes and reads the fields itself (leaf kinds), private ones through UnsafeAccessor; readonly fields are written, never set by it.
    /// </summary>
    [TestMethod]
    public void Fields_TheGeneratedCodeIsUsed()
    {
      LtMsgPackSerializer serializer = Serializer(true, SerializedFields.All);
      GfItem read = serializer.Deserialize<GfItem>(serializer.Serialize(Item()));
      Assert.AreEqual("n|2.5|" + Item().When.Ticks + "|Other|4|a|8|5|6|11111111-2222-3333-4444-555555555555|0|t|3|s|p", Values(read), "[NonSerialized] left out, the readonly int skipped, the readonly list filled");

      object inner = Inner(serializer);
      object plan = inner.GetType().GetMethod("Plan", Internal).Invoke(inner, new object[] { typeof(GfItem), false });
      LtMsgPack.Generated.GeneratedWritePlan generated = (LtMsgPack.Generated.GeneratedWritePlan)plan.GetType().GetField("GeneratedPlan", Internal).GetValue(plan);
      Assert.IsNotNull(generated, "the generated writer is used");
      Assert.DoesNotContain(-1, generated.Ids, "every field is a generated member");

      object readPlan = inner.GetType().GetMethod("GetReadPlan", Internal).Invoke(inner, new object[] { typeof(GfItem) });
      foreach (LtMsgPack.Reading.PropReader reader in (LtMsgPack.Reading.PropReader[])readPlan.GetType().GetField("Props", Internal).GetValue(readPlan))
      {
        FullPropertyInfo info = (FullPropertyInfo)typeof(LtMsgPack.Reading.PropReader).GetField("Info", Internal).GetValue(reader);
        bool runtime = info.Name == nameof(GfItem.Tags) || info.Name == nameof(GfItem.Filled) || info.Name == nameof(GfItem.Computed); // a collection, readonly fields
        Assert.AreEqual(runtime, reader.GeneratedId < 0, info.Name);
      }
    }

    /// <summary>
    /// Field writers and readers the runtime creates for a field use the generated accessors (no expression compiled), also for a struct by reference.
    /// </summary>
    [TestMethod]
    public void Fields_TheGeneratedAccessorsAreUsed()
    {
      LtMsgPackSerializer serializer = Serializer(true, SerializedFields.All);
      object inner = Inner(serializer);
      foreach ((Type type, string name) in new[] { (typeof(GfItem), "_secret"), (typeof(GfItem), nameof(GfItem.Name)), (typeof(GfPoint), "_tag"), (typeof(GfPoint), nameof(GfPoint.X)), (typeof(Vector3), nameof(Vector3.X)) })
      {
        FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object generated = inner.GetType().GetMethod("GeneratedProperty", Internal).Invoke(inner, new object[] { field });
        Assert.IsNotNull(generated, type.Name + "." + name);
        Assert.IsNotNull(generated.GetType().GetField("Get", Internal | BindingFlags.Public).GetValue(generated), type.Name + "." + name + ": a getter");
        Assert.IsNotNull(generated.GetType().GetField("Set", Internal | BindingFlags.Public).GetValue(generated), type.Name + "." + name + ": a setter");
      }
      object readOnly = inner.GetType().GetMethod("GeneratedProperty", Internal).Invoke(inner, new object[] { typeof(GfItem).GetField(nameof(GfItem.Computed)) });
      Assert.IsNull(readOnly.GetType().GetField("Set", Internal | BindingFlags.Public).GetValue(readOnly), "never set behind the type's back");

      Assert.IsTrue(TestMsgPackContext.Default.TryGetType(typeof(Vector3), out GeneratedType vector));
      Assert.IsTrue(vector.GetType().GetInterfaces().Any(i => i.Name == "IGeneratedStruct"), "a framework struct written by its fields is described as a struct");
    }

    /// <summary>
    /// Private members (a field, a readonly field, the getter of an auto-property): the generated default check reads them through UnsafeAccessors, so no box under NativeAOT.
    /// </summary>
    public struct GfPrivateDefaults
    {
      [DataMember] private int _count;
      private readonly double _ratio;
      [DataMember] private string Label { get; set; }
      public float Visible;

      public GfPrivateDefaults(int count, double ratio, string label)
      {
        _count = count;
        _ratio = ratio;
        Label = label;
        Visible = 0;
      }
    }

    public class GfPrivateDefaultsHolder
    {
      public GfPrivateDefaults Value { get; set; }
    }

    [TestMethod]
    public void DefaultCheck_PrivateMembers_ThroughUnsafeAccessors()
    {
      LtMsgPackSerializer serializer = Serializer(true, SerializedFields.Attributed);
      object code = Code(serializer, typeof(GfPrivateDefaults));
      LtMsgPack.Generated.GeneratedCapabilities capabilities = (LtMsgPack.Generated.GeneratedCapabilities)code.GetType().GetField("Capabilities", Internal | BindingFlags.Public).GetValue(code);
      Assert.AreNotEqual(LtMsgPack.Generated.GeneratedCapabilities.None, capabilities & LtMsgPack.Generated.GeneratedCapabilities.IsDefault, "a generated default check");
      LtMsgPack.Generated.GeneratedStructCode<GfPrivateDefaults> typed = (LtMsgPack.Generated.GeneratedStructCode<GfPrivateDefaults>)code;
      GfPrivateDefaults[] values = { default(GfPrivateDefaults), new GfPrivateDefaults(1, 0, null), new GfPrivateDefaults(0, -0.0, null), new GfPrivateDefaults(0, double.NaN, null), new GfPrivateDefaults(0, 0, ""),
        new GfPrivateDefaults(0, 0, null) { Visible = 2 } };
      for (int t = 0; t < values.Length; t++)
      {
        GfPrivateDefaults value = values[t];
        Assert.AreEqual(default(GfPrivateDefaults).Equals(value), typed.IsDefault(ref value), "#" + t);
      }

      GfPrivateDefaultsHolder holder = new GfPrivateDefaultsHolder() { Value = new GfPrivateDefaults(3, 0.5, "x") };
      CollectionAssert.AreEqual(Serializer(false, SerializedFields.Attributed).Serialize(holder), serializer.Serialize(holder));
      CollectionAssert.AreEqual(Serializer(false, SerializedFields.Attributed).Serialize(new GfPrivateDefaultsHolder()), serializer.Serialize(new GfPrivateDefaultsHolder()));
    }

    #region Registered for the process (LsMsgPack's accessors)

    public class GfRegistered
    {
      public string Name;
      [DataMember] private int _number;
      public readonly int Fixed = 2;

      public int GetNumber() { return _number; }
      public void SetNumber(int value) { _number = value; }
    }

    [LtMsgPackSerializable(typeof(GfRegistered))]
    public partial class GfRegisteredContext : LtMsgPackContext { }

    [TestMethod]
    public void Ls_UsesRegisteredFieldAccessors()
    {
      GeneratedTypes.Register(GfRegisteredContext.Default); // only GfRegistered: the process keeps it

      foreach (string name in new[] { nameof(GfRegistered.Name), "_number", nameof(GfRegistered.Fixed) })
      {
        PropertyAccessor accessor = PropertyAccessor.Get(typeof(GfRegistered).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        Delegate getter = (Delegate)typeof(PropertyAccessor).GetField("_getter", Internal).GetValue(accessor);
        Assert.IsNotNull(getter, name + ": set at once (not after 100 calls)");
        Delegate typed = (Delegate)getter.Target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0].GetValue(getter.Target);
        Assert.IsTrue(GeneratedCodeTests.IsNestedIn(typed.Method.DeclaringType, typeof(GfRegisteredContext)), name + ": the getter of the generated context");
      }
      Assert.IsNull(typeof(PropertyAccessor).GetField("_setter", Internal).GetValue(PropertyAccessor.Get(typeof(GfRegistered).GetField(nameof(GfRegistered.Fixed)))), "readonly: no setter");

      GfRegistered value = new GfRegistered() { Name = "x" };
      value.SetNumber(5);
      MsgPackSettings settings = new MsgPackSettings() { StaticFilters = Filters(SerializedFields.Public) };
      GfRegistered read = MsgPackSerializer.Deserialize<GfRegistered>(MsgPackSerializer.Serialize(value, settings), settings);
      Assert.AreEqual("x", read.Name);
      Assert.AreEqual(5, read.GetNumber());
    }

    #endregion
  }
}
