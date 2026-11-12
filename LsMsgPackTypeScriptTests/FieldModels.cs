#nullable enable
using LsMsgPack;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.Http;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace LsMsgPackTypeScriptTests
{
  // Models with fields for the generator tests (nullable annotations on, as the project's other models are without)

  /// <summary>
  /// Attributed fields (the default mode) and a public one without an attribute (only in the Public mode).
  /// </summary>
  public class TsFieldsAttributed
  {
    [DataMember] public int Level = 3;
    [JsonInclude] private string? _secret = "s";
    [DataMember] public string Title = "t";
    [DataMember] public string? Maybe;
    [DataMember] public readonly int Fixed = 5;
    [DataMember] public readonly int Computed;
    public int Plain = 9;

    public TsFieldsAttributed()
    {
      Computed = Fixed * 2; // an initonly field set by the constructor
    }

    public string? Secret { get { return _secret; } }
  }

  /// <summary>
  /// Public fields: serialized with SerializedFields.Public.
  /// </summary>
  public class TsFieldsPublic
  {
    public int Count = 4;
    public string Name = "n";
    public TsFieldsStruct Inner;
    public TsAddress? Address;
  }

  public struct TsFieldsStruct
  {
    [DataMember] public int A;
    [DataMember] public double B;
  }

  public class TsFieldsVector
  {
    public Vector3 Position { get; set; }
  }

  public class TsFieldsNumerics
  {
    public Quaternion Rotation { get; set; }
    public Matrix4x4 Transform { get; set; }
    public List<Vector3> Points { get; set; } = new List<Vector3>();
  }

  public class TsFieldsVectorField
  {
    public Vector3 Position;
  }

  public static class TsFieldsSetup
  {
    /// <summary>
    /// The server's options with the public fields serialized.
    /// </summary>
    public static LsMsgPackHttpOptions PublicFields
    {
      get
      {
        LsMsgPackOptions xls = LsMsgPackPresets.LsMsgPack();
        xls.StaticFilters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(GetOnlyProperties.All, NonPublicProperties.None, SerializedFields.Public), new FilterIgnoredAttribute(), new FilterStatic() };
        return new LsMsgPackHttpOptions() { XLsMsgPack = xls };
      }
    }
  }
}
