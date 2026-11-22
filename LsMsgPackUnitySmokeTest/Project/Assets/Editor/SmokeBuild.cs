using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LsMsgPackUnitySmoke.Editor
{
  /// <summary>
  /// Called headless with -executeMethod (run.sh): builds Linux, WebGL or Android players, or runs the checks in the editor (Mono JIT).
  /// Arguments: -target Linux|WebGL|Android, -backend Mono|IL2CPP (WebGL, Android: always IL2CPP), -stripping Disabled|Minimal|Low|Medium|High, -keep, -player path (WebGL: a folder), -golden path, -out path.
  /// </summary>
  public static class SmokeBuild
  {
    private const string ScenePath = "Assets/Scenes/Smoke.unity";

    public static void Build()
    {
      bool webGL = Argument("-target") == "WebGL";
      bool android = Argument("-target") == "Android";
      ScriptingImplementation backend = webGL || android || Argument("-backend") == "IL2CPP" ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x;
      ManagedStrippingLevel stripping = (ManagedStrippingLevel)Enum.Parse(typeof(ManagedStrippingLevel), Argument("-stripping") ?? "Minimal");
      if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
      {
        AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single), ScenePath);
      }
      // -keep: the application keeps its model from the managed code stripper (as LsMsgPackAotReflectionTest's Model.xml for NativeAOT)
      const string linkXml = "Assets/link.xml";
      if (Array.IndexOf(Environment.GetCommandLineArgs(), "-keep") >= 0)
        System.IO.File.WriteAllText(linkXml, "<linker>\n  <assembly fullname=\"Assembly-CSharp\">\n    <namespace fullname=\"LsMsgPackUnitySmoke\" preserve=\"all\" />\n  </assembly>\n"
          // Unity's engine assemblies are stripped at every level: its structs read by reflection that are not structs of public fields (no attribute types: LsMsgPack.Core names the ones it reads by name)
          + "  <assembly fullname=\"UnityEngine.CoreModule\">\n    <type fullname=\"UnityEngine.Vector2Int\" preserve=\"all\" />\n    <type fullname=\"UnityEngine.Rect\" preserve=\"all\" />\n"
          + "    <type fullname=\"UnityEngine.Bounds\" preserve=\"all\" />\n  </assembly>\n</linker>\n");
      else
        AssetDatabase.DeleteAsset(linkXml);
      AssetDatabase.Refresh();
      NamedBuildTarget named = webGL ? NamedBuildTarget.WebGL : android ? NamedBuildTarget.Android : NamedBuildTarget.Standalone;
      PlayerSettings.SetScriptingBackend(named, backend);
      PlayerSettings.SetManagedStrippingLevel(named, stripping);
      if (webGL)
      {
        // Uncompressed: served by serve.py, which sets no Content-Encoding. The exceptions are Unity's default (explicitly thrown ones only), as a game ships
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        Debug.Log($"[smoke] WebGL exception support {PlayerSettings.WebGL.exceptionSupport}");
      }
      if (android)
      {
        // An APK for adb install (no app bundle), 64-bit ARM, signed with the debug key; plain HTTP to run.sh's server through adb reverse (127.0.0.1 on the phone)
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.lsmsgpack.smoke");
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        EditorUserBuildSettings.buildAppBundle = false;
      }
      BuildPlayerOptions options = new BuildPlayerOptions()
      {
        scenes = new[] { ScenePath },
        locationPathName = Argument("-player"),
        target = webGL ? BuildTarget.WebGL : android ? BuildTarget.Android : BuildTarget.StandaloneLinux64,
        options = BuildOptions.None
      };
      BuildReport report = BuildPipeline.BuildPlayer(options);
      Debug.Log($"[smoke] build {options.target} {backend} stripping {stripping}{(System.IO.File.Exists(linkXml) ? " + link.xml" : "")}: {report.summary.result}, {report.summary.totalSize} bytes, {report.summary.totalTime}");
      EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    public static void RunInEditor()
    {
      int failures = UnityRunner.Run(Argument("-golden"), Argument("-out"));
      EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static string Argument(string name)
    {
      string[] args = Environment.GetCommandLineArgs();
      for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == name)
          return args[i + 1];
      return null;
    }
  }
}
