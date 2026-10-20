using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Nodes;

namespace LsMsgPackTypeScript
{
  /// <summary>
  /// The compiled assemblies of the user (the model classes, or the web application that has them), loaded with their dependencies (deps.json of the build output)
  /// and the shared frameworks they name (runtimeconfig.json, e.g. ASP.NET Core), as lsmsgpack-mcp's read-as does. LsMsgPack's own assemblies are this tool's (one identity, so its attributes and options are the user's).
  /// </summary>
  internal sealed class ModelAssemblies
  {
    private readonly List<Assembly> _assemblies = new List<Assembly>();
    private List<Type> _types;

    internal IReadOnlyList<Assembly> Assemblies { get { return _assemblies; } }

    internal void Load(string assemblyPath)
    {
      string full = Path.GetFullPath(assemblyPath);
      if (!File.Exists(full))
        throw new UsageException(string.Concat("The assembly ", full, " does not exist (build the project first)."));
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
      _assemblies.Add(AssemblyLoadContext.Default.LoadFromAssemblyPath(full));
      _types = null;
    }

    /// <summary>
    /// The types of the loaded assemblies and the assemblies of their build output they reference (not the framework's nor LsMsgPack's).
    /// </summary>
    internal List<Type> Types
    {
      get
      {
        if (_types != null)
          return _types;
        List<Type> all = new List<Type>();
        HashSet<Assembly> seen = new HashSet<Assembly>();
        foreach (Assembly assembly in _assemblies)
        {
          if (seen.Add(assembly))
            all.AddRange(TypesOf(assembly));
          foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
          {
            Assembly referenced = TryLoad(reference);
            if (referenced != null && IsUsers(referenced) && seen.Add(referenced))
              all.AddRange(TypesOf(referenced));
          }
        }
        return _types = all;
      }
    }

    internal static Assembly TryLoad(AssemblyName name)
    {
      try
      {
        return AssemblyLoadContext.Default.LoadFromAssemblyName(name);
      }
      catch (Exception) // not available here
      {
        return null;
      }
    }

    internal static Type[] TypesOf(Assembly assembly)
    {
      try
      {
        return assembly.GetTypes();
      }
      catch (ReflectionTypeLoadException ex) // e.g. controllers of a framework that is not here: the models still count
      {
        return ex.Types.Where(t => t != null).ToArray();
      }
    }

    /// <summary>
    /// Not the framework's and not LsMsgPack's.
    /// </summary>
    internal static bool IsUsers(Assembly assembly)
    {
      if (assembly.IsDynamic || assembly == typeof(ModelAssemblies).Assembly || assembly == typeof(LsMsgPack.MsgPackOptions).Assembly || assembly == typeof(LtMsgPack.LtMsgPackSerializer).Assembly)
        return false;
      string name = assembly.GetName().Name ?? string.Empty;
      return !(name.StartsWith("System", StringComparison.Ordinal) || name.StartsWith("Microsoft", StringComparison.Ordinal) || name == "netstandard" || name == "mscorlib");
    }

    /// <summary>
    /// A type by its full name (C# notation for generic arguments and arrays: <c>Shop.Page&lt;Shop.Invoice&gt;</c>, <c>Shop.Invoice[]</c>, <c>List&lt;Shop.Invoice&gt;</c>),
    /// or by the end of it when only one type has it. Framework types by their full name (<c>System.Collections.Generic.List</c>) or the usual short names of collections.
    /// </summary>
    internal Type Find(string name)
    {
      name = name.Trim();
      if (name.EndsWith("[]", StringComparison.Ordinal))
        return Find(name.Substring(0, name.Length - 2)).MakeArrayType();

      int open = name.IndexOf('<');
      if (open > 0 && name.EndsWith(">", StringComparison.Ordinal))
      {
        List<string> args = SplitArguments(name.Substring(open + 1, name.Length - open - 2));
        Type definition = FindDefinition(name.Substring(0, open), args.Count);
        return definition.MakeGenericType(args.Select(Find).ToArray());
      }
      return FindNamed(name, 0);
    }

    private static readonly Dictionary<string, Type> Shorthands = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
      { "string", typeof(string) }, { "int", typeof(int) }, { "long", typeof(long) }, { "bool", typeof(bool) }, { "double", typeof(double) }, { "decimal", typeof(decimal) }, { "object", typeof(object) },
      { "Guid", typeof(Guid) }, { "DateTime", typeof(DateTime) },
      { "List", typeof(List<>) }, { "IEnumerable", typeof(IEnumerable<>) }, { "IList", typeof(IList<>) }, { "ICollection", typeof(ICollection<>) }, { "IReadOnlyList", typeof(IReadOnlyList<>) },
      { "IReadOnlyCollection", typeof(IReadOnlyCollection<>) }, { "Dictionary", typeof(Dictionary<,>) }, { "IDictionary", typeof(IDictionary<,>) }, { "IReadOnlyDictionary", typeof(IReadOnlyDictionary<,>) },
      { "HashSet", typeof(HashSet<>) },
    };

    private Type FindDefinition(string name, int arity)
    {
      if (Shorthands.TryGetValue(name, out Type shorthand) && shorthand.IsGenericTypeDefinition && shorthand.GetGenericArguments().Length == arity)
        return shorthand;
      return FindNamed(name, arity);
    }

    private Type FindNamed(string name, int arity)
    {
      if (arity == 0 && Shorthands.TryGetValue(name, out Type shorthand) && !shorthand.IsGenericTypeDefinition)
        return shorthand;
      string metadataName = arity == 0 ? name : string.Concat(name, "`", arity.ToString(System.Globalization.CultureInfo.InvariantCulture));
      Type framework = Type.GetType(metadataName, false);
      if (framework != null)
        return framework;

      List<Type> found = Types.Where(t => string.Equals(t.FullName, metadataName, StringComparison.Ordinal) || string.Equals(t.FullName?.Replace('+', '.'), metadataName, StringComparison.Ordinal)).ToList();
      if (found.Count == 0) // the end of the full name: a name, or a part of the namespace and the name
        found = Types.Where(t => t.FullName != null && t.FullName.Replace('+', '.').EndsWith(string.Concat(".", metadataName), StringComparison.Ordinal)).ToList();
      if (found.Count == 1)
        return found[0];
      if (found.Count > 1)
        throw new UsageException(string.Concat("Several types are called ", name, ", use the full name: ", string.Join(", ", found.Select(t => t.FullName))));
      throw new UsageException(string.Concat("No type ", name, " in ", string.Join(", ", _assemblies.Select(a => a.GetName().Name)), " or the assemblies of its build output."));
    }

    private static List<string> SplitArguments(string text)
    {
      List<string> args = new List<string>();
      int depth = 0, start = 0;
      for (int t = 0; t < text.Length; t++)
      {
        char c = text[t];
        if (c == '<')
          depth++;
        else if (c == '>')
          depth--;
        else if (c == ',' && depth == 0)
        {
          args.Add(text.Substring(start, t - start).Trim());
          start = t + 1;
        }
      }
      args.Add(text.Substring(start).Trim());
      return args;
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
  }

  /// <summary>
  /// A mistake in the arguments: the message is shown with the usage.
  /// </summary>
  internal sealed class UsageException : Exception
  {
    internal UsageException(string message) : base(message) { }
  }
}
