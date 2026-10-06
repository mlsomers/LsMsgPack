using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MsgPackDebuggerExtension
{
  /// <summary>
  /// Answers the MCP server with the debugger of Visual Studio (EnvDTE, on the main thread): the current stack frame, as the Locals window shows it.
  /// Reading bytes is the .NET part of the VS Code extension (LsMsgPackVsCode/src/debugBytes.ts, DotNetReader): evaluations of C# in the paused program,
  /// the bytes as base64 in chunks that get smaller when the debugger shortens long strings.
  /// </summary>
  internal sealed class DebuggerBridgeHandler : IBridgeHandler
  {
    private const int EvaluationTimeout = 10000;
    private const int ChunkSize = 49152;
    private const int DefaultMaxBytes = 16 * 1024 * 1024;

    private static readonly Regex SimplePath = new Regex(
      @"^\s*[A-Za-z_$@][\w$]*(?:\s*!?\s*(?:\??\.\s*[A-Za-z_$@][\w$]*|\??\[\s*(?:\d+|""[^""\\]*""|'[^'\\]*')\s*\]))*\s*!?\s*$",
      RegexOptions.CultureInvariant);

    private static readonly Regex SchemaId = new Regex("^[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant);

    private readonly AsyncPackage _package;
    private readonly DTE2 _dte;

    public DebuggerBridgeHandler(AsyncPackage package, DTE2 dte)
    {
      _package = package;
      _dte = dte;
    }

    public string Name
    {
      get
      {
        ThreadHelper.ThrowIfNotOnUIThread();
        return string.Concat("Visual Studio ", _dte.Version);
      }
    }

    /// <summary>
    /// The folder of the open solution.
    /// </summary>
    public List<string> WorkspaceFolders()
    {
      ThreadHelper.ThrowIfNotOnUIThread();
      List<string> folders = new List<string>();
      string solution = _dte.Solution?.FullName;
      if (!string.IsNullOrEmpty(solution))
        folders.Add(Path.GetDirectoryName(solution));
      return folders;
    }

    private McpOptionsPage Options
    {
      get { return (McpOptionsPage)_package.GetDialogPage(typeof(McpOptionsPage)); }
    }

    public async Task<JObject> StatusAsync()
    {
      await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
      Debugger debugger = _dte.Debugger;
      JArray sessions = new JArray();
      if (debugger.CurrentMode != dbgDebugMode.dbgDesignMode && debugger.DebuggedProcesses != null)
      {
        int current = debugger.CurrentProcess?.ProcessID ?? 0;
        foreach (EnvDTE.Process process in debugger.DebuggedProcesses)
        {
          sessions.Add(new JObject()
          {
            ["id"] = process.ProcessID.ToString(CultureInfo.InvariantCulture),
            ["name"] = string.Concat(Path.GetFileName(process.Name), " (pid ", process.ProcessID.ToString(CultureInfo.InvariantCulture), ")"),
            ["type"] = debugger.CurrentMode == dbgDebugMode.dbgBreakMode ? "visualstudio, paused" : "visualstudio, running",
            ["active"] = process.ProcessID == current
          });
        }
      }

      JObject status = new JObject()
      {
        ["ide"] = "visualstudio",
        ["name"] = Name,
        ["workspaceFolders"] = new JArray(WorkspaceFolders()),
        ["sessions"] = sessions,
        ["expressions"] = Options.AllowAnyExpression ? "any" : "paths"
      };
      JObject frame = CurrentFrame();
      if (frame != null)
        status["frame"] = frame;
      return status;
    }

    /// <summary>
    /// The current stack frame while the program is paused, null otherwise.
    /// </summary>
    private JObject CurrentFrame()
    {
      ThreadHelper.ThrowIfNotOnUIThread();
      Debugger debugger = _dte.Debugger;
      if (debugger.CurrentMode != dbgDebugMode.dbgBreakMode || debugger.CurrentStackFrame is null)
        return null;
      StackFrame frame = debugger.CurrentStackFrame;
      JObject info = new JObject() { ["name"] = frame.FunctionName };
      if (frame is EnvDTE90a.StackFrame2 located)
      {
        if (!string.IsNullOrEmpty(located.FileName))
          info["source"] = located.FileName;
        if (located.LineNumber > 0)
          info["line"] = (long)located.LineNumber;
      }
      if (debugger.CurrentProcess != null)
        info["session"] = Path.GetFileName(debugger.CurrentProcess.Name);
      return info;
    }

    public async Task<JObject> LocalsAsync(int maxVariables)
    {
      await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
      CheckEnabled();
      JObject frame = CurrentFrame();
      if (frame is null)
        throw new InvalidOperationException(_dte.Debugger.CurrentMode == dbgDebugMode.dbgDesignMode ? "Visual Studio is not debugging." : "The program is not paused: pause it (e.g. at a breakpoint) first.");

      JArray variables = new JArray();
      bool truncated = false;
      foreach (Expression local in _dte.Debugger.CurrentStackFrame.Locals)
      {
        if (variables.Count >= maxVariables)
        {
          truncated = true;
          break;
        }
        variables.Add(new JObject()
        {
          ["scope"] = "Locals",
          ["name"] = local.Name,
          ["type"] = local.Type,
          ["value"] = local.Value,
          ["evaluateName"] = local.Name
        });
      }
      return new JObject() { ["frame"] = frame, ["variables"] = variables, ["truncated"] = truncated };
    }

    public async Task<JObject> ReadAsync(string expression, string schemaId, int? maxBytes)
    {
      await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
      CheckEnabled();
      if (!Options.AllowAnyExpression && !SimplePath.IsMatch(expression))
        throw new InvalidOperationException(string.Concat("Only variable and member paths are read for AI agents (e.g. buffer, this._payload, items[2].Data), not \"", expression,
          "\". Tools > Options > MsgPack Explorer > AI agents (MCP) allows any expression."));
      if (_dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
        throw new InvalidOperationException("The program is not paused: pause it (e.g. at a breakpoint) first.");

      string target = expression;
      if (schemaId != null)
      {
        if (!SchemaId.IsMatch(schemaId))
          throw new ArgumentException(string.Concat("Not a schema id: ", schemaId));
        target = string.Concat("((LsMsgPack.SchemaStore)(", expression, ")).GetSchema(LsMsgPack.SchemaId.Parse(\"", schemaId, "\"))");
      }

      string session = _dte.Debugger.CurrentProcess != null ? Path.GetFileName(_dte.Debugger.CurrentProcess.Name) : "Visual Studio";
      _dte.StatusBar.Text = string.Concat("MsgPack: an AI agent reads ", expression, "...");
      try
      {
        JObject result = new Reader(this, target, Math.Min(maxBytes ?? DefaultMaxBytes, DefaultMaxBytes), expression).Read();
        result["session"] = session;
        return result;
      }
      finally
      {
        _dte.StatusBar.Text = string.Empty;
      }
    }

    private void CheckEnabled()
    {
      if (!Options.Enabled)
        throw new InvalidOperationException("AI agents are not allowed to read from the debugger (Tools > Options > MsgPack Explorer > AI agents (MCP)).");
    }

    /// <summary>
    /// Evaluates in the current stack frame, the value as the debugger shows it (strings with quotes and escapes).
    /// </summary>
    private string Evaluate(string expression)
    {
      ThreadHelper.ThrowIfNotOnUIThread();
      Expression result = _dte.Debugger.GetExpression(expression, false, EvaluationTimeout);
      if (result is null || !result.IsValidValue)
        throw new InvalidOperationException(string.Concat(expression, ": ", result?.Value ?? "cannot be evaluated"));
      return result.Value;
    }

    private bool IsTrue(string expression)
    {
      ThreadHelper.ThrowIfNotOnUIThread();
      try
      {
        return string.Equals(Evaluate(expression).Trim(), "true", StringComparison.OrdinalIgnoreCase);
      }
      catch (InvalidOperationException)
      {
        return false; // e.g. the type is not loaded in the program
      }
    }

    private bool Confirm(string message)
    {
      ThreadHelper.ThrowIfNotOnUIThread();
      int answer = VsShellUtilities.ShowMessageBox(_package, message, "MsgPack Explorer", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OKCANCEL, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);
      return answer == 1; // IDOK
    }

    /// <summary>
    /// A number as the debugger shows it: 400, 0x00000190 (hexadecimal display).
    /// </summary>
    internal static long ParseInteger(string value)
    {
      Match match = Regex.Match(value ?? string.Empty, @"-?0x[0-9a-fA-F]+|-?\d+");
      if (match.Success)
      {
        string text = match.Value;
        bool negative = text.StartsWith("-", StringComparison.Ordinal);
        string digits = negative ? text.Substring(1) : text;
        long number;
        if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
          ? long.TryParse(digits.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out number)
          : long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number))
          return negative ? -number : number;
      }
      throw new InvalidOperationException(string.Concat("Not a number: ", value));
    }

    /// <summary>
    /// The text of a string as the debugger shows it: without the quotes, with the C# escapes undone.
    /// </summary>
    internal static string Unquote(string value)
    {
      string text = (value ?? string.Empty).Trim();
      bool verbatim = text.StartsWith("@\"", StringComparison.Ordinal);
      if (verbatim)
        text = text.Substring(1);
      if (text.Length < 2 || text[0] != '"' || text[text.Length - 1] != '"')
        return text;
      text = text.Substring(1, text.Length - 2);
      if (verbatim)
        return text.Replace("\"\"", "\"");
      return Regex.Replace(text, @"\\(u[0-9a-fA-F]{4}|x[0-9a-fA-F]{1,4}|.)", m =>
      {
        string escape = m.Groups[1].Value;
        switch (escape[0])
        {
          case 'n': return "\n";
          case 'r': return "\r";
          case 't': return "\t";
          case '0': return "\0";
          case 'u':
          case 'x':
            return ((char)Convert.ToInt32(escape.Substring(1), 16)).ToString();
          default: return escape;
        }
      });
    }

    /// <summary>
    /// Reads one value: byte[], streams (seekable ones are put back at their position), strings, HTTP content, Memory&lt;byte&gt;, IEnumerable&lt;byte&gt;.
    /// </summary>
    private sealed class Reader
    {
      private readonly DebuggerBridgeHandler _owner;
      private readonly string _e;
      private readonly int _maxBytes;
      private readonly string _shown;

      public Reader(DebuggerBridgeHandler owner, string expression, int maxBytes, string shown)
      {
        _owner = owner;
        _e = string.Concat("(", expression, ")");
        _maxBytes = maxBytes;
        _shown = shown;
      }

      public JObject Read()
      {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_owner.IsTrue(string.Concat(_e, " == null")))
          throw new InvalidOperationException("The value is null.");
        if (Is("byte[]"))
          return ReadArray(_e, "byte[]");
        if (Is("System.IO.MemoryStream"))
          return ReadArray(string.Concat("((System.IO.MemoryStream)", _e, ").ToArray()"), "MemoryStream");
        if (Is("System.IO.Stream"))
          return ReadStream(string.Concat("((System.IO.Stream)", _e, ")"));
        if (Is("string"))
          return ReadText(string.Concat("((string)", _e, ")"));
        if (Is("System.Net.Http.HttpResponseMessage"))
          return ReadArray(string.Concat("((System.Net.Http.HttpResponseMessage)", _e, ").Content.ReadAsByteArrayAsync().Result"), "HttpResponseMessage content");
        if (Is("System.Net.Http.HttpContent"))
          return ReadArray(string.Concat("((System.Net.Http.HttpContent)", _e, ").ReadAsByteArrayAsync().Result"), "HttpContent");
        if (Is("System.ReadOnlyMemory<byte>"))
          return ReadArray(string.Concat("((System.ReadOnlyMemory<byte>)", _e, ").ToArray()"), "ReadOnlyMemory<byte>");
        if (Is("System.Memory<byte>"))
          return ReadArray(string.Concat("((System.Memory<byte>)", _e, ").ToArray()"), "Memory<byte>");
        if (Is("System.Collections.Generic.IEnumerable<byte>"))
          return ReadArray(string.Concat("System.Linq.Enumerable.ToArray((System.Collections.Generic.IEnumerable<byte>)", _e, ")"), "IEnumerable<byte>");
        if (Is("System.Buffers.ReadOnlySequence<byte>"))
          return ReadArray(string.Concat("System.Buffers.BuffersExtensions.ToArray((System.Buffers.ReadOnlySequence<byte>)", _e, ")"), "ReadOnlySequence<byte>");
        throw new InvalidOperationException("Not a byte array, stream, list of bytes, (base64) string or HTTP content.");
      }

      private bool Is(string type)
      {
        ThreadHelper.ThrowIfNotOnUIThread();
        return _owner.IsTrue(string.Concat(_e, " is ", type));
      }

      private JObject ReadArray(string array, string description)
      {
        ThreadHelper.ThrowIfNotOnUIThread();
        long length = ParseInteger(_owner.Evaluate(string.Concat(array, ".Length")));
        int count = (int)Math.Min(length, _maxBytes);
        byte[] bytes = ReadChunks(count, (offset, n) =>
        {
          ThreadHelper.ThrowIfNotOnUIThread();
          return _owner.Evaluate(string.Concat("System.Convert.ToBase64String(", array, ", ", offset.ToString(CultureInfo.InvariantCulture), ", ", n.ToString(CultureInfo.InvariantCulture), ")"));
        });
        return Bytes(bytes, description, length > _maxBytes ? string.Concat("Only the first ", _maxBytes.ToString(CultureInfo.InvariantCulture), " of ", length.ToString(CultureInfo.InvariantCulture), " bytes were read.") : null);
      }

      /// <summary>
      /// Like the visualizer: a seekable stream is read from the start and put back at its position, any other stream is read from where it is
      /// (after the user agrees), and the program cannot read those bytes any more.
      /// </summary>
      private JObject ReadStream(string stream)
      {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!_owner.IsTrue(string.Concat(stream, ".CanRead")))
          throw new InvalidOperationException("The stream cannot be read (CanRead is false).");
        string name = Unquote(_owner.Evaluate(string.Concat(stream, ".GetType().Name")));
        Func<int, string> read = n =>
        {
          ThreadHelper.ThrowIfNotOnUIThread();
          return _owner.Evaluate(string.Concat("System.Convert.ToBase64String(new System.IO.BinaryReader(", stream, ").ReadBytes(", n.ToString(CultureInfo.InvariantCulture), "))"));
        };

        if (_owner.IsTrue(string.Concat(stream, ".CanSeek")))
        {
          long position = ParseInteger(_owner.Evaluate(string.Concat(stream, ".Position")));
          long length = ParseInteger(_owner.Evaluate(string.Concat(stream, ".Length")));
          int count = (int)Math.Min(length, _maxBytes);
          try
          {
            byte[] bytes = ReadChunks(count, (offset, n) =>
            {
              ThreadHelper.ThrowIfNotOnUIThread();
              _owner.Evaluate(string.Concat(stream, ".Seek(", offset.ToString(CultureInfo.InvariantCulture), ", System.IO.SeekOrigin.Begin)"));
              return read(n);
            });
            return Bytes(bytes, name, length > _maxBytes ? string.Concat("Only the first ", _maxBytes.ToString(CultureInfo.InvariantCulture), " of ", length.ToString(CultureInfo.InvariantCulture), " bytes were read.") : null);
          }
          finally
          {
            _owner.Evaluate(string.Concat(stream, ".Seek(", position.ToString(CultureInfo.InvariantCulture), ", System.IO.SeekOrigin.Begin)"));
          }
        }

        if (!_owner.Confirm(string.Concat("An AI agent (MCP) asks to read ", _shown, ". The ", name,
          " cannot seek: its bytes will be read from the current position and the program cannot read them any more.\n\nRead anyway?")))
          throw new InvalidOperationException("The user did not allow reading the stream.");

        MemoryStream all = new MemoryStream();
        const int size = 3072; // small chunks: a shortened result cannot be read again
        while (all.Length < _maxBytes)
        {
          int n = (int)Math.Min(size, _maxBytes - all.Length);
          byte[] part = Convert.FromBase64String(Unquote(read(n)));
          all.Write(part, 0, part.Length);
          if (part.Length < n)
            break;
        }
        return Bytes(all.ToArray(), name, all.Length >= _maxBytes ? string.Concat("Only the first ", all.Length.ToString(CultureInfo.InvariantCulture), " bytes were read.") : "The stream cannot seek, the bytes read are gone for the program.");
      }

      /// <summary>
      /// A string holding the bytes (base64 like the visualizer, or hex...): the MCP server converts the text.
      /// </summary>
      private JObject ReadText(string text)
      {
        ThreadHelper.ThrowIfNotOnUIThread();
        long length = ParseInteger(_owner.Evaluate(string.Concat(text, ".Length")));
        StringBuilder value = new StringBuilder();
        for (long offset = 0; offset < length;)
        {
          long n = Math.Min(ChunkSize, length - offset);
          string part = Unquote(_owner.Evaluate(string.Concat(text, ".Substring(", offset.ToString(CultureInfo.InvariantCulture), ", ", n.ToString(CultureInfo.InvariantCulture), ")")));
          if (part.Length == 0)
            throw new InvalidOperationException("The debugger returned an empty part of the string.");
          value.Append(part);
          offset += part.Length;
        }
        return new JObject() { ["text"] = value.ToString(), ["description"] = "string", ["length"] = length };
      }

      /// <summary>
      /// Reads base64 chunks, a chunk that comes back shorter (the debugger shortened the string) is read again in smaller parts.
      /// </summary>
      private static byte[] ReadChunks(int length, Func<int, int, string> chunk)
      {
        byte[] bytes = new byte[length];
        int size = ChunkSize / 3 * 3; // a multiple of 3: no padding in the middle
        int offset = 0;
        while (offset < length)
        {
          int count = Math.Min(size, length - offset);
          string text = chunk(offset, count);
          byte[] part;
          try
          {
            part = Convert.FromBase64String(Unquote(text));
          }
          catch (FormatException)
          {
            part = null; // a shortened string usually ends with "..." instead of the quote
          }
          if (part is null || part.Length != count)
          {
            if (size <= 3)
              throw new InvalidOperationException(string.Concat("The debugger returned something else than ", count.ToString(CultureInfo.InvariantCulture), " bytes at offset ", offset.ToString(CultureInfo.InvariantCulture), "."));
            size = Math.Max(3, size / 2 / 3 * 3);
            continue;
          }
          Buffer.BlockCopy(part, 0, bytes, offset, count);
          offset += count;
        }
        return bytes;
      }

      private static JObject Bytes(byte[] bytes, string description, string warning)
      {
        JObject result = new JObject() { ["base64"] = Convert.ToBase64String(bytes), ["description"] = description, ["length"] = bytes.Length };
        if (warning != null)
          result["warning"] = warning;
        return result;
      }
    }
  }
}
