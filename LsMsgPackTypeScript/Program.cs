using LsMsgPack;
using LtMsgPack;
using LtMsgPack.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LsMsgPackTypeScript
{
  /// <summary>
  /// lsmsgpack-ts: TypeScript classes of the .NET models of a server (with the values their constructors set) and the schemas the server will write, for the TypeScript client of application/x-lsmsgpack.
  /// </summary>
  public static class Program
  {
    private const string Usage =
@"lsmsgpack-ts: TypeScript models and schemas for @lsmsgpack/client, from the compiled .NET classes.

  lsmsgpack-ts --assembly <dll> [--assembly <dll>...] --out <models.ts> [options]

Which classes (at least one of):
  --root <type>          A type the server responds with (e.g. Shop.Invoice[], List<Shop.Invoice>), repeatable.
  --request <type>       A type the server reads (a request body): a model, no schema. Repeatable.
                         [LtMsgPackSerializable(typeof(T))] in the assemblies are roots as well.
  --solution <sln|csproj>  Finds the roots as LsMsgPack's analyzers do: the results and parameters of controller actions,
                         the results and bodies of minimal APIs, the types handed to the serializers. Build it first: the types are taken from the assemblies.
  --all-types            Every public class, struct and enum of the assemblies (models only).

Output:
  --out <file.ts>        The models (classes with their defaults, enums, interfaces, and defineModels).
  --schemas <file.ts>    The schema set (default: next to --out, .schemas.ts). --no-schemas: none.
  --import <module>      Where defineModels comes from (default @lsmsgpack/client).

How the client reads (the types depend on it, the client checks it):
  --int64 number|bigint  long and ulong (default number).
  --dates date|iso       DateTime and DateTimeOffset (default date).
  --decimal number|string  decimal (default number).
  --property-names camelCase|dotnet  The field names: camelCase (default), as System.Text.Json's JsonNamingPolicy.CamelCase
                         writes them (the server reads request bodies ignoring case), or the .NET names.

The server's formatters:
  --options <Type.Member>  A static property, field or method of the assemblies returning the LtMsgPackHttpOptions
                         (or the LtMsgPackOptions of application/x-lsmsgpack) the server uses. Default: the defaults.
";

    public static int Main(string[] args)
    {
      try
      {
        return Run(args, Console.Out, Console.Error);
      }
      catch (UsageException ex)
      {
        Console.Error.WriteLine(ex.Message);
        Console.Error.WriteLine("lsmsgpack-ts --help shows the options.");
        return 2;
      }
    }

    internal static int Run(string[] args, TextWriter output, TextWriter errors)
    {
      if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
      {
        output.Write(Usage);
        return args.Length == 0 ? 2 : 0;
      }
      if (args.Contains("--version"))
      {
        output.WriteLine(GeneratorOptions.Version);
        return 0;
      }

      List<string> assemblies = new List<string>(), roots = new List<string>(), requests = new List<string>();
      string solution = null, outFile = null, schemasFile = null, optionsMember = null;
      bool allTypes = false, noSchemas = false;
      GeneratorOptions options = new GeneratorOptions();
      for (int t = 0; t < args.Length; t++)
      {
        string arg = args[t];
        switch (arg)
        {
          case "--assembly": assemblies.Add(Value(args, ref t)); break;
          case "--root": roots.Add(Value(args, ref t)); break;
          case "--request": requests.Add(Value(args, ref t)); break;
          case "--solution": solution = Value(args, ref t); break;
          case "--all-types": allTypes = true; break;
          case "--out": outFile = Value(args, ref t); break;
          case "--schemas": schemasFile = Value(args, ref t); break;
          case "--no-schemas": noSchemas = true; break;
          case "--import": options.Import = Value(args, ref t); break;
          case "--int64": options.Int64 = Choice(args, ref t, "number", "bigint"); break;
          case "--dates": options.Dates = Choice(args, ref t, "date", "iso"); break;
          case "--decimal": options.Decimal = Choice(args, ref t, "number", "string"); break;
          case "--property-names": options.PropertyNames = Choice(args, ref t, "dotnet", "camelCase"); break;
          case "--options": optionsMember = Value(args, ref t); break;
          default: throw new UsageException(string.Concat("Unknown argument: ", arg));
        }
      }
      if (assemblies.Count == 0)
        throw new UsageException("--assembly is required: the compiled classes (the build output of the server or its models).");
      if (outFile is null)
        throw new UsageException("--out is required: the file of the models.");

      ModelAssemblies loaded = new ModelAssemblies();
      foreach (string assembly in assemblies)
        loaded.Load(assembly);
      if (optionsMember != null)
        options.Http = FindOptions(loaded, optionsMember);

      List<string> warnings = new List<string>();
      List<Type> written = roots.Select(loaded.Find).ToList();
      List<Type> read = requests.Select(loaded.Find).ToList();
      written.AddRange(SerializableRoots(loaded));
      if (solution != null)
      {
        SolutionRoots.Found found = SolutionRoots.Find(solution, warnings);
        written.AddRange(found.Written);
        read.AddRange(found.Read);
      }
      written = written.Distinct().ToList();
      read = read.Distinct().ToList();
      List<Type> all = allTypes ? loaded.Types.Where(t => (t.IsPublic || t.IsNestedPublic) && !t.ContainsGenericParameters && !t.IsInterface && !(t.IsAbstract && t.IsSealed)
        && t.Name.IndexOf('<') < 0 && ModelAssemblies.IsUsers(t.Assembly)).ToList() : new List<Type>();
      if (written.Count == 0 && read.Count == 0 && all.Count == 0)
        throw new UsageException("No classes: pass --root (or --request), --solution or --all-types, or list the roots with [LtMsgPackSerializable(typeof(T))].");

      LtMsgPackOptions xls = new LtMsgPackHttpSerializer(options.Http).For(MsgPackMediaTypes.XLsMsgPack).Options; // as the server's formatters copy them
      ModelSet models = new ModelSet(xls, loaded.Types);
      foreach (Type type in written.Concat(read).Concat(all))
        models.Add(type);

      string source = string.Join(", ", loaded.Assemblies.Select(a => a.GetName().Name));
      Defaults defaults = new Defaults(models, options, warnings);
      string code = new TypeScriptWriter(models, options, defaults).Write(source);
      WriteFile(outFile, code);
      output.WriteLine($"{outFile}: {models.Classes.Count} classes, {models.Enums.Count} enums, {models.Interfaces.Count} interfaces.");

      if (!noSchemas)
      {
        List<SchemaSetWriter.Prepared> schemas = SchemaSetWriter.Prepare(options.Http, written, warnings);
        string target = schemasFile ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outFile)), Path.GetFileNameWithoutExtension(outFile) + ".schemas.ts");
        WriteFile(target, SchemaSetWriter.Write(schemas, source));
        output.WriteLine($"{target}: {schemas.Count} schemas ({string.Join(", ", schemas.Select(s => SchemaSetWriter.TypeName(s.Root)))}).");
      }
      foreach (string warning in warnings.Distinct())
        errors.WriteLine("warning: " + warning);
      return 0;
    }

    private static string Value(string[] args, ref int t)
    {
      if (t + 1 >= args.Length)
        throw new UsageException(string.Concat(args[t], " needs a value."));
      return args[++t];
    }

    private static string Choice(string[] args, ref int t, params string[] choices)
    {
      string name = args[t];
      string value = Value(args, ref t);
      if (!choices.Contains(value))
        throw new UsageException(string.Concat(name, " is one of ", string.Join(", ", choices), "."));
      return value;
    }

    /// <summary>
    /// The types of <c>[LtMsgPackSerializable(typeof(T))]</c> on the classes of the assemblies (the contexts of LtMsgPack's source generator).
    /// </summary>
    private static IEnumerable<Type> SerializableRoots(ModelAssemblies loaded)
    {
      foreach (Type type in loaded.Types)
      {
        IList<CustomAttributeData> attributes;
        try
        {
          attributes = type.GetCustomAttributesData();
        }
        catch (Exception) // an attribute of an assembly that is not here
        {
          continue;
        }
        foreach (CustomAttributeData attribute in attributes)
          if (attribute.AttributeType.FullName == "LtMsgPack.LtMsgPackSerializableAttribute" && attribute.ConstructorArguments.Count > 0 && attribute.ConstructorArguments[0].Value is Type root)
            yield return root;
      }
    }

    private static LtMsgPackHttpOptions FindOptions(ModelAssemblies loaded, string member)
    {
      int dot = member.LastIndexOf('.');
      if (dot <= 0)
        throw new UsageException("--options is Type.Member, e.g. Shop.MsgPackSetup.HttpOptions.");
      Type type = loaded.Find(member.Substring(0, dot));
      string name = member.Substring(dot + 1);
      const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
      object value = type.GetProperty(name, flags)?.GetValue(null) ?? type.GetField(name, flags)?.GetValue(null) ?? type.GetMethod(name, flags, null, Type.EmptyTypes, null)?.Invoke(null, null);
      if (value is LtMsgPackHttpOptions http)
        return http;
      if (value is LtMsgPackOptions xls)
        return new LtMsgPackHttpOptions() { XLsMsgPack = xls };
      throw new UsageException(string.Concat(member, " is not a static property, field or method returning LtMsgPackHttpOptions or LtMsgPackOptions."));
    }

    private static void WriteFile(string path, string content)
    {
      string full = Path.GetFullPath(path);
      Directory.CreateDirectory(Path.GetDirectoryName(full));
      File.WriteAllText(full, content, new UTF8Encoding(false));
    }
  }
}
