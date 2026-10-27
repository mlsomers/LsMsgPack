#if NET8_0_OR_GREATER
using LsMsgPack;
using LtMsgPack.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace LsMsgPackFormatters
{
  /// <summary>
  /// The result of a minimal API endpoint, as <c>Ok(value)</c> of a controller with the formatters: MsgPack when the Accept header asks for it (<see cref="MsgPackMediaTypes"/>), JSON otherwise
  /// (the JSON options of minimal APIs, <c>ConfigureHttpJsonOptions</c>). Create it with <see cref="MsgPackResults.MsgPack{T}(T, int?)"/> or <c>Results.Extensions.MsgPack(value)</c>;
  /// the serializer is the one <see cref="LsMsgPackFormatter.AddLsMsgPack(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{LtMsgPackHttpOptions})"/> registers.
  /// <para>The response varies with the Accept header (<c>Vary: Accept</c>), and for application/x-lsmsgpack with the schemas the client holds, as the output formatter's.</para>
  /// </summary>
  /// <typeparam name="T">The declared type: with application/x-lsmsgpack a value of another class gets a type id, as for the declared result type of an action (object: the value's class)</typeparam>
  public sealed class MsgPackResult<T> : IResult, IStatusCodeHttpResult, IValueHttpResult, IValueHttpResult<T>, IEndpointMetadataProvider
  {
    /// <summary>
    /// The content types of the response, in the order of MVC's formatters (JSON first).
    /// </summary>
    internal static readonly string[] ContentTypes = { "application/json", MsgPackMediaTypes.MsgPack, MsgPackMediaTypes.XMsgPack, MsgPackMediaTypes.XLsMsgPack };

    internal MsgPackResult(T value, int? statusCode)
    {
      Value = value;
      StatusCode = statusCode;
    }

    /// <summary>
    /// The value written.
    /// </summary>
    public T Value { get; }

    object IValueHttpResult.Value
    {
      get { return Value; }
    }

    /// <summary>
    /// The status code of the response (null: 200, or what the endpoint set).
    /// </summary>
    public int? StatusCode { get; }

    public Task ExecuteAsync(HttpContext httpContext)
    {
      if (httpContext is null)
        throw new ArgumentNullException(nameof(httpContext));
      LtMsgPackHttpSerializer serializer = LsMsgPackFormatter.MinimalApiSerializer(httpContext);
      HttpResponse response = httpContext.Response;
      if (StatusCode.HasValue)
        response.StatusCode = StatusCode.Value;

      string mediaType = MsgPackNegotiation.Choose(httpContext.Request.Headers.Accept);
      if (mediaType is null)
      {
        response.Headers.Append("Vary", "Accept");
        return WriteJsonAsync(httpContext);
      }

      response.ContentType = mediaType;
      Type declared = typeof(T) == typeof(object) && !(Value is null) ? Value.GetType() : typeof(T); // as MVC's ObjectResult: an object is written as its class
      return MsgPackResponse.WriteAsync(httpContext, serializer, Value, declared, mediaType, true);
    }

    /// <summary>
    /// As <c>Results.Json(value)</c>: the declared type, or the value's class when the declared type may hold others and has no polymorphism of its own (as the JSON formatter of MVC).
    /// </summary>
    private Task WriteJsonAsync(HttpContext httpContext)
    {
      JsonSerializerOptions options = LsMsgPackFormatter.MinimalApiJson(httpContext);
      JsonTypeInfo info = options.GetTypeInfo(typeof(T));
      Type runtime = Value?.GetType();
      if (!(runtime is null) && runtime != info.Type && !info.Type.IsSealed && !info.Type.IsValueType && info.PolymorphismOptions is null)
        info = options.GetTypeInfo(runtime);
      return httpContext.Response.WriteAsJsonAsync(Value, info, null, httpContext.RequestAborted);
    }

    static void IEndpointMetadataProvider.PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
      if (builder is null)
        throw new ArgumentNullException(nameof(builder));
      builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(T), ContentTypes));
    }
  }

  /// <summary>
  /// Results of minimal API endpoints in MsgPack, when the client asks for it (see <see cref="MsgPackResult{T}"/>).
  /// </summary>
  public static class MsgPackResults
  {
    /// <summary>
    /// Writes <paramref name="value"/> as MsgPack when the Accept header asks for it, as JSON otherwise (as <c>Ok(value)</c> of a controller with the formatters).
    /// </summary>
    /// <typeparam name="T">The declared type (see <see cref="MsgPackResult{T}"/>)</typeparam>
    /// <param name="statusCode">null: 200</param>
    public static MsgPackResult<T> MsgPack<T>(T value, int? statusCode = null)
    {
      return new MsgPackResult<T>(value, statusCode);
    }
  }

  /// <summary>
  /// <c>Results.Extensions.MsgPack(value)</c>, the same as <see cref="MsgPackResults.MsgPack{T}(T, int?)"/>.
  /// </summary>
  public static class MsgPackResultExtensions
  {
    /// <summary>
    /// Writes <paramref name="value"/> as MsgPack when the Accept header asks for it, as JSON otherwise (as <c>Ok(value)</c> of a controller with the formatters).
    /// </summary>
    /// <typeparam name="T">The declared type (see <see cref="MsgPackResult{T}"/>)</typeparam>
    /// <param name="resultExtensions"><c>Results.Extensions</c></param>
    /// <param name="statusCode">null: 200</param>
    public static MsgPackResult<T> MsgPack<T>(this IResultExtensions resultExtensions, T value, int? statusCode = null)
    {
      if (resultExtensions is null)
        throw new ArgumentNullException(nameof(resultExtensions));
      return new MsgPackResult<T>(value, statusCode);
    }
  }
}
#endif
