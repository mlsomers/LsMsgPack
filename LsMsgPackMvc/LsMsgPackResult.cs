using LsMsgPack;
using LtMsgPack.Http;
using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Web;
using System.Web.Mvc;

namespace LsMsgPackMvc
{
  /// <summary>
  /// Writes the data as application/msgpack, application/x-msgpack (plain MsgPack) or application/x-lsmsgpack, with LtMsgPack (see <see cref="LtMsgPackHttpOptions"/>).
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
    public LtMsgPackHttpSerializer Serializer { get; set; }

    public override void ExecuteResult(ControllerContext context)
    {
      if (context is null)
        throw new ArgumentNullException(nameof(context));

      string mediaType = ContentType ?? Negotiate(context.HttpContext.Request);
      LtMsgPackHttpSerializer serializer = Serializer ?? LsMsgPackMvc.Serializer;
      bool negotiated = serializer.NegotiatesSchemas && MsgPackMediaTypes.IsLsMsgPack(mediaType);
      string clientSchemas = negotiated ? context.HttpContext.Request.Headers[LtMsgPackHttpSerializer.SchemasHeader] : null;

      MsgPackPayload payload = serializer.Serialize(Data, DeclaredType ?? Data?.GetType(), mediaType, clientSchemas);

      HttpResponseBase response = context.HttpContext.Response;
      response.ContentType = mediaType;
      if (negotiated)
      {
        response.AppendHeader("Vary", LtMsgPackHttpSerializer.SchemasHeader);
        if (!(payload.SchemaId is null))
          response.AppendHeader(LtMsgPackHttpSerializer.SchemaHeader, payload.SchemaId);
      }
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

      string preferred = acceptTypes
        .Select(accept => MediaTypeWithQualityHeaderValue.TryParse(accept, out MediaTypeWithQualityHeaderValue parsed) ? parsed : null)
        .Where(accept => !(accept is null) && (accept.Quality ?? 1) > 0 && MsgPackMediaTypes.IsSupported(accept.MediaType))
        .OrderByDescending(accept => accept.Quality ?? 1) // OrderBy is stable, so equal qualities keep the header order
        .Select(accept => accept.MediaType)
        .FirstOrDefault();
      return preferred ?? MsgPackMediaTypes.MsgPack;
    }
  }
}
