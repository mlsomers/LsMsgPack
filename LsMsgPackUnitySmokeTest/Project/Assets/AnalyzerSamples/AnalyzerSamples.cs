using LsMsgPack;

namespace LsMsgPackUnitySmoke
{
  /// <summary>
  /// Deliberate warnings of LsMsgPack's analyzers (Assets/Plugins/LsMsgPack/Analyzers/LsMsgPack.Analyzers.dll, label RoslynAnalyzer): run.sh looks for them in the editor's log,
  /// which shows that Unity loaded and ran the analyzers. An editor-only assembly of its own: run.sh writes Stamp.cs next to it, so Unity compiles (and logs) it on every run.
  /// </summary>
  public class AnalyzerSample
  {
    public int Lives { get; set; } = 3;   // LSMP001: initialized without [DefaultValue]
    public object Anything { get; set; }  // LSMP004: holds any type the data picks
    public string Note;                   // LSMP010: a public field the default fields mode leaves out

    public static byte[] Write(AnalyzerSample sample)
    {
      return new LsMsgPackSerializer().Serialize(sample);
    }
  }
}
