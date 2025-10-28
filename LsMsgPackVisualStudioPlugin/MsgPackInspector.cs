using DebuggerProxy;
using Microsoft.VisualStudio.DebuggerVisualizers;
using System.Collections.Generic;
using System.IO;

[assembly: System.Diagnostics.DebuggerVisualizer(
typeof(LsMsgPackVisualStudioPlugin.MsgPackInspector),
typeof(VisualizerObjectSource),
Target = typeof(MsgPackByteArray),
Description = "MsgPack debugging tool")]

[assembly: System.Diagnostics.DebuggerVisualizer(
typeof(LsMsgPackVisualStudioPlugin.MsgPackInspector),
typeof(VisualizerObjectSource),
Target = typeof(Stream),
Description = "MsgPack debugging tool")]

[assembly: System.Diagnostics.DebuggerVisualizer(
typeof(LsMsgPackVisualStudioPlugin.MsgPackInspector),
typeof(VisualizerObjectSource),
Target = typeof(byte[]),
Description = "MsgPack debugging tool")]

[assembly: System.Diagnostics.DebuggerVisualizer(
typeof(LsMsgPackVisualStudioPlugin.MsgPackInspector),
typeof(VisualizerObjectSource),
Target = typeof(List<byte>),
Description = "MsgPack debugging tool")]

namespace LsMsgPackVisualStudioPlugin
{
  // https://learn.microsoft.com/en-us/visualstudio/debugger/walkthrough-writing-a-visualizer-in-csharp?view=vs-2022
  //
  // https://learn.microsoft.com/en-us/previous-versions/visualstudio/visual-studio-2015/debugger/how-to-write-a-visualizer?view=vs-2015&redirectedfrom=MSDN

  /// <summary>
  /// MsgPack Explorer integrated as debugging tool
  /// </summary>
  public class MsgPackInspector : DialogDebuggerVisualizer
  {
    public MsgPackInspector() : base(FormatterPolicy.NewtonsoftJson)
    {
    }

    public MsgPackInspector(FormatterPolicy preferredFormatterPolicy) : base(preferredFormatterPolicy)
    {
    }

    protected override void Show(IDialogVisualizerService windowService, IVisualizerObjectProvider objectProvider)
    {
      System.Diagnostics.Debug.WriteLine("Init MsgPack inspector");
      byte[] bytes = ((IVisualizerObjectProvider3)objectProvider).GetObject<MsgPackByteArray>();
      System.Diagnostics.Debug.WriteLine($"{bytes.Length} Bytes ready for MsgPack inspector");
      using (InspectorWindow inspectorWindow = new InspectorWindow())
      {
        System.Diagnostics.Debug.WriteLine("Created instance of MsgPack InspectorWindow");
        inspectorWindow.Explorer.Data = bytes;
        System.Diagnostics.Debug.WriteLine("MsgPack data loaded into Inspector");
        inspectorWindow.ShowDialog();
      }

    }

    public static void TestShowVisualizer(object objectToVisualize)
    {
      MsgPackByteArray correctType = objectToVisualize as MsgPackByteArray;
      if(correctType is null){
        if(objectToVisualize is byte[])
          correctType = new MsgPackByteArray((byte[])objectToVisualize);
        else if (objectToVisualize is Stream)
          correctType = new MsgPackByteArray((Stream)objectToVisualize);
        else if (objectToVisualize is string)
          correctType = new MsgPackByteArray((string)objectToVisualize);
      }
      
      VisualizerDevelopmentHost visualizerHost = new VisualizerDevelopmentHost(correctType, typeof(MsgPackInspector));
      visualizerHost.ShowVisualizer();
    }
  }
}
