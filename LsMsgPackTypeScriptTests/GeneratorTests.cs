using LsMsgPack;
using LsMsgPackTypeScript;
using LtMsgPack.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LsMsgPackTypeScriptTests
{
  [TestClass]
  public class GeneratorTests
  {
    private static string _folder;

    [ClassInitialize]
    public static void Init(TestContext context)
    {
      _folder = Path.Combine(Path.GetTempPath(), "lsmsgpack-ts-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(_folder);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
      Directory.Delete(_folder, true);
    }

    private static string Assembly
    {
      get { return typeof(TsOrder).Assembly.Location; }
    }

    private sealed class Result
    {
      internal string Models;
      internal string Schemas;
      internal string Output;
      internal string Errors;
    }

    private static Result Run(params string[] args)
    {
      string name = Guid.NewGuid().ToString("N");
      string models = Path.Combine(_folder, name + ".ts");
      StringWriter output = new StringWriter(), errors = new StringWriter();
      int code = Program.Run(new[] { "--assembly", Assembly, "--out", models }.Concat(args).ToArray(), output, errors);
      Assert.AreEqual(0, code, errors.ToString());
      string schemas = Path.Combine(_folder, name + ".schemas.ts");
      return new Result() { Models = File.ReadAllText(models), Schemas = File.Exists(schemas) ? File.ReadAllText(schemas) : null, Output = output.ToString(), Errors = errors.ToString() };
    }

    private static string ClassOf(string models, string name)
    {
      Match match = Regex.Match(models, @"export (abstract )?class " + name + @"\b[^\n]*\{\n(.*?)\n\}", RegexOptions.Singleline);
      Assert.IsTrue(match.Success, "class " + name);
      return match.Groups[2].Value;
    }

    [TestMethod]
    public void CamelCaseFieldNames()
    {
      string models = Run("--root", "LsMsgPackTypeScriptTests.TsOrder", "--property-names", "camelCase").Models;
      string order = ClassOf(models, "TsOrder");
      StringAssert.Contains(order, "  retries: number = 3;");
      StringAssert.Contains(order, "  address: TsAddress | null = Object.assign(new TsAddress(), { street: 'Main' });");
      StringAssert.Contains(order, "  pair: { key: string | null; value: number } = { key: null, value: 0 };");
      StringAssert.Contains(models, "  propertyNames: 'camelCase',\n");
      StringAssert.Contains(models, "['Retries', 'int']", "defineModels has the .NET names (the keys the client writes)");
      Assert.IsFalse(Run("--root", "LsMsgPackTypeScriptTests.TsOrder").Models.Contains("propertyNames"), "the default is not written");

      StringWriter output = new StringWriter(), errors = new StringWriter();
      UsageException ex = Assert.ThrowsExactly<UsageException>(() => Program.Run(new[] { "--assembly", Assembly, "--out", Path.Combine(_folder, "clash.ts"), "--root", "LsMsgPackTypeScriptTests.TsClash", "--property-names", "camelCase" }, output, errors));
      StringAssert.Contains(ex.Message, "the same field name id");
    }

    [TestMethod]
    public void FieldsHaveTheConstructorValues()
    {
      string order = ClassOf(Run("--root", "LsMsgPackTypeScriptTests.TsOrder").Models, "TsOrder");
      StringAssert.Contains(order, "  Id: string = '00000000-0000-0000-0000-000000000000';");
      StringAssert.Contains(order, "  Retries: number = 3;");
      StringAssert.Contains(order, "  Big: number = 1152921504606846976;");
      StringAssert.Contains(order, "  Price: number = 1.50;");
      StringAssert.Contains(order, "  Status: TsStatus = TsStatus.Open;");
      StringAssert.Contains(order, "  Flags: TsFlags = 3;", "[Flags]: the number");
      StringAssert.Contains(order, "  Due: Date = new Date(1792497600000);");
      StringAssert.Contains(order, "  Created: Date = new Date(-62135596800000);", "DateTime.UtcNow: the default of the type");
      StringAssert.Contains(order, "  Timeout: string = '01:30:00';");
      StringAssert.Contains(order, "  Letter: string = 'x';");
      StringAssert.Contains(order, "  Note: string | null = 'none';");
      StringAssert.Contains(order, "  Tags: string[] | null = ['a', 'b'];");
      StringAssert.Contains(order, "  Address: TsAddress | null = Object.assign(new TsAddress(), { Street: 'Main' });", "only what differs from the class");
      StringAssert.Contains(order, "  Point: TsPoint = new TsPoint();");
      StringAssert.Contains(order, "  MaybePoint: TsPoint | null = null;");
      StringAssert.Contains(order, "  Animals: TsAnimal[] | null = null;");
      StringAssert.Contains(order, "  Shape: ITsShape | null = null;");
      StringAssert.Contains(order, "  Places: Record<string, TsAddress> | null = null;");
      StringAssert.Contains(order, "  Pair: { Key: string | null; Value: number } = { Key: null, Value: 0 };");
      StringAssert.Contains(order, "  Data: Uint8Array | null = null;");
      StringAssert.Contains(order, "  Anything: unknown = null;");
      StringAssert.Contains(order, "  ReadOnly: number = 7;"); // a computed property is written (GetOnlyProperties.Computed)
      StringAssert.Contains(ClassOf(Run("--root", "LsMsgPackTypeScriptTests.TsOrder").Models, "TsAddress"), "  City: string | null = 'Utrecht';");
    }

    [TestMethod]
    public void OptionsChangeTheTypes()
    {
      string order = ClassOf(Run("--root", "LsMsgPackTypeScriptTests.TsOrder", "--int64", "bigint", "--dates", "iso", "--decimal", "string").Models, "TsOrder");
      StringAssert.Contains(order, "  Big: bigint = 1152921504606846976n;");
      StringAssert.Contains(order, "  Price: string = '1.50';");
      StringAssert.Contains(order, "  Due: string = '2026-10-20T12:00:00.000Z';");
    }

    [TestMethod]
    public void WarnsAboutValuesItCannotRepeat()
    {
      StringAssert.Contains(Run("--root", "LsMsgPackTypeScriptTests.TsOrder").Errors, "TsOrder.Created: the constructor sets another value each time");
    }

    [TestMethod]
    public void HierarchiesAndInterfaces()
    {
      string models = Run("--root", "LsMsgPackTypeScriptTests.TsOrder").Models;
      StringAssert.Contains(models, "export abstract class TsAnimal {\n  declare readonly $type: 'TsDog' | 'TsPuppy';\n  Name: string | null = null;\n}");
      StringAssert.Contains(models, "export class TsDog extends TsAnimal {\n  declare readonly $type: 'TsDog' | 'TsPuppy';\n  Barks: number = 0;\n}");
      StringAssert.Contains(models, "export class TsPuppy extends TsDog {\n  declare readonly $type: 'TsPuppy';\n  override Name: string | null = 'Puppy';\n}");
      StringAssert.Contains(models, "export interface ITsShape {\n  readonly $type: string;\n  readonly Label?: string | null;\n}");
      StringAssert.Contains(models, "export class TsCircle {", "found as an implementation of the interface");
      Assert.IsLessThan(models.IndexOf("class TsDog ", StringComparison.Ordinal), models.IndexOf("class TsAnimal ", StringComparison.Ordinal), "base classes first");
      StringAssert.Contains(models, "export const TsStatus = {\n  Open: 1,\n  Closed: 2,\n} as const;\nexport type TsStatus = (typeof TsStatus)[keyof typeof TsStatus];");
      Assert.DoesNotContain("[TsAnimal,", models, "abstract classes are not created");
    }

    [TestMethod]
    public void TheRegistryDescribesEveryProperty()
    {
      string models = Run("--root", "LsMsgPackTypeScriptTests.TsOrder").Models;
      StringAssert.Contains(models, "import { defineModels } from '@lsmsgpack/client';");
      StringAssert.Contains(models, "    [TsPuppy, 'TsPuppy', [['Barks', 'int'], ['Name', 'string']]],");
      StringAssert.Contains(models, "['Animals', ['list', ['class', 'TsAnimal']]]");
      StringAssert.Contains(models, "['Point', ['struct', 'TsPoint']], ['MaybePoint', ['?', ['struct', 'TsPoint']]]");
      StringAssert.Contains(models, "['Places', ['dict', 'string', ['class', 'TsAddress']]], ['Pair', ['pair', 'string', 'int']]");
      StringAssert.Contains(models, "['Shape', 'any']");
      StringAssert.Contains(models, "['Timeout', 'timespan'], ['Letter', 'char']");
    }

    [TestMethod]
    public void TheSchemasAreTheOnesTheServerWrites()
    {
      Result result = Run("--root", "LsMsgPackTypeScriptTests.TsOrder", "--root", "System.Collections.Generic.List<LsMsgPackTypeScriptTests.TsAddress>");
      string[] ids = Regex.Matches(result.Schemas, @"// ([0-9a-f]{32}),").Select(m => m.Groups[1].Value).ToArray();
      Assert.HasCount(2, ids);

      LtMsgPackHttpSerializer server = new LtMsgPackHttpSerializer();
      MsgPackPayload order = server.Serialize(new TsOrder() { Animals = new System.Collections.Generic.List<TsAnimal>() { new TsDog() } }, typeof(TsOrder), MsgPackMediaTypes.XLsMsgPack, null, true);
      Assert.AreEqual(ids[0], order.SchemaId);
      MsgPackPayload list = server.Serialize(new System.Collections.Generic.List<TsAddress>(), typeof(System.Collections.Generic.List<TsAddress>), MsgPackMediaTypes.XLsMsgPack, null, true);
      Assert.AreEqual(ids[1], list.SchemaId);

      string base64 = Regex.Match(result.Schemas, "'LsMsgPackTypeScriptTests.TsOrder': '([^']+)'").Groups[1].Value;
      Assert.AreEqual(ids[0], new SchemaStore().Register(Convert.FromBase64String(base64)).ToString(), "the bytes have the id");
    }

    [TestMethod]
    public void TheServersOptions()
    {
      Result result = Run("--root", "LsMsgPackTypeScriptTests.TsAddress", "--options", "LsMsgPackTypeScriptTests.TsSetup.Alphabetical");
      StringAssert.Contains(result.Models, "[TsAddress, 'TsAddress', [['City', 'string'], ['Street', 'string']]]");
      string id = Regex.Match(result.Schemas, @"// ([0-9a-f]{32}),").Groups[1].Value;
      Assert.AreEqual(new LtMsgPackHttpSerializer(TsSetup.Alphabetical).PrepareSchema(typeof(TsAddress), true).ToString(), id);
      Assert.AreNotEqual(new LtMsgPackHttpSerializer().PrepareSchema(typeof(TsAddress), true).ToString(), id);
    }

    [TestMethod]
    public void RootsOfASolution()
    {
      string project = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly), "..", "..", "..", "LsMsgPackTypeScriptTests.csproj"));
      Result result = Run("--solution", project);
      StringAssert.Contains(result.Output, "LsMsgPackTypeScriptTests.TsOrder", "LtMsgPackSerializer.Default.Serialize(order) in TsUses");
      StringAssert.Contains(result.Models, "export class TsOrder {");
      StringAssert.Contains(result.Models, "export class TsReply {", "MsgPackResults.MsgPack(new TsReply()) of a minimal API");
      StringAssert.Contains(result.Models, "export class TsRequest {", "MsgPackBody<TsRequest> of a minimal API");
      StringAssert.Contains(result.Schemas, "TsReply", "written: a schema");
    }

    [TestMethod]
    public void Usage()
    {
      StringWriter output = new StringWriter(), errors = new StringWriter();
      Assert.AreEqual(0, Program.Run(new[] { "--help" }, output, errors));
      StringAssert.Contains(output.ToString(), "--solution");
      Assert.ThrowsExactly<UsageException>(() => Program.Run(new[] { "--assembly", Assembly, "--out", Path.Combine(_folder, "none.ts") }, output, errors));
      Assert.ThrowsExactly<UsageException>(() => Program.Run(new[] { "--assembly", Assembly, "--out", Path.Combine(_folder, "x.ts"), "--root", "NoSuchType" }, output, errors));
    }
  }
}
