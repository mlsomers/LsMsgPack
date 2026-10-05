using Microsoft.VisualStudio.Shell;
using System.ComponentModel;

namespace MsgPackDebuggerExtension
{
  /// <summary>
  /// Tools &gt; Options &gt; MsgPack Explorer &gt; AI agents (MCP).
  /// </summary>
  public class McpOptionsPage : DialogPage
  {
    [Category("AI agents (MCP)")]
    [DisplayName("Allow reading from the debugger")]
    [Description("Lets AI agents read MsgPack bytes from variables of the paused program through the MsgPack MCP server (lsmsgpack-mcp). It finds Visual Studio through a lock file in %USERPROFILE%\\.lsmsgpack\\ide and needs the random token in it. Takes effect for the next request.")]
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    [Category("AI agents (MCP)")]
    [DisplayName("Allow any expression")]
    [Description("Lets AI agents read any expression, including method calls that can change the debugged program. Off: only variable and member paths (e.g. buffer, this._payload, items[2].Data).")]
    [DefaultValue(false)]
    public bool AllowAnyExpression { get; set; }
  }
}
