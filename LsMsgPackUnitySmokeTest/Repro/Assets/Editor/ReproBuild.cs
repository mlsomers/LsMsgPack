using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

public static class ReproBuild
{
  public static void Build()
  {
    string[] args = Environment.GetCommandLineArgs();
    string variant = args[Array.IndexOf(args, "-variant") + 1];
    if (AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Repro.unity") == null)
    {
      AssetDatabase.CreateFolder("Assets", "Scenes");
      EditorSceneManager.SaveScene(EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single), "Assets/Scenes/Repro.unity");
    }
    PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, variant);
    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
    BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/Repro.unity" }, locationPathName = args[Array.IndexOf(args, "-player") + 1], target = BuildTarget.StandaloneLinux64 });
    UnityEngine.Debug.Log("[repro] build " + variant + ": " + report.summary.result);
    EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
  }
}
