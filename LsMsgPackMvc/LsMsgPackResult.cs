using LsMsgPack;
using LsMsgPack.Http;
using System;
using System.Net.Http.Headers;
using System.Web;
using System.Web.Mvc;

namespace LsMsgPackMvc
{
  /// <summary>
  /// Writes the data as application/msgpack, application/x-msgpack (plain MsgPack) or application/x-lsmsgpack, with LsMsgPack (see <see cref="LsMsgPackHttpOptions"/>).
  /// <para>Unless <see cref="ContentType"/> is set, the media type is chosen from the request's Accept header, application/msgpack is used when none of them is accepted.</para>
  /// </summary>
  public class LsMsgPackResult : ActionResult
  {
    public LsMsgPackResult() { }

    public LsMsgPackResult(object data)
    {
      Data = data;
    }

    /// <summary>
    /// The object to serialize.
    /// </summary>
    public object Data { get; set; }

    /// <summary>
    /// The type the data is declared as, a type id is only added to the root when the data's actual type differs (application/x-lsmsgpack only). Defaults to the data's own type.
    /// </summary>
    public Type DeclaredType { get; set; }

    /// <summary>
    /// One of the <see cref="MsgPackMediaTypes"/>, leave null to use the request's Accept header.
    /// </summary>
    public string ContentType { get; set; }

    /// <summary>
    /// The serializers per media type, defaults to <see cref="LsMsgPackMvc.Serializer"/> (create one per set of options and keep it).
    /// </summary>
    public LsMsgPackHttpSerializer Serializer { get; set; }

    public override void ExecuteResult(ControllerContext context)
    {
      if (context is null)
        throw new ArgumentNullException(nameof(context));

      string mediaType = ContentType ?? Negotiate(context.HttpContext.Request);
      LsMsgPackHttpSerializer serializer = Serializer ?? LsMsgPackMvc.Serializer;
      bool lsMsgPack = MsgPackMediaTypes.IsLsMsgPack(mediaType);
      bool negotiated = serializer.NegotiatesSchemas && lsMsgPack;
      bool describes = serializer.DescribesSchemas && lsMsgPack;
      string clientSchemas = negotiated ? context.HttpContext.Request.Headers[LsMsgPackHttpSerializer.SchemasHeader] : null;
      bool described = describes && MsgPackMediaTypes.AcceptsDescribedSchema(context.HttpContext.Request.Headers["Accept"]);

      MsgPackPayload payload = serializer.Serialize(Data, DeclaredType ?? Data?.GetType(), mediaType, clientSchemas, described);

      HttpResponseBase response = context.HttpContext.Response;
      response.ContentType = mediaType;
      if (negotiated || describes) // version 2 of the schema depends on the Accept header
        response.AppendHeader("Vary", !negotiated ? "Accept" : describes ? LsMsgPackHttpSerializer.SchemasHeader + ", Accept" : LsMsgPackHttpSerializer.SchemasHeader);
      if (negotiated && !(payload.SchemaId is null))
        response.AppendHeader(LsMsgPackHttpSerializer.SchemaHeader, payload.SchemaId);
      payload.WriteTo(response.OutputStream);
    }

    /// <summary>
    /// Returns the supported media type the client prefers (highest quality, then the order in the header), or application/msgpack when none of them is accepted.
    /// </summary>
    internal static string Negotiate(HttpRequestBase request)
    {
      string[] acceptTypes = request.AcceptTypes;
      if (acceptTypes is null)
        return MsgPackMediaTypes.MsgPack;

      string preferred = null;
      double best = 0; // a quality of 0 is not acceptable
      for (int t = 0; t < acceptTypes.Length; t++)
      {
        if (!MediaTypeWithQualityHeaderValue.TryParse(acceptTypes[t], out MediaTypeWithQualityHeaderValue accept))
          continue;
        double quality = accept.Quality ?? 1;
        if (quality > best && MsgPackMediaTypes.IsSupported(accept.MediaType)) // only a higher one: equal qualities keep the header order
        {
          best = quality;
          preferred = accept.MediaType;
        }
      }
      return preferred ?? MsgPackMediaTypes.MsgPack;
    }
  }
}
