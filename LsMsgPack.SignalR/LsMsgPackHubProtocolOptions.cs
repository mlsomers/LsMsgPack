using LsMsgPack.TypeResolving.Interfaces;

namespace LsMsgPack.SignalR
{
  /// <summary>
  /// The settings of <see cref="LsMsgPackHubProtocol"/>. Read when the protocol is created, later changes do not apply.
  /// </summary>
  public class LsMsgPackHubProtocolOptions
  {
    /// <summary>
    /// The protocol is named "messagepack" and writes the values as Microsoft's MessagePack protocol does (its resolver: MessagePack-CSharp's contractless resolver with enums as strings),
    /// so the clients of that protocol (<c>@microsoft/signalr-protocol-msgpack</c>, <c>AddMessagePackProtocol()</c> of the .NET, Java and Swift clients) connect without a change. Default false: "lsmsgpack".
    /// <para>Set by <c>AddLsMsgPackAsMessagePackProtocol()</c>; it decides the defaults of <see cref="SerializerOptions"/>.</para>
    /// </summary>
    public bool AsMessagePack { get; set; }

    /// <summary>
    /// The options of the values (the arguments, stream items and results). Null (the default): <see cref="DefaultSerializerOptions(bool)"/> of <see cref="AsMessagePack"/>.
    /// <para>A copy is used, without the indexed schema and schema references whatever these say: SignalR writes a message once for all the connections it goes to (groups, all clients),
    /// so its bytes cannot depend on what one connection received before, and a schema in every message costs more than it saves.</para>
    /// </summary>
    public LsMsgPackOptions SerializerOptions { get; set; }

    /// <summary>
    /// The most arrays and maps (objects, collections) one value may hold (<see cref="MsgPackOptions.MaxObjects"/>, replaces the value of <see cref="SerializerOptions"/>), default 1,000,000, as the web formatters.
    /// SignalR's <c>MaximumReceiveMessageSize</c> (32 KB by default) limits the bytes, this the objects those bytes become (docs/security.md). <see cref="int.MaxValue"/> for no limit.
    /// </summary>
    public int MaxObjects { get; set; } = 1000000;

    /// <summary>
    /// The most array elements and map entries one value may hold, all together (<see cref="MsgPackOptions.MaxItems"/>, replaces the value of <see cref="SerializerOptions"/>), default 10,000,000.
    /// </summary>
    public int MaxItems { get; set; } = 10000000;

    /// <summary>
    /// The longest string in bytes (<see cref="MsgPackOptions.MaxStringLength"/>, replaces the value of <see cref="SerializerOptions"/>), default <see cref="int.MaxValue"/> (no limit beyond the message size).
    /// </summary>
    public int MaxStringLength { get; set; } = int.MaxValue;

    /// <summary>
    /// The longest binary value or extension in bytes (<see cref="MsgPackOptions.MaxBinLength"/>, replaces the value of <see cref="SerializerOptions"/>), default <see cref="int.MaxValue"/>.
    /// </summary>
    public int MaxBinLength { get; set; } = int.MaxValue;

    /// <summary>
    /// The default options of the values. Every call returns new options.
    /// <list type="bullet">
    /// <item>"lsmsgpack": LsMsgPack's values (<see cref="LsMsgPackPresets.LsMsgPack"/>: decimals exact, DateTime with its kind, DateTimeOffset with its offset, type ids where the type differs, enums as numbers)
    /// with objects as maps keyed by property names and every value written (also defaults: a client without the classes, JavaScript, gets complete objects), Guids as text (without a schema JavaScript
    /// cannot tell a Guid from 16 bytes).</item>
    /// <item>"messagepack": the values of Microsoft's MessagePack protocol (<see cref="LsMsgPackPresets.MessagePackCSharp"/>, enums by name).</item>
    /// </list>
    /// Both read property names ignoring case when the exact name is not found (<see cref="MsgPackOptions.PropertyNameCaseInsensitive"/>): JavaScript clients send camelCase objects.
    /// </summary>
    public static LsMsgPackOptions DefaultSerializerOptions(bool asMessagePack)
    {
      LsMsgPackOptions options;
      if (asMessagePack)
      {
        options = LsMsgPackPresets.MessagePackCSharp();
        options.EnumFormat = EnumFormat.Name; // SignalR's resolver starts with MessagePack-CSharp's DynamicEnumAsStringResolver
      }
      else
      {
        options = LsMsgPackPresets.LsMsgPack();
        options.ObjectLayout = ObjectLayout.Map; // without a schema an array can only be read in the writer's order
        options.DynamicFilters = new IMsgPackPropertyIncludeDynamically[0];
        options.GuidFormat = GuidFormat.String;
      }
      options.UseIndexedSchema = false;
      options.WriteSchemaReference = false;
      options.PropertyNameCaseInsensitive = true;
      return options;
    }
  }
}
