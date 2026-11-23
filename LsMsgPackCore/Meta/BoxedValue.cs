using System.Runtime.CompilerServices;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// The value in a box by reference (reads and writes the boxed struct itself, without copying it).
  /// </summary>
  internal static class BoxedValue
  {
    /// <summary>
    /// <paramref name="box"/> must be a boxed <typeparamref name="T"/> (not checked in the .NET Standard builds).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ref T Ref<T>(object box) where T : struct
    {
#if NET5_0_OR_GREATER
      return ref Unsafe.Unbox<T>(box);
#else
      // Unity's IL2CPP (6000.6) fails to compile Unsafe.Unbox<T> in a method of a generic class ("IL2CPP Internal Error: Attempt to access method info data ...
      // for TTarget with Type when no method info would be passed in"), for any struct. A box has the layout of a StrongBox<T>: the value right after the object header.
      return ref Unsafe.As<StrongBox<T>>(box).Value;
#endif
    }
  }
}
