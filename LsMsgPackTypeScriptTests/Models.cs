using LsMsgPack;
using LsMsgPackFormatters;
using LsMsgPack.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;

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

  /// <summary>Written as a string by its converter: the models have the string, not the class.</summary>
  [LsMsgPack.Converters.MsgPackConverter(typeof(TsMoneyConverter))]
  public sealed class TsMoney
  {
    public decimal Amount { get; set; }
    public string Currency { get; set; }
  }

  public sealed class TsMoneyConverter : LsMsgPack.Converters.MsgPackConverter<TsMoney, string>
  {
    public override string ToSurrogate(TsMoney value) { return value.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + value.Currency; }
    public override TsMoney FromSurrogate(string surrogate) { string[] parts = surrogate.Split(' '); return new TsMoney { Amount = decimal.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), Currency = parts[1] }; }
  }

  public class TsWallet
  {
    public TsMoney Balance { get; set; } = new TsMoney { Amount = 5, Currency = "EUR" };
    public List<TsMoney> History { get; set; }
    [LsMsgPack.Converters.MsgPackConverter(typeof(TsUnixSeconds))]
    public DateTime Opened { get; set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    [LsMsgPack.Converters.MsgPackConverter(typeof(TsUnixSeconds))]
    public DateTime? Closed { get; set; }
  }

  public sealed class TsUnixSeconds : LsMsgPack.Converters.MsgPackConverter<DateTime, long>
  {
    public override long ToSurrogate(DateTime value) { return new DateTimeOffset(value).ToUnixTimeSeconds(); }
    public override DateTime FromSurrogate(long surrogate) { return DateTimeOffset.FromUnixTimeSeconds(surrogate).UtcDateTime; }
  }

  /// <summary>Converted by a converter of the options (--options), not by an attribute.</summary>
  public sealed class TsPercentage
  {
    public double Value { get; set; }
  }

  public sealed class TsPercentageConverter : LsMsgPack.Converters.MsgPackConverter<TsPercentage, double>
  {
    public override double ToSurrogate(TsPercentage value) { return value.Value; }
    public override TsPercentage FromSurrogate(double surrogate) { return new TsPercentage { Value = surrogate }; }
  }

  public class TsDiscount
  {
    public TsPercentage Rate { get; set; } = new TsPercentage { Value = 12.5 };
    public TsPercentage[] Tiers { get; set; }
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

  /// <summary>Written by name (its attribute), a custom name.</summary>
  [MsgPackEnumFormat(EnumFormat.Name)]
  public enum TsSize { Small, [System.Runtime.Serialization.EnumMember(Value = "large")] Large }

  [Flags, MsgPackEnumFormat(EnumFormat.Name)]
  public enum TsSides { None = 0, Left = 1, Right = 2 }

  public class TsShirt
  {
    public TsSize Size { get; set; } = TsSize.Large;
    public TsSize? MaybeSize { get; set; }
    [MsgPackEnumFormat(EnumFormat.Name)] public TsStatus Status { get; set; } = TsStatus.Closed;
    [MsgPackEnumFormat(EnumFormat.Number)] public TsSize SizeNumber { get; set; } = TsSize.Large;
    public TsStatus Plain { get; set; } = TsStatus.Open;
    public List<TsSize> Sizes { get; set; }
    public TsSides Sides { get; set; } = TsSides.Left | TsSides.Right;
  }

  public static class TsSetup
  {
    /// <summary>
    /// The server's options writing every enum by name.
    /// </summary>
    public static LsMsgPackOptions WithEnumNames
    {
      get
      {
        LsMsgPackOptions xls = LsMsgPackPresets.LsMsgPack();
        xls.EnumFormat = EnumFormat.Name;
        return xls;
      }
    }

    /// <summary>
    /// The server's options for --options.
    /// </summary>
    public static LsMsgPackHttpOptions Alphabetical
    {
      get
      {
        LsMsgPackOptions xls = LsMsgPackPresets.LsMsgPack();
        xls.PropertyOrder = PropertyOrder.Alphabetical;
        return new LsMsgPackHttpOptions() { XLsMsgPack = xls };
      }
    }

    /// <summary>
    /// The server's options with a converter of its own (not an attribute on the type).
    /// </summary>
    public static LsMsgPackOptions WithConverters
    {
      get
      {
        LsMsgPackOptions xls = LsMsgPackPresets.LsMsgPack();
        xls.Converters = new LsMsgPack.Converters.MsgPackConverter[] { new TsPercentageConverter() };
        return xls;
      }
    }
  }

  public class TsReply
  {
    public string Message { get; set; }
  }

  public class TsRequest
  {
    public int Count { get; set; }
  }

  public class TsLine
  {
    public decimal Amount { get; set; }
  }

  /// <summary>
  /// Uses the analyzers find (the roots of --solution).
  /// </summary>
  internal static class TsUses
  {
    internal static byte[] Save(TsOrder order)
    {
      return LsMsgPackSerializer.Default.Serialize(order);
    }

    /// <summary>
    /// A minimal API: the body is read (models only), the result written (models and schemas).
    /// </summary>
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
      endpoints.MapPost("/reply", (MsgPackBody<TsRequest> request) => MsgPackResults.MsgPack(new TsReply()));
      endpoints.MapGet("/lines", () => MsgPackResults.MsgPack(Lines()));
    }

    private static async IAsyncEnumerable<TsLine> Lines()
    {
      await Task.Yield();
      yield return new TsLine();
    }
  }
}
