using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace MsgPackExplorer {
  public class Installer {
    private static readonly KeyValuePair<Environment.SpecialFolder,string>[] KnownDestinations = new[] { 
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.UserProfile,"AppData\\Local\\Programs\\Fiddler\\Inspectors"), // Installed using Chocolatey
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFiles,"Fiddler\\Inspectors"),
      new KeyValuePair<Environment.SpecialFolder,string> (Environment.SpecialFolder.ProgramFilesX86,"Fiddler\\Inspectors")
    };

    private static readonly string[] files = new[] {
      "LsMsgPack.dll",
      "LsMsgPackFiddlerInspector.dll",
      "MsgPackExplorer.exe" 
    };

    public static bool FiddlerIsRunning {
      get {
        Process[] proc = Process.GetProcessesByName("Fiddler");
        return proc.Length > 0;
      }
    }

    public static bool TryInstall() {

      string[] source = new string[files.Length];
      string baseDir = AppDomain.CurrentDomain.BaseDirectory;
      for (int t = source.Length - 1; t >= 0; t--) {
        string path = Path.Combine(baseDir, files[t]);
        if (!File.Exists(path))
          throw new Exception("Could not find source file " + path);
        source[t] = path;
      }

      bool success = false;

      foreach (KeyValuePair<Environment.SpecialFolder, string> destination in KnownDestinations) {
        string destDir = Path.Combine(Environment.GetFolderPath(destination.Key), destination.Value);
        if (Directory.Exists(destDir)) {
          for (int t = files.Length - 1; t >= 0; t--) {
            string destPath = Path.Combine(destDir, files[t]);
            if (File.Exists(destPath)) {
              if (FiddlerIsRunning)
                throw new Exception("Found previous installation, please close fiddler and try again in order to replace the files.");
              try {
                File.Delete(destPath);
              }catch(Exception ex) {
                throw new Exception("Unable to remove old version:\r\n  " + ex.Message);
              }
            }
            File.Copy(source[t], destPath);
            success = true;
          }
        }
      }

      return success;
    }
  }
}
