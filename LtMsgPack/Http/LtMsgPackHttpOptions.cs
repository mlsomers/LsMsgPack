using LsMsgPack;

namespace LtMsgPack.Http
{
  /// <summary>
  /// The settings of the web formatters (ASP.NET Core, ASP.NET Web API and HttpClient, ASP.NET MVC 5) per media type, see <see cref="MsgPackMediaTypes"/>.
  /// <para>The defaults need nothing from the other side: application/x-lsmsgpack uses the indexed schema and, between these formatters, schema references (see <see cref="NegotiateSchemas"/>).
  /// application/msgpack and application/x-msgpack write maps keyed by property names, set <see cref="Plain"/> to one of the <see cref="LtMsgPackPresets"/> to match another library.</para>
  /// <para>Read when the formatters are created (<see cref="LtMsgPackHttpSerializer"/>), later changes do not apply.</para>
  /// </summary>
  public class LtMsgPackHttpOptions
  {
    /// <summary>
    /// application/x-lsmsgpack: by default the indexed schema, type ids where the type differs and default values left out (<see cref="LtMsgPackPresets.LsMsgPack"/>).
    /// With <see cref="NegotiateSchemas"/> the schemas are kept in its <see cref="MsgPackOptions.SchemaStore"/> (one is created when it has none).
    /// </summary>
    public LtMsgPackOptions XLsMsgPack { get; set; } = LtMsgPackPresets.LsMsgPack();

    /// <summary>
    /// application/msgpack and application/x-msgpack: by default maps keyed by property names without type ids. For other libraries use a preset, e.g. <c>Plain = LtMsgPackPresets.MessagePackCSharp()</c>.
    /// </summary>
    public LtMsgPackOptions Plain { get; set; } = MsgPackMediaTypes.ToPlain(new LtMsgPackOptions());

    /// <summary>
    /// application/x-lsmsgpack with the indexed schema: a response refers to its schema (18 bytes) instead of containing it when the request says the client holds it (header <see cref="LtMsgPackHttpSerializer.SchemasHeader"/>), otherwise the schema is sent inline. Default true.
    /// <para>HttpClient advertises the schemas it received with the handler of LsMsgPackMediaTypeFormatter.CreateHandler(). Request bodies always carry their schema inline (the server caches it by its bytes).</para>
    /// </summary>
    public bool NegotiateSchemas { get; set; } = true;

    /// <summary>
    /// Collect what did not match between a body and the classes it is read into (<see cref="ReadDifferences"/>: unknown properties, classes without a schema entry, type ids that are not found...). Default false.
    /// <para>ASP.NET Core: in <c>HttpContext.Features</c> (<c>HttpContext.GetReadDifferences()</c>) and logged as a warning. Web API and HttpClient: <c>HttpContent.GetReadDifferences()</c> (also <c>request.GetReadDifferences()</c>).
    /// MVC 5: <c>HttpContextBase.GetReadDifferences()</c>. Reading with it is a bit slower (see docs/ReadDifferences.md), the report itself is only made when it is asked for.</para>
    /// </summary>
    public bool ReportDifferences { get; set; }

    /// <summary>
    /// Client side: the most schema ids sent per server (the most recently received ones), 32 by default.
    /// </summary>
    public int MaxAdvertisedSchemas { get; set; } = 32;
  }
}
