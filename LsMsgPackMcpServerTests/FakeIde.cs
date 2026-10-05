using LsMsgPackMcp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackMcpServerTests
{
  /// <summary>
  /// An IDE extension as the MCP server sees it: answers status, locals and read on 127.0.0.1 (the protocol of LsMsgPackVsCode/src/mcpBridge.ts and the Visual Studio package).
  /// </summary>
  public sealed class FakeIde : IDisposable
  {
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new CancellationTokenSource();

    public FakeIde(string token = "secret")
    {
      Token = token;
      _listener = new TcpListener(IPAddress.Loopback, 0);
      _listener.Start();
      Task.Run(AcceptAsync);
    }

    public string Token { get; }

    public int Port
    {
      get { return ((IPEndPoint)_listener.LocalEndpoint).Port; }
    }

    /// <summary>
    /// The values the debugger has, by expression.
    /// </summary>
    public Dictionary<string, byte[]> Values { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);

    /// <summary>
    /// Schemas of the program's SchemaStore, by expression of the store and schema id.
    /// </summary>
    public Dictionary<string, byte[]> Schemas { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);

    public List<JsonObject> Requests { get; } = new List<JsonObject>();

    public IdeConnection Connection(string token = null)
    {
      return new IdeConnection() { Ide = "vscode", Name = "Fake Code", Pid = 0, Port = Port, Token = token ?? Token, WorkspaceFolders = new List<string>() { "/work/project" } };
    }

    private async Task AcceptAsync()
    {
      while (!_stop.IsCancellationRequested)
      {
        TcpClient client;
        try
        {
          client = await _listener.AcceptTcpClientAsync(_stop.Token);
        }
        catch (Exception)
        {
          return;
        }
        _ = Task.Run(() => HandleAsync(client));
      }
    }

    private async Task HandleAsync(TcpClient client)
    {
      using (client)
      using (NetworkStream stream = client.GetStream())
      using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false)))
      using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
      {
        string line = await reader.ReadLineAsync();
        JsonObject request = (JsonObject)JsonNode.Parse(line);
        lock (Requests)
          Requests.Add(request);
        JsonObject response;
        if ((string)request["token"] != Token)
          response = new JsonObject() { ["error"] = "Wrong token." };
        else
          response = Answer((string)request["method"], (JsonObject)request["params"]);
        await writer.WriteAsync(response.ToJsonString() + "\n");
        await writer.FlushAsync();
      }
    }

    private JsonObject Answer(string method, JsonObject parameters)
    {
      switch (method)
      {
        case "status":
          return new JsonObject()
          {
            ["result"] = new JsonObject()
            {
              ["ide"] = "vscode",
              ["name"] = "Fake Code 1.0",
              ["workspaceFolders"] = new JsonArray("/work/project"),
              ["sessions"] = new JsonArray(new JsonObject() { ["id"] = "1", ["name"] = "Launch Api", ["type"] = "coreclr", ["active"] = true }),
              ["frame"] = new JsonObject() { ["name"] = "InvoiceController.Get", ["source"] = "/work/project/InvoiceController.cs", ["line"] = 42, ["session"] = "Launch Api" },
              ["expressions"] = "paths"
            }
          };
        case "locals":
          return new JsonObject()
          {
            ["result"] = new JsonObject()
            {
              ["frame"] = new JsonObject() { ["name"] = "InvoiceController.Get" },
              ["variables"] = new JsonArray(
                new JsonObject() { ["scope"] = "Locals", ["name"] = "id", ["type"] = "int", ["value"] = "5", ["evaluateName"] = "id" },
                new JsonObject() { ["scope"] = "Locals", ["name"] = "payload", ["type"] = "byte[]", ["value"] = "{byte[247]}", ["evaluateName"] = "payload" })
            }
          };
        case "read":
          {
            string expression = (string)parameters["expression"];
            string schemaId = (string)parameters["schemaId"];
            byte[] bytes;
            if (schemaId != null ? Schemas.TryGetValue(string.Concat(expression, "|", schemaId), out bytes) : Values.TryGetValue(expression, out bytes))
              return new JsonObject() { ["result"] = new JsonObject() { ["base64"] = Convert.ToBase64String(bytes), ["description"] = "byte[]", ["length"] = bytes.Length, ["session"] = "Launch Api" } };
            return new JsonObject() { ["error"] = string.Concat("error CS0103: The name '", expression, "' does not exist in the current context") };
          }
        default:
          return new JsonObject() { ["error"] = string.Concat("Unknown method ", method) };
      }
    }

    public void Dispose()
    {
      _stop.Cancel();
      _listener.Stop();
    }
  }
}
