using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackMcp
{
  /// <summary>
  /// Without arguments (or with "mcp") an MCP server over stdio, otherwise a command line tool that runs the same tools.
  /// </summary>
  public static class Program
  {
    private const string Usage =
@"lsmsgpack-mcp: MsgPack for AI agents (and people in a terminal).

  lsmsgpack-mcp [mcp]                       Runs the MCP server over stdin/stdout (what MCP clients start).
  lsmsgpack-mcp decode <file|-> [options]   Decodes a file (- reads the bytes from stdin).
  lsmsgpack-mcp decode --data <text>        Decodes bytes written as hex, base64 or byte values.
  lsmsgpack-mcp explain <file> <offset>     What the byte at the offset is (items, object, bytes around it).
  lsmsgpack-mcp search <file> <text>        The items holding the text or the value.
  lsmsgpack-mcp read-as <file> --assembly <dll> --type <class>
                                            Reads the data into a class of a compiled assembly and reports what does not match.
  lsmsgpack-mcp debug status                The IDEs with the MsgPack Explorer extension and their debug sessions.
  lsmsgpack-mcp debug locals                The variables of the paused stack frame.
  lsmsgpack-mcp debug read <expression>     Reads and decodes the bytes of a variable of the paused program.

Options:
  --view objects|items|both   objects: JSON with comments (default), items: MsgPack items with offsets and encodings.
  --path <path>               Only this part of the objects (e.g. Lines[2]).
  --from <offset> --to <offset>  Items view: only this range of bytes.
  --offsets                   Objects view: the offset of every value.
  --max-nodes <n>             The most values or items written (default 1000).
  --max-string <n>            Longer strings are cut (default 200).
  --issues errors|all|none    Validation issues listed (default errors).
  --endian <action>           SwapIfCurrentSystemIsLittleEndian (default), NeverSwap or AlwaysSwap.
  --stop-on-error             Stop at the first breaking error (default: read on, a best guess).
  --schemas <file>            A SchemaStore export (or a schema) for payloads that refer to a cached schema.
  --ide <pid|vscode|visualstudio>  debug: which IDE when several run.
  --schema-store <path>       debug read: the program's SchemaStore (e.g. _options.SchemaStore).
  --save-to <file>            debug read: also write the bytes to a file.
  --max-bytes <n>             debug read: read at most this many bytes.
  --match-case                search: match upper and lower case.
";

    public static int Main(string[] args)
    {
      try
      {
        return MainAsync(args).GetAwaiter().GetResult();
      }
      catch (UsageException ex)
      {
        Console.Error.WriteLine(ex.Message);
        Console.Error.WriteLine("lsmsgpack-mcp --help shows the commands.");
        return 2;
      }
    }

    private static async Task<int> MainAsync(string[] args)
    {
      if (args.Length > 0 && args[0] == ClassReader.WorkerCommand) // the child process of msgpack_read_as_class
        return ClassReader.Worker(args);
      if (args.Length == 0 || args[0] == "mcp")
      {
        await ServeAsync().ConfigureAwait(false);
        return 0;
      }
      switch (args[0])
      {
        case "-h":
        case "--help":
        case "help":
          Console.Out.Write(Usage);
          return 0;
        case "--version":
        case "version":
          Console.Out.WriteLine(McpServer.Version);
          return 0;
      }

      Console.OutputEncoding = new UTF8Encoding(false);
      McpTools tools = new McpTools();
      List<Tool> list = tools.CreateTools();
      List<string> positional = new List<string>();
      JsonObject options = ParseOptions(args, 1, positional);
      string command = args[0];
      if (command == "debug")
      {
        if (positional.Count == 0)
          throw new UsageException("debug status, debug locals or debug read <expression>.");
        command = string.Concat("debug ", positional[0]);
        positional.RemoveAt(0);
      }

      switch (command)
      {
        case "decode":
          if (!options.ContainsKey("data"))
          {
            if (positional.Count != 1)
              throw new UsageException("decode <file>, decode - (bytes from stdin) or decode --data <text>.");
            AddInput(options, positional[0]);
          }
          return await RunAsync(list, "msgpack_decode", options).ConfigureAwait(false);

        case "explain":
        case "search":
          {
            if (positional.Count != 2)
              throw new UsageException(command == "explain" ? "explain <file> <offset>" : "search <file> <text>");
            JsonObject decode = new JsonObject();
            AddInput(decode, positional[0]);
            foreach (string name in new string[] { "endian", "continueOnError", "schemas" })
              if (options.ContainsKey(name))
                decode[name] = options[name].DeepClone();
            ToolResult decoded = await Call(list, "msgpack_decode", decode).ConfigureAwait(false);
            if (decoded.IsError)
              return Print(decoded);
            options["doc"] = "doc1";
            options[command == "explain" ? "offset" : "text"] = positional[1];
            return await RunAsync(list, command == "explain" ? "msgpack_explain_offset" : "msgpack_search", options).ConfigureAwait(false);
          }

        case "read-as":
          if (!options.ContainsKey("data"))
          {
            if (positional.Count != 1)
              throw new UsageException("read-as <file> --assembly <dll> --type <class>, or read-as --data <text> ...");
            AddInput(options, positional[0]);
          }
          return await RunAsync(list, "msgpack_read_as_class", options).ConfigureAwait(false);

        case "debug status":
          return await RunAsync(list, "msgpack_debug_status", options).ConfigureAwait(false);
        case "debug locals":
          return await RunAsync(list, "msgpack_debug_locals", options).ConfigureAwait(false);
        case "debug read":
          if (positional.Count != 1)
            throw new UsageException("debug read <expression>");
          options["expression"] = positional[0];
          return await RunAsync(list, "msgpack_debug_read", options).ConfigureAwait(false);

        default:
          throw new UsageException(string.Concat("Unknown command: ", command));
      }
    }

    private static async Task ServeAsync()
    {
      // stdout only carries protocol messages (UTF-8, no BOM), anything else goes to stderr
      Stream stdout = Console.OpenStandardOutput();
      StreamWriter output = new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = false };
      Console.SetOut(Console.Error);
      StreamReader input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
      McpTools tools = new McpTools();
      McpServer server = new McpServer(tools.CreateTools(), output);
      await server.RunAsync(input).ConfigureAwait(false);
      output.Flush();
    }

    /// <summary>
    /// A file name, or "-" for the bytes on stdin (passed as base64 data).
    /// </summary>
    private static void AddInput(JsonObject options, string input)
    {
      if (input == "-")
      {
        using (MemoryStream bytes = new MemoryStream())
        {
          Console.OpenStandardInput().CopyTo(bytes);
          options["data"] = Convert.ToBase64String(bytes.ToArray());
        }
      }
      else
      {
        options["file"] = input;
      }
    }

    private static async Task<ToolResult> Call(List<Tool> tools, string name, JsonObject options)
    {
      Tool tool = tools.Find(t => t.Name == name);
      try
      {
        return await tool.Handler(options, CancellationToken.None).ConfigureAwait(false);
      }
      catch (Exception ex) when (ex is ToolArgumentException || ex is IdeBridgeException || ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
      {
        return ToolResult.Error(ex.Message);
      }
    }

    private static async Task<int> RunAsync(List<Tool> tools, string name, JsonObject options)
    {
      return Print(await Call(tools, name, options).ConfigureAwait(false));
    }

    private static int Print(ToolResult result)
    {
      if (result.IsError)
      {
        Console.Error.WriteLine(result.Text);
        return 1;
      }
      Console.Out.Write(result.Text);
      if (!result.Text.EndsWith("\n", StringComparison.Ordinal))
        Console.Out.WriteLine();
      return 0;
    }

    /// <summary>
    /// --name value options (as the tools' arguments, kebab-case to camelCase), flags become true, the rest is positional.
    /// </summary>
    internal static JsonObject ParseOptions(string[] args, int start, List<string> positional)
    {
      JsonObject options = new JsonObject();
      for (int t = start; t < args.Length; t++)
      {
        string arg = args[t];
        if (!arg.StartsWith("--", StringComparison.Ordinal) || arg.Length == 2)
        {
          positional.Add(arg);
          continue;
        }

        string name = ToCamelCase(arg.Substring(2));
        switch (name)
        {
          case "offsets":
          case "matchCase":
            options[name] = true;
            continue;
          case "stopOnError":
            options["continueOnError"] = false;
            continue;
        }
        if (t + 1 >= args.Length)
          throw new UsageException(string.Concat(arg, " needs a value."));
        options[name] = args[++t];
      }
      return options;
    }

    private static string ToCamelCase(string kebab)
    {
      StringBuilder sb = new StringBuilder(kebab.Length);
      bool upper = false;
      foreach (char ch in kebab)
      {
        if (ch == '-')
        {
          upper = true;
          continue;
        }
        sb.Append(upper ? char.ToUpperInvariant(ch) : ch);
        upper = false;
      }
      return sb.ToString();
    }

    private sealed class UsageException : Exception
    {
      public UsageException(string message) : base(message) { }
    }
  }
}
