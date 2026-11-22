using LsMsgPack;

namespace LsMsgPackUnitySmoke
{
  /// <summary>
  /// LsMsgPack's source generator in Unity (Assets/Plugins/LsMsgPack/Analyzers, label RoslynAnalyzer). The subclasses are only reached through type ids, so they are listed.
  /// Unity only: Golden writes the expected data with the runtime plans.
  /// </summary>
  [LsMsgPackSerializable(typeof(Invoice))]
  [LsMsgPackSerializable(typeof(Circle))]
  [LsMsgPackSerializable(typeof(Square))]
  [LsMsgPackSerializable(typeof(GameState))]
  [LsMsgPackSerializable(typeof(PlayerData))]
  public partial class SmokeContext : LsMsgPackContext { }
}
