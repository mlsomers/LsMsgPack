using LsMsgPack;
using LsMsgPackMcp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace LsMsgPackMcpServerTests
{
  /// <summary>
  /// The MCP protocol (JSON-RPC lines) and the tools, with a fake IDE for the debugger tools.
  /// </summary>
  [TestClass]
  public class McpServerTests
  {
    public TestContext TestContext { get; set; }

    private sealed class Session
    {
      public McpTools Tools = new McpTools();
      public StringWriter Output = new StringWriter();
      public McpServer Server;
      private int _id;

      public Session(List<IdeConnection> ides = null)
      {
        Tools.DiscoverIdes = () => ides ?? new List<IdeConnection>();
        Server = new McpServer(Tools.CreateTools(), Output);
      }

      /// <summary>
      /// Sends a request and returns its response.
      /// </summary>
      public async Task<JsonObject> Request(string method, JsonObject parameters = null)
      {
        int id = ++_id;
        JsonObject request = new JsonObject() { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (parameters != null)
          request["params"] = parameters;
        await Server.HandleLineAsync(request.ToJsonString());
        foreach (string line in Output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
          JsonObject response = (JsonObject)JsonNode.Parse(line);
          if ((int?)response["id"] == id)
            return response;
        }
        Assert.Fail($"No response to {method}: {Output}");
        return null;
      }

      /// <summary>
      /// Calls a tool, returns its text (asserting whether it failed).
      /// </summary>
      public async Task<string> Call(string tool, JsonObject arguments, bool expectError = false)
      {
        JsonObject response = await Request("tools/call", new JsonObject() { ["name"] = tool, ["arguments"] = arguments });
        Assert.IsNull(response["error"], response.ToJsonString());
        string text = (string)response["result"]["content"][0]["text"];
        Assert.AreEqual(expectError, (bool)response["result"]["isError"], text);
        return text;
      }
    }

    private static void AssertContains(string text, string expected)
    {
      Assert.IsTrue(text.Contains(expected, StringComparison.Ordinal), $"Expected \"{expected}\" in:\n{text}");
    }

    [TestMethod]
    public async Task Initialize_ListTools_Ping()
    {
      Session session = new Session();
      JsonObject init = await session.Request("initialize", new JsonObject() { ["protocolVersion"] = "2025-06-18", ["capabilities"] = new JsonObject(), ["clientInfo"] = new JsonObject() { ["name"] = "test", ["version"] = "1" } });
      Assert.AreEqual("2025-06-18", (string)init["result"]["protocolVersion"]);
      Assert.AreEqual("lsmsgpack", (string)init["result"]["serverInfo"]["name"]);
      Assert.IsNotNull(init["result"]["capabilities"]["tools"]);

      // An unknown version gets the newest
      JsonObject future = await session.Request("initialize", new JsonObject() { ["protocolVersion"] = "2099-01-01" });
      Assert.AreEqual(McpServer.ProtocolVersions.Last(), (string)future["result"]["protocolVersion"]);

      // Notifications get no answer
      int before = session.Output.ToString().Length;
      await session.Server.HandleLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
      Assert.AreEqual(before, session.Output.ToString().Length);

      JsonObject tools = await session.Request("tools/list");
      string[] names = ((JsonArray)tools["result"]["tools"]).Select(t => (string)t["name"]).ToArray();
      CollectionAssert.AreEquivalent(new[] { "msgpack_debug_status", "msgpack_debug_locals", "msgpack_debug_read", "msgpack_decode", "msgpack_explain_offset", "msgpack_search" }, names);
      foreach (JsonNode tool in (JsonArray)tools["result"]["tools"])
      {
        Assert.AreEqual("object", (string)tool["inputSchema"]["type"]);
        Assert.IsFalse(string.IsNullOrEmpty((string)tool["description"]));
      }
      JsonNode read = ((JsonArray)tools["result"]["tools"]).First(t => (string)t["name"] == "msgpack_debug_read");
      Assert.AreEqual("expression", (string)read["inputSchema"]["required"][0]);
      Assert.IsFalse((bool)read["annotations"]["readOnlyHint"]);

      JsonObject ping = await session.Request("ping");
      Assert.IsNotNull(ping["result"]);
    }

    [TestMethod]
    public async Task ProtocolErrors()
    {
      Session session = new Session();
      JsonObject unknown = await session.Request("resources/list");
      Assert.AreEqual(-32601, (int)unknown["error"]["code"]);

      JsonObject unknownTool = await session.Request("tools/call", new JsonObject() { ["name"] = "nope" });
      Assert.AreEqual(-32602, (int)unknownTool["error"]["code"]);

      await session.Server.HandleLineAsync("{not json");
      AssertContains(session.Output.ToString(), "-32700");
    }

    [TestMethod]
    public async Task Decode_ThenExplainAndSearchByDocumentId()
    {
      Session session = new Session();
      byte[] bytes = Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Array, true);
      string text = await session.Call("msgpack_decode", new JsonObject() { ["data"] = Convert.ToHexString(bytes) });
      AssertContains(text, "MsgPack \"doc1\" 247 bytes");
      AssertContains(text, "{ // McpInvoice~");

      // Another view of the same document
      string items = await session.Call("msgpack_decode", new JsonObject() { ["doc"] = "doc1", ["view"] = "items", ["from"] = "0x7B", ["to"] = 123 });
      AssertContains(items, "uint 16 1234 // Id");

      string explained = await session.Call("msgpack_explain_offset", new JsonObject() { ["doc"] = "doc1", ["offset"] = "0x7B" });
      AssertContains(explained, "0xCD, as a type byte: uint 16");
      AssertContains(explained, "Object: Id = 1234");

      string found = await session.Call("msgpack_search", new JsonObject() { ["doc"] = "doc1", ["text"] = "Amsterdam" });
      AssertContains(found, "// Billing.City");

      // Base64 and a file give the next ids
      string second = await session.Call("msgpack_decode", new JsonObject() { ["data"] = Convert.ToBase64String(bytes), ["path"] = "Billing" });
      AssertContains(second, "\"doc2\"");
      AssertContains(second, "Objects at Billing");

      string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".msgpack");
      try
      {
        File.WriteAllBytes(file, bytes);
        string fromFile = await session.Call("msgpack_decode", new JsonObject() { ["file"] = file });
        AssertContains(fromFile, "\"doc3\"");
        AssertContains(fromFile, file);
      }
      finally
      {
        File.Delete(file);
      }
    }

    [TestMethod]
    public async Task Decode_ErrorsAreToolResults()
    {
      Session session = new Session();
      AssertContains(await session.Call("msgpack_decode", new JsonObject(), true), "Pass one of data");
      AssertContains(await session.Call("msgpack_decode", new JsonObject() { ["data"] = "not bytes at all" }, true), "does not seem to hold bytes");
      AssertContains(await session.Call("msgpack_decode", new JsonObject() { ["file"] = "/nope/missing.msgpack" }, true), "File not found");
      AssertContains(await session.Call("msgpack_explain_offset", new JsonObject() { ["doc"] = "doc9", ["offset"] = 1 }, true), "Unknown document \"doc9\"");
      AssertContains(await session.Call("msgpack_decode", new JsonObject() { ["data"] = "c0", ["view"] = "pretty" }, true), "view is objects, items or both");
    }

    [TestMethod]
    public async Task Decode_SchemaReferenceWithAnExportedStore()
    {
      SchemaStore store = new SchemaStore();
      byte[] bytes = Payloads.SerializeWithReference(Payloads.Invoice(), store);
      MemoryStream export = new MemoryStream();
      store.Export(export);

      Session session = new Session();
      string without = await session.Call("msgpack_decode", new JsonObject() { ["data"] = Convert.ToBase64String(bytes) });
      AssertContains(without, "pass schemas");

      string with = await session.Call("msgpack_decode", new JsonObject() { ["data"] = Convert.ToBase64String(bytes), ["schemas"] = Convert.ToBase64String(export.ToArray()) });
      AssertContains(with, "\"Customer\": \"Alice\"");

      // A single schema works too, and a document can be read again with it
      string single = await session.Call("msgpack_decode", new JsonObject() { ["doc"] = "doc1", ["schemas"] = Convert.ToBase64String(store.GetSchema(store.GetSchemaIds()[0])) });
      AssertContains(single, "MsgPack \"doc1\"");
      AssertContains(single, "\"Customer\": \"Alice\"");
    }

    [TestMethod]
    public async Task Debug_NoIde()
    {
      Session session = new Session();
      AssertContains(await session.Call("msgpack_debug_status", new JsonObject(), true), "No IDE with the MsgPack Explorer extension is running");
      AssertContains(await session.Call("msgpack_debug_read", new JsonObject() { ["expression"] = "x" }, true), "No IDE");
    }

    [TestMethod]
    public async Task Debug_StatusLocalsRead()
    {
      using (FakeIde ide = new FakeIde())
      {
        byte[] bytes = Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Array, true);
        ide.Values["payload"] = bytes;
        Session session = new Session(new List<IdeConnection>() { ide.Connection() });

        string status = await session.Call("msgpack_debug_status", new JsonObject());
        AssertContains(status, "Fake Code 1.0");
        AssertContains(status, "Debug session \"Launch Api\" (coreclr), active");
        AssertContains(status, "Paused in InvoiceController.Get at /work/project/InvoiceController.cs:42");
        AssertContains(status, "variable and member paths only");

        string locals = await session.Call("msgpack_debug_locals", new JsonObject());
        AssertContains(locals, "* payload : byte[] = {byte[247]}");
        AssertContains(locals, "  id : int = 5");

        string saved = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".msgpack");
        try
        {
          string read = await session.Call("msgpack_debug_read", new JsonObject() { ["expression"] = "payload", ["saveTo"] = saved, ["path"] = "Lines" });
          AssertContains(read, "MsgPack \"doc1\" 247 bytes from payload (byte[], Launch Api)");
          AssertContains(read, "Objects at Lines");
          AssertContains(read, "\"Apples\"");
          CollectionAssert.AreEqual(bytes, File.ReadAllBytes(saved));
        }
        finally
        {
          File.Delete(saved);
        }

        // The document can be explained afterwards
        AssertContains(await session.Call("msgpack_explain_offset", new JsonObject() { ["doc"] = "doc1", ["offset"] = 0 }), "fixmap of 3 entries");

        // A string holding the bytes comes as text (Visual Studio)
        AssertContains(await session.Call("msgpack_debug_read", new JsonObject() { ["expression"] = "hexText" }), "\ntrue\n");

        // The IDE's error reaches the agent
        AssertContains(await session.Call("msgpack_debug_read", new JsonObject() { ["expression"] = "nope" }, true), "The name 'nope' does not exist");
        Assert.IsTrue(ide.Requests.All(r => (string)r["token"] == "secret"));
      }
    }

    [TestMethod]
    public async Task Debug_ReadsTheSchemaFromTheProgramsStore()
    {
      using (FakeIde ide = new FakeIde())
      {
        SchemaStore store = new SchemaStore();
        ide.Values["payload"] = Payloads.SerializeWithReference(Payloads.Invoice(), store);
        SchemaId id = store.GetSchemaIds()[0];
        ide.Schemas[string.Concat("_settings.SchemaStore|", id.ToString())] = store.GetSchema(id);
        Session session = new Session(new List<IdeConnection>() { ide.Connection() });

        string without = await session.Call("msgpack_debug_read", new JsonObject() { ["expression"] = "payload" });
        AssertContains(without, "pass schemaStore");

        string with = await session.Call("msgpack_debug_read", new JsonObject() { ["expression"] = "payload", ["schemaStore"] = "_settings.SchemaStore" });
        AssertContains(with, string.Concat("Schema ", id.ToString(), " was read from _settings.SchemaStore."));
        AssertContains(with, "\"Customer\": \"Alice\"");
      }
    }

    [TestMethod]
    public async Task Debug_WrongTokenAndUnreachableIde()
    {
      using (FakeIde ide = new FakeIde())
      {
        Session session = new Session(new List<IdeConnection>() { ide.Connection("wrong") });
        AssertContains(await session.Call("msgpack_debug_locals", new JsonObject(), true), "Wrong token.");
      }

      IdeConnection gone = new IdeConnection() { Ide = "vscode", Name = "Gone", Port = 1, Token = "x" };
      Session unreachable = new Session(new List<IdeConnection>() { gone });
      AssertContains(await unreachable.Call("msgpack_debug_locals", new JsonObject(), true), "Could not reach Gone");
    }

    [TestMethod]
    public void PickIde()
    {
      IdeConnection code = new IdeConnection() { Ide = "vscode", Pid = 10, Port = 1, Token = "a", WorkspaceFolders = new List<string>() { "/work/a" } };
      IdeConnection vs = new IdeConnection() { Ide = "visualstudio", Pid = 20, Port = 2, Token = "b", WorkspaceFolders = new List<string>() { "C:\\work\\b" } };
      List<IdeConnection> both = new List<IdeConnection>() { code, vs };

      Assert.AreSame(code, IdeBridge.Pick(new List<IdeConnection>() { code }, null, "/elsewhere"));
      Assert.AreSame(vs, IdeBridge.Pick(both, "20", "/elsewhere"));
      Assert.AreSame(vs, IdeBridge.Pick(both, "VisualStudio", "/elsewhere"));
      Assert.AreSame(code, IdeBridge.Pick(both, null, "/work/a/src"));
      Assert.ThrowsExactly<IdeBridgeException>(() => IdeBridge.Pick(both, null, "/elsewhere"));
      Assert.ThrowsExactly<IdeBridgeException>(() => IdeBridge.Pick(both, "rider", "/work/a"));
      Assert.ThrowsExactly<IdeBridgeException>(() => IdeBridge.Pick(new List<IdeConnection>(), null, "/work/a"));
    }

    [TestMethod]
    public void LockFiles()
    {
      string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      try
      {
        string file = Path.Combine(dir, "1.json");
        File.WriteAllText(file, "{\"ide\":\"vscode\",\"name\":\"Visual Studio Code 1.105.0\",\"pid\":" + Environment.ProcessId + ",\"port\":5000,\"token\":\"t\",\"workspaceFolders\":[\"/a\",\"/b\"]}");
        IdeConnection connection = IdeBridge.ReadLockFile(file);
        Assert.AreEqual(5000, connection.Port);
        Assert.AreEqual("t", connection.Token);
        CollectionAssert.AreEqual(new[] { "/a", "/b" }, connection.WorkspaceFolders);

        File.WriteAllText(file, "{\"port\":5000}"); // no token
        Assert.IsNull(IdeBridge.ReadLockFile(file));
        File.WriteAllText(file, "garbage");
        Assert.IsNull(IdeBridge.ReadLockFile(file));
      }
      finally
      {
        Directory.Delete(dir, true);
      }
    }

    [TestMethod]
    public void CommandLineOptions()
    {
      List<string> positional = new List<string>();
      JsonObject options = Program.ParseOptions(new[] { "decode", "file.msgpack", "--view", "items", "--max-nodes", "50", "--offsets", "--stop-on-error" }, 1, positional);
      CollectionAssert.AreEqual(new[] { "file.msgpack" }, positional);
      Assert.AreEqual("items", (string)options["view"]);
      Assert.AreEqual("50", (string)options["maxNodes"]);
      Assert.IsTrue((bool)options["offsets"]);
      Assert.IsFalse((bool)options["continueOnError"]);
      Assert.AreEqual(0x1F, McpTools.ParseOffset("0x1F"));
      Assert.AreEqual(31, McpTools.ParseOffset(" 31 "));
    }
  }
}
