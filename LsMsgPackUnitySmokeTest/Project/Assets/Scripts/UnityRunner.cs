using LsMsgPack;
using LsMsgPack.Converters;
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
    /// <summary>By its settable properties, which overlap: x, y, position, center, min, max, width, height, size, xMin...</summary>
    public Rect Area { get; set; }
    /// <summary>By its settable properties: center, extents, size, min, max.</summary>
    public Bounds Box { get; set; }
    /// <summary>The converter of docs/Unity.md: [x, y, width, height].</summary>
    [MsgPackConverter(typeof(RectConverter))]
    public Rect View { get; set; }
    /// <summary>Its memory as bin (24 bytes: center and extents).</summary>
    [MsgPackConverter(typeof(BlitConverter<Bounds>))]
    public Bounds Volume { get; set; }
  }

  /// <summary>
  /// A Rect as x, y, width and height (docs/Unity.md), instead of its overlapping settable properties.
  /// </summary>
  public sealed class RectConverter : MsgPackConverter<Rect, UnityEngine.Vector4>
  {
    public override UnityEngine.Vector4 ToSurrogate(Rect value) { return new UnityEngine.Vector4(value.x, value.y, value.width, value.height); }
    public override Rect FromSurrogate(UnityEngine.Vector4 surrogate) { return new Rect(surrogate.x, surrogate.y, surrogate.z, surrogate.w); }
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
#if UNITY_WEBGL
      // No command line or files in a browser: WebRunner fetches golden.txt and posts the report to the page's server (webgl-serve.py)
      new GameObject("smoke").AddComponent<WebRunner>();
#else
      string golden = Argument("-golden");
      string output = Argument("-out");
      int failures = Run(golden, output);
      Application.Quit(failures == 0 ? 0 : 1);
#endif
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
      int failures = Run(goldenPath == null ? null : new Func<string>(() => File.ReadAllText(goldenPath)), out string report);
      if (outputPath != null)
        File.WriteAllText(outputPath, report);
      return failures;
    }

    /// <summary>
    /// <see cref="Run(string, string)"/> with the golden text from <paramref name="readGolden"/> (null: none), the report as text.
    /// </summary>
    public static int Run(Func<string> readGolden, out string text)
    {
      StringBuilder report = new StringBuilder();
      Action<string> log = line => { report.Append(line).Append('\n'); UnityEngine.Debug.Log("[smoke] " + line); };
      int failures = 0;
      try
      {
#if UNITY_WEBGL && !UNITY_EDITOR
        string backend = "IL2CPP WebGL";
#elif ENABLE_IL2CPP
        string backend = "IL2CPP";
#else
        string backend = "Mono";
#endif
        log($"{backend}, {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, Unity {Application.unityVersion}, IsDynamicCodeSupported={RuntimeFeature.IsDynamicCodeSupported}, editor={Application.isEditor}");
        if (readGolden == null)
        {
          log("FAILED: no -golden path");
          failures++;
        }
        else
        {
          string golden = readGolden();
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
      text = report.ToString();
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
        Path = new List<UnityEngine.Vector3>() { UnityEngine.Vector3.zero, UnityEngine.Vector3.one },
        // Values whose overlapping properties (center, extents...) are exact floats: the reader sets them one after another
        Area = new Rect(1, 2, 4, 8),
        Box = new Bounds(new UnityEngine.Vector3(1, 2, 3), new UnityEngine.Vector3(4, 6, 8)),
        View = new Rect(-1.5f, 0.25f, 640, 480),
        Volume = new Bounds(new UnityEngine.Vector3(-1, 0, 1), new UnityEngine.Vector3(2, 2, 2))
      };
      // IL2CPP strips Unity's engine assemblies at every level: the properties of its structs reached only by reflection are gone without a link.xml (the generated code calls them)
      List<string> stripped = new List<string>();
      foreach (Type type in new[] { typeof(Vector2Int), typeof(Rect), typeof(Bounds) })
        if (Stripped(type))
          stripped.Add(type.Name);
      if (stripped.Count > 0)
        log($"game: properties of {string.Join(", ", stripped)} were stripped, the runtime plans miss them (keep them with a link.xml, or use the generator)");
      foreach ((string name, Func<LsMsgPackOptions> options) in new (string, Func<LsMsgPackOptions>)[]
      {
        // Short type names: the invoice's System.Numerics.Vector3 was read in this process first, so "Vector3" resolves to it; the reader pairs the entry with the declared UnityEngine.Vector3
        ("game-default", () => new LsMsgPackOptions()),
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
          List<string> problems = new List<string>();
          bool expected = true; // only values of stripped types differ, with the runtime plans
          Action<bool, string, Type> differs = (same, what, type) =>
          {
            if (same)
              return;
            problems.Add(what);
            expected &= type != null && which.Length == 0 && stripped.Contains(type.Name);
          };
          differs(read.Level == state.Level, "Level", null);
          differs(read.Position == state.Position, $"Position {read.Position}", null);
          differs(read.Rotation == state.Rotation, $"Rotation {read.Rotation}", null);
          differs(read.Tint == state.Tint, $"Tint {read.Tint}", null);
          differs(read.Cell == state.Cell, $"Cell {read.Cell}", typeof(Vector2Int));
          differs(read.Path != null && read.Path.Count == 2 && read.Path[1] == UnityEngine.Vector3.one, "Path", null);
          differs(read.Area == state.Area, $"Area {read.Area}", typeof(Rect));
          differs(read.Box == state.Box, $"Box {read.Box}", typeof(Bounds));
          differs(read.View == state.View, $"View {read.View}", null);
          differs(read.Volume == state.Volume, $"Volume {read.Volume}", null);
          string problem = problems.Count == 0 ? null : string.Join(", ", problems);
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
    /// The stripper removed accessors of a settable property of Unity's struct (what the runtime plans read and write by reflection).
    /// </summary>
    private static bool Stripped(Type type)
    {
      string[] names;
      if (type == typeof(Vector2Int))
        names = new[] { "x", "y" };
      else if (type == typeof(Rect))
        names = new[] { "x", "y", "width", "height" };
      else
        names = new[] { "center", "extents" };
      for (int i = 0; i < names.Length; i++)
      {
        System.Reflection.PropertyInfo property = type.GetProperty(names[i]);
        if (property is null || property.GetMethod is null || property.SetMethod is null)
          return true;
      }
      return false;
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
