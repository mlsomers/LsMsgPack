using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackMcp
{
  /// <summary>
  /// A running IDE (VS Code or Visual Studio with the MsgPack Explorer extension) that reads bytes from its debugger for the MCP server.
  /// <para>The extension listens on 127.0.0.1 and writes a lock file in <see cref="IdeBridge.LockDirectory"/> with its port and a random token
  /// (only the user can read it). When the extension starts the MCP server itself, it passes them in <see cref="IdeBridge.PortVariable"/> and <see cref="IdeBridge.TokenVariable"/>.</para>
  /// </summary>
  public sealed class IdeConnection
  {
    /// <summary>"vscode" or "visualstudio"</summary>
    public string Ide { get; set; }

    /// <summary>E.g. "Visual Studio Code 1.105.0", for the agent and the user</summary>
    public string Name { get; set; }

    public int Pid { get; set; }
    public int Port { get; set; }
    public string Token { get; set; }
    public List<string> WorkspaceFolders { get; set; } = new List<string>();

    /// <summary>
    /// The lock file, null when the connection came from the environment.
    /// </summary>
    public string File { get; set; }

    public override string ToString()
    {
      string folders = WorkspaceFolders.Count == 0 ? "no folder" : string.Join(", ", WorkspaceFolders);
      return string.Concat(Name ?? Ide, " (pid ", Pid.ToString(System.Globalization.CultureInfo.InvariantCulture), ", ", folders, ")");
    }
  }

  public class IdeBridgeException : Exception
  {
    public IdeBridgeException(string message) : base(message) { }
    public IdeBridgeException(string message, Exception inner) : base(message, inner) { }
  }

  /// <summary>
  /// Finds the IDEs and sends them requests: one JSON line per connection (<c>{"token", "method", "params"}</c>), answered by one line (<c>{"result"}</c> or <c>{"error"}</c>).
  /// </summary>
  public static class IdeBridge
  {
    public const string PortVariable = "LSMSGPACK_IDE_PORT";
    public const string TokenVariable = "LSMSGPACK_IDE_TOKEN";

    /// <summary>
    /// Overrides <see cref="LockDirectory"/> (tests, or another location).
    /// </summary>
    public const string DirectoryVariable = "LSMSGPACK_IDE_DIR";

    public static string LockDirectory
    {
      get
      {
        string dir = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (!string.IsNullOrEmpty(dir))
          return dir;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lsmsgpack", "ide");
      }
    }

    /// <summary>
    /// The IDE that started this process (environment), otherwise the IDEs with a lock file whose process still runs.
    /// </summary>
    public static List<IdeConnection> Discover()
    {
      List<IdeConnection> found = new List<IdeConnection>();
      string port = Environment.GetEnvironmentVariable(PortVariable);
      string token = Environment.GetEnvironmentVariable(TokenVariable);
      int portNumber;
      if (!string.IsNullOrEmpty(port) && int.TryParse(port, out portNumber) && !string.IsNullOrEmpty(token))
      {
        found.Add(new IdeConnection() { Ide = "ide", Name = "the IDE that started this server", Port = portNumber, Token = token });
        return found;
      }

      string dir = LockDirectory;
      if (!Directory.Exists(dir))
        return found;
      foreach (string file in Directory.GetFiles(dir, "*.json"))
      {
        IdeConnection connection = ReadLockFile(file);
        if (connection != null && IsRunning(connection.Pid))
          found.Add(connection);
      }
      return found;
    }

    internal static IdeConnection ReadLockFile(string file)
    {
      try
      {
        JsonNode json = JsonNode.Parse(System.IO.File.ReadAllText(file));
        IdeConnection connection = new IdeConnection()
        {
          Ide = (string)json["ide"],
          Name = (string)json["name"],
          Pid = (int?)json["pid"] ?? 0,
          Port = (int?)json["port"] ?? 0,
          Token = (string)json["token"],
          File = file
        };
        if (json["workspaceFolders"] is JsonArray folders)
          foreach (JsonNode folder in folders)
            if (folder != null)
              connection.WorkspaceFolders.Add((string)folder);
        return connection.Port > 0 && !string.IsNullOrEmpty(connection.Token) ? connection : null;
      }
      catch (Exception)
      {
        return null; // being written, or not ours
      }
    }

    private static bool IsRunning(int pid)
    {
      if (pid <= 0)
        return true; // unknown, try it
      try
      {
        using (Process process = Process.GetProcessById(pid))
          return !process.HasExited;
      }
      catch (Exception)
      {
        return false;
      }
    }

    /// <summary>
    /// The IDE to ask: the one named (its pid, "vscode" or "visualstudio"), otherwise the only one, otherwise the one whose workspace holds the current directory.
    /// </summary>
    /// <exception cref="IdeBridgeException">None found, or several and none can be picked</exception>
    public static IdeConnection Pick(List<IdeConnection> connections, string wanted, string currentDirectory)
    {
      if (connections.Count == 0)
        throw new IdeBridgeException(
          "No IDE with the MsgPack Explorer extension is running (VS Code: the \"MsgPack Explorer (LsMsgPack)\" extension, Visual Studio: the \"MsgPack Debugger Extension\"). " +
          "Start debugging in one of them, or pass the bytes to msgpack_decode instead.");

      List<IdeConnection> candidates = connections;
      if (!string.IsNullOrWhiteSpace(wanted))
      {
        candidates = connections.FindAll(c => string.Equals(c.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture), wanted.Trim(), StringComparison.Ordinal)
          || string.Equals(c.Ide, wanted.Trim(), StringComparison.OrdinalIgnoreCase));
        if (candidates.Count == 0)
          throw new IdeBridgeException(string.Concat("No IDE matches \"", wanted, "\". Running: ", Describe(connections)));
      }
      if (candidates.Count == 1)
        return candidates[0];

      // The IDE whose workspace holds the directory the agent works in (longest folder wins)
      IdeConnection best = null;
      int bestLength = -1;
      string current = NormalizeFolder(currentDirectory);
      foreach (IdeConnection connection in candidates)
      {
        foreach (string folder in connection.WorkspaceFolders)
        {
          string normalized = NormalizeFolder(folder);
          if (normalized.Length > bestLength && (current.Equals(normalized, PathComparison) || current.StartsWith(normalized + "/", PathComparison) || normalized.StartsWith(current + "/", PathComparison)))
          {
            best = connection;
            bestLength = normalized.Length;
          }
        }
      }
      if (best != null)
        return best;
      throw new IdeBridgeException(string.Concat("Several IDEs are running, pass ide (a pid, or vscode / visualstudio) to pick one: ", Describe(candidates)));
    }

    private static StringComparison PathComparison
    {
      get { return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal; }
    }

    private static string NormalizeFolder(string folder)
    {
      if (string.IsNullOrEmpty(folder))
        return string.Empty;
      return folder.Replace('\\', '/').TrimEnd('/');
    }

    internal static string Describe(List<IdeConnection> connections)
    {
      List<string> texts = connections.ConvertAll(c => c.ToString());
      return string.Join("; ", texts);
    }

    /// <summary>
    /// Sends one request and returns its result.
    /// </summary>
    /// <exception cref="IdeBridgeException">The IDE could not be reached or answered with an error</exception>
    public static async Task<JsonNode> RequestAsync(IdeConnection connection, string method, JsonObject parameters, TimeSpan timeout, CancellationToken cancellation)
    {
      JsonObject request = new JsonObject()
      {
        ["token"] = connection.Token,
        ["method"] = method,
        ["params"] = parameters ?? new JsonObject()
      };

      using (CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
      using (TcpClient client = new TcpClient())
      {
        timeoutSource.CancelAfter(timeout);
        string line;
        try
        {
          await client.ConnectAsync("127.0.0.1", connection.Port, timeoutSource.Token).ConfigureAwait(false);
          using (NetworkStream stream = client.GetStream())
          {
            byte[] bytes = Encoding.UTF8.GetBytes(request.ToJsonString() + "\n");
            await stream.WriteAsync(bytes, 0, bytes.Length, timeoutSource.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeoutSource.Token).ConfigureAwait(false);
            using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false)))
              line = await reader.ReadLineAsync(timeoutSource.Token).ConfigureAwait(false);
          }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
          throw new IdeBridgeException(string.Concat(connection.ToString(), " did not answer within ", timeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), " seconds."));
        }
        catch (SocketException ex)
        {
          throw new IdeBridgeException(string.Concat("Could not reach ", connection.ToString(), ": ", ex.Message, ". Is it still running?"), ex);
        }
        catch (IOException ex)
        {
          throw new IdeBridgeException(string.Concat("The connection to ", connection.ToString(), " failed: ", ex.Message), ex);
        }

        if (string.IsNullOrEmpty(line))
          throw new IdeBridgeException(string.Concat(connection.ToString(), " closed the connection without an answer."));

        JsonNode response;
        try
        {
          response = JsonNode.Parse(line);
        }
        catch (JsonException ex)
        {
          throw new IdeBridgeException(string.Concat(connection.ToString(), " answered something else than JSON."), ex);
        }
        string error = (string)response?["error"];
        if (error != null)
          throw new IdeBridgeException(error);
        return response?["result"];
      }
    }
  }
}
