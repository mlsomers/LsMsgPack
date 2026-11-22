using LsMsgPack;
using LsMsgPackTypeScript;
using LsMsgPack.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
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
    public void CamelCaseFieldNamesByDefault()
    {
      string models = Run("--root", "LsMsgPackTypeScriptTests.TsOrder").Models;
      string order = ClassOf(models, "TsOrder");
      StringAssert.Contains(order, "  retries: number = 3;");
      StringAssert.Contains(order, "  address: TsAddress | null = Object.assign(new TsAddress(), { street: 'Main' });");
      StringAssert.Contains(order, "  pair: { key: string | null; value: number } = { key: null, value: 0 };");
      StringAssert.Contains(models, "  propertyNames: 'camelCase',\n");
      StringAssert.Contains(models, "['Retries', 'int']", "defineModels has the .NET names (the keys the client writes)");
      StringAssert.Contains(models, ", propertyNames: 'camelCase' (the client checks them).\n");
      string dotnet = Run("--root", "LsMsgPackTypeScriptTests.TsOrder", "--property-names", "dotnet").Models;
      StringAssert.Contains(dotnet, "  propertyNames: 'dotnet',\n", "always written: defineModels and released clients read a file without it as 'dotnet'");
      StringAssert.Contains(ClassOf(dotnet, "TsOrder"), "  Retries: number = 3;");

      StringWriter output = new StringWriter(), errors = new StringWriter();
      UsageException ex = Assert.ThrowsExactly<UsageException>(() => Program.Run(new[] { "--assembly", Assembly, "--out", Path.Combine(_folder, "clash.ts"), "--root", "LsMsgPackTypeScriptTests.TsClash" }, output, errors));
      StringAssert.Contains(ex.Message, "the same field name id");
    }

    [TestMethod]
    public void ConvertedTypesAreTheirSurrogates()
    {
      string models = Run("--root", "LsMsgPackTypeScriptTests.TsWallet", "--property-names", "dotnet").Models;
      string wallet = ClassOf(models, "TsWallet");
      StringAssert.Contains(wallet, "  Balance: string | null = '5 EUR';");
      StringAssert.Contains(wallet, "  History: ");
      StringAssert.Contains(models, "['Balance', 'string']");
      StringAssert.Contains(models, "['History', ['list', 'string']]");
      Assert.DoesNotContain("class TsMoney", models, "a converted class has no model");
      StringAssert.Contains(wallet, "  Opened: number = 1767225600;", "a member's own converter: the surrogate's type and value");
      StringAssert.Contains(models, "['Opened', 'long']");
      StringAssert.Contains(models, "['Closed', ['?', 'long']]");
    }

    [TestMethod]
    public void FSharpTypesAsTheServerWritesThem()
    {
      // docs/FSharp.md: options are their values, a union's fieldless cases their names, its cases with fields classes with the case name as the type id
      string fsharp = Path.Combine(Path.GetDirectoryName(Assembly), "LsMsgPackFSharpTestTypes.dll"); // its build output has FSharp.Core (an F# library's own bin does not: use the app's)
      string models = Run("--assembly", fsharp, "--root", "LsMsgPackFSharpTestTypes.FsHolder", "--root", "LsMsgPackFSharpTestTypes.FsWithOutcome", "--root", "LsMsgPackFSharpTestTypes.FsPerson").Models;
      string holder = ClassOf(models, "FsHolder");
      StringAssert.Contains(holder, "  shapes: (FsShape | 'Empty')[] | null = null;");
      StringAssert.Contains(holder, "  colour: 'Red' | 'Green' | 'Blue' | null = null;", "a union of fieldless cases: its names");
      StringAssert.Contains(holder, "  mail: string | null = null;", "one case with one field: the field");
      StringAssert.Contains(holder, "  maybe: FsShape | 'Empty' | null = null;", "an option: its value");
      StringAssert.Contains(holder, "  many: ('A' | 'B' | 'C' | 'D' | 'F' | E)[] | null = null;", "the union's own values are names, E has a class");
      StringAssert.Contains(holder, "  tags: string[] | null = null;");
      StringAssert.Contains(holder, "  scores: Record<string, number> | null = null;");
      StringAssert.Contains(ClassOf(models, "FsShape"), "declare readonly $type: 'Circle' | 'Rect';");
      StringAssert.Contains(ClassOf(models, "Circle"), "  radius: number = 0;");
      Assert.DoesNotContain("Tag", ClassOf(models, "Rect"), "only the fields");
      StringAssert.Contains(ClassOf(models, "Value_String"), "declare readonly $type: 'Value';", "a case of a generic union: the case name");
      StringAssert.Contains(ClassOf(models, "Choice1Of2_Int32_String"), "declare readonly $type: 'Choice1Of2';");
      string outcome = ClassOf(models, "FsWithOutcome");
      StringAssert.Contains(outcome, "  answer: FsAnswer | 'Later' | null = null;");
      StringAssert.Contains(outcome, "  structColour: 'Up' | 'Down' = 'Up';", "a struct union of fieldless cases: never null");
      StringAssert.Contains(outcome, "  nested: number | null = null;", "an option of an option");
      string person = ClassOf(models, "FsPerson");
      StringAssert.Contains(person, "  nick: string | null = null;");
      StringAssert.Contains(person, "  age: number | null = null;", "a value option");
      StringAssert.Contains(models, "['Nick', 'string']");
      StringAssert.Contains(models, "[Circle, 'Circle', [['radius', 'float']]]");
    }

    [TestMethod]
    public void EnumsByNameAreTheirNames()
    {
      string models = Run("--root", "LsMsgPackTypeScriptTests.TsShirt", "--property-names", "dotnet").Models;
      StringAssert.Contains(models, "export const TsSize = {\n  Small: 'Small',\n  Large: 'large',\n} as const;\n", "the const object has the written names");
      StringAssert.Contains(models, "export type TsSides = string; // [Flags]");
      StringAssert.Contains(models, "  Open: 1,\n", "an enum by number keeps its numbers");
      string shirt = ClassOf(models, "TsShirt");
      StringAssert.Contains(shirt, "  Size: TsSize = TsSize.Large;");
      StringAssert.Contains(shirt, "  MaybeSize: TsSize | null = null;");
      StringAssert.Contains(shirt, "  Status: 'Open' | 'Closed' = 'Closed';", "a member by name of an enum by number: the union of the names");
      StringAssert.Contains(shirt, "  SizeNumber: number = 1;", "a member by number of an enum by name");
      StringAssert.Contains(shirt, "  Plain: TsStatus = TsStatus.Open;");
      StringAssert.Contains(shirt, "  Sizes: TsSize[] | null = null;");
      StringAssert.Contains(shirt, "  Sides: TsSides = 'Left, Right';");
      StringAssert.Contains(models, "['Size', 'string']");
      StringAssert.Contains(models, "['MaybeSize', ['?', 'string']]");
      StringAssert.Contains(models, "['Status', 'string']");
      StringAssert.Contains(models, "['SizeNumber', 'int']");
      StringAssert.Contains(models, "['Plain', 'int']");

      string names = Run("--root", "LsMsgPackTypeScriptTests.TsShirt", "--property-names", "dotnet", "--options", "LsMsgPackTypeScriptTests.TsSetup.WithEnumNames").Models;
      StringAssert.Contains(names, "  Open: 'Open',\n", "the server's options write every enum by name");
      StringAssert.Contains(ClassOf(names, "TsShirt"), "  Plain: TsStatus = TsStatus.Open;");
      StringAssert.Contains(names, "['Plain', 'string']");
    }

    [TestMethod]
    public void NamingPoliciesOfTheOptions()
    {
      // The models list the names the server writes (PropertyNamingPolicy), the fields are named from them; enums by the names EnumNamingPolicy gives
      foreach (string fields in new[] { "camelCase", "dotnet" })
      {
        string models = Run("--root", "LsMsgPackTypeScriptTests.TsShirt", "--property-names", fields, "--options", "LsMsgPackTypeScriptTests.TsSetup.SnakeCase").Models;
        string shirt = ClassOf(models, "TsShirt");
        StringAssert.Contains(models, "['maybe_size', ['?', 'string']]", fields);
        StringAssert.Contains(models, "['size_number', 'int']", fields);
        StringAssert.Contains(shirt, "  size_number: number", fields);
        Assert.IsFalse(models.Contains("SizeNumber", StringComparison.Ordinal), fields);
        StringAssert.Contains(models, "  Open: 'open',\n", fields);
        StringAssert.Contains(models, "  Small: 'small',\n", fields);
        StringAssert.Contains(models, "  Large: 'large',\n", "a custom name: " + fields);
      }
    }

    [TestMethod]
    public void ConvertersOfTheOptionsAreTheirSurrogates()
    {
      Result result = Run("--root", "LsMsgPackTypeScriptTests.TsDiscount", "--property-names", "dotnet", "--options", "LsMsgPackTypeScriptTests.TsSetup.WithConverters");
      string discount = ClassOf(result.Models, "TsDiscount");
      StringAssert.Contains(discount, "  Rate: number | null = 12.5;");
      StringAssert.Contains(result.Models, "['Rate', 'float']");
      StringAssert.Contains(result.Models, "['Tiers', ['list', 'float']]");
      Assert.DoesNotContain("class TsPercentage", result.Models, "a converted class has no model");
    }

    [TestMethod]
    public void FieldsHaveTheConstructorValues()
    {
      string order = ClassOf(Run("--root", "LsMsgPackTypeScriptTests.TsOrder", "--property-names", "dotnet").Models, "TsOrder");
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
      StringAssert.Contains(ClassOf(Run("--root", "LsMsgPackTypeScriptTests.TsOrder", "--property-names", "dotnet").Models, "TsAddress"), "  City: string | null = 'Utrecht';");
    }

    [TestMethod]
    public void OptionsChangeTheTypes()
    {
      string order = ClassOf(Run("--root", "LsMsgPackTypeScriptTests.TsOrder", "--int64", "bigint", "--dates", "iso", "--decimal", "string", "--property-names", "dotnet").Models, "TsOrder");
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
      string models = Run("--root", "LsMsgPackTypeScriptTests.TsOrder", "--property-names", "dotnet").Models;
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

      LsMsgPackHttpSerializer server = new LsMsgPackHttpSerializer();
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
      Assert.AreEqual(new LsMsgPackHttpSerializer(TsSetup.Alphabetical).PrepareSchema(typeof(TsAddress), true).ToString(), id);
      Assert.AreNotEqual(new LsMsgPackHttpSerializer().PrepareSchema(typeof(TsAddress), true).ToString(), id);
    }

    [TestMethod]
    public void RootsOfASolution()
    {
      string project = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly), "..", "..", "..", "LsMsgPackTypeScriptTests.csproj"));
      Result result = Run("--solution", project);
      StringAssert.Contains(result.Output, "LsMsgPackTypeScriptTests.TsOrder", "LsMsgPackSerializer.Default.Serialize(order) in TsUses");
      StringAssert.Contains(result.Models, "export class TsOrder {");
      StringAssert.Contains(result.Models, "export class TsReply {", "MsgPackResults.MsgPack(new TsReply()) of a minimal API");
      StringAssert.Contains(result.Models, "export class TsRequest {", "MsgPackBody<TsRequest> of a minimal API");
      StringAssert.Contains(result.Schemas, "TsReply", "written: a schema");
      StringAssert.Contains(result.Schemas, "System.Collections.Generic.List<LsMsgPackTypeScriptTests.TsLine>", "IAsyncEnumerable<TsLine>: the formatters write a List<TsLine>");
      StringAssert.Contains(result.Schemas, new LsMsgPackHttpSerializer().PrepareSchema(typeof(List<TsLine>), true).ToString());
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
