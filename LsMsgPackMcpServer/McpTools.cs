using LsMsgPack;
using ObjectDebugger;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackMcp
{
  /// <summary>
  /// The decoded payloads of this server, so follow-up calls can refer to them by id. The oldest are dropped.
  /// </summary>
  public sealed class DocumentStore
  {
    private const int Max = 20;
    private readonly List<PayloadDocument> _documents = new List<PayloadDocument>();
    private int _next = 1;

    public PayloadDocument Add(PayloadDocument doc)
    {
      lock (_documents)
      {
        doc.Id = string.Concat("doc", (_next++).ToString(CultureInfo.InvariantCulture));
        _documents.Add(doc);
        if (_documents.Count > Max)
          _documents.RemoveAt(0);
      }
      return doc;
    }

    /// <summary>
    /// Replaces a document by one read again with other settings, under the same id.
    /// </summary>
    public void Replace(PayloadDocument old, PayloadDocument doc)
    {
      lock (_documents)
      {
        doc.Id = old.Id;
        int index = _documents.IndexOf(old);
        if (index >= 0)
          _documents[index] = doc;
        else
          _documents.Add(doc);
      }
    }

    /// <exception cref="ToolArgumentException">Unknown id</exception>
    public PayloadDocument Get(string id)
    {
      lock (_documents)
      {
        PayloadDocument doc = _documents.Find(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        if (doc is null)
          throw new ToolArgumentException(string.Concat("Unknown document \"", id, "\". Known: ", _documents.Count == 0 ? "none" : string.Join(", ", _documents.ConvertAll(d => d.Id)),
            ". Decode the data again (msgpack_decode or msgpack_debug_read)."));
        return doc;
      }
    }
  }

  /// <summary>
  /// The tools of the MCP server.
  /// </summary>
  public sealed class McpTools
  {
    /// <summary>
    /// The most bytes a file or the debugger delivers by default (the rendering is limited anyway, the decoding needs the whole payload).
    /// </summary>
    private const int DefaultMaxBytes = 16 * 1024 * 1024;

    private static readonly Regex LikelyBytes = new Regex(
      @"byte\[\]|\bStream\b|MemoryStream|FileStream|NetworkStream|Memory<byte>|List<byte>|IEnumerable<byte>|ArraySegment<byte>|ReadOnlySequence<byte>|ImmutableArray<byte>|HttpContent|ByteArrayContent|HttpResponseMessage|Uint8Array|ArrayBuffer|\bBuffer\b|DataView|^bytes$|^bytearray$|memoryview|BytesIO",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly DocumentStore _documents = new DocumentStore();

    /// <summary>
    /// Where relative file names are looked up (the agent's working directory, as the client started this server there).
    /// </summary>
    public string CurrentDirectory { get; set; } = Directory.GetCurrentDirectory();

    /// <summary>
    /// Finds the IDEs (tests replace it).
    /// </summary>
    public Func<List<IdeConnection>> DiscoverIdes { get; set; } = IdeBridge.Discover;

    public List<Tool> CreateTools()
    {
      return new List<Tool>()
      {
        new Tool()
        {
          Name = "msgpack_debug_status",
          Title = "MsgPack: debugger status",
          Description = "Lists the IDEs (VS Code, Visual Studio) with the MsgPack Explorer extension, their debug sessions and where the program is paused. Call it first when the bytes are in a program being debugged.",
          InputSchema = Schema(Prop("ide", "string", "Which IDE when several run: its pid, \"vscode\" or \"visualstudio\". Optional.")),
          Handler = DebugStatusAsync
        },
        new Tool()
        {
          Name = "msgpack_debug_locals",
          Title = "MsgPack: variables of the paused frame",
          Description = "The variables of the stack frame selected in the debugger (locals, arguments, this), with their types. Variables that can hold MsgPack bytes (byte[], Stream, Memory<byte>, List<byte>, HttpContent, Uint8Array, bytes...) are marked with *.",
          InputSchema = Schema(
            Prop("ide", "string", "Which IDE when several run: its pid, \"vscode\" or \"visualstudio\". Optional."),
            Prop("all", "boolean", "Also the variables that cannot hold bytes (default: true, marked ones first).")),
          Handler = DebugLocalsAsync
        },
        new Tool()
        {
          Name = "msgpack_debug_read",
          Title = "MsgPack: read bytes from the debugger",
          Description = "Reads the bytes of an expression in the paused program (byte[], MemoryStream and other streams (seekable ones are put back at their position), Memory<byte>, List<byte>, ArraySegment<byte>, HttpContent, " +
            "a base64 or hex string; JavaScript Uint8Array/Buffer; Python bytes/BytesIO) and decodes them as msgpack_decode does. The result gets a document id for follow-up calls. " +
            "By default the IDE only accepts variable and member paths (e.g. buffer, this._payload, response.Content, items[2].Data): no method calls.",
          ReadOnly = false, // the IDE evaluates the expression in the program (a stream that cannot seek is consumed, after the user agrees)
          InputSchema = Schema(new JsonObject[] {
            Prop("expression", "string", "The variable or member path holding the bytes, evaluated in the selected stack frame."),
            Prop("ide", "string", "Which IDE when several run: its pid, \"vscode\" or \"visualstudio\". Optional."),
            Prop("maxBytes", "integer", "Read at most this many bytes (default 16 MiB)."),
            Prop("schemaStore", "string", "A path to the LsMsgPack SchemaStore of the program (e.g. _options.SchemaStore): read when the payload refers to a cached schema (WriteSchemaReference) so the property names are known."),
            Prop("saveTo", "string", "Also write the bytes to this file (e.g. to keep them as a test fixture).")
          }.Concat(RenderProps()), "expression"),
          Handler = DebugReadAsync
        },
        new Tool()
        {
          Name = "msgpack_decode",
          Title = "MsgPack: decode",
          Description = "Decodes MsgPack data into JSON-like text with comments for what JSON does not tell (types from type ids or the LsMsgPack indexed schema, timestamps, decimals, bin, extensions, errors with offsets), " +
            "or into the MsgPack items with their offsets and encodings (view \"items\"). Give data (hex, base64, a list of byte values), a file, or the id of a document decoded before (to see another view or part).",
          InputSchema = Schema(new JsonObject[] {
            Prop("data", "string", "The bytes as text: hex (\"82 a4 4e 61...\", \"0x82a4...\"), base64, decimal byte values (\"130, 164, ...\") or a Python bytes literal."),
            Prop("file", "string", "A file holding the bytes (relative to the working directory of the server)."),
            Prop("doc", "string", "The id of a document decoded before (e.g. \"doc1\")."),
            Prop("schemas", "string", "Schemas the data may refer to (LsMsgPack WriteSchemaReference): a file, or bytes as text, holding a SchemaStore export (SchemaStore.Export) or one schema.")
          }.Concat(RenderProps())),
          Handler = DecodeAsync
        },
        new Tool()
        {
          Name = "msgpack_explain_offset",
          Title = "MsgPack: explain an offset",
          Description = "What the byte at an offset of a decoded document is: the items holding it (outermost first), the object member it belongs to, the header and content bytes of its item, and the bytes around it. Use it at the offset of an error.",
          InputSchema = Schema(new JsonObject[] {
            Prop("doc", "string", "The id of a decoded document (e.g. \"doc1\")."),
            Prop("offset", "string", "The offset: a number, or hex with 0x (\"0x1F\").")
          }, "doc", "offset"),
          Handler = ExplainAsync
        },
        new Tool()
        {
          Name = "msgpack_search",
          Title = "MsgPack: search",
          Description = "Finds the items of a decoded document that hold a text (strings containing it) or a value it converts to (numbers, booleans, timestamps, Guids), with their offsets and object paths.",
          InputSchema = Schema(new JsonObject[] {
            Prop("doc", "string", "The id of a decoded document (e.g. \"doc1\")."),
            Prop("text", "string", "The text or value to find."),
            Prop("matchCase", "boolean", "Match upper and lower case (default false).")
          }, "doc", "text"),
          Handler = SearchAsync
        }
      };
    }

    #region Debugger

    private Task<IdeConnection> PickIdeAsync(JsonObject args)
    {
      return Task.FromResult(IdeBridge.Pick(DiscoverIdes(), GetString(args, "ide"), CurrentDirectory));
    }

    private async Task<ToolResult> DebugStatusAsync(JsonObject args, CancellationToken cancellation)
    {
      List<IdeConnection> connections = DiscoverIdes();
      string wanted = GetString(args, "ide");
      if (connections.Count == 0 || connections.Count > 1 && string.IsNullOrWhiteSpace(wanted))
      {
        if (connections.Count == 0)
          IdeBridge.Pick(connections, wanted, CurrentDirectory); // throws the explanation

        // Several: the status of each
        StringBuilder all = new StringBuilder();
        all.Append(connections.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" IDEs are running (pass ide to choose one in the other tools; without it the one whose workspace holds the working directory is used):");
        foreach (IdeConnection connection in connections)
          all.AppendLine(await StatusTextAsync(connection, cancellation).ConfigureAwait(false));
        return ToolResult.Ok(all.ToString());
      }
      IdeConnection ide = IdeBridge.Pick(connections, wanted, CurrentDirectory);
      return ToolResult.Ok(await StatusTextAsync(ide, cancellation).ConfigureAwait(false));
    }

    private static async Task<string> StatusTextAsync(IdeConnection ide, CancellationToken cancellation)
    {
      StringBuilder sb = new StringBuilder();
      JsonNode status;
      try
      {
        status = await IdeBridge.RequestAsync(ide, "status", null, TimeSpan.FromSeconds(10), cancellation).ConfigureAwait(false);
      }
      catch (IdeBridgeException ex)
      {
        return string.Concat("- ", ide.ToString(), ": ", ex.Message);
      }

      string name = (string)status?["name"] ?? ide.Name ?? ide.Ide;
      sb.Append("- ").Append(name);
      if (ide.Pid > 0)
        sb.Append(" (pid ").Append(ide.Pid.ToString(CultureInfo.InvariantCulture)).Append(')');
      List<string> folders = Strings(status?["workspaceFolders"]);
      if (folders.Count == 0)
        folders = ide.WorkspaceFolders;
      if (folders.Count > 0)
        sb.Append(", workspace ").Append(string.Join(", ", folders));
      sb.AppendLine();

      JsonArray sessions = status?["sessions"] as JsonArray;
      if (sessions is null || sessions.Count == 0)
      {
        sb.AppendLine("  Not debugging.");
      }
      else
      {
        foreach (JsonNode session in sessions)
        {
          sb.Append("  Debug session \"").Append((string)session?["name"]).Append("\" (").Append((string)session?["type"]).Append(')');
          if ((bool?)session?["active"] == true)
            sb.Append(", active");
          sb.AppendLine();
        }
      }
      JsonNode frame = status?["frame"];
      if (frame != null)
        sb.Append("  Paused in ").AppendLine(DescribeFrame(frame));
      else if (sessions != null && sessions.Count > 0)
        sb.AppendLine("  No stack frame is selected: the program is running, or no thread is paused. Pause it (a breakpoint) before reading variables.");
      string expressions = (string)status?["expressions"];
      if (expressions == "paths")
        sb.AppendLine("  Expressions: variable and member paths only (the IDE's setting allows no method calls).");
      else if (expressions == "any")
        sb.AppendLine("  Expressions: any (the IDE's setting allows them).");
      return sb.ToString().TrimEnd();
    }

    private static string DescribeFrame(JsonNode frame)
    {
      StringBuilder sb = new StringBuilder((string)frame["name"] ?? "(unknown frame)");
      string source = (string)frame["source"];
      if (!string.IsNullOrEmpty(source))
      {
        sb.Append(" at ").Append(source);
        int? line = (int?)frame["line"];
        if (line.HasValue && line.Value > 0)
          sb.Append(':').Append(line.Value.ToString(CultureInfo.InvariantCulture));
      }
      string session = (string)frame["session"];
      if (!string.IsNullOrEmpty(session))
        sb.Append(" (session \"").Append(session).Append("\")");
      return sb.ToString();
    }

    private async Task<ToolResult> DebugLocalsAsync(JsonObject args, CancellationToken cancellation)
    {
      IdeConnection ide = await PickIdeAsync(args).ConfigureAwait(false);
      bool all = GetBool(args, "all") ?? true;
      JsonNode result = await IdeBridge.RequestAsync(ide, "locals", new JsonObject() { ["maxVariables"] = 300 }, TimeSpan.FromSeconds(30), cancellation).ConfigureAwait(false);

      StringBuilder sb = new StringBuilder();
      JsonNode frame = result?["frame"];
      if (frame != null)
        sb.Append("Frame: ").AppendLine(DescribeFrame(frame));
      JsonArray variables = result?["variables"] as JsonArray ?? new JsonArray();
      List<JsonNode> likely = new List<JsonNode>();
      List<JsonNode> others = new List<JsonNode>();
      foreach (JsonNode variable in variables)
      {
        if (variable is null)
          continue;
        if (LikelyBytes.IsMatch((string)variable["type"] ?? string.Empty))
          likely.Add(variable);
        else
          others.Add(variable);
      }

      if (likely.Count == 0)
        sb.AppendLine("No variable has a type that holds bytes (look into members, e.g. this._buffer or request.Body, and pass that path to msgpack_debug_read).");
      else
        sb.AppendLine("* can hold MsgPack bytes, pass the expression to msgpack_debug_read:");
      foreach (JsonNode variable in likely)
        sb.Append("* ").AppendLine(DescribeVariable(variable));
      if (all)
        foreach (JsonNode variable in others)
          sb.Append("  ").AppendLine(DescribeVariable(variable));
      else if (others.Count > 0)
        sb.Append("(").Append(others.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" other variables not listed.)");
      if ((bool?)result?["truncated"] == true)
        sb.AppendLine("(More variables than listed.)");
      return ToolResult.Ok(sb.ToString());
    }

    private static string DescribeVariable(JsonNode variable)
    {
      string name = (string)variable["evaluateName"];
      if (string.IsNullOrEmpty(name))
        name = (string)variable["name"];
      StringBuilder sb = new StringBuilder(name);
      string type = (string)variable["type"];
      if (!string.IsNullOrEmpty(type))
        sb.Append(" : ").Append(type);
      string value = (string)variable["value"];
      if (!string.IsNullOrEmpty(value))
      {
        if (value.Length > 120)
          value = string.Concat(value.Substring(0, 120), "...");
        sb.Append(" = ").Append(value.Replace('\n', ' ').Replace('\r', ' '));
      }
      string scope = (string)variable["scope"];
      if (!string.IsNullOrEmpty(scope))
        sb.Append("  (").Append(scope).Append(')');
      return sb.ToString();
    }

    private async Task<ToolResult> DebugReadAsync(JsonObject args, CancellationToken cancellation)
    {
      string expression = GetString(args, "expression");
      if (string.IsNullOrWhiteSpace(expression))
        throw new ToolArgumentException("Pass the expression holding the bytes (see msgpack_debug_locals).");
      IdeConnection ide = await PickIdeAsync(args).ConfigureAwait(false);
      int maxBytes = GetInt(args, "maxBytes") ?? DefaultMaxBytes;
      RenderOptions render = GetRenderOptions(args);
      DecodeOptions decode = GetDecodeOptions(args);

      ReadResult read = await ReadAsync(ide, expression, null, maxBytes, cancellation).ConfigureAwait(false);
      string saveTo = GetString(args, "saveTo");
      if (!string.IsNullOrWhiteSpace(saveTo))
        await File.WriteAllBytesAsync(ResolvePath(saveTo), read.Bytes, cancellation).ConfigureAwait(false);

      PayloadDocument doc = new PayloadDocument(read.Bytes, string.Concat(expression, " (", read.Description, ", ", read.Session, ")"), decode);
      string missing = MissingSchema(doc);
      string schemaStore = GetString(args, "schemaStore");
      if (missing != null && !string.IsNullOrWhiteSpace(schemaStore))
      {
        try
        {
          ReadResult schema = await ReadAsync(ide, schemaStore, missing, 1024 * 1024, cancellation).ConfigureAwait(false);
          if (schema.Bytes.Length == 0)
            throw new IdeBridgeException("the store does not hold it");
          SchemaStore store = new SchemaStore();
          store.Register(schema.Bytes);
          decode.Schemas = store;
          doc = new PayloadDocument(read.Bytes, doc.Source, decode);
          doc.Notes.Add(string.Concat("Schema ", missing, " was read from ", schemaStore, "."));
        }
        catch (Exception ex) when (ex is IdeBridgeException || ex is MsgPackException || ex is FormatException)
        {
          doc.Notes.Add(string.Concat("Schema ", missing, " could not be read from ", schemaStore, ": ", ex.Message));
        }
      }
      else if (missing != null)
      {
        doc.Notes.Add("The payload refers to a cached schema: pass schemaStore (the path of the program's SchemaStore, e.g. _options.SchemaStore) to resolve the property names.");
      }
      if (read.Warning != null)
        doc.Notes.Add(read.Warning);
      if (!string.IsNullOrWhiteSpace(saveTo))
        doc.Notes.Add(string.Concat("The bytes were saved to ", ResolvePath(saveTo), "."));
      _documents.Add(doc);
      return ToolResult.Ok(TextRenderer.Render(doc, render));
    }

    private sealed class ReadResult
    {
      public byte[] Bytes;
      public string Description;
      public string Session;
      public string Warning;
    }

    private static async Task<ReadResult> ReadAsync(IdeConnection ide, string expression, string schemaId, int maxBytes, CancellationToken cancellation)
    {
      JsonObject parameters = new JsonObject() { ["expression"] = expression, ["maxBytes"] = maxBytes };
      if (schemaId != null)
        parameters["schemaId"] = schemaId;
      // Large values take many evaluations, and a stream that cannot seek waits for the user to agree
      JsonNode result = await IdeBridge.RequestAsync(ide, "read", parameters, TimeSpan.FromMinutes(5), cancellation).ConfigureAwait(false);
      string base64 = (string)result?["base64"];
      if (base64 is null)
        throw new IdeBridgeException("The IDE answered without bytes.");
      return new ReadResult()
      {
        Bytes = Convert.FromBase64String(base64),
        Description = (string)result["description"] ?? "bytes",
        Session = (string)result["session"] ?? ide.Name ?? ide.Ide,
        Warning = (string)result["warning"]
      };
    }

    /// <summary>
    /// The id of the schema the payload refers to when it is not available, otherwise null.
    /// </summary>
    private static string MissingSchema(PayloadDocument doc)
    {
      RootObject objects = doc.Objects;
      if (objects?.Schema != null && objects.Schema.Source == SchemaSource.Reference && !objects.Schema.IsAvailable)
        return objects.Schema.Id;
      return null;
    }

    #endregion

    #region Decoding

    private Task<ToolResult> DecodeAsync(JsonObject args, CancellationToken cancellation)
    {
      string data = GetString(args, "data");
      string file = GetString(args, "file");
      string id = GetString(args, "doc");
      int given = (string.IsNullOrEmpty(data) ? 0 : 1) + (string.IsNullOrEmpty(file) ? 0 : 1) + (string.IsNullOrEmpty(id) ? 0 : 1);
      if (given != 1)
        throw new ToolArgumentException("Pass one of data (the bytes as hex or base64), file or doc (the id of a document decoded before).");

      RenderOptions render = GetRenderOptions(args);
      DecodeOptions decode = GetDecodeOptions(args);
      string schemas = GetString(args, "schemas");
      if (!string.IsNullOrWhiteSpace(schemas))
        decode.Schemas = LoadSchemas(schemas);

      PayloadDocument doc;
      if (!string.IsNullOrEmpty(id))
      {
        PayloadDocument old = _documents.Get(id);
        doc = old;
        bool rereadSettings = args.ContainsKey("endian") || args.ContainsKey("continueOnError") || decode.Schemas != null;
        if (rereadSettings)
        {
          if (decode.Schemas is null)
            decode.Schemas = old.Options.Schemas;
          doc = new PayloadDocument(old.Data, old.Source, decode);
          doc.Notes.AddRange(old.Notes);
          _documents.Replace(old, doc);
        }
      }
      else
      {
        byte[] bytes;
        string source;
        if (!string.IsNullOrEmpty(file))
        {
          string path = ResolvePath(file);
          FileInfo info = new FileInfo(path);
          if (!info.Exists)
            throw new ToolArgumentException(string.Concat("File not found: ", path));
          if (info.Length > DefaultMaxBytes * 4L)
            throw new ToolArgumentException(string.Concat("The file is too large (", info.Length.ToString(CultureInfo.InvariantCulture), " bytes)."));
          bytes = File.ReadAllBytes(path);
          source = path;
        }
        else
        {
          bytes = ByteText.Parse(data);
          source = "the data passed";
        }
        doc = _documents.Add(new PayloadDocument(bytes, source, decode));
        if (decode.Schemas is null && MissingSchema(doc) != null)
          doc.Notes.Add("The payload refers to a cached schema: pass schemas (a SchemaStore export or the schema) to resolve the property names.");
      }
      return Task.FromResult(ToolResult.Ok(TextRenderer.Render(doc, render)));
    }

    /// <summary>
    /// A SchemaStore export (a map of ids to schemas) or a single schema, from a file or as text.
    /// </summary>
    private SchemaStore LoadSchemas(string schemas)
    {
      byte[] bytes;
      string path = ResolvePath(schemas);
      if (schemas.IndexOfAny(Path.GetInvalidPathChars()) < 0 && File.Exists(path))
        bytes = File.ReadAllBytes(path);
      else
        bytes = ByteText.Parse(schemas);

      SchemaStore store = new SchemaStore();
      try
      {
        store.Import(new MemoryStream(bytes));
        return store;
      }
      catch (Exception)
      {
        // not an export: a single schema
      }
      try
      {
        store.Register(bytes);
      }
      catch (Exception ex)
      {
        throw new ToolArgumentException(string.Concat("schemas holds neither a SchemaStore export nor a schema: ", ex.Message));
      }
      return store;
    }

    private Task<ToolResult> ExplainAsync(JsonObject args, CancellationToken cancellation)
    {
      PayloadDocument doc = _documents.Get(Required(args, "doc"));
      long offset = ParseOffset(Required(args, "offset"));
      return Task.FromResult(ToolResult.Ok(TextRenderer.ExplainOffset(doc, offset, GetRenderOptions(args))));
    }

    private Task<ToolResult> SearchAsync(JsonObject args, CancellationToken cancellation)
    {
      PayloadDocument doc = _documents.Get(Required(args, "doc"));
      string text = Required(args, "text");
      return Task.FromResult(ToolResult.Ok(TextRenderer.Search(doc, text, GetBool(args, "matchCase") ?? false, GetRenderOptions(args))));
    }

    #endregion

    #region Arguments

    private static JsonObject[] RenderProps()
    {
      return new JsonObject[]
      {
        Enum("view", "objects: the values as JSON with comments (default). items: the MsgPack items with offsets and encodings (for encoding problems). both.", "objects", "items", "both"),
        Prop("path", "string", "Only this part of the objects, as the objects view writes paths (e.g. \"Lines[2]\", \"Customer.Address\")."),
        Prop("from", "string", "Items view: only items holding bytes from this offset (number or 0x hex)."),
        Prop("to", "string", "Items view: only items holding bytes up to this offset (number or 0x hex)."),
        Prop("offsets", "boolean", "Objects view: add the offset of every value."),
        Prop("maxNodes", "integer", "The most values or items written (default 1000), the rest is summarized."),
        Prop("maxString", "integer", "Longer strings are cut (default 200 chars)."),
        Enum("issues", "Which validation issues to list: errors (default), all (also warnings such as larger encodings than needed), none.", "errors", "all", "none"),
        Enum("endian", "Byte order (default: the specification's).", "SwapIfCurrentSystemIsLittleEndian", "NeverSwap", "AlwaysSwap"),
        Prop("continueOnError", "boolean", "Keep reading after an error, the rest is a best guess (default true).")
      };
    }

    private static RenderOptions GetRenderOptions(JsonObject args)
    {
      RenderOptions options = new RenderOptions();
      string view = GetString(args, "view");
      if (!string.IsNullOrEmpty(view))
      {
        DecodeView parsed;
        if (!System.Enum.TryParse(view, true, out parsed))
          throw new ToolArgumentException("view is objects, items or both.");
        options.View = parsed;
      }
      options.Path = GetString(args, "path");
      string from = GetString(args, "from");
      if (!string.IsNullOrEmpty(from))
        options.From = ParseOffset(from);
      string to = GetString(args, "to");
      if (!string.IsNullOrEmpty(to))
        options.To = ParseOffset(to);
      options.Offsets = GetBool(args, "offsets") ?? false;
      options.MaxNodes = Math.Max(1, GetInt(args, "maxNodes") ?? options.MaxNodes);
      options.MaxString = Math.Max(1, GetInt(args, "maxString") ?? options.MaxString);
      string issues = GetString(args, "issues");
      if (!string.IsNullOrEmpty(issues))
      {
        IssueLevel level;
        if (!System.Enum.TryParse(issues, true, out level))
          throw new ToolArgumentException("issues is errors, all or none.");
        options.Issues = level;
      }
      return options;
    }

    private static DecodeOptions GetDecodeOptions(JsonObject args)
    {
      DecodeOptions options = new DecodeOptions();
      string endian = GetString(args, "endian");
      if (!string.IsNullOrEmpty(endian))
      {
        EndianAction action;
        if (!System.Enum.TryParse(endian, true, out action))
          throw new ToolArgumentException("endian is SwapIfCurrentSystemIsLittleEndian, NeverSwap or AlwaysSwap.");
        options.Endian = action;
      }
      options.ContinueOnError = GetBool(args, "continueOnError") ?? true;
      return options;
    }

    internal static long ParseOffset(string text)
    {
      string trimmed = text.Trim();
      long value;
      if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? long.TryParse(trimmed.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value)
        : long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        return value;
      throw new ToolArgumentException(string.Concat("Not an offset: ", text, " (a number, or hex with 0x)."));
    }

    private string ResolvePath(string file)
    {
      return Path.GetFullPath(file, CurrentDirectory);
    }

    private static string Required(JsonObject args, string name)
    {
      string value = GetString(args, name);
      if (string.IsNullOrEmpty(value))
        throw new ToolArgumentException(string.Concat(name, " is required."));
      return value;
    }

    /// <summary>
    /// A string, or the text of a number (agents pass offsets either way).
    /// </summary>
    internal static string GetString(JsonObject args, string name)
    {
      JsonNode node = args[name];
      if (node is JsonValue value)
      {
        if (value.TryGetValue(out string text))
          return text;
        return value.ToJsonString();
      }
      return null;
    }

    private static int? GetInt(JsonObject args, string name)
    {
      string text = GetString(args, name);
      if (string.IsNullOrEmpty(text))
        return null;
      double value;
      if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        return (int)Math.Min(int.MaxValue, Math.Max(int.MinValue, value));
      throw new ToolArgumentException(string.Concat(name, " is a number."));
    }

    private static bool? GetBool(JsonObject args, string name)
    {
      JsonNode node = args[name];
      if (node is JsonValue value)
      {
        if (value.TryGetValue(out bool flag))
          return flag;
        if (value.TryGetValue(out string text))
          return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
      }
      return null;
    }

    private static List<string> Strings(JsonNode node)
    {
      List<string> list = new List<string>();
      if (node is JsonArray array)
        foreach (JsonNode item in array)
          if (item != null)
            list.Add((string)item);
      return list;
    }

    private static JsonObject Schema(params JsonObject[] props)
    {
      return Schema(props, new string[0]);
    }

    private static JsonObject Schema(IEnumerable<JsonObject> props, params string[] required)
    {
      JsonObject properties = new JsonObject();
      foreach (JsonObject prop in props)
      {
        string name = (string)prop["name"];
        prop.Remove("name");
        properties[name] = prop;
      }
      JsonObject schema = new JsonObject() { ["type"] = "object", ["properties"] = properties };
      if (required.Length > 0)
      {
        JsonArray list = new JsonArray();
        foreach (string name in required)
          list.Add(name);
        schema["required"] = list;
      }
      return schema;
    }

    private static JsonObject Prop(string name, string type, string description)
    {
      // Offsets and numbers may come as strings or numbers
      if (type == "string" && (name == "from" || name == "to" || name == "offset"))
        return new JsonObject() { ["name"] = name, ["type"] = new JsonArray("string", "integer"), ["description"] = description };
      return new JsonObject() { ["name"] = name, ["type"] = type, ["description"] = description };
    }

    private static JsonObject Enum(string name, string description, params string[] values)
    {
      JsonArray list = new JsonArray();
      foreach (string value in values)
        list.Add(value);
      return new JsonObject() { ["name"] = name, ["type"] = "string", ["enum"] = list, ["description"] = description };
    }

    #endregion
  }
}
