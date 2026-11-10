// The messages of a multiplayer game (the model and data of BenchmarkGame in the interop tests): a state of 8 values, a snapshot of 15 states, Vec3 as a struct and as a class.
// Usage: dotnet game-new.dll [seconds per pass=0.25] [name filter] [passes=5]    prints ns per message (fastest pass) and allocated bytes
//        dotnet game-new.dll one <candidate> <ws|rs|ss|wn|rn|sn> <seconds>        one operation for perf (ws = write state, rs = read, ss = send to a reused stream; n = snapshot)
// Asserts the round trip by comparing the bytes written again after reading. The candidates are the LtMsgPack rows of BenchmarkGame; "-gen" uses the generated code when the library has the generator,
// "fields*" (the model with [DataMember] fields) and "vector3*" (System.Numerics.Vector3) only exist when it serializes fields (FIELDS, set by setup.sh).
using LtMsgPack;
using LsMsgPack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
#if FIELDS
using System.Numerics;
using System.Runtime.Serialization;
#endif

public enum PlayerAction { Idle, Walking, Running, Jumping, Crouching, Shooting, Reloading, Dead }

public struct Vec3 { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } }

public class PlayerState
{
  public int PlayerId { get; set; }
  public uint Sequence { get; set; }
  public Vec3 Position { get; set; }
  public Vec3 Velocity { get; set; }
  public float Yaw { get; set; }
  public float Pitch { get; set; }
  public PlayerAction Action { get; set; }
  public byte Health { get; set; }
}

public class WorldSnapshot
{
  public uint Tick { get; set; }
  public uint LastProcessedSequence { get; set; }
  public List<PlayerState> Players { get; set; }
}

public class Vec3C { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } }

public class PlayerStateC
{
  public int PlayerId { get; set; }
  public uint Sequence { get; set; }
  public Vec3C Position { get; set; }
  public Vec3C Velocity { get; set; }
  public float Yaw { get; set; }
  public float Pitch { get; set; }
  public PlayerAction Action { get; set; }
  public byte Health { get; set; }
}

public class WorldSnapshotC
{
  public uint Tick { get; set; }
  public uint LastProcessedSequence { get; set; }
  public List<PlayerStateC> Players { get; set; }
}

#if FIELDS
// Fields instead of properties (opted in: the default SerializedFields.Attributed), and System.Numerics.Vector3 (always written by its fields)
public struct Vec3F { [DataMember] public float X; [DataMember] public float Y; [DataMember] public float Z; }

public class PlayerStateF
{
  [DataMember] public int PlayerId;
  [DataMember] public uint Sequence;
  [DataMember] public Vec3F Position;
  [DataMember] public Vec3F Velocity;
  [DataMember] public float Yaw;
  [DataMember] public float Pitch;
  [DataMember] public PlayerAction Action;
  [DataMember] public byte Health;
}

public class WorldSnapshotF
{
  [DataMember] public uint Tick;
  [DataMember] public uint LastProcessedSequence;
  [DataMember] public List<PlayerStateF> Players;
}

public class PlayerStateV
{
  public int PlayerId { get; set; }
  public uint Sequence { get; set; }
  public Vector3 Position { get; set; }
  public Vector3 Velocity { get; set; }
  public float Yaw { get; set; }
  public float Pitch { get; set; }
  public PlayerAction Action { get; set; }
  public byte Health { get; set; }
}

public class WorldSnapshotV
{
  public uint Tick { get; set; }
  public uint LastProcessedSequence { get; set; }
  public List<PlayerStateV> Players { get; set; }
}
#endif

#if GENERATED
[LtMsgPackSerializable(typeof(PlayerState))]
[LtMsgPackSerializable(typeof(WorldSnapshot))]
[LtMsgPackSerializable(typeof(PlayerStateC))]
[LtMsgPackSerializable(typeof(WorldSnapshotC))]
#if FIELDS
[LtMsgPackSerializable(typeof(PlayerStateF))]
[LtMsgPackSerializable(typeof(WorldSnapshotF))]
[LtMsgPackSerializable(typeof(PlayerStateV))]
[LtMsgPackSerializable(typeof(WorldSnapshotV))]
#endif
public partial class GameContext : LtMsgPackContext { }
#endif

public static class LtGame
{
  private const int Players = 16;
  private const int Ticks = 64;
  private static readonly PlayerState[] AllStates;
  private static readonly WorldSnapshot[] AllSnapshots;

  static LtGame()
  {
    Random random = new Random(42);
    PlayerState[] current = new PlayerState[Players];
    for (int p = 0; p < Players; p++)
      current[p] = new PlayerState() { PlayerId = 1001 + p, Sequence = (uint)random.Next(1000, 50_000), Health = 100, Position = new Vec3() { X = Coordinate(random, 500), Y = (float)(random.NextDouble() * 40), Z = Coordinate(random, 500) } };
    List<PlayerState> states = new List<PlayerState>(Players * Ticks);
    AllSnapshots = new WorldSnapshot[Ticks];
    const float dt = 1f / 30;
    for (int t = 0; t < Ticks; t++)
    {
      PlayerState[] tick = new PlayerState[Players];
      for (int p = 0; p < Players; p++)
      {
        PlayerState last = current[p];
        PlayerAction action = last.Health == 0 ? PlayerAction.Dead : (PlayerAction)random.Next(0, 7);
        float speed = action == PlayerAction.Idle || action == PlayerAction.Dead ? 0 : action == PlayerAction.Running ? 7f : action == PlayerAction.Crouching ? 1.5f : 4f;
        float yaw = (last.Yaw + Coordinate(random, 15) + 360) % 360;
        double radians = yaw * Math.PI / 180;
        Vec3 velocity = speed == 0 ? default(Vec3) : new Vec3() { X = (float)(Math.Sin(radians) * speed), Y = action == PlayerAction.Jumping ? 3.5f : 0, Z = (float)(Math.Cos(radians) * speed) };
        int damage = action == PlayerAction.Dead ? 0 : random.Next(0, 20) == 0 ? random.Next(5, 40) : 0;
        PlayerState state = new PlayerState()
        {
          PlayerId = last.PlayerId,
          Sequence = last.Sequence + 1,
          Position = new Vec3() { X = last.Position.X + velocity.X * dt, Y = Math.Max(0, last.Position.Y + velocity.Y * dt), Z = last.Position.Z + velocity.Z * dt },
          Velocity = velocity,
          Yaw = yaw,
          Pitch = Math.Max(-89, Math.Min(89, last.Pitch + Coordinate(random, 5))),
          Action = action,
          Health = (byte)Math.Max(0, last.Health - damage),
        };
        tick[p] = current[p] = state;
        states.Add(state);
      }
      List<PlayerState> others = new List<PlayerState>(Players - 1);
      for (int p = 1; p < Players; p++)
        others.Add(tick[p]);
      AllSnapshots[t] = new WorldSnapshot() { Tick = 250_000 + (uint)t, LastProcessedSequence = tick[0].Sequence, Players = others };
    }
    AllStates = states.ToArray();
  }

  private static float Coordinate(Random random, double range) => (float)((random.NextDouble() * 2 - 1) * range);

  private static Vec3C C(Vec3 v) => new Vec3C() { X = v.X, Y = v.Y, Z = v.Z };

  private static PlayerStateC C(PlayerState s) => new PlayerStateC() { PlayerId = s.PlayerId, Sequence = s.Sequence, Position = C(s.Position), Velocity = C(s.Velocity), Yaw = s.Yaw, Pitch = s.Pitch, Action = s.Action, Health = s.Health };

  private static WorldSnapshotC C(WorldSnapshot w) => new WorldSnapshotC() { Tick = w.Tick, LastProcessedSequence = w.LastProcessedSequence, Players = w.Players.Select(C).ToList() };
#if FIELDS

  private static Vec3F F(Vec3 v) => new Vec3F() { X = v.X, Y = v.Y, Z = v.Z };

  private static PlayerStateF F(PlayerState s) => new PlayerStateF() { PlayerId = s.PlayerId, Sequence = s.Sequence, Position = F(s.Position), Velocity = F(s.Velocity), Yaw = s.Yaw, Pitch = s.Pitch, Action = s.Action, Health = s.Health };

  private static WorldSnapshotF F(WorldSnapshot w) => new WorldSnapshotF() { Tick = w.Tick, LastProcessedSequence = w.LastProcessedSequence, Players = w.Players.Select(F).ToList() };

  private static PlayerStateV V(PlayerState s) => new PlayerStateV() { PlayerId = s.PlayerId, Sequence = s.Sequence, Position = new Vector3(s.Position.X, s.Position.Y, s.Position.Z), Velocity = new Vector3(s.Velocity.X, s.Velocity.Y, s.Velocity.Z), Yaw = s.Yaw, Pitch = s.Pitch, Action = s.Action, Health = s.Health };

  private static WorldSnapshotV V(WorldSnapshot w) => new WorldSnapshotV() { Tick = w.Tick, LastProcessedSequence = w.LastProcessedSequence, Players = w.Players.Select(V).ToList() };
#endif

  private sealed class Candidate
  {
    internal string Name;
    internal Action<int>[] Ops; // ws rs ss wn rn sn, each one call for a message index
    internal double StateSize, SnapshotSize;
  }

  private static readonly string[] OpNames = { "ws", "rs", "ss", "wn", "rn", "sn" };

  private static Candidate Create<TS, TW>(string name, Func<LtMsgPackOptions> make, bool generated, TS[] states, TW[] snapshots)
  {
    LtMsgPackOptions options = make();
#if GENERATED
    if (generated)
    {
      options.Contexts = new LtMsgPackContext[] { GameContext.Default };
    }
#else
    if (generated) return null;
#endif
    options.UseRegisteredContexts = false; // the generated context registers itself: the other rows keep the runtime's plans
#pragma warning disable IL2026, IL3050
    LtMsgPackSerializer writer = new LtMsgPackSerializer(options);
    LtMsgPackSerializer reader = writer;
    if (options.WriteSchemaReference)
    {
      writer.Serialize(states[0]);
      writer.Serialize(snapshots[0]);
      LtMsgPackOptions receiving = options.Clone();
      receiving.SchemaStore = new SchemaStore();
      using (MemoryStream handshake = new MemoryStream())
      {
        options.SchemaStore.Export(handshake);
        handshake.Position = 0;
        receiving.SchemaStore.Import(handshake);
      }
      reader = new LtMsgPackSerializer(receiving);
    }
#pragma warning restore IL2026, IL3050
    byte[][] sp = new byte[states.Length][];
    byte[][] np = new byte[snapshots.Length][];
    long sb = 0, nb = 0;
    for (int t = 0; t < sp.Length; t++)
    {
      sb += (sp[t] = writer.Serialize(states[t])).Length;
      if (!sp[t].AsSpan().SequenceEqual(writer.Serialize(reader.Deserialize<TS>(sp[t]))))
        throw new Exception(name + ": state does not round trip");
    }
    for (int t = 0; t < np.Length; t++)
    {
      nb += (np[t] = writer.Serialize(snapshots[t])).Length;
      if (!np[t].AsSpan().SequenceEqual(writer.Serialize(reader.Deserialize<TW>(np[t]))))
        throw new Exception(name + ": snapshot does not round trip");
    }
    MemoryStream stream = new MemoryStream(4096);
    int sm = states.Length - 1, nm = snapshots.Length - 1;
    return new Candidate()
    {
      Name = name,
      StateSize = (double)sb / sp.Length,
      SnapshotSize = (double)nb / np.Length,
      Ops = new Action<int>[]
      {
        t => writer.Serialize(states[t & sm]),
        t => reader.Deserialize<TS>(sp[t & sm]),
        t => { stream.SetLength(0); writer.Serialize(states[t & sm], stream); },
        t => writer.Serialize(snapshots[t & nm]),
        t => reader.Deserialize<TW>(np[t & nm]),
        t => { stream.SetLength(0); writer.Serialize(snapshots[t & nm], stream); },
      },
    };
  }

  private static LtMsgPackOptions NoSchema() => new LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Array, PropertyOrder = PropertyOrder.Declaration };

  private static LtMsgPackOptions Preset()
  {
    LtMsgPackOptions options = LtMsgPackPresets.MessagePackCSharp();
    options.ObjectLayout = ObjectLayout.Array;
    options.PropertyOrder = PropertyOrder.Declaration;
    return options;
  }

  private static LtMsgPackOptions Reference() => new LtMsgPackOptions() { SchemaStore = new SchemaStore(), WriteSchemaReference = true };

  private static LtMsgPackOptions Names() => new LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map };

  private static List<Candidate> Candidates(string filter)
  {
    PlayerStateC[] statesC = AllStates.Select(C).ToArray();
    WorldSnapshotC[] snapshotsC = AllSnapshots.Select(C).ToArray();
    List<Func<Candidate>> make = new List<Func<Candidate>>()
    {
      () => Create("preset", Preset, false, AllStates, AllSnapshots),
      () => Create("noschema", NoSchema, false, AllStates, AllSnapshots),
      () => Create("noschema-gen", NoSchema, true, AllStates, AllSnapshots),
      () => Create("reference", Reference, false, AllStates, AllSnapshots),
      () => Create("reference-gen", Reference, true, AllStates, AllSnapshots),
      () => Create("default", () => new LtMsgPackOptions(), false, AllStates, AllSnapshots),
      () => Create("default-gen", () => new LtMsgPackOptions(), true, AllStates, AllSnapshots),
      () => Create("names", Names, false, AllStates, AllSnapshots),
      () => Create("names-gen", Names, true, AllStates, AllSnapshots),
      () => Create("noschema-class", NoSchema, false, statesC, snapshotsC),
      () => Create("noschema-class-gen", NoSchema, true, statesC, snapshotsC),
      () => Create("default-class", () => new LtMsgPackOptions(), false, statesC, snapshotsC),
    };
#if FIELDS
    PlayerStateF[] statesF = AllStates.Select(F).ToArray();
    WorldSnapshotF[] snapshotsF = AllSnapshots.Select(F).ToArray();
    PlayerStateV[] statesV = AllStates.Select(V).ToArray();
    WorldSnapshotV[] snapshotsV = AllSnapshots.Select(V).ToArray();
    make.Add(() => Create("fields-noschema", NoSchema, false, statesF, snapshotsF));
    make.Add(() => Create("fields-noschema-gen", NoSchema, true, statesF, snapshotsF));
    make.Add(() => Create("fields-default", () => new LtMsgPackOptions(), false, statesF, snapshotsF));
    make.Add(() => Create("fields-default-gen", () => new LtMsgPackOptions(), true, statesF, snapshotsF));
    make.Add(() => Create("fields-names", Names, false, statesF, snapshotsF));
    make.Add(() => Create("vector3-noschema", NoSchema, false, statesV, snapshotsV));
    make.Add(() => Create("vector3-default", () => new LtMsgPackOptions(), false, statesV, snapshotsV));
    make.Add(() => Create("vector3-default-gen", () => new LtMsgPackOptions(), true, statesV, snapshotsV));
#endif
    List<Candidate> candidates = new List<Candidate>();
    for (int t = 0; t < make.Count; t++)
    {
      Candidate candidate = make[t]();
      if (candidate != null && (filter is null || candidate.Name == filter || (filter.EndsWith('*') && candidate.Name.StartsWith(filter.TrimEnd('*'), StringComparison.Ordinal))))
        candidates.Add(candidate);
    }
    return candidates;
  }

  private static double Pass(Action<int> op, double seconds, bool snapshot)
  {
    int batch = snapshot ? 1_000 : 10_000;
    long calls = 0;
    int message = 0;
    Stopwatch sw = Stopwatch.StartNew();
    do
    {
      for (int t = 0; t < batch; t++)
        op(message++);
      calls += batch;
    } while (sw.Elapsed.TotalSeconds < seconds);
    return sw.Elapsed.TotalMilliseconds * 1_000_000 / calls;
  }


  /// <summary>LT_COMPILE_AFTER=n sets LtMsgPackOptions.Default_CompileWritersAfter (compiled writers after n objects per type, 0 at once, -1 never); ignored by a base without the option.</summary>
  private static void CompileAfterFromEnvironment()
  {
    string after = System.Environment.GetEnvironmentVariable("LT_COMPILE_AFTER");
    if (after != null)
      {
      typeof(LtMsgPack.LtMsgPackOptions).GetField("Default_CompileWritersAfter")?.SetValue(null, int.Parse(after));
      typeof(LtMsgPack.LtMsgPackOptions).GetField("Default_CompileReadersAfter")?.SetValue(null, int.Parse(after)); // the compiled readers too
    }
  }
  public static void Main(string[] args)
  {
    CompileAfterFromEnvironment();
    if (args.Length > 0 && args[0] == "one")
    {
      Candidate one = Candidates(args[1])[0];
      int o = Array.IndexOf(OpNames, args[2]);
      double seconds = double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
      Pass(one.Ops[o], 1.5, o >= 3); // past tiering
      Console.WriteLine($"{one.Name} {args[2]}: {Pass(one.Ops[o], seconds, o >= 3):N1} ns");
      return;
    }
    double perPass = args.Length > 0 ? double.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 0.25;
    string filter = args.Length > 1 && args[1] != "all" ? args[1] : null;
    int passes = args.Length > 2 ? int.Parse(args[2]) : 5;
    List<Candidate> candidates = Candidates(filter);
    for (int c = 0; c < candidates.Count; c++)
      for (int o = 0; o < OpNames.Length; o++)
        Pass(candidates[c].Ops[o], 0.4, o >= 3); // warm up, past tiering
    double[,] fastest = new double[candidates.Count, OpNames.Length];
    for (int p = 0; p < passes; p++)
      for (int c = 0; c < candidates.Count; c++)
        for (int o = 0; o < OpNames.Length; o++)
        {
          double ns = Pass(candidates[c].Ops[o], perPass, o >= 3);
          if (p == 0 || ns < fastest[c, o])
            fastest[c, o] = ns;
        }
    Console.WriteLine($"{"",-20} {"state",6} {"snap",6} | {"ws",6} {"rs",6} {"ss",6} | {"wn",7} {"rn",7} {"sn",7} | allocated: ws rs ss wn rn sn");
    for (int c = 0; c < candidates.Count; c++)
    {
      string allocated = "";
      for (int o = 0; o < OpNames.Length; o++)
      {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int t = 0; t < 1024; t++)
          candidates[c].Ops[o](t);
        allocated += " " + ((GC.GetAllocatedBytesForCurrentThread() - before) / 1024);
      }
      Console.WriteLine($"{candidates[c].Name,-20} {candidates[c].StateSize,6:N0} {candidates[c].SnapshotSize,6:N0} | {fastest[c, 0],6:N0} {fastest[c, 1],6:N0} {fastest[c, 2],6:N0} | {fastest[c, 3],7:N0} {fastest[c, 4],7:N0} {fastest[c, 5],7:N0} |{allocated}");
    }
  }
}
