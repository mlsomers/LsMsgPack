using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MsgPackExplorer {
  public class Installer {
    private static readonly KeyValuePair<Environment.SpecialFolder,string>[] KnownDestinations = new[] { 
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.UserProfile,"AppData\\Local\\Programs\\Fiddler\\Inspectors"), // Installed using Chocolatey
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFiles,"Fiddler\\Inspectors"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFilesX86,"Fiddler\\Inspectors")
    };

    private static readonly KeyValuePair<Environment.SpecialFolder,string>[] KnownDestinationsVs = new[] {
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.MyDocuments,"Visual Studio 2005\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.MyDocuments,"Visual Studio 2008\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.MyDocuments,"Visual Studio 2012\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.MyDocuments,"Visual Studio 2013\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.MyDocuments,"Visual Studio 2015\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.MyDocuments,"Visual Studio 2017\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.MyDocuments,"Visual Studio 2019\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.MyDocuments,"Visual Studio 2022\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFilesX86,"Microsoft Visual Studio 8\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFilesX86,"Microsoft Visual Studio 9.0\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFilesX86,"Microsoft Visual Studio 10.0\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFilesX86,"Microsoft Visual Studio 11.0\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFilesX86,"Microsoft Visual Studio 12.0\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFilesX86,"Microsoft Visual Studio 14.0\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFilesX86,"Microsoft Visual Studio\\2017\\Enterprise\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFilesX86,"Microsoft Visual Studio\\2019\\Enterprise\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFilesX86,"Microsoft Visual Studio\\2022\\Enterprise\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFiles,"Microsoft Visual Studio 10.0\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFiles,"Microsoft Visual Studio 11.0\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFiles,"Microsoft Visual Studio 12.0\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFiles,"Microsoft Visual Studio 14.0\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFiles,"Microsoft Visual Studio\\2017\\Community\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFiles,"Microsoft Visual Studio\\2017\\Professional\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFiles,"Microsoft Visual Studio\\2017\\Enterprise\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFiles,"Microsoft Visual Studio\\2019\\Community\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFiles,"Microsoft Visual Studio\\2019\\Professional\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFiles,"Microsoft Visual Studio\\2019\\Enterprise\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFiles,"Microsoft Visual Studio\\2022\\Community\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFiles,"Microsoft Visual Studio\\2022\\Professional\\Common7\\Packages\\Debugger\\Visualizers"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFiles,"Microsoft Visual Studio\\2022\\Enterprise\\Common7\\Packages\\Debugger\\Visualizers")
    };

    private static readonly string[] files = new[] {
      "LsMsgPack.dll",
      "LsMsgPack.Core.dll",
      "ObjectDebugger.dll",
      "LsMsgPackFiddlerInspector.dll",
      "MsgPackExplorer.exe"
    };

    private static readonly string[] filesVs = new[] {
      "LsMsgPack.dll",
      "LsMsgPack.Core.dll",
      "ObjectDebugger.dll",
      "LsMsgPackVisualStudioPlugin.dll",
      "MsgPackExplorer.exe"
    };

    // The netstandard2.0 libraries use System.Memory (Span), which .NET Framework does not have.
    // Copied when the build placed them next to the explorer, the host (Fiddler, Visual Studio) may already have them.
    private static readonly string[] dependencies = new[] {
      "System.Memory.dll",
      "System.Buffers.dll",
      "System.Numerics.Vectors.dll",
      "System.Runtime.CompilerServices.Unsafe.dll"
    };

    public static bool FiddlerIsRunning {
      get {
        Process[] proc = Process.GetProcessesByName("Fiddler");
        return proc.Length > 0;
      }
    }

    public static bool VsIsRunning {
      get {
        Process[] proc = Process.GetProcessesByName("Devenv");
        return proc.Length > 0;
      }
    }

    public static string TryInstall(bool vs) {

      List<string> sourceFiles = new List<string>();
      string baseDir = AppDomain.CurrentDomain.BaseDirectory;
      foreach (string file in vs ? filesVs : files) {
        string path = Path.Combine(baseDir, file);
        if (!File.Exists(path))
          throw new Exception("Could not find source file " + path);
        sourceFiles.Add(path);
      }
      foreach (string file in dependencies) {
        string path = Path.Combine(baseDir, file);
        if (File.Exists(path))
          sourceFiles.Add(path);
      }
      string[] source = sourceFiles.ToArray();

      List<string> filesCopied= new List<string>();

      KeyValuePair<Environment.SpecialFolder,string>[] potentialDestinations= vs? KnownDestinationsVs : KnownDestinations;

      foreach (KeyValuePair<Environment.SpecialFolder, string> destination in potentialDestinations) {
        string destDir = Path.Combine(Environment.GetFolderPath(destination.Key), destination.Value);
        if (Directory.Exists(destDir)) {
          bool localSuccess=false;
          for (int t = source.Length - 1; t >= 0; t--) {
            string filename = Path.GetFileName(source[t]);
            string destPath = Path.Combine(destDir, filename);
            if (File.Exists(destPath)) {
              try {
                File.Delete(destPath);
              }catch(Exception ex) {
                if (vs ? VsIsRunning : FiddlerIsRunning)
                  throw new Exception("Found previous installation, please close fiddler or Visual studio and try again in order to replace the files.");
                throw new Exception("Unable to remove old version:\r\n  " + ex.Message);
              }
            }
            File.Copy(source[t], destPath, true);
            filesCopied.Add(destPath);
            localSuccess=true;
          }
          if (vs && localSuccess)
          {
            destDir = Path.Combine(destDir,"netstandard2.0");
            if (!Directory.Exists(destDir))
              Directory.CreateDirectory(destDir);
            string destPath = Path.Combine(destDir, "DebuggerProxy.dll");
            if (File.Exists(destPath)) {
              try {
                File.Delete(destPath);
              }catch(Exception ex) {
                if (VsIsRunning)
                  throw new Exception("Found previous installation, please close Visual studio and try again in order to replace the files.");
                throw new Exception("Unable to remove old version:\r\n  " + ex.Message);
              }
            }
            File.Copy("DebuggerProxy.dll", destPath, true);
            filesCopied.Add(destPath);
          }
        }
      }

      return "  " + string.Join("\r\n  ", filesCopied);
    }

    public static string UnInstall(bool vs)
    {
      HashSet<string> src= new HashSet<string>(files);
      src.UnionWith(filesVs);
      src.UnionWith(dependencies);
      string[] source= src.ToArray();

      List<string> filesRemoved = new List<string>();
      List<string> filesCouldNotRemove = new List<string>();

      KeyValuePair<Environment.SpecialFolder, string>[] potentialDestinations = vs ? KnownDestinationsVs : KnownDestinations;

      foreach (KeyValuePair<Environment.SpecialFolder, string> destination in potentialDestinations)
      {
        string destDir = Path.Combine(Environment.GetFolderPath(destination.Key), destination.Value);
        if (Directory.Exists(destDir))
        {
          for (int t = source.Length - 1; t >= 0; t--)
          {
            string filename = Path.GetFileName(source[t]);
            string destPath = Path.Combine(destDir, filename);
            if (File.Exists(destPath))
            {
              try
              {
                File.Delete(destPath);
                filesRemoved.Add(destPath);
              }
              catch (Exception ex)
              {
                filesCouldNotRemove.Add(destPath);
                Trace.WriteLine(ex.Message);
              }
            }
          }
          if (vs)
          {
            destDir = Path.Combine(destDir, "netstandard2.0");
            if (!Directory.Exists(destDir))
              continue;
            string destPath = Path.Combine(destDir, "DebuggerProxy.dll");
            if (File.Exists(destPath))
            {
              try
              {
                File.Delete(destPath);
                filesRemoved.Add(destPath);
              }
              catch (Exception ex)
              {
                filesCouldNotRemove.Add(destPath);
                Trace.WriteLine(ex.Message);
              }
            }
          }
        }
      }

      if (filesCouldNotRemove.Count > 0)
      {
        return $"Unable to remove the following files:\r\n  {string.Join("\r\n  ", filesCouldNotRemove)}\r\n\r\nThe following were successfully removed:\r\n  {string.Join("\r\n  ", filesRemoved)}";
      }
      return $"Successfully removed the following files:\r\n  {string.Join("\r\n  ", filesRemoved)}";
    }
  }
}
