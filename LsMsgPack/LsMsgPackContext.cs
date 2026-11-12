using LsMsgPack.Generated;

namespace LsMsgPack
{
  /// <summary>
  /// Code written at compile time by LsMsgPack's source generator for the types it lists: property accessors, creators, the generic instantiations of the serializer
  /// and writers and readers per type. Faster, and it works where code cannot be generated at runtime (NativeAOT, WebAssembly AOT, iOS) and with trimming.
  /// <para>Declare a partial class deriving from it with the root types, the generator writes the rest (a parameterless constructor and a static <c>Default</c> instance):</para>
  /// <code>
  /// [LsMsgPackSerializable(typeof(Invoice))]
  /// public partial class AppMsgPackContext : LsMsgPackContext { }
  /// </code>
  /// <para>Use it with <see cref="LsMsgPackOptions.Contexts"/>, or let the generated module initializer register it for the process (C# 9 and later, see <see cref="LsMsgPackGenerateAttribute.AutoRegister"/>).
  /// The data does not change: generated code writes the same bytes and reads the same values as the serializer does without it, and anything it does not cover is done as without it.</para>
  /// </summary>
  public abstract class LsMsgPackContext : GeneratedContext
  {
    /// <param name="contractVersion">Written by the generator</param>
    protected LsMsgPackContext(int contractVersion) : base(contractVersion)
    {
    }
  }
}
