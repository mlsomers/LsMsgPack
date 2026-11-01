using LsMsgPack;
using LsMsgPack.TypeResolving.Interfaces;
using System.Collections.Generic;

namespace LtMsgPack.DataTests
{
  /// <summary>
  /// The settings the rows are compared in, for both libraries.
  /// </summary>
  public sealed class Variant
  {
    public ObjectLayout Layout;
    public bool Indexed;
    public SchemaVersion Version;
    public bool Compact;
    public bool Trim;
    public bool Filters;
    public EndianAction Endian;

    public T Apply<T>(T options) where T : MsgPackOptions
    {
      options.ObjectLayout = Layout;
      options.UseInexedSchema = Indexed;
      options.SchemaVersion = Version;
      options.DynamicallyCompact = Compact;
      options.TrimTrailingNulls = Trim;
      options.EndianAction = Endian;
      if (!Filters)
        options.DynamicFilters = new IMsgPackPropertyIncludeDynamically[0];
      return options;
    }

    public LtMsgPackOptions Lt() { return Apply(new LtMsgPackOptions()); }

    public MsgPackSettings Ls() { return Apply(new MsgPackSettings()); }

    public override string ToString()
    {
      return $"{Layout} {(Indexed ? "indexed " + Version : "names")} compact={Compact} trim={Trim} filters={Filters} {Endian}";
    }

    public static IEnumerable<object[]> All()
    {
      foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
        foreach (int schema in new[] { 0, 1, 2 })
          foreach (bool compact in new[] { true, false })
            foreach (bool trim in new[] { false, true })
              foreach (bool filters in new[] { true, false })
                foreach (EndianAction endian in new[] { EndianAction.SwapIfCurrentSystemIsLittleEndian, EndianAction.AlwaysSwap })
                {
                  if (trim && layout == ObjectLayout.Map)
                    continue;
                  yield return new object[] { new Variant() { Layout = layout, Indexed = schema > 0, Version = schema == 2 ? SchemaVersion.Described : SchemaVersion.Names,
                    Compact = compact, Trim = trim, Filters = filters, Endian = endian } };
                }
    }
  }
}
