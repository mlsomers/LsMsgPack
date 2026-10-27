using LsMsgPack;
using LtMsgPack;
using LtMsgPack.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace LsMsgPackTypeScriptTests
{
  // The classes the generator writes models for in the tests (the test assembly is the model assembly)

  public enum TsStatus { Open = 1, Closed = 2 }

  [Flags]
  public enum TsFlags { None = 0, A = 1, B = 2 }

  public struct TsPoint
  {
    public int X { get; set; }
    public int Y { get; set; }
  }

  public class TsAddress
  {
    public string Street { get; set; }
    public string City { get; set; } = "Utrecht";
  }

  public interface ITsShape
  {
    string Label { get; }
  }

  public class TsCircle : ITsShape
  {
    public string Label { get; set; }
    public double Radius { get; set; } = 1;
  }

  public abstract class TsAnimal
  {
    public string Name { get; set; }
  }

  public class TsDog : TsAnimal
  {
    public int Barks { get; set; }
  }

  public class TsPuppy : TsDog
  {
    public TsPuppy()
    {
      Name = "Puppy"; // an inherited property set by the derived class: an override
    }
  }

  public class TsOrder
  {
    public Guid Id { get; set; }
    public int Retries { get; set; } = 3;
    public long Big { get; set; } = 1L << 60;
    public decimal Price { get; set; } = 1.50m;
    public TsStatus Status { get; set; } = TsStatus.Open;
    public TsFlags Flags { get; set; } = TsFlags.A | TsFlags.B;
    public DateTime Due { get; set; } = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
    public DateTime Created { get; set; } = DateTime.UtcNow; // another value each time
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(90);
    public char Letter { get; set; } = 'x';
    [DefaultValue("none")] public string Note { get; set; } = "none";
    public List<string> Tags { get; set; } = new List<string> { "a", "b" };
    public TsAddress Address { get; set; } = new TsAddress { Street = "Main" };
    public TsPoint Point { get; set; }
    public TsPoint? MaybePoint { get; set; }
    public List<TsAnimal> Animals { get; set; }
    public ITsShape Shape { get; set; }
    public Dictionary<string, TsAddress> Places { get; set; }
    public KeyValuePair<string, int> Pair { get; set; }
    public byte[] Data { get; set; }
    public object Anything { get; set; }
    public int ReadOnly { get { return 7; } } // computed: written, not read (GetOnlyProperties.Computed)
  }

  /// <summary>
  /// Id and ID are both id in camelCase.
  /// </summary>
  public class TsClash
  {
    public int Id { get; set; }
    public int ID { get; set; }
  }

  public static class TsSetup
  {
    /// <summary>
    /// The server's options for --options.
    /// </summary>
    public static LtMsgPackHttpOptions Alphabetical
    {
      get
      {
        LtMsgPackOptions xls = LtMsgPackPresets.LsMsgPack();
        xls.PropertyOrder = PropertyOrder.Alphabetical;
        return new LtMsgPackHttpOptions() { XLsMsgPack = xls };
      }
    }
  }

  /// <summary>
  /// A use the analyzers find (the root of --solution).
  /// </summary>
  internal static class TsUses
  {
    internal static byte[] Save(TsOrder order)
    {
      return LtMsgPackSerializer.Default.Serialize(order);
    }
  }
}
