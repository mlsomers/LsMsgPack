using LsMsgPack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace LsMsgPackUnitySmoke
{
  /// <summary>
  /// What a game would write: Unity's own structs as members, and a data class the way Unity's serializer wants it (public fields, [SerializeField] private ones).
  /// </summary>
  public class GameState
  {
    public string Level { get; set; }
    public UnityEngine.Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public Color Tint { get; set; }
    public Vector2Int Cell { get; set; }
    public List<UnityEngine.Vector3> Path { get; set; }
  }

  [Serializable]
  public class PlayerData
  {
    public string Name;
    public int Health;
    [SerializeField] private float _speed;

    public float Speed { get { return _speed; } }

    public PlayerData() { }
    public PlayerData(string name, int health, float speed) { Name = name; Health = health; _speed = speed; }
  }

  public static class UnityRunner
  {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InPlayer()
    {
      if (Application.isEditor)
        return;
      string golden = Argument("-golden");
      string output = Argument("-out");
      int failures = Run(golden, output);
      Application.Quit(failures == 0 ? 0 : 1);
    }

    private static string Argument(string name)
    {
      string[] args = Environment.GetCommandLineArgs();
      for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == name)
          return args[i + 1];
      return null;
    }

    /// <summary>
    /// The checks against golden.txt, then the Unity types (reported, compared between the players by their output files).
    /// </summary>
    public static int Run(string goldenPath, string outputPath)
    {
      StringBuilder report = new StringBuilder();
      Action<string> log = line => { report.Append(line).Append('\n'); UnityEngine.Debug.Log("[smoke] " + line); };
      int failures = 0;
      try
      {
#if ENABLE_IL2CPP
        string backend = "IL2CPP";
#else
        string backend = "Mono";
#endif
        log($"{backend}, {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, Unity {Application.unityVersion}, IsDynamicCodeSupported={RuntimeFeature.IsDynamicCodeSupported}, editor={Application.isEditor}");
        if (goldenPath == null)
        {
          log("FAILED: no -golden path");
          failures++;
        }
        else
        {
          string golden = File.ReadAllText(goldenPath);
          SmokeChecks.Create = SmokeChecks.Runtime;
          failures += SmokeChecks.Check(golden, log);
          log($"SmokeContext.IsUsable={SmokeContext.Default.IsUsable}");
          SmokeChecks.Create = Generated;
          failures += SmokeChecks.Check(golden, log, " generated");
        }
        failures += UnityTypes(log);
        Timing(log);
      }
      catch (Exception ex)
      {
        log("FAILED: " + ex);
        failures++;
      }
      log(failures == 0 ? "PASSED" : $"FAILED: {failures}");
      if (outputPath != null)
        File.WriteAllText(outputPath, report.ToString());
      return failures;
    }

    private static LsMsgPackSerializer Generated(LsMsgPackOptions options)
    {
      return new LsMsgPackSerializer(SmokeContext.Default, options);
    }

    private static int UnityTypes(Action<string> log)
    {
      int failures = 0;
      GameState state = new GameState()
      {
        Level = "dungeon-3",
        Position = new UnityEngine.Vector3(1.5f, -2, 30),
        Rotation = Quaternion.Euler(0, 90, 0),
        Tint = new Color(0.25f, 0.5f, 1, 0.75f),
        Cell = new Vector2Int(4, -7),
        Path = new List<UnityEngine.Vector3>() { UnityEngine.Vector3.zero, UnityEngine.Vector3.one }
      };
      // IL2CPP strips Unity's engine assemblies at every level: Vector2Int's x and y, reached only by reflection, are gone without a link.xml (the generated code calls them)
      bool vector2IntStripped = typeof(Vector2Int).GetProperty("x") is null;
      if (vector2IntStripped)
        log("game: Vector2Int's properties were stripped, the runtime plans write it as an empty object (keep it with a link.xml, or use the generator)");
      foreach ((string name, Func<LsMsgPackOptions> options) in new (string, Func<LsMsgPackOptions>)[]
      {
        // Full type names: the invoice's System.Numerics.Vector3 is read in this process too, and a short name ("Vector3") resolves to one type per process
        ("game-full-names", () => new LsMsgPackOptions() { AddTypeIdOptions = AddTypeIdOption.IfAmbiguious | AddTypeIdOption.FullName }),
        ("game-names-map", () => new LsMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map }),
      })
      foreach ((string which, Func<LsMsgPackOptions, LsMsgPackSerializer> create) in new (string, Func<LsMsgPackOptions, LsMsgPackSerializer>)[] { ("", SmokeChecks.Runtime), (" generated", Generated) })
      {
        try
        {
          LsMsgPackSerializer serializer = create(options());
          byte[] bytes = serializer.Serialize(state);
          GameState read = serializer.Deserialize<GameState>(bytes);
          string problem = read.Level != state.Level ? "Level" : read.Position != state.Position ? $"Position {read.Position}" : read.Rotation != state.Rotation ? $"Rotation {read.Rotation}"
            : read.Tint != state.Tint ? $"Tint {read.Tint}" : read.Cell != state.Cell ? $"Cell {read.Cell}" : read.Path == null || read.Path.Count != 2 || read.Path[1] != UnityEngine.Vector3.one ? "Path" : null;
          bool expected = problem != null && problem.StartsWith("Cell", StringComparison.Ordinal) && vector2IntStripped && which.Length == 0;
          log($"{name}{which}: {bytes.Length} bytes {SmokeChecks.Hex(bytes)}: {(problem == null ? "OK" : "other value: " + problem + (expected ? " (stripped, not counted)" : ""))}");
          if (problem != null && !expected)
            failures++;
        }
        catch (Exception ex)
        {
          log($"{name}{which}: FAILED {ex}");
          failures++;
        }
      }

      // Unity's serializer conventions: [SerializeField] opts a private field in (the public fields with SerializedFields.Public, as the unit tests check)
      try
      {
        LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map });
        byte[] bytes = serializer.Serialize(new PlayerData("Kim", 80, 4.5f));
        PlayerData read = serializer.Deserialize<PlayerData>(bytes);
        bool ok = read.Speed == 4.5f;
        log($"player-data: {bytes.Length} bytes {SmokeChecks.Hex(bytes)}, read back Speed={read.Speed}: {(ok ? "OK" : "the [SerializeField] field was not written")}");
        if (!ok)
          failures++;
      }
      catch (Exception ex)
      {
        log("player-data: FAILED " + ex);
        failures++;
      }
      return failures;
    }

    /// <summary>
    /// Rough speed of the invoice (default options), for comparing the backends; not a benchmark.
    /// </summary>
    private static void Timing(Action<string> log)
    {
      Timing(log, "runtime", SmokeChecks.Runtime);
      Timing(log, "generated", Generated);
    }

    private static void Timing(Action<string> log, string which, Func<LsMsgPackOptions, LsMsgPackSerializer> create)
    {
      LsMsgPackSerializer serializer = create(new LsMsgPackOptions() { Converters = new LsMsgPack.Converters.MsgPackConverter[] { new ParcelConverter() } });
      Invoice sample = SmokeChecks.Sample();
      byte[] bytes = serializer.Serialize(sample);
      for (int i = 0; i < 200; i++)
        serializer.Deserialize<Invoice>(serializer.Serialize(sample));
      const int count = 2000;
      Stopwatch watch = Stopwatch.StartNew();
      for (int i = 0; i < count; i++)
        serializer.Serialize(sample);
      double write = watch.Elapsed.TotalMilliseconds * 1000 / count;
      watch.Restart();
      for (int i = 0; i < count; i++)
        serializer.Deserialize<Invoice>(bytes);
      double read = watch.Elapsed.TotalMilliseconds * 1000 / count;
      log($"timing {which} (informational): invoice of {bytes.Length} bytes, write {write:F1} µs, read {read:F1} µs");
    }
  }
}
