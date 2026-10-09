using LsMsgPack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackMcp
{
  /// <summary>
  /// msgpack_read_as_class: reads a payload into a class of the user's compiled assembly with LsMsgPack and reports the differences (<see cref="ReadDifferences"/>).
  /// <para>The assembly is loaded in a child process (this program with <see cref="WorkerCommand"/>): it runs the user's code (static constructors, constructors, setters), may need another runtime
  /// than this one, and LsMsgPack's type name caches are global, so a rebuilt assembly would otherwise meet the types of the previous build.</para>
  /// </summary>
  internal static class ClassReader
  {
    internal const string WorkerCommand = "read-as-worker";

    /// <summary>
    /// The JSON of the object that was read is cut after this many characters.
    /// </summary>
    private const int MaxJson = 6000;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    #region Parent

    /// <returns>The report (the output of the worker)</returns>
    /// <exception cref="ToolArgumentException">The worker could not load the assembly or find the class, or did not finish</exception>
    internal static async Task<string> RunAsync(PayloadDocument doc, string assemblyPath, string typeName, CancellationToken cancellation)
    {
      if (!File.Exists(assemblyPath))
        throw new ToolArgumentException(string.Concat("Assembly not found: ", assemblyPath, ". Pass the compiled .dll of the program or library with the class (e.g. bin/Debug/net8.0/MyApp.dll), build it first."));

      string folder = Path.Combine(Path.GetTempPath(), string.Concat("lsmsgpack-mcp-", Guid.NewGuid().ToString("N")));
      Directory.CreateDirectory(folder);
      try
      {
        string payload = Path.Combine(folder, "payload.bin");
        await File.WriteAllBytesAsync(payload, doc.Data, cancellation).ConfigureAwait(false);

        List<string> args = new List<string>() { typeof(ClassReader).Assembly.Location, WorkerCommand, "--assembly", Path.GetFullPath(assemblyPath), "--type", typeName, "--payload", payload,
          "--indexed", HasSchema(doc) ? "true" : "false", "--endian", doc.Options.Endian.ToString() };
        if (doc.Options.Schemas != null)
        {
          string schemas = Path.Combine(folder, "schemas.bin");
          using (FileStream file = File.Create(schemas))
            doc.Options.Schemas.Export(file);
          args.Add("--schemas");
          args.Add(schemas);
        }

        ProcessStartInfo start = new ProcessStartInfo(DotnetHost())
        {
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true,
          StandardOutputEncoding = new UTF8Encoding(false),
          StandardErrorEncoding = new UTF8Encoding(false),
          WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))
        };
        foreach (string arg in args)
          start.ArgumentList.Add(arg);
        start.Environment["DOTNET_ROLL_FORWARD"] = "LatestMajor"; // the newest runtime installed, so an assembly for a newer .NET than this server loads too

        using (Process process = Process.Start(start))
        using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
          timeout.CancelAfter(Timeout);
          Task<string> output = process.StandardOutput.ReadToEndAsync();
          Task<string> errors = process.StandardError.ReadToEndAsync();
          try
          {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
          }
          catch (OperationCanceledException)
          {
            try
            {
              process.Kill(true);
            }
            catch (Exception) // already gone
            {
            }
            if (cancellation.IsCancellationRequested)
              throw;
            throw new ToolArgumentException(string.Concat("Reading did not finish within ", Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture), " seconds (a constructor or setter of the class that does not return?)."));
          }

          string text = await output.ConfigureAwait(false);
          string error = await errors.ConfigureAwait(false);
          if (process.ExitCode != 0)
            throw new ToolArgumentException(string.IsNullOrWhiteSpace(error) ? string.Concat("The reader stopped with exit code ", process.ExitCode.ToString(CultureInfo.InvariantCulture), ".\n", text) : error.Trim());
          return text;
        }
      }
      finally
      {
        try
        {
          Directory.Delete(folder, true);
        }
        catch (Exception) // a temporary folder
        {
        }
      }
    }

    private static bool HasSchema(PayloadDocument doc)
    {
      return doc.Objects?.Schema != null;
    }

    /// <summary>
    /// The dotnet muxer this server runs with (it is a framework dependent dll, also as a .NET tool), otherwise the one on the PATH.
    /// </summary>
    private static string DotnetHost()
    {
      string host = Environment.ProcessPath;
      if (!string.IsNullOrEmpty(host) && string.Equals(Path.GetFileNameWithoutExtension(host), "dotnet", StringComparison.OrdinalIgnoreCase))
        return host;
      return "dotnet";
    }

    #endregion

    #region Worker (the child process)

    /// <summary>
    /// read-as-worker --assembly &lt;dll&gt; --type &lt;name&gt; --payload &lt;file&gt; --indexed true|false --endian &lt;action&gt; [--schemas &lt;file&gt;]: writes the report to stdout.
    /// </summary>
    /// <returns>0, or 1 when the assembly or class could not be loaded (the reason on stderr)</returns>
    internal static int Worker(string[] args)
    {
      Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.Ordinal);
      for (int t = 1; t + 1 < args.Length; t += 2)
        options[args[t]] = args[t + 1];

      string assemblyPath = options["--assembly"];
      Type type;
      try
      {
        Assembly assembly = Load(assemblyPath);
        type = FindType(assembly, options["--type"]);
      }
      catch (Exception ex) when (ex is ToolArgumentException || ex is FileLoadException || ex is FileNotFoundException || ex is BadImageFormatException || ex is ReflectionTypeLoadException)
      {
        Console.Error.WriteLine(ex.Message);
        return 1;
      }

      byte[] payload = File.ReadAllBytes(options["--payload"]);
      bool indexed = options["--indexed"] == "true";
      MsgPackSettings settings = new MsgPackSettings()
      {
        UseInexedSchema = indexed,
        EndianAction = (EndianAction)Enum.Parse(typeof(EndianAction), options["--endian"]),
        ReadErrors = ReadErrorHandling.ReportAndContinue,
        ContinueProcessingOnBreakingError = false
      };
      if (options.TryGetValue("--schemas", out string schemas))
      {
        settings.SchemaStore = new SchemaStore();
        using (FileStream file = File.OpenRead(schemas))
          settings.SchemaStore.Import(file);
      }

      Console.OutputEncoding = new UTF8Encoding(false);
      Console.Out.Write(Read(payload, type, settings, Path.GetFileName(assemblyPath)));
      return 0;
    }

    internal static string Read(byte[] payload, Type type, MsgPackSettings settings, string assemblyName)
    {
      StringBuilder sb = new StringBuilder();
      sb.Append("Read ").Append(payload.Length.ToString(CultureInfo.InvariantCulture)).Append(" bytes into ").Append(TypeName(type)).Append(" (").Append(assemblyName).Append(") with LsMsgPack: ")
        .Append(settings.UseInexedSchema ? "the indexed schema of the data" : "property names or positions (no schema)")
        .Append(", ReadErrors = ReportAndContinue, other settings default.").Append('\n');

      MsgPackTypes.CacheAssemblyTypes(type.IsArray ? type.GetElementType() : type);
      object result = null;
      ReadDifferences differences;
      string failure = null;
      try
      {
        result = MsgPackSerializer.Deserialize(type, payload, settings, out differences);
        if (result is Exception error) // KEEPTRACK: a breaking error is the value
        {
          failure = string.Concat(error.GetType().Name, ": ", error.Message);
          result = null;
        }
      }
      catch (Exception ex)
      {
        failure = string.Concat(ex.GetType().Name, ": ", ex.Message);
        differences = ex.Data[ReadDifferences.ExceptionDataKey] as ReadDifferences;
      }

      if (failure != null)
        sb.Append('\n').Append("Reading failed: ").Append(failure).Append('\n');
      sb.Append('\n');
      if (differences != null)
        sb.Append(differences.GenerateReport().Replace("\r\n", "\n"));
      else if (failure is null)
        sb.Append("The data matched the classes: no properties the classes do not have, no extra values, no values that could not be read.");
      else
        sb.Append("No differences were found before the error.");
      sb.Append('\n');

      if (result != null)
        sb.Append('\n').Append("The object that was read (System.Text.Json):").Append('\n').Append(Json(result)).Append('\n');

      sb.Append('\n').Append("Not applied: the program's own settings (static filters, property id resolvers, type resolvers, custom extensions, PropertyOrder for arrays without the schema). ")
        .Append("Properties the data does not have keep the value the constructor gives them; they are only reported as \"left at their default\" next to an unknown property.").Append('\n');
      return sb.ToString();
    }

    private static string Json(object value)
    {
      try
      {
        JsonSerializerOptions options = new JsonSerializerOptions() { WriteIndented = true, ReferenceHandler = ReferenceHandler.IgnoreCycles, MaxDepth = 64 };
        options.Converters.Add(new JsonStringEnumConverter());
        string json = JsonSerializer.Serialize(value, value.GetType(), options);
        if (json.Length > MaxJson)
          json = string.Concat(json.Substring(0, MaxJson), "\n... (cut, ", json.Length.ToString(CultureInfo.InvariantCulture), " characters)");
        return json;
      }
      catch (Exception ex)
      {
        return string.Concat("(could not be written as JSON: ", ex.Message, ")");
      }
    }

    private static string TypeName(Type type)
    {
      return type.FullName ?? type.Name;
    }

    /// <summary>
    /// Loads the assembly with its dependencies (deps.json of its build output) and the shared frameworks it names (runtimeconfig.json, e.g. ASP.NET Core).
    /// </summary>
    private static Assembly Load(string assemblyPath)
    {
      string full = Path.GetFullPath(assemblyPath);
      AssemblyDependencyResolver resolver = new AssemblyDependencyResolver(full);
      List<string> frameworkFolders = FrameworkFolders(full);
      string folder = Path.GetDirectoryName(full);
      AssemblyLoadContext.Default.Resolving += (context, name) =>
      {
        string path = resolver.ResolveAssemblyToPath(name);
        if (path is null)
        {
          foreach (string candidate in frameworkFolders.Select(f => Path.Combine(f, name.Name + ".dll")).Concat(new[] { Path.Combine(folder, name.Name + ".dll") }))
          {
            if (File.Exists(candidate))
            {
              path = candidate;
              break;
            }
          }
        }
        return path is null ? null : context.LoadFromAssemblyPath(path);
      };
      return AssemblyLoadContext.Default.LoadFromAssemblyPath(full);
    }

    /// <summary>
    /// The folders of the shared frameworks other than Microsoft.NETCore.App that the assembly's runtimeconfig.json names, of the version this runtime has (or the newest).
    /// </summary>
    private static List<string> FrameworkFolders(string assemblyPath)
    {
      List<string> folders = new List<string>();
      string config = Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");
      if (!File.Exists(config))
        return folders;
      try
      {
        JsonNode root = JsonNode.Parse(File.ReadAllText(config));
        JsonNode options = root?["runtimeOptions"];
        List<string> names = new List<string>();
        if (options?["framework"]?["name"] != null)
          names.Add((string)options["framework"]["name"]);
        if (options?["frameworks"] is JsonArray frameworks)
          names.AddRange(frameworks.Select(f => (string)f?["name"]).Where(n => n != null));

        string runtime = Path.GetDirectoryName(typeof(object).Assembly.Location); // .../shared/Microsoft.NETCore.App/<version>
        string shared = Path.GetDirectoryName(Path.GetDirectoryName(runtime));
        string version = Path.GetFileName(runtime);
        foreach (string name in names.Where(n => !string.Equals(n, "Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase)))
        {
          string framework = Path.Combine(shared, name);
          if (!Directory.Exists(framework))
            continue;
          string same = Path.Combine(framework, version);
          folders.Add(Directory.Exists(same) ? same : Directory.GetDirectories(framework).OrderBy(d => Version.TryParse(Path.GetFileName(d).Split('-')[0], out Version v) ? v : new Version()).Last());
        }
      }
      catch (Exception) // without the shared frameworks: their types fail when they are used
      {
      }
      return folders;
    }

    /// <summary>
    /// The class by its full name, or by the end of it (its name, with a part of the namespace) when only one class has it, in the assembly or the assemblies of its build output it references. A name ending in [] is an array of it.
    /// </summary>
    private static Type FindType(Assembly assembly, string name)
    {
      name = name.Trim();
      if (name.EndsWith("[]", StringComparison.Ordinal))
        return FindType(assembly, name.Substring(0, name.Length - 2)).MakeArrayType();

      Type type = assembly.GetType(name, false);
      if (type != null)
        return type;

      List<Type> all = new List<Type>(Types(assembly));
      foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
      {
        Assembly referenced;
        try
        {
          referenced = AssemblyLoadContext.Default.LoadFromAssemblyName(reference);
        }
        catch (Exception) // not available here
        {
          continue;
        }
        if (!IsFramework(referenced))
          all.AddRange(Types(referenced));
      }

      List<Type> found = all.Where(t => string.Equals(t.FullName, name, StringComparison.Ordinal) || string.Equals(t.FullName?.Replace('+', '.'), name, StringComparison.Ordinal)).ToList();
      if (found.Count == 0) // the end of the full name: a name, or a part of the namespace and the name
        found = all.Where(t => t.FullName != null && t.FullName.Replace('+', '.').EndsWith(string.Concat(".", name), StringComparison.Ordinal)).ToList();
      if (found.Count == 1)
        return found[0];
      if (found.Count > 1)
        throw new ToolArgumentException(string.Concat("Several classes are called ", name, ", pass the full name: ", string.Join(", ", found.Select(TypeName))));

      List<string> similar = all.Where(t => t.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf(t.Name, StringComparison.OrdinalIgnoreCase) >= 0)
        .Select(TypeName).Take(10).ToList();
      throw new ToolArgumentException(string.Concat("No class ", name, " in ", assembly.GetName().Name, " or the assemblies it references from its build output.",
        similar.Count == 0 ? string.Empty : string.Concat(" Similar: ", string.Join(", ", similar), ".")));
    }

    private static IEnumerable<Type> Types(Assembly assembly)
    {
      try
      {
        return assembly.GetTypes();
      }
      catch (ReflectionTypeLoadException ex)
      {
        return ex.Types.Where(t => t != null);
      }
    }

    private static bool IsFramework(Assembly assembly)
    {
      string name = assembly.GetName().Name ?? string.Empty;
      return name.StartsWith("System", StringComparison.Ordinal) || name.StartsWith("Microsoft", StringComparison.Ordinal) || name == "netstandard" || name == "mscorlib"
        || name.StartsWith("LsMsgPack", StringComparison.Ordinal) || name == "LtMsgPack";
    }

    #endregion
  }
}
