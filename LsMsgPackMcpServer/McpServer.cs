using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackMcp
{
  /// <summary>
  /// The result of a tool: text for the agent, <see cref="IsError"/> when the tool failed (the agent sees the message and can try otherwise).
  /// </summary>
  public sealed class ToolResult
  {
    public string Text { get; set; }
    public bool IsError { get; set; }

    public static ToolResult Ok(string text)
    {
      return new ToolResult() { Text = text };
    }

    public static ToolResult Error(string text)
    {
      return new ToolResult() { Text = text, IsError = true };
    }
  }

  public sealed class Tool
  {
    public string Name { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public JsonObject InputSchema { get; set; }
    public bool ReadOnly { get; set; } = true;
    public Func<JsonObject, CancellationToken, Task<ToolResult>> Handler { get; set; }
  }

  /// <summary>
  /// A Model Context Protocol server over stdio: JSON-RPC 2.0 messages, one per line (https://modelcontextprotocol.io/specification).
  /// Only tools: initialize, ping, tools/list, tools/call and cancellation. Requests are handled concurrently, so a cancellation can reach a running tool.
  /// </summary>
  public sealed class McpServer
  {
    /// <summary>
    /// The protocol versions this server speaks, the newest last: the client's version is used when it is one of them.
    /// </summary>
    internal static readonly string[] ProtocolVersions = new string[] { "2024-11-05", "2025-03-26", "2025-06-18", "2025-11-25" };

    private const string Instructions =
      "Decodes MsgPack data (also LsMsgPack's indexed schema, type ids, timestamps, decimals and extensions) into JSON-like text, and reads MsgPack bytes from variables " +
      "of a program paused in the debugger of VS Code or Visual Studio. Prefer these tools over decoding hex or base64 yourself. " +
      "While debugging: msgpack_debug_status, then msgpack_debug_locals to find the variable, then msgpack_debug_read. Otherwise msgpack_decode (data or file). " +
      "Results get a document id: use it with msgpack_decode (another view or path), msgpack_explain_offset (what a byte is, e.g. at an error) and msgpack_search.";

    private readonly List<Tool> _tools;
    private readonly TextWriter _output;
    private readonly object _writeLock = new object();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.Ordinal);

    public McpServer(List<Tool> tools, TextWriter output)
    {
      _tools = tools;
      _output = output;
    }

    /// <summary>
    /// Serves until the input ends, then waits for the running requests.
    /// </summary>
    public async Task RunAsync(TextReader input)
    {
      List<Task> pending = new List<Task>();
      string line;
      while ((line = await input.ReadLineAsync().ConfigureAwait(false)) != null)
      {
        if (line.Trim().Length == 0)
          continue;
        Task task = HandleLineAsync(line);
        if (!task.IsCompleted)
          pending.Add(task);
        pending.RemoveAll(t => t.IsCompleted);
      }
      await Task.WhenAll(pending).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles one message (a request, a notification or a batch), writing the responses.
    /// </summary>
    internal async Task HandleLineAsync(string line)
    {
      JsonNode message;
      try
      {
        message = JsonNode.Parse(line);
      }
      catch (JsonException ex)
      {
        Write(ErrorResponse(null, -32700, string.Concat("Parse error: ", ex.Message)));
        return;
      }

      if (message is JsonArray batch) // 2025-03-26 allowed batches
      {
        List<Task> tasks = new List<Task>();
        foreach (JsonNode item in batch)
          if (item is JsonObject request)
            tasks.Add(HandleMessageAsync(request));
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return;
      }
      if (message is JsonObject obj)
      {
        await HandleMessageAsync(obj).ConfigureAwait(false);
        return;
      }
      Write(ErrorResponse(null, -32600, "Invalid request."));
    }

    private async Task HandleMessageAsync(JsonObject message)
    {
      JsonNode id = message["id"]?.DeepClone();
      string method = message["method"] is JsonValue m && m.TryGetValue(out string name) ? name : null;
      if (method is null)
        return; // a response to a request of ours (we send none) or garbage

      JsonObject parameters = message["params"] as JsonObject ?? new JsonObject();
      bool isNotification = !message.ContainsKey("id");
      if (isNotification)
      {
        if (method == "notifications/cancelled")
        {
          string requestId = parameters["requestId"]?.ToJsonString();
          CancellationTokenSource source;
          if (requestId != null && _running.TryGetValue(requestId, out source))
            source.Cancel();
        }
        return; // notifications/initialized and others need no answer
      }

      string key = id?.ToJsonString() ?? "null";
      CancellationTokenSource cancellation = new CancellationTokenSource();
      _running[key] = cancellation;
      try
      {
        JsonNode result;
        switch (method)
        {
          case "initialize":
            result = Initialize(parameters);
            break;
          case "ping":
            result = new JsonObject();
            break;
          case "tools/list":
            result = ListTools();
            break;
          case "tools/call":
            result = await CallToolAsync(parameters, cancellation.Token).ConfigureAwait(false);
            break;
          default:
            Write(ErrorResponse(id, -32601, string.Concat("Method not found: ", method)));
            return;
        }
        if (!cancellation.IsCancellationRequested) // a cancelled request gets no answer
          Write(new JsonObject() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
      }
      catch (ArgumentException ex)
      {
        Write(ErrorResponse(id, -32602, ex.Message));
      }
      catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
      {
      }
      catch (Exception ex)
      {
        Write(ErrorResponse(id, -32603, ex.Message));
      }
      finally
      {
        _running.TryRemove(key, out _);
        cancellation.Dispose();
      }
    }

    private static JsonObject Initialize(JsonObject parameters)
    {
      string requested = parameters["protocolVersion"] is JsonValue v && v.TryGetValue(out string version) ? version : null;
      string agreed = Array.IndexOf(ProtocolVersions, requested) >= 0 ? requested : ProtocolVersions[ProtocolVersions.Length - 1];
      return new JsonObject()
      {
        ["protocolVersion"] = agreed,
        ["capabilities"] = new JsonObject() { ["tools"] = new JsonObject() { ["listChanged"] = false } },
        ["serverInfo"] = new JsonObject() { ["name"] = "lsmsgpack", ["title"] = "MsgPack (LsMsgPack)", ["version"] = Version },
        ["instructions"] = Instructions
      };
    }

    internal static string Version
    {
      get
      {
        Assembly assembly = typeof(McpServer).Assembly;
        AssemblyFileVersionAttribute version = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
        return version?.Version ?? assembly.GetName().Version.ToString();
      }
    }

    private JsonObject ListTools()
    {
      JsonArray tools = new JsonArray();
      foreach (Tool tool in _tools)
      {
        tools.Add(new JsonObject()
        {
          ["name"] = tool.Name,
          ["title"] = tool.Title,
          ["description"] = tool.Description,
          ["inputSchema"] = tool.InputSchema.DeepClone(),
          ["annotations"] = new JsonObject()
          {
            ["title"] = tool.Title,
            ["readOnlyHint"] = tool.ReadOnly,
            ["destructiveHint"] = false,
            ["openWorldHint"] = false
          }
        });
      }
      return new JsonObject() { ["tools"] = tools };
    }

    private async Task<JsonObject> CallToolAsync(JsonObject parameters, CancellationToken cancellation)
    {
      string name = parameters["name"] is JsonValue n && n.TryGetValue(out string text) ? text : null;
      Tool tool = _tools.Find(t => t.Name == name);
      if (tool is null)
        throw new ArgumentException(string.Concat("Unknown tool: ", name));

      JsonObject arguments = parameters["arguments"] as JsonObject ?? new JsonObject();
      ToolResult result;
      try
      {
        // On the thread pool: decoding is synchronous work, the read loop stays free for cancellations and other requests
        result = await Task.Run(() => tool.Handler(arguments, cancellation), cancellation).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex) when (ex is ToolArgumentException || ex is IdeBridgeException || ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
      {
        // Errors the agent can act on are results, not protocol errors
        result = ToolResult.Error(ex.Message);
      }
      catch (Exception ex)
      {
        result = ToolResult.Error(string.Concat(ex.GetType().Name, ": ", ex.Message));
      }
      return new JsonObject()
      {
        ["content"] = new JsonArray(new JsonObject() { ["type"] = "text", ["text"] = result.Text }),
        ["isError"] = result.IsError
      };
    }

    private static JsonObject ErrorResponse(JsonNode id, int code, string message)
    {
      return new JsonObject()
      {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject() { ["code"] = code, ["message"] = message }
      };
    }

    private void Write(JsonObject message)
    {
      string text = message.ToJsonString(new JsonSerializerOptions() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
      lock (_writeLock)
      {
        _output.Write(text);
        _output.Write('\n');
        _output.Flush();
      }
    }
  }

  /// <summary>
  /// A wrong or missing argument of a tool: reported to the agent as the tool's error.
  /// </summary>
  public class ToolArgumentException : Exception
  {
    public ToolArgumentException(string message) : base(message) { }
  }
}
