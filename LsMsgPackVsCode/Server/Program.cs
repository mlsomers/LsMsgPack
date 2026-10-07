using LsMsgPack;
using ObjectDebugger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LsMsgPackInspector
{
  /// <summary>
  /// The backend of the VS Code extension. Reads one JSON request per line from stdin and writes one JSON response per line to stdout:
  /// <code>
  /// {"id":1,"method":"load","doc":"a","data":"&lt;base64&gt;","continueOnError":false,"endian":"SwapIfCurrentSystemIsLittleEndian","displayLimit":1000,"objects":"auto","showObjectsAt":"high"}
  /// {"id":2,"method":"search","doc":"a","text":"abc","matchCase":false}
  /// {"id":3,"method":"close","doc":"a"}
  /// {"id":4,"method":"version"}
  /// </code>
  /// The response is <c>{"id":1,"result":...}</c> or <c>{"id":1,"error":"..."}</c>. "load" without data reads the data of the document again (other settings).
  /// <para>With a file name as argument it writes the model of that file (for trying it out): <c>LsMsgPackInspector file.msgpack [--objects]</c>.</para>
  /// </summary>
  public static class Program
  {
    internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions()
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      // Read by the extension only (never embedded in HTML), so quotes and non-ASCII text stay readable and short
      Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static int Main(string[] args)
    {
      UTF8Encoding utf8 = new UTF8Encoding(false);
      if (args.Length > 0)
        return InspectFile(args);

      Dictionary<string, InspectorDocument> documents = new Dictionary<string, InspectorDocument>(StringComparer.Ordinal);
      using (StreamReader input = new StreamReader(Console.OpenStandardInput(), utf8))
      using (StreamWriter output = new StreamWriter(Console.OpenStandardOutput(), utf8))
      {
        string line;
        while ((line = input.ReadLine()) != null)
        {
          if (line.Length == 0)
            continue;
          output.WriteLine(Handle(line, documents));
          output.Flush();
        }
      }
      return 0;
    }

    private static int InspectFile(string[] args)
    {
      InspectorSettings settings = new InspectorSettings();
      if (Array.IndexOf(args, "--objects") > 0)
        settings.Objects = ObjectsMode.Show;
      if (Array.IndexOf(args, "--continue") > 0)
        settings.ContinueOnError = true;
      LoadResult result = new InspectorDocument().Load(File.ReadAllBytes(args[0]), settings);
      Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
      return result.Error is null ? 0 : 1;
    }

    /// <summary>
    /// Handles one request, any exception becomes the error of the response (the process keeps serving).
    /// </summary>
    public static string Handle(string request, Dictionary<string, InspectorDocument> documents)
    {
      JsonElement id = default(JsonElement);
      try
      {
        using (JsonDocument json = JsonDocument.Parse(request))
        {
          JsonElement root = json.RootElement;
          if (root.TryGetProperty("id", out JsonElement idElement))
            id = idElement.Clone();
          string method = GetString(root, "method");
          string doc = GetString(root, "doc") ?? string.Empty;
          object result;
          switch (method)
          {
            case "load":
              result = Load(root, doc, documents);
              break;
            case "search":
              result = GetDocument(doc, documents).Search(GetString(root, "text"), GetBool(root, "matchCase"));
              break;
            case "close":
              documents.Remove(doc);
              result = true;
              break;
            case "version":
              result = GetVersion();
              break;
            default:
              throw new ArgumentException(string.Concat("Unknown method: ", method));
          }
          return Respond(id, result, null);
        }
      }
      catch (Exception ex)
      {
        return Respond(id, null, ex.Message);
      }
    }

    private static LoadResult Load(JsonElement root, string doc, Dictionary<string, InspectorDocument> documents)
    {
      string data = GetString(root, "data");
      InspectorDocument document;
      if (!documents.TryGetValue(doc, out document))
      {
        if (data is null)
          throw new ArgumentException(string.Concat("Unknown document: ", doc)); // e.g. the process was started again, the extension sends the data
        document = new InspectorDocument();
        documents[doc] = document;
      }

      InspectorSettings settings = new InspectorSettings()
      {
        ContinueOnError = GetBool(root, "continueOnError")
      };
      string endian = GetString(root, "endian");
      if (!string.IsNullOrEmpty(endian))
        settings.Endian = (EndianAction)Enum.Parse(typeof(EndianAction), endian, true);
      if (root.TryGetProperty("displayLimit", out JsonElement limit) && limit.ValueKind == JsonValueKind.Number)
        settings.DisplayLimit = limit.GetInt64();
      string objects = GetString(root, "objects");
      if (!string.IsNullOrEmpty(objects))
        settings.Objects = (ObjectsMode)Enum.Parse(typeof(ObjectsMode), objects, true);
      string showObjectsAt = GetString(root, "showObjectsAt");
      if (!string.IsNullOrEmpty(showObjectsAt))
        settings.ShowObjectsAt = (ObjectConfidence)Enum.Parse(typeof(ObjectConfidence), showObjectsAt, true);

      return document.Load(data is null ? null : Convert.FromBase64String(data), settings);
    }

    private static InspectorDocument GetDocument(string doc, Dictionary<string, InspectorDocument> documents)
    {
      InspectorDocument document;
      if (!documents.TryGetValue(doc, out document))
        throw new ArgumentException(string.Concat("Unknown document: ", doc));
      return document;
    }

    private static string GetVersion()
    {
      Assembly assembly = typeof(Program).Assembly;
      AssemblyFileVersionAttribute version = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
      return version?.Version ?? assembly.GetName().Version.ToString();
    }

    private static string GetString(JsonElement root, string name)
    {
      return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool GetBool(JsonElement root, string name)
    {
      return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }

    private static string Respond(JsonElement id, object result, string error)
    {
      Dictionary<string, object> response = new Dictionary<string, object>();
      response["id"] = id.ValueKind == JsonValueKind.Undefined ? null : (object)id;
      if (error != null)
        response["error"] = error;
      else
        response["result"] = result;
      return JsonSerializer.Serialize(response, JsonOptions);
    }
  }
}
