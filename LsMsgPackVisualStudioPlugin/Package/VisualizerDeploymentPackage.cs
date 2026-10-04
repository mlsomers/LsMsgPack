using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace MsgPackDebuggerExtension
{
  /// <summary>
  /// Visual Studio looks for debugger visualizers in Common7\Packages\Debugger\Visualizers of its install folder and in
  /// Documents\Visual Studio NN\Visualizers, not in the folders of extensions, and a VSIX cannot install into either
  /// (InstallRoot only allows a few other folders). So the VSIX carries the visualizer in Debugger\Visualizers and this
  /// package copies it to the user's Visualizers folder when Visual Studio starts, before a debugging session looks for it.
  /// </summary>
  [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
  [Guid(PackageGuidString)]
  [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
  public sealed class VisualizerDeploymentPackage : AsyncPackage
  {
    public const string PackageGuidString = "4cb0c0e2-8acb-46ea-93df-f64bfb300ded";

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
      await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
      IVsShell shell = await GetServiceAsync(typeof(SVsShell)) as IVsShell;
      object userDir = null;
      // "Documents\Visual Studio 2022" or "Documents\Visual Studio 18", or wherever the user moved it.
      if (shell == null || ErrorHandler.Failed(shell.GetProperty((int)__VSSPROPID2.VSSPROPID_VisualStudioDir, out userDir)) || !(userDir is string))
        return;

      await System.Threading.Tasks.TaskScheduler.Default; // continue on a background thread
      string sourceDir = Path.Combine(Path.GetDirectoryName(typeof(VisualizerDeploymentPackage).Assembly.Location), "Debugger", "Visualizers");
      string destDir = Path.Combine((string)userDir, "Visualizers");
      List<string> errors = new List<string>();
      CopyChangedFiles(sourceDir, destDir, errors);

      if (errors.Count > 0)
      {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        foreach (string error in errors)
          ActivityLog.TryLogWarning(nameof(VisualizerDeploymentPackage), error);
      }
    }

    /// <summary>
    /// Copies the files (with the netstandard2.0 subfolder) that are missing or differ, so an update of the extension
    /// replaces the visualizer and an unchanged one costs a few file lookups per start.
    /// </summary>
    private static void CopyChangedFiles(string sourceDir, string destDir, List<string> errors)
    {
      if (!Directory.Exists(sourceDir))
      {
        errors.Add("Visualizer files not found in " + sourceDir);
        return;
      }

      foreach (string source in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
      {
        string dest = Path.Combine(destDir, source.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar));
        try
        {
          FileInfo sourceInfo = new FileInfo(source);
          FileInfo destInfo = new FileInfo(dest);
          // File.Copy keeps the last write time, so equal files are recognized on the next start.
          if (destInfo.Exists && destInfo.Length == sourceInfo.Length && destInfo.LastWriteTimeUtc == sourceInfo.LastWriteTimeUtc)
            continue;
          Directory.CreateDirectory(destInfo.DirectoryName);
          File.Copy(source, dest, true);
        }
        catch (Exception ex) // e.g. another instance of Visual Studio is debugging with the old version loaded, retried on the next start
        {
          errors.Add("Could not copy " + source + " to " + dest + ": " + ex.Message);
        }
      }
    }
  }
}
