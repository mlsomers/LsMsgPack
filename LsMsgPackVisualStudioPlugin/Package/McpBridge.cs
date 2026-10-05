using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MsgPackDebuggerExtension
{
  /// <summary>
  /// What Visual Studio does for the requests of the MCP server (<see cref="DebuggerBridgeHandler"/>).
  /// </summary>
  internal interface IBridgeHandler
  {
    Task<JObject> StatusAsync();
    Task<JObject> LocalsAsync(int maxVariables);
    Task<JObject> ReadAsync(string expression, string schemaId, int? maxBytes);
  }

  /// <summary>
  /// The bridge between the MCP server (LsMsgPackMcpServer) and the debugger of Visual Studio, the same protocol as the VS Code extension
  /// (LsMsgPackVsCode/src/mcpBridge.ts): a server on 127.0.0.1 that answers one JSON line per connection
  /// (<c>{"token", "method", "params"}</c> → <c>{"result"}</c> or <c>{"error"}</c>), and a lock file in %USERPROFILE%\.lsmsgpack\ide
  /// that tells the MCP server where it is and which token it needs.
  /// </summary>
  internal sealed class McpBridge : IDisposable
  {
    private const int MaxRequestLength = 64 * 1024;

    private readonly IBridgeHandler _handler;
    private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new CancellationTokenSource();
    private string _lockFile;
    private Task _accepting;

    public McpBridge(IBridgeHandler handler)
    {
      _handler = handler;
      byte[] random = new byte[24];
      using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
        generator.GetBytes(random);
      Token = BitConverter.ToString(random).Replace("-", string.Empty).ToLowerInvariant();
    }

    public string Token { get; }

    public int Port
    {
      get { return ((IPEndPoint)_listener.LocalEndpoint).Port; }
    }

    public static string LockDirectory
    {
      get
      {
        string dir = Environment.GetEnvironmentVariable("LSMSGPACK_IDE_DIR");
        if (!string.IsNullOrEmpty(dir))
          return dir;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lsmsgpack", "ide");
      }
    }

    public void Start()
    {
      _listener.Start();
      _accepting = Task.Run(AcceptAsync);
    }

    /// <summary>
    /// Writes (or rewrites, e.g. when another solution is opened) the lock file. Lock files of processes that are gone are removed.
    /// </summary>
    public void WriteLockFile(string name, IList<string> workspaceFolders)
    {
      string dir = LockDirectory;
      Directory.CreateDirectory(dir);
      RemoveStaleLockFiles(dir);
      int pid = Process.GetCurrentProcess().Id;
      string file = _lockFile ?? Path.Combine(dir, string.Concat(pid.ToString(), "-", Port.ToString(), ".json"));
      JObject content = new JObject()
      {
        ["ide"] = "visualstudio",
        ["name"] = name,
        ["pid"] = pid,
        ["port"] = Port,
        ["token"] = Token,
        ["workspaceFolders"] = new JArray(workspaceFolders)
      };
      // Written aside and moved, so a reader never sees half a file
      string temp = string.Concat(file, ".tmp");
      File.WriteAllText(temp, content.ToString(Formatting.None), new UTF8Encoding(false));
      if (File.Exists(file))
        File.Delete(file);
      File.Move(temp, file);
      _lockFile = file;
    }

    private static void RemoveStaleLockFiles(string dir)
    {
      foreach (string file in Directory.GetFiles(dir, "*.json"))
      {
        try
        {
          int pid = (int?)JObject.Parse(File.ReadAllText(file))["pid"] ?? 0;
          if (pid > 0 && !IsRunning(pid))
            File.Delete(file);
        }
        catch (Exception)
        {
          // being written by another instance, or not ours
        }
      }
    }

    private static bool IsRunning(int pid)
    {
      try
      {
        using (Process process = Process.GetProcessById(pid))
          return !process.HasExited;
      }
      catch (ArgumentException)
      {
        return false;
      }
      catch (Exception)
      {
        return true; // e.g. no access: it runs
      }
    }

    private async Task AcceptAsync()
    {
      while (!_stop.IsCancellationRequested)
      {
        TcpClient client;
        try
        {
          client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
          return; // stopped
        }
        Task served = Task.Run(() => ServeAsync(client));
      }
    }

    private async Task ServeAsync(TcpClient client)
    {
      using (client)
      {
        try
        {
          NetworkStream stream = client.GetStream();
          string line = await ReadLineAsync(stream).ConfigureAwait(false);
          JObject response = line is null ? Error("Request too long or incomplete.") : await HandleAsync(line).ConfigureAwait(false);
          byte[] bytes = new UTF8Encoding(false).GetBytes(response.ToString(Formatting.None) + "\n");
          await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
          await stream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
          // the client went away
        }
      }
    }

    /// <summary>
    /// The request line, null when it is longer than allowed or the connection ends first (or stays silent for 10 seconds).
    /// </summary>
    private static async Task<string> ReadLineAsync(NetworkStream stream)
    {
      List<byte> line = new List<byte>();
      byte[] buffer = new byte[4096];
      using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
      {
        while (line.Count <= MaxRequestLength)
        {
          int read;
          try
          {
            read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false);
          }
          catch (OperationCanceledException)
          {
            return null;
          }
          if (read <= 0)
            return null;
          int newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
          line.AddRange(new ArraySegment<byte>(buffer, 0, newline >= 0 ? newline : read));
          if (newline >= 0)
            return Encoding.UTF8.GetString(line.ToArray());
        }
      }
      return null;
    }

    internal async Task<JObject> HandleAsync(string line)
    {
      JObject request;
      try
      {
        request = JObject.Parse(line);
      }
      catch (JsonException)
      {
        return Error("Not JSON.");
      }
      string token = (string)request["token"];
      if (token is null || !SameToken(token, Token))
        return Error("Wrong token.");

      JObject parameters = request["params"] as JObject ?? new JObject();
      try
      {
        switch ((string)request["method"])
        {
          case "status":
            return Result(await _handler.StatusAsync().ConfigureAwait(false));
          case "locals":
            return Result(await _handler.LocalsAsync((int?)parameters["maxVariables"] ?? 300).ConfigureAwait(false));
          case "read":
            string expression = (string)parameters["expression"];
            if (string.IsNullOrWhiteSpace(expression))
              return Error("The expression is missing.");
            return Result(await _handler.ReadAsync(expression, (string)parameters["schemaId"], (int?)parameters["maxBytes"]).ConfigureAwait(false));
          default:
            return Error(string.Concat("Unknown method: ", (string)request["method"]));
        }
      }
      catch (Exception ex)
      {
        return Error(ex.Message);
      }
    }

    private static bool SameToken(string given, string expected)
    {
      if (given.Length != expected.Length)
        return false;
      int difference = 0;
      for (int t = 0; t < given.Length; t++)
        difference |= given[t] ^ expected[t];
      return difference == 0;
    }

    private static JObject Result(JObject result)
    {
      return new JObject() { ["result"] = result };
    }

    private static JObject Error(string message)
    {
      return new JObject() { ["error"] = message };
    }

    public void Dispose()
    {
      _stop.Cancel();
      try
      {
        _listener.Stop();
      }
      catch (Exception)
      {
      }
      if (_lockFile != null)
      {
        try
        {
          File.Delete(_lockFile);
        }
        catch (Exception)
        {
        }
        _lockFile = null;
      }
    }
  }
}
