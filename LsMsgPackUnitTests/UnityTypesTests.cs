using LsMsgPack;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPackDebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LsMsgPackUnitTests
{
  #region Model

  /// <summary>
  /// What a game writes: Unity's structs (the look-alikes of LsMsgPackUnityStubs, an assembly named UnityEngine.CoreModule) as members.
  /// </summary>
  public class UnityGameState
  {
    public string Level { get; set; }
    public UnityEngine.Vector3 Position { get; set; }
    public UnityEngine.Quaternion Rotation { get; set; }
    public Color Tint { get; set; }
    public Vector2Int Cell { get; set; }
    public List<UnityEngine.Vector3> Path { get; set; }
    public UnityEngine.Vector3? Target { get; set; }

    public override bool Equals(object obj)
    {
      return obj is UnityGameState other && Level == other.Level && Position.Equals(other.Position) && Rotation.Equals(other.Rotation) && Tint.Equals(other.Tint) && Cell.Equals(other.Cell)
        && (Path == null ? other.Path == null : other.Path != null && Path.SequenceEqual(other.Path)) && Nullable.Equals(Target, other.Target);
    }

    public override int GetHashCode() { return 0; }
  }

  /// <summary>
  /// A data class as Unity's serializer wants it: public fields and a private [SerializeField] one.
  /// </summary>
  public class UnityPlayerData
  {
    public string Name;
    public int Health;
    [SerializeField] private float _speed;

    public UnityPlayerData() { }

    public UnityPlayerData(string name, int health, float speed) { Name = name; Health = health; _speed = speed; }

    public float Speed { get { return _speed; } }
  }

  /// <summary>
  /// System.Numerics' Vector3 next to Unity's: both are written as "Vector3" with short type names.
  /// </summary>
  public class UnityNumericsHolder
  {
    public string Name { get; set; }
    public System.Numerics.Vector3 Point { get; set; }
  }

  #endregion

  /// <summary>
  /// Unity's types (docs/Unity.md, maintainer's decisions 2026-11-22): its structs of public fields written by them as the framework's (Vector3 as System.Numerics.Vector3), their computed
  /// properties left out (Vector3.normalized is another Vector3: written, it never ended), [SerializeField] opts a field in. Unity itself: LsMsgPackUnitySmokeTest.
  /// </summary>
  public abstract class UnityTypesTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    private static readonly MsgPackSettings Untyped = new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map };

    private static IEnumerable<MsgPackSettings> AllSettings()
    {
      yield return new MsgPackSettings();
      yield return new MsgPackSettings() { ObjectLayout = ObjectLayout.Map };
      yield return new MsgPackSettings() { AddTypeIdOptions = AddTypeIdOption.IfAmbiguious | AddTypeIdOption.FullName };
      yield return new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map };
      yield return new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Array };
      yield return new MsgPackSettings() { ReferenceHandling = ReferenceHandling.Preserve };
    }

    private static UnityGameState GameState()
    {
      return new UnityGameState()
      {
        Level = "dungeon-3",
        Position = new UnityEngine.Vector3(1.5f, -2, 30),
        Rotation = new UnityEngine.Quaternion(0, 0.7071068f, 0, 0.7071068f),
        Tint = new Color(0.25f, 0.5f, 1, 0.75f),
        Cell = new Vector2Int(4, -7),
        Path = new List<UnityEngine.Vector3>() { UnityEngine.Vector3.zero, UnityEngine.Vector3.one },
        Target = new UnityEngine.Vector3(0, 0, 9)
      };
    }

    private Dictionary<string, object> Written(object value)
    {
      return Serializer.Deserialize<Dictionary<string, object>>(Serializer.Serialize(value, value.GetType(), Untyped), Untyped);
    }

    [TestMethod]
    public void GameState_RoundTrip()
    {
      foreach (MsgPackSettings settings in AllSettings())
      {
        UnityGameState state = GameState();
        Assert.AreEqual(state, Serializer.Deserialize<UnityGameState>(Serializer.Serialize(state, settings), settings));
      }
    }

    [TestMethod]
    public void ShortTypeNames_BothVector3s_EitherOrder()
    {
      // "Vector3" resolves to one type per process (whichever was asked first, also by other tests): the reader pairs the entry with the Vector3 its property declares
      foreach (MsgPackSettings settings in new[] { new MsgPackSettings(), new MsgPackSettings() { ObjectLayout = ObjectLayout.Map }, new MsgPackSettings() { ReferenceHandling = ReferenceHandling.Preserve } })
      {
        for (int round = 0; round < 2; round++)
        {
          UnityNumericsHolder numerics = new UnityNumericsHolder() { Name = "n", Point = new System.Numerics.Vector3(1.5f, -2, 3) };
          UnityGameState state = GameState();
          if (round == 0)
            Assert.AreEqual(state, Serializer.Deserialize<UnityGameState>(Serializer.Serialize(state, settings), settings));
          UnityNumericsHolder read = Serializer.Deserialize<UnityNumericsHolder>(Serializer.Serialize(numerics, settings), settings);
          Assert.AreEqual(numerics.Point, read.Point);
          Assert.AreEqual("n", read.Name);
          Assert.AreEqual(state, Serializer.Deserialize<UnityGameState>(Serializer.Serialize(state, settings), settings));
        }
      }
    }

    [TestMethod]
    public void Structs_WrittenByTheirFields()
    {
      CollectionAssert.AreEquivalent(new[] { "x", "y", "z" }, Written(new UnityEngine.Vector3(1, 2, 3)).Keys.ToArray(), "Vector3: not normalized, magnitude, the indexer");
      CollectionAssert.AreEquivalent(new[] { "x", "y", "z", "w" }, Written(new UnityEngine.Quaternion(1, 2, 3, 4)).Keys.ToArray(), "Quaternion: not eulerAngles (settable, computed from the fields)");
      CollectionAssert.AreEquivalent(new[] { "r", "g", "b", "a" }, Written(new Color(0.25f, 0.5f, 1, 0.75f)).Keys.ToArray(), "Color: not linear, gamma, grayscale");
      CollectionAssert.AreEquivalent(new[] { "x", "y" }, Written(new Vector2Int(4, -7)).Keys.ToArray(), "Vector2Int: its settable properties, not magnitude or its private fields");
    }

    [TestMethod]
    public void Structs_SameBytesAsSystemNumerics()
    {
      // In the array layout without names: the bytes of System.Numerics' structs (and of MessagePack-CSharp's Unity resolver for Vector3, Quaternion, Color)
      MsgPackSettings array = new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Array };
      CollectionAssert.AreEqual(Serializer.Serialize(new System.Numerics.Vector3(1.5f, -2, 30), array), Serializer.Serialize(new UnityEngine.Vector3(1.5f, -2, 30), array));
      CollectionAssert.AreEqual(Serializer.Serialize(new System.Numerics.Quaternion(1, 2, 3, 4), array), Serializer.Serialize(new UnityEngine.Quaternion(1, 2, 3, 4), array));
      CollectionAssert.AreEqual(Serializer.Serialize(new System.Numerics.Vector4(0.25f, 0.5f, 1, 0.75f), array), Serializer.Serialize(new Color(0.25f, 0.5f, 1, 0.75f), array));
    }

    [TestMethod]
    public void SerializeField_PrivateFieldWritten()
    {
      Dictionary<string, object> written = Written(new UnityPlayerData("Kim", 80, 4.5f));
      CollectionAssert.AreEquivalent(new[] { "_speed", "Speed" }, written.Keys.ToArray(), "the public fields are opted in by SerializedFields.Public");
      foreach (MsgPackSettings settings in AllSettings())
        Assert.AreEqual(4.5f, Serializer.Deserialize<UnityPlayerData>(Serializer.Serialize(new UnityPlayerData("Kim", 80, 4.5f), settings), settings).Speed);
    }

    [TestMethod]
    public void SerializeField_WithPublicFields_AsUnityWritesThem()
    {
      MsgPackSettings settings = new MsgPackSettings()
      {
        UseInexedSchema = false,
        ObjectLayout = ObjectLayout.Map,
        StaticFilters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(GetOnlyProperties.None, NonPublicProperties.None, SerializedFields.Public), new FilterIgnoredAttribute(), new FilterStatic() }
      };
      byte[] bytes = Serializer.Serialize(new UnityPlayerData("Kim", 80, 4.5f), settings);
      CollectionAssert.AreEquivalent(new[] { "Name", "Health", "_speed" }, Serializer.Deserialize<Dictionary<string, object>>(bytes, Untyped).Keys.ToArray());
      UnityPlayerData read = Serializer.Deserialize<UnityPlayerData>(bytes, settings);
      Assert.AreEqual("Kim", read.Name);
      Assert.AreEqual(80, read.Health);
      Assert.AreEqual(4.5f, read.Speed);
    }

    [TestMethod]
    public void SameBytesAsDebug()
    {
      foreach (MsgPackSettings settings in AllSettings())
      {
        CollectionAssert.AreEqual(Serializers.Debug.Serialize(GameState(), settings), Serializer.Serialize(GameState(), settings));
        CollectionAssert.AreEqual(Serializers.Debug.Serialize(new UnityPlayerData("Kim", 80, 4.5f), settings), Serializer.Serialize(new UnityPlayerData("Kim", 80, 4.5f), settings));
      }
    }
  }

  [TestClass]
  public class DebugUnityTypesTests : UnityTypesTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Debug; } }
  }

  [TestClass]
  public class LsUnityTypesTests : UnityTypesTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class GenUnityTypesTests : UnityTypesTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
