using LsMsgPack;

namespace LsMsgPack.Http
{
  /// <summary>
  /// The settings of the web formatters (ASP.NET Core, ASP.NET Web API and HttpClient, ASP.NET MVC 5) per media type, see <see cref="MsgPackMediaTypes"/>.
  /// <para>The defaults need nothing from the other side: application/x-lsmsgpack uses the indexed schema and, between these formatters, schema references (see <see cref="NegotiateSchemas"/>).
  /// application/msgpack and application/x-msgpack write what any MsgPack library reads (<see cref="LsMsgPackPresets.Generic"/>), set <see cref="Plain"/> to another of the <see cref="LsMsgPackPresets"/> to match a specific library.</para>
  /// <para>Read when the formatters are created (<see cref="LsMsgPackHttpSerializer"/>), later changes do not apply.</para>
  /// </summary>
  public class LsMsgPackHttpOptions
  {
    /// <summary>
    /// application/x-lsmsgpack: by default the indexed schema, type ids where the type differs and default values left out (<see cref="LsMsgPackPresets.LsMsgPack"/>).
    /// With <see cref="NegotiateSchemas"/> the schemas are kept in its <see cref="MsgPackOptions.SchemaStore"/> (one is created when it has none).
    /// </summary>
    public LsMsgPackOptions XLsMsgPack { get; set; } = LsMsgPackPresets.LsMsgPack();

    /// <summary>
    /// application/msgpack and application/x-msgpack: by default what any MsgPack library reads (<see cref="LsMsgPackPresets.Generic"/>): maps keyed by property names, every value, no type ids, Guids and decimals as text, dates as timestamps (a DateTimeOffset loses its offset).
    /// For a specific library use its preset, e.g. <c>Plain = LsMsgPackPresets.MessagePackCSharp()</c>; between .NET programs application/x-lsmsgpack keeps the offset and the class hierarchies.
    /// </summary>
    public LsMsgPackOptions Plain { get; set; } = LsMsgPackPresets.Generic();

    /// <summary>
    /// application/x-lsmsgpack with the indexed schema: a response refers to its schema (18 bytes) instead of containing it when the request says the client holds it (header <see cref="LsMsgPackHttpSerializer.SchemasHeader"/>), otherwise the schema is sent inline. Default true.
    /// <para>HttpClient advertises the schemas it received with the handler of LsMsgPackMediaTypeFormatter.CreateHandler(). Request bodies always carry their schema inline (the server caches it by its bytes).</para>
    /// </summary>
    public bool NegotiateSchemas { get; set; } = true;

    /// <summary>
    /// application/x-lsmsgpack: a client that asks for it (<c>Accept: application/x-lsmsgpack; schema=described</c>, see <see cref="MsgPackMediaTypes.AcceptsDescribedSchema"/>) gets version 2 of the schema
    /// (<see cref="SchemaVersion.Described"/>: the kind and default of each property and the type of the root), so a client without the classes (JavaScript) can make complete objects. Default true.
    /// <para>The body is the same. The schema of each version has its own id (negotiated like any other), and the .NET readers read both.</para>
    /// </summary>
    public bool DescribeSchemas { get; set; } = true;

    /// <summary>
    /// With <see cref="NegotiateSchemas"/>: the schema of a response type is made at once with every class its values can hold (<see cref="MsgPackOptions.CompleteSchemas"/>), default true.
    /// <para>Its id then does not depend on the data: every instance of the server (a web farm, after a restart) writes the same schemas, so a client keeps using what it holds,
    /// and a client can hold them before its first request (the schemas the TypeScript generator writes, see docs/TypeScriptClient.md).</para>
    /// </summary>
    public bool CompleteSchemas { get; set; } = true;

    /// <summary>
    /// ASP.NET Core: a response of an <c>IAsyncEnumerable&lt;T&gt;</c> is written as an array of unknown length, the items as they come, when the client asks for it
    /// (<see cref="MsgPackMediaTypes.StreamParameter"/> in the Accept header, see <see cref="MsgPackMediaTypes.AcceptsStream"/>). Default true. False: such responses are read to the end and written as a <c>List&lt;T&gt;</c>, as for other clients.
    /// </summary>
    public bool WriteStreams { get; set; } = true;

    /// <summary>
    /// Bodies with maps keyed by property names (application/msgpack, x-msgpack; x-lsmsgpack without the indexed schema): a key that is not a property name sets the one property
    /// whose name equals it ignoring case (<see cref="MsgPackOptions.PropertyNameCaseInsensitive"/>), default true, as ASP.NET Core reads JSON.
    /// <para>For clients that write other names, e.g. the TypeScript client with <c>propertyNames: 'camelCase'</c> (docs/TypeScriptClient.md). Set on the options of both media types, the exact name is looked up first.</para>
    /// </summary>
    public bool PropertyNameCaseInsensitive { get; set; } = true;

    /// <summary>
    /// When the writers of a type are compiled (<see cref="LsMsgPackOptions.CompileWritersAfter"/>, replaces the value of <see cref="XLsMsgPack"/> and <see cref="Plain"/>), default 0: when a type is first written.
    /// <para>A server writes its types many times, so the compiled writer is used from the first response on (the type's first response waits for it: 1-7 ms, the first in the process ~16 ms more),
    /// and the JIT optimizes the serializer's object loop with the compiled writers in place (compiled later, after its profile was taken: about 4% slower on arrays).</para>
    /// </summary>
    public int CompileWritersAfter { get; set; } = 0;

    /// <summary>
    /// When the readers of a type are compiled (<see cref="LsMsgPackOptions.CompileReadersAfter"/>, replaces the value of <see cref="XLsMsgPack"/> and <see cref="Plain"/>), default 0: when a type is first read.
    /// <para>As for the writers: the compiled reader is used from the first request body on (the type's first request waits for it), and the JIT optimizes the serializer's object loops
    /// with the compiled readers in place.</para>
    /// </summary>
    public int CompileReadersAfter { get; set; } = 0;

    /// <summary>
    /// The most arrays and maps (objects, collections) one body may hold (<see cref="MsgPackOptions.MaxObjects"/>, replaces the value of <see cref="XLsMsgPack"/> and <see cref="Plain"/>),
    /// default 1,000,000; more are refused (ASP.NET Core: 400 Bad Request). An empty map is one byte that becomes a whole object, so the size of the body alone does not limit the memory
    /// a request takes (docs/security.md). <see cref="int.MaxValue"/> for no limit.
    /// </summary>
    public int MaxObjects { get; set; } = 1000000;

    /// <summary>
    /// The most array elements and map entries one body may hold, all together (<see cref="MsgPackOptions.MaxItems"/>, replaces the value of <see cref="XLsMsgPack"/> and <see cref="Plain"/>),
    /// default 10,000,000. <see cref="int.MaxValue"/> for no limit.
    /// </summary>
    public int MaxItems { get; set; } = 10000000;

    /// <summary>
    /// The longest string in a body, in bytes (<see cref="MsgPackOptions.MaxStringLength"/>, replaces the value of <see cref="XLsMsgPack"/> and <see cref="Plain"/>), default no limit
    /// (<see cref="int.MaxValue"/>): the size of the body limits it (<c>MaxRequestBodySize</c>).
    /// </summary>
    public int MaxStringLength { get; set; } = int.MaxValue;

    /// <summary>
    /// The longest binary value or extension in a body, in bytes (<see cref="MsgPackOptions.MaxBinLength"/>, replaces the value of <see cref="XLsMsgPack"/> and <see cref="Plain"/>),
    /// default no limit (<see cref="int.MaxValue"/>): the size of the body limits it.
    /// </summary>
    public int MaxBinLength { get; set; } = int.MaxValue;

    /// <summary>
    /// Collect what did not match between a body and the classes it is read into (<see cref="ReadDifferences"/>: unknown properties, classes without a schema entry, type ids that are not found...). Default false.
    /// <para>ASP.NET Core: in <c>HttpContext.Features</c> (<c>HttpContext.GetReadDifferences()</c>) and logged (see <see cref="LogDifferencesAsWarning"/>). Web API and HttpClient: <c>HttpContent.GetReadDifferences()</c> (also <c>request.GetReadDifferences()</c>).
    /// MVC 5: <c>HttpContextBase.GetReadDifferences()</c>. Reading with it is a bit slower (see docs/ReadDifferences.md), the report itself is only made when it is asked for.</para>
    /// </summary>
    public bool ReportDifferences { get; set; }

    /// <summary>
    /// ASP.NET Core, with <see cref="ReportDifferences"/>: log the differences of a request body as a warning. Default false: they are logged at the Debug level,
    /// so clients that send properties the classes do not have (other versions of the models) do not fill the logs. Ignored without <see cref="ReportDifferences"/>.
    /// </summary>
    public bool LogDifferencesAsWarning { get; set; }

    /// <summary>
    /// Client side: the most schema ids sent per server (the most recently received ones), 32 by default.
    /// </summary>
    public int MaxAdvertisedSchemas { get; set; } = 32;

    /// <summary>
    /// Client side: a request lists the schemas received for its route (its path, in which segments that look like ids count as one: all digits, or 16 or more hexadecimal digits and dashes),
    /// at most 4 and <see cref="MaxAdvertisedSchemas"/>, and the most recently received ones only for a route it has not called yet. Default true.
    /// <para>So a server with more response types than <see cref="MaxAdvertisedSchemas"/> still refers to the schemas (200 endpoints called in turn: 100% instead of 16% of the responses),
    /// with a header of one id instead of 32. A route the rule does not see (a path with a name in it, /products/red-shoes) only costs an inline schema at its first call:
    /// a client lists only schemas it holds. False: always the most recently received ones.</para>
    /// </summary>
    public bool AdvertiseByRoute { get; set; } = true;
  }
}
