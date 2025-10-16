using DebuggerProxy;
using LsMsgPackVisualStudioPlugin;
using System;
using System.IO;

namespace PluginTester
{
  internal class Program
  {

    // Note: You may need to manually copy the Microsoft.VisualStudio.DebuggerVisualizers.dll to the bin directory from
    // C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\PublicAssemblies
    static void Main(string[] args)
    {
      Console.WriteLine("Hello, World!");
      Byte[] bytes = File.ReadAllBytes("AllSmallTypes.MsgPack");
      MsgPackInspector.TestShowVisualizer(bytes);
    }
  }
}
