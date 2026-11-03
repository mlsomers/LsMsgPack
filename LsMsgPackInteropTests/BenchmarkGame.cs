using LsMsgPack;
using LsMsgPackUnitTests;
using LtMsgPack;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyType;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using NB = Nerdbank.MessagePack;

namespace LsMsgPackInteropTests
{
  // The messages of a multiplayer game: each client sends its own state every tick and receives the states of the other players

  public enum PlayerAction { Idle, Walking, Running, Jumping, Crouching, Shooting, Reloading, Dead }

  public struct Vec3
  {
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
  }

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

  // The same model with a class instead of the struct: LtMsgPack writes and reads structs boxed, through its slow path

  public class Vec3C
  {
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
  }

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

  // MessagePack-CSharp's models: integer keys (arrays) and keyAsPropertyName (maps)

  [MessagePackObject]
  public struct Vec3K
  {
    [Key(0)] public float X { get; set; }
    [Key(1)] public float Y { get; set; }
    [Key(2)] public float Z { get; set; }
  }

  [MessagePackObject]
  public class PlayerStateK
  {
    [Key(0)] public int PlayerId { get; set; }
    [Key(1)] public uint Sequence { get; set; }
    [Key(2)] public Vec3K Position { get; set; }
    [Key(3)] public Vec3K Velocity { get; set; }
    [Key(4)] public float Yaw { get; set; }
    [Key(5)] public float Pitch { get; set; }
    [Key(6)] public PlayerAction Action { get; set; }
    [Key(7)] public byte Health { get; set; }
  }

  [MessagePackObject]
  public class WorldSnapshotK
  {
    [Key(0)] public uint Tick { get; set; }
    [Key(1)] public uint LastProcessedSequence { get; set; }
    [Key(2)] public List<PlayerStateK> Players { get; set; }
  }

  [MessagePackObject(keyAsPropertyName: true)]
  public struct Vec3N
  {
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
  }

  [MessagePackObject(keyAsPropertyName: true)]
  public class PlayerStateN
  {
    public int PlayerId { get; set; }
    public uint Sequence { get; set; }
    public Vec3N Position { get; set; }
    public Vec3N Velocity { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public PlayerAction Action { get; set; }
    public byte Health { get; set; }
  }

  [MessagePackObject(keyAsPropertyName: true)]
  public class WorldSnapshotN
  {
    public uint Tick { get; set; }
    public uint LastProcessedSequence { get; set; }
    public List<PlayerStateN> Players { get; set; }
  }

  /// <summary>
  /// Nerdbank.MessagePack's shapes of the game model, written by PolyType's source generator.
  /// </summary>
  [GenerateShapeFor<PlayerState>]
  [GenerateShapeFor<WorldSnapshot>]
  public partial class GameShapes
  {
  }

  /// <summary>
  /// The libraries of <see cref="BenchmarkLibraries"/> (in the same setups) on the messages of a multiplayer game with 16 players:
  /// a client sends its own <see cref="PlayerState"/> (a few numbers) every tick and receives a <see cref="WorldSnapshot"/> with the states of the 15 others.
  /// Lots of small messages, so the cost per call and the size of every message count, not the throughput on large data.
  /// <para>Each message is written to a new byte[] ("Write") and to a buffer that is reused ("Send": an ArrayBufferWriter, or a MemoryStream for LtMsgPack, which has no IBufferWriter overload), as a game loop would to keep the garbage collector quiet.
  /// The schema reference rows get the writer's schemas once, as in a handshake when connecting.</para>
  /// <para>The "Vec3 class" rows are LtMsgPack with <see cref="Vec3C"/>: LtMsgPack writes and reads structs boxed (no typed plans, also not in the generated code), which costs it 4-5x here.</para>
  /// <para>Results are written to the test output (docs/Comparison.md has a run), timings are not asserted.</para>
  /// </summary>
  [TestClass]
  [TestCategory("Benchmark")]
  public class BenchmarkGame
  {
    public TestContext TestContext { get; set; }

    private const int Players = 16;
    private const int Ticks = 64;
    private const int StateCalls = 200_000;
    private const int SnapshotCalls = 20_000;
    private const int Passes = 7;

    /// <summary>The states each client sent, all ticks (<see cref="Players"/> × <see cref="Ticks"/>)</summary>
    private static readonly PlayerState[] AllStates;

    /// <summary>What player 0 receives each tick: the other players' states</summary>
    private static readonly WorldSnapshot[] AllSnapshots;

    static BenchmarkGame()
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
          // Standing still is (0, 0, 0), not sin * 0 (which can be -0.0: LtMsgPack's default filter leaves out zeros as defaults, so -0.0 reads back as 0.0)
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

    private static float Coordinate(Random random, double range)
    {
      return (float)((random.NextDouble() * 2 - 1) * range);
    }

    private enum Operation { WriteState, ReadState, SendState, WriteSnapshot, ReadSnapshot, SendSnapshot }

    private static readonly Operation[] Operations = (Operation[])Enum.GetValues(typeof(Operation));

    private interface ICandidate
    {
      string Name { get; }

      /// <summary>Asserts the round trip of every message, keeps the payloads</summary>
      void Prepare();

      /// <summary>Average payload size of a state and a snapshot</summary>
      double StateSize { get; }
      double SnapshotSize { get; }

      byte[][] StatePayloads { get; }
      byte[][] SnapshotPayloads { get; }

      /// <summary>Runs the operation <see cref="StateCalls"/> or <see cref="SnapshotCalls"/> times, cycling through the messages</summary>
      TimeSpan Run(Operation operation, int calls);

      int Calls(Operation operation);
    }

    private sealed class Candidate<TState, TSnapshot> : ICandidate
    {
      private readonly TState[] _states;
      private readonly TSnapshot[] _snapshots;
      private readonly Func<TState, byte[]> _writeState;
      private readonly Func<byte[], TState> _readState;
      private readonly Func<TState, int> _sendState;
      private readonly Func<TSnapshot, byte[]> _writeSnapshot;
      private readonly Func<byte[], TSnapshot> _readSnapshot;
      private readonly Func<TSnapshot, int> _sendSnapshot;
      private byte[][] _statePayloads;
      private byte[][] _snapshotPayloads;

      public Candidate(string name,
        Func<TState, byte[]> writeState, Func<byte[], TState> readState, Func<TState, int> sendState,
        Func<TSnapshot, byte[]> writeSnapshot, Func<byte[], TSnapshot> readSnapshot, Func<TSnapshot, int> sendSnapshot)
      {
        Name = name;
        _writeState = writeState;
        _readState = readState;
        _sendState = sendState;
        _writeSnapshot = writeSnapshot;
        _readSnapshot = readSnapshot;
        _sendSnapshot = sendSnapshot;
        _states = new TState[AllStates.Length];
        for (int t = 0; t < _states.Length; t++)
          _states[t] = BenchmarkLibraries.As<TState>(AllStates[t]);
        _snapshots = new TSnapshot[AllSnapshots.Length];
        for (int t = 0; t < _snapshots.Length; t++)
          _snapshots[t] = BenchmarkLibraries.As<TSnapshot>(AllSnapshots[t]);
      }

      public string Name { get; }

      public double StateSize { get; private set; }

      public double SnapshotSize { get; private set; }

      public byte[][] StatePayloads => _statePayloads;

      public byte[][] SnapshotPayloads => _snapshotPayloads;

      public void Prepare()
      {
        _statePayloads = new byte[_states.Length][];
        long bytes = 0;
        for (int t = 0; t < _states.Length; t++)
        {
          bytes += (_statePayloads[t] = _writeState(_states[t])).Length;
          Same.AssertEqual(_states[t], _readState(_statePayloads[t]), Name + " round trip of a state");
          Assert.AreEqual(_statePayloads[t].Length, _sendState(_states[t]), Name + " sends what it writes");
        }
        StateSize = (double)bytes / _states.Length;

        _snapshotPayloads = new byte[_snapshots.Length][];
        bytes = 0;
        for (int t = 0; t < _snapshots.Length; t++)
        {
          bytes += (_snapshotPayloads[t] = _writeSnapshot(_snapshots[t])).Length;
          Same.AssertEqual(_snapshots[t], _readSnapshot(_snapshotPayloads[t]), Name + " round trip of a snapshot");
          Assert.AreEqual(_snapshotPayloads[t].Length, _sendSnapshot(_snapshots[t]), Name + " sends what it writes");
        }
        SnapshotSize = (double)bytes / _snapshots.Length;
      }

      public int Calls(Operation operation)
      {
        return operation <= Operation.SendState ? StateCalls : SnapshotCalls;
      }

      public TimeSpan Run(Operation operation, int calls)
      {
        // The message index wraps with a mask: the counts are powers of two
        int stateMask = _states.Length - 1;
        int snapshotMask = _snapshots.Length - 1;
        Stopwatch sw = Stopwatch.StartNew();
        switch (operation)
        {
          case Operation.WriteState:
            for (int t = 0; t < calls; t++)
              _writeState(_states[t & stateMask]);
            break;
          case Operation.ReadState:
            for (int t = 0; t < calls; t++)
              _readState(_statePayloads[t & stateMask]);
            break;
          case Operation.SendState:
            for (int t = 0; t < calls; t++)
              _sendState(_states[t & stateMask]);
            break;
          case Operation.WriteSnapshot:
            for (int t = 0; t < calls; t++)
              _writeSnapshot(_snapshots[t & snapshotMask]);
            break;
          case Operation.ReadSnapshot:
            for (int t = 0; t < calls; t++)
              _readSnapshot(_snapshotPayloads[t & snapshotMask]);
            break;
          case Operation.SendSnapshot:
            for (int t = 0; t < calls; t++)
              _sendSnapshot(_snapshots[t & snapshotMask]);
            break;
        }
        return sw.Elapsed;
      }
    }

    /// <summary>A reused output buffer for the libraries that write to an IBufferWriter</summary>
    private sealed class Outbox
    {
      private readonly ArrayBufferWriter<byte> _buffer = new ArrayBufferWriter<byte>(4096);

      public IBufferWriter<byte> Clear()
      {
        _buffer.ResetWrittenCount();
        return _buffer;
      }

      public int Length => _buffer.WrittenCount;
    }

    /// <summary>LtMsgPack writes to a Stream: a MemoryStream that is reused</summary>
    private sealed class StreamOutbox
    {
      private readonly MemoryStream _stream = new MemoryStream(4096);

      public Stream Clear()
      {
        _stream.SetLength(0);
        return _stream;
      }

      public int Length => (int)_stream.Length;
    }

    private static ICandidate MessagePackCSharp<TState, TSnapshot>(string name, MessagePackSerializerOptions options)
    {
      Outbox outbox = new Outbox();
      return new Candidate<TState, TSnapshot>(name,
        s => MessagePackSerializer.Serialize(s, options), b => MessagePackSerializer.Deserialize<TState>(b, options), s => { MessagePackSerializer.Serialize(outbox.Clear(), s, options); return outbox.Length; },
        w => MessagePackSerializer.Serialize(w, options), b => MessagePackSerializer.Deserialize<TSnapshot>(b, options), w => { MessagePackSerializer.Serialize(outbox.Clear(), w, options); return outbox.Length; });
    }

    private static ICandidate Nerdbank(string name, NB.MessagePackSerializer serializer)
    {
      Outbox outbox = new Outbox();
      return new Candidate<PlayerState, WorldSnapshot>(name,
        s => serializer.Serialize<PlayerState, GameShapes>(s), b => serializer.Deserialize<PlayerState, GameShapes>(b), s => { serializer.Serialize<PlayerState, GameShapes>(outbox.Clear(), s); return outbox.Length; },
        w => serializer.Serialize<WorldSnapshot, GameShapes>(w), b => serializer.Deserialize<WorldSnapshot, GameShapes>(b), w => { serializer.Serialize<WorldSnapshot, GameShapes>(outbox.Clear(), w); return outbox.Length; });
    }

    private static ICandidate Lt(string name, LtMsgPackOptions options, bool generated)
    {
      return Lt<PlayerState, WorldSnapshot>(name, options, generated);
    }

    private static ICandidate Lt<TState, TSnapshot>(string name, LtMsgPackOptions options, bool generated)
    {
      if (generated)
      {
        options.Contexts = new LtMsgPackContext[] { TestMsgPackContext.Default };
        options.UseRegisteredContexts = false;
      }
      LtMsgPackSerializer writer = new LtMsgPackSerializer(options);
      LtMsgPackSerializer reader = writer;
      if (options.WriteSchemaReference)
      {
        // The receiver has its own store, filled once with the sender's schemas (a handshake when connecting)
        writer.Serialize(BenchmarkLibraries.As<TState>(AllStates[0]));
        writer.Serialize(BenchmarkLibraries.As<TSnapshot>(AllSnapshots[0]));
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
      StreamOutbox outbox = new StreamOutbox();
      return new Candidate<TState, TSnapshot>(name,
        s => writer.Serialize(s), b => reader.Deserialize<TState>(b), s => { writer.Serialize(s, outbox.Clear()); return outbox.Length; },
        w => writer.Serialize(w), b => reader.Deserialize<TSnapshot>(b), w => { writer.Serialize(w, outbox.Clear()); return outbox.Length; });
    }

    private static ICandidate[] CreateCandidates()
    {
      Assert.IsNotNull(GeneratedMessagePackResolver.Instance.GetFormatter<WorldSnapshotK>(), "MessagePack-CSharp's source generator did not run");
      Assert.IsNotNull(GeneratedMessagePackResolver.Instance.GetFormatter<WorldSnapshotN>(), "MessagePack-CSharp's source generator did not run");
      MessagePackSerializerOptions mpGenerated = MessagePackSerializerOptions.Standard.WithResolver(CompositeResolver.Create(GeneratedMessagePackResolver.Instance, StandardResolver.Instance));

      LtMsgPackOptions mpArrays = LtMsgPackPresets.MessagePackCSharp();
      mpArrays.ObjectLayout = ObjectLayout.Array;
      mpArrays.PropertyOrder = PropertyOrder.Declaration;

      JsonSerializerOptions stj = new JsonSerializerOptions();
      Outbox jsonOutbox = new Outbox();
      Utf8JsonWriter json = new Utf8JsonWriter(jsonOutbox.Clear());

      return new ICandidate[]
      {
        new Candidate<PlayerState, WorldSnapshot>("System.Text.Json", // the baseline
          s => JsonSerializer.SerializeToUtf8Bytes(s, stj), b => JsonSerializer.Deserialize<PlayerState>(b, stj), s => { json.Reset(jsonOutbox.Clear()); JsonSerializer.Serialize(json, s, stj); json.Flush(); return jsonOutbox.Length; },
          w => JsonSerializer.SerializeToUtf8Bytes(w, stj), b => JsonSerializer.Deserialize<WorldSnapshot>(b, stj), w => { json.Reset(jsonOutbox.Clear()); JsonSerializer.Serialize(json, w, stj); json.Flush(); return jsonOutbox.Length; }),

        // Objects as arrays (positional)
        MessagePackCSharp<PlayerStateK, WorldSnapshotK>("MessagePack-CSharp [Key(n)], generated", mpGenerated),
        Nerdbank("Nerdbank PerfOverSchemaStability, shapes", new NB.MessagePackSerializer() { PerfOverSchemaStability = true }),
        Lt("LtMsgPack MessagePackCSharp preset, arrays", mpArrays, false), // the bytes of MessagePack-CSharp's row
        Lt("LtMsgPack arrays, no schema", new LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Array, PropertyOrder = PropertyOrder.Declaration }, false),
        Lt("LtMsgPack arrays, no schema, generated", new LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Array, PropertyOrder = PropertyOrder.Declaration }, true),
        Lt("LtMsgPack schema reference (arrays)", new LtMsgPackOptions() { SchemaStore = new SchemaStore(), WriteSchemaReference = true }, false),
        Lt("LtMsgPack schema reference, generated", new LtMsgPackOptions() { SchemaStore = new SchemaStore(), WriteSchemaReference = true }, true),
        Lt("LtMsgPack default (arrays, schema)", new LtMsgPackOptions(), false),
        Lt("LtMsgPack default, generated", new LtMsgPackOptions(), true),

        // LtMsgPack with Vec3 as a class
        Lt<PlayerStateC, WorldSnapshotC>("LtMsgPack MessagePackCSharp preset, Vec3 class", mpArrays.Clone(), false), // still MessagePack-CSharp's bytes
        Lt<PlayerStateC, WorldSnapshotC>("LtMsgPack arrays, no schema, Vec3 class", new LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Array, PropertyOrder = PropertyOrder.Declaration }, false),
        Lt<PlayerStateC, WorldSnapshotC>("LtMsgPack arrays, no schema, gen., Vec3 class", new LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Array, PropertyOrder = PropertyOrder.Declaration }, true),
        Lt<PlayerStateC, WorldSnapshotC>("LtMsgPack schema ref., gen., Vec3 class", new LtMsgPackOptions() { SchemaStore = new SchemaStore(), WriteSchemaReference = true }, true),
        Lt<PlayerStateC, WorldSnapshotC>("LtMsgPack default, Vec3 class", new LtMsgPackOptions(), false),
        Lt<PlayerStateC, WorldSnapshotC>("LtMsgPack default, generated, Vec3 class", new LtMsgPackOptions(), true),

        // Objects as maps keyed by property names
        MessagePackCSharp<PlayerStateN, WorldSnapshotN>("MessagePack-CSharp names, generated", mpGenerated),
        Nerdbank("Nerdbank names, shapes", new NB.MessagePackSerializer()),
        Lt("LtMsgPack names", new LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map }, false),
        Lt("LtMsgPack names, generated", new LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map }, true),
      };
    }

    [TestMethod]
    public void GameMessages_Libraries()
    {
      // PreservePackages only exists when LsMsgPack is compiled with KEEPTRACK, its overhead would skew the results
      if (typeof(MsgPackSettings).GetProperty("PreservePackages") != null)
        Assert.Inconclusive("Compiled with KEEPTRACK, run the benchmark using the Release (or Debug) configuration.");

      ICandidate[] candidates = CreateCandidates();
      for (int c = 0; c < candidates.Length; c++)
        candidates[c].Prepare();
      AssertSameBytes(candidates, "MessagePack-CSharp [Key(n)], generated", "LtMsgPack MessagePackCSharp preset, arrays");
      AssertSameBytes(candidates, "MessagePack-CSharp [Key(n)], generated", "LtMsgPack MessagePackCSharp preset, Vec3 class");

      for (int c = 0; c < candidates.Length; c++)
      {
        ICandidate candidate = candidates[c];
        for (int o = 0; o < Operations.Length; o++)
        {
          Operation operation = Operations[o];
          BenchmarkLibraries.WarmUp(() => candidate.Run(operation, candidate.Calls(operation) / 10));
        }
      }

      // The fastest of several passes, interleaving the candidates to spread out background noise
      double[,] fastest = new double[candidates.Length, Operations.Length]; // ns per message
      for (int p = 0; p < Passes; p++)
      {
        for (int c = 0; c < candidates.Length; c++)
        {
          for (int o = 0; o < Operations.Length; o++)
          {
            int calls = candidates[c].Calls(Operations[o]);
            double ns = candidates[c].Run(Operations[o], calls).TotalMilliseconds * 1_000_000 / calls;
            if (p == 0 || ns < fastest[c, o])
              fastest[c, o] = ns;
          }
        }
      }

      // Bytes allocated per message
      double[,] allocated = new double[candidates.Length, Operations.Length];
      for (int c = 0; c < candidates.Length; c++)
      {
        for (int o = 0; o < Operations.Length; o++)
        {
          const int calls = 1024;
          long before = GC.GetAllocatedBytesForCurrentThread();
          candidates[c].Run(Operations[o], calls);
          allocated[c, o] = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / calls;
        }
      }

      StringBuilder sb = new StringBuilder();
      sb.AppendLine($"Game messages, {Players} players: a state (own position, sent) and a snapshot ({Players - 1} other players, received); ns per message, fastest of {Passes} passes");
      sb.AppendLine($"({StateCalls:N0} states and {SnapshotCalls:N0} snapshots per pass; Write: a new byte[], Send: a reused buffer)");
      sb.AppendLine();
      sb.AppendLine(string.Format("{0,-47} {1,6} {2,6} | {3,6} {4,6} {5,6} | {6,6} {7,6} {8,6}", "Serializer", "State", "Snap.", "Write", "Read", "Send", "Write", "Read", "Send"));
      sb.AppendLine(string.Format("{0,-47} {1,6} {2,6} | {3,-20} | {4,-20}", "", "bytes", "bytes", "state ns", "snapshot ns"));
      sb.AppendLine(new string('-', 108));
      for (int c = 0; c < candidates.Length; c++)
        sb.AppendLine(string.Format("{0,-47} {1,6:N0} {2,6:N0} | {3,6:N0} {4,6:N0} {5,6:N0} | {6,6:N0} {7,6:N0} {8,6:N0}", candidates[c].Name, candidates[c].StateSize, candidates[c].SnapshotSize,
          fastest[c, 0], fastest[c, 1], fastest[c, 2], fastest[c, 3], fastest[c, 4], fastest[c, 5]));
      sb.AppendLine();
      sb.AppendLine("Bytes allocated per message");
      sb.AppendLine();
      sb.AppendLine(string.Format("{0,-47} | {1,6} {2,6} {3,6} | {4,6} {5,6} {6,6}", "Serializer", "Write", "Read", "Send", "Write", "Read", "Send"));
      sb.AppendLine(new string('-', 94));
      for (int c = 0; c < candidates.Length; c++)
        sb.AppendLine(string.Format("{0,-47} | {1,6:N0} {2,6:N0} {3,6:N0} | {4,6:N0} {5,6:N0} {6,6:N0}", candidates[c].Name,
          allocated[c, 0], allocated[c, 1], allocated[c, 2], allocated[c, 3], allocated[c, 4], allocated[c, 5]));
      TestContext.WriteLine(sb.ToString());
    }

    private static void AssertSameBytes(ICandidate[] candidates, string expected, string actual)
    {
      ICandidate e = Array.Find(candidates, c => c.Name == expected);
      ICandidate a = Array.Find(candidates, c => c.Name == actual);
      for (int t = 0; t < e.StatePayloads.Length; t++)
        CollectionAssert.AreEqual(e.StatePayloads[t], a.StatePayloads[t], $"{actual} writes other bytes than {expected} (state {t})");
      for (int t = 0; t < e.SnapshotPayloads.Length; t++)
        CollectionAssert.AreEqual(e.SnapshotPayloads[t], a.SnapshotPayloads[t], $"{actual} writes other bytes than {expected} (snapshot {t})");
    }
  }
}
