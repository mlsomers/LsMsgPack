using LsMsgPack.Converters;
using System;
using System.Collections.Generic;
using System.Text;

namespace LsMsgPack.References
{
  /// <summary>
  /// The content of a reference (<see cref="MsgPackOptions.ReferenceExtensionType"/>, <see cref="ReferenceHandling.Preserve"/>): the path from the root value to the first occurrence of the object,
  /// a run of MsgPack positive integers without an array header (the extension's length ends it). A step is an index in an array, or the number of an entry in a map (standing for that entry's value),
  /// counting what was written. The empty path is the root itself. Always in the specification's byte order, whatever <see cref="MsgPackOptions.EndianAction"/> says, and in the smallest format.
  /// </summary>
  internal static class ReferencePath
  {
    internal static byte[] Encode(IList<int> steps)
    {
      int length = 0;
      for (int t = 0; t < steps.Count; t++)
        length += SizeOf(steps[t]);
      byte[] bytes = new byte[length];
      int at = 0;
      for (int t = 0; t < steps.Count; t++)
        at = Write(bytes, at, steps[t]);
      return bytes;
    }

    internal static int SizeOf(int step)
    {
      if (step < 128)
        return 1;
      if (step < 256)
        return 2;
      if (step < 65536)
        return 3;
      return 5;
    }

    private static int Write(byte[] bytes, int at, int step)
    {
      if (step < 128)
      {
        bytes[at] = (byte)step;
        return at + 1;
      }
      if (step < 256)
      {
        bytes[at] = 0xcc;
        bytes[at + 1] = (byte)step;
        return at + 2;
      }
      if (step < 65536)
      {
        bytes[at] = 0xcd;
        bytes[at + 1] = (byte)(step >> 8);
        bytes[at + 2] = (byte)step;
        return at + 3;
      }
      bytes[at] = 0xce;
      bytes[at + 1] = (byte)(step >> 24);
      bytes[at + 2] = (byte)(step >> 16);
      bytes[at + 3] = (byte)(step >> 8);
      bytes[at + 4] = (byte)step;
      return at + 5;
    }

    /// <returns>The steps, or null when the content is not a run of positive integers that fit an int (any integer format is read)</returns>
    internal static int[] Decode(byte[] content)
    {
      if (content is null)
        return null;
      List<int> steps = new List<int>(content.Length);
      int at = 0;
      while (at < content.Length)
      {
        byte format = content[at++];
        ulong value;
        int size;
        if (format < 0x80)
        {
          steps.Add(format);
          continue;
        }
        switch (format)
        {
          case 0xcc: case 0xd0: size = 1; break;
          case 0xcd: case 0xd1: size = 2; break;
          case 0xce: case 0xd2: size = 4; break;
          case 0xcf: case 0xd3: size = 8; break;
          default: return null;
        }
        if (at + size > content.Length)
          return null;
        value = 0;
        for (int t = 0; t < size; t++)
          value = value << 8 | content[at + t];
        at += size;
        bool signed = format >= 0xd0;
        if (signed && (content[at - size] & 0x80) != 0) // negative
          return null;
        if (value > int.MaxValue)
          return null;
        steps.Add((int)value);
      }
      return steps.ToArray();
    }

    /// <summary>
    /// For messages: [0, 1, 3].
    /// </summary>
    internal static string Format(IList<int> steps)
    {
      if (steps is null)
        return "(not a path)";
      StringBuilder text = new StringBuilder("[");
      for (int t = 0; t < steps.Count; t++)
      {
        if (t > 0)
          text.Append(", ");
        text.Append(steps[t]);
      }
      return text.Append(']').ToString();
    }
  }

  /// <summary>
  /// The extension type codes the options give a meaning, refused for converters and custom extensions while they do: 5 with <see cref="ReferenceHandling.Preserve"/> (maintainer's decision
  /// 2026-11-16: without the option it stays usable, where such a converter reads, ext 5 is its value), 6 with <see cref="DateTimeFormat.Native"/> (the default; with
  /// <see cref="DateTimeFormat.Timestamp"/> it stays usable, e.g. for other libraries' extensions of that code).
  /// </summary>
  internal static class ReservedCodes
  {
    /// <summary>
    /// Whether the settings give a code a meaning (then <see cref="ThrowIfUsed(MsgPackOptions)"/> is needed).
    /// </summary>
    internal static bool Any(MsgPackOptions settings)
    {
      return settings.PreservesReferences || settings._nativeDateTime;
    }

    internal static void ThrowIfUsed(MsgPackOptions settings)
    {
      MsgPackConverter[] converters = settings._converters;
      for (int t = 0; t < converters.Length; t++)
        ThrowIfUsed(converters[t] as MsgPackBinaryConverter, settings);
    }

    internal static void ThrowIfUsed(MsgPackBinaryConverter converter, MsgPackOptions settings)
    {
      if (converter != null && converter.ExtensionTypeCode.HasValue)
        ThrowIfUsed(converter.ExtensionTypeCode.Value, converter.GetType().Name + " (of " + converter.ConvertedType.Name + ")", settings);
    }

    /// <param name="what">The converter or custom extension, for the message</param>
    internal static void ThrowIfUsed(sbyte code, string what, MsgPackOptions settings)
    {
      if (code == MsgPackOptions.ReferenceExtensionType && settings.PreservesReferences)
        throw new InvalidOperationException($"{what} uses extension type code {MsgPackOptions.ReferenceExtensionType}, which ReferenceHandling.Preserve uses for references. Give it another code, or leave ReferenceHandling off.");
      if (code == MsgPackOptions.DateTimeExtensionType && settings._nativeDateTime)
        throw new InvalidOperationException($"{what} uses extension type code {MsgPackOptions.DateTimeExtensionType}, which DateTimeFormat.Native uses for DateTime values. Give it another code, or set DateTimeFormat to Timestamp.");
    }
  }

  /// <summary>
  /// Compares objects by reference (ReferenceEqualityComparer is .NET 5 and later): the identity of the objects written and read.
  /// </summary>
  internal sealed class IdentityComparer : IEqualityComparer<object>
  {
    internal static readonly IdentityComparer Instance = new IdentityComparer();

    private IdentityComparer() { }

    public new bool Equals(object x, object y)
    {
      return ReferenceEquals(x, y);
    }

    public int GetHashCode(object obj)
    {
      return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
  }
}
