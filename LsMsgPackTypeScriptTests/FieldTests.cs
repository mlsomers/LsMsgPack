using LsMsgPack;
using LsMsgPackTypeScript;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LsMsgPackTypeScriptTests
{
  [TestClass]
  public class FieldTests
  {
    private static string _folder;

    [ClassInitialize]
    public static void Init(TestContext context)
    {
      _folder = Path.Combine(Path.GetTempPath(), "lsmsgpack-ts-fields-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(_folder);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
      Directory.Delete(_folder, true);
    }

    private static string Models(params string[] args)
    {
      string models = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".ts");
      StringWriter output = new StringWriter(), errors = new StringWriter();
      int code = Program.Run(new[] { "--assembly", typeof(TsOrder).Assembly.Location, "--out", models, "--property-names", "dotnet" }.Concat(args).ToArray(), output, errors);
      Assert.AreEqual(0, code, errors.ToString());
      return File.ReadAllText(models);
    }

    private static string ClassOf(string models, string name)
    {
      Match match = Regex.Match(models, @"export (abstract )?class " + name + @"\b[^\n]*\{\n(.*?)\n\}", RegexOptions.Singleline);
      Assert.IsTrue(match.Success, "class " + name);
      return match.Groups[2].Value;
    }

    [TestMethod]
    public void AttributedFields_DefaultMode()
    {
      string cls = ClassOf(Models("--root", "LsMsgPackTypeScriptTests.TsFieldsAttributed"), "TsFieldsAttributed");
      StringAssert.Contains(cls, "  Level: number = 3;", "the initializer of the field");
      StringAssert.Contains(cls, "  _secret: string | null = 's';", "a private [JsonInclude] field, nullable by its annotation");
      StringAssert.Contains(cls, "  Title: string = 't';", "string with annotations: not null");
      StringAssert.Contains(cls, "  Maybe: string | null = null;", "string?");
      Assert.DoesNotContain("Plain", cls, "a public field without an attribute is not serialized by default");
    }

    [TestMethod]
    public void ReadonlyFields_AreInTheModel()
    {
      string cls = ClassOf(Models("--root", "LsMsgPackTypeScriptTests.TsFieldsAttributed"), "TsFieldsAttributed");
      StringAssert.Contains(cls, "  Fixed: number = 5;", "a readonly field is written");
      StringAssert.Contains(cls, "  Computed: number = 10;", "a readonly field the constructor computes");
    }

    [TestMethod]
    public void PublicFields_WithTheOptionsOfTheServer()
    {
      string models = Models("--root", "LsMsgPackTypeScriptTests.TsFieldsPublic", "--options", "LsMsgPackTypeScriptTests.TsFieldsSetup.PublicFields");
      string cls = ClassOf(models, "TsFieldsPublic");
      StringAssert.Contains(cls, "  Count: number = 4;");
      StringAssert.Contains(cls, "  Name: string = 'n';");
      StringAssert.Contains(cls, "  Inner: TsFieldsStruct = new TsFieldsStruct();");
      StringAssert.Contains(cls, "  Address: TsAddress | null = null;");
      StringAssert.Contains(models, "['Count', 'int']");
      StringAssert.Contains(models, "['Inner', ['struct', 'TsFieldsStruct']]");

      string attributed = ClassOf(Models("--root", "LsMsgPackTypeScriptTests.TsFieldsAttributed", "--options", "LsMsgPackTypeScriptTests.TsFieldsSetup.PublicFields"), "TsFieldsAttributed");
      StringAssert.Contains(attributed, "  Plain: number = 9;", "Public: the public fields without attribute too");
    }

    [TestMethod]
    public void PublicFields_NotInTheDefaultMode()
    {
      string models = Models("--root", "LsMsgPackTypeScriptTests.TsFieldsPublic");
      string cls = ClassOf(models, "TsFieldsPublic");
      Assert.DoesNotContain("Count", cls);
      Assert.DoesNotContain("Name", cls);
    }

    [TestMethod]
    public void StructWithFields()
    {
      string models = Models("--root", "LsMsgPackTypeScriptTests.TsFieldsPublic", "--options", "LsMsgPackTypeScriptTests.TsFieldsSetup.PublicFields");
      string cls = ClassOf(models, "TsFieldsStruct");
      StringAssert.Contains(cls, "  A: number = 0;");
      StringAssert.Contains(cls, "  B: number = 0;");
      StringAssert.Contains(models, "[TsFieldsStruct, 'TsFieldsStruct', [['A', 'int'], ['B', 'float']]]");
    }

    [TestMethod]
    public void Vector3Property_HasAModelClass()
    {
      // Vector3 is written by its public fields X, Y, Z: the generator gives it a struct model like a user's struct
      string models = Models("--root", "LsMsgPackTypeScriptTests.TsFieldsVector");
      StringAssert.Contains(ClassOf(models, "TsFieldsVector"), "  Position: Vector3 = new Vector3();");
      string cls = ClassOf(models, "Vector3");
      StringAssert.Contains(cls, "  X: number = 0;");
      StringAssert.Contains(cls, "  Y: number = 0;");
      StringAssert.Contains(cls, "  Z: number = 0;");
      StringAssert.Contains(models, "[TsFieldsVector, 'TsFieldsVector', [['Position', ['struct', 'Vector3']]]]");
      StringAssert.Contains(models, "[Vector3, 'Vector3', [['X', 'float'], ['Y', 'float'], ['Z', 'float']]]");
    }

    [TestMethod]
    public void QuaternionMatrixAndList_HaveModelClasses()
    {
      string models = Models("--root", "LsMsgPackTypeScriptTests.TsFieldsNumerics");
      StringAssert.Contains(ClassOf(models, "Quaternion"), "  W: number = 0;");
      string matrix = ClassOf(models, "Matrix4x4");
      StringAssert.Contains(matrix, "  M11: number = 0;");
      StringAssert.Contains(matrix, "  M44: number = 0;");
      Assert.DoesNotContain("Translation", matrix, "the computed property is not serialized");
      string cls = ClassOf(models, "TsFieldsNumerics");
      StringAssert.Contains(cls, "Rotation: Quaternion");
      StringAssert.Contains(cls, "Transform: Matrix4x4");
      StringAssert.Contains(cls, "Points: Vector3[]");
      StringAssert.Contains(models, "['Points', ['list', ['struct', 'Vector3']]]");
    }

    [TestMethod]
    public void Vector3Field_HasAModelClass()
    {
      string models = Models("--root", "LsMsgPackTypeScriptTests.TsFieldsVectorField");
      Assert.IsFalse(ClassOf(models, "TsFieldsVectorField").Contains("Position"), "a field is not serialized by default");
      string withFields = Models("--root", "LsMsgPackTypeScriptTests.TsFieldsVectorField", "--options", typeof(TsFieldsSetup).FullName + ".PublicFields");
      StringAssert.Contains(ClassOf(withFields, "TsFieldsVectorField"), "  Position: Vector3");
      StringAssert.Contains(ClassOf(withFields, "Vector3"), "  Z: number = 0;");
    }
  }
}
