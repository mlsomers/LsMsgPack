using DebuggerProxy;
using Microsoft.VisualStudio.DebuggerVisualizers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
  /// 
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
      byte[] bytes = ((IVisualizerObjectProvider3)objectProvider).GetObject<MsgPackByteArray>();
      using (InspectorWindow inspectorWindow = new InspectorWindow())
      {
        inspectorWindow.Explorer.Data = bytes;
        inspectorWindow.ShowDialog();
      }

    }

    public static void TestShowVisualizer(object objectToVisualize)
    {
      MsgPackByteArray correctType = objectToVisualize as MsgPackByteArray;
      if(correctType is null){
        if(objectToVisualize is byte[])
          correctType = new MsgPackByteArray((byte[])objectToVisualize);
        else
          correctType = new MsgPackByteArray((Stream)objectToVisualize);
      }
      
      VisualizerDevelopmentHost visualizerHost = new VisualizerDevelopmentHost(correctType, typeof(MsgPackInspector));
      visualizerHost.ShowVisualizer();
    }
  }
}
