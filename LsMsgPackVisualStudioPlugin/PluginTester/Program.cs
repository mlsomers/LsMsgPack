using DebuggerProxy;
using LsMsgPackVisualStudioPlugin;
using System;
using System.IO;

namespace PluginTester
{
  internal class Program
  {
    static void Main(string[] args)
    {
      Console.WriteLine("Hello, World!");
      Byte[] bytes = File.ReadAllBytes("AllSmallTypes.MsgPack");
      MsgPackInspector.TestShowVisualizer(bytes);
    }
  }
}
