#if NET8_0_OR_GREATER
using LsMsgPack;
using LtMsgPack.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace LsMsgPackFormatters
{
  /// <summary>
  /// The body of a minimal API request, as a <c>[FromBody]</c> parameter of a controller with the formatters: read with LtMsgPack when the Content-Type is one of <see cref="MsgPackMediaTypes"/>,
  /// as JSON for a JSON Content-Type (the JSON options of minimal APIs, <c>ConfigureHttpJsonOptions</c>). The serializer is the one <see cref="LsMsgPackFormatter.AddLsMsgPack(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{LtMsgPackHttpOptions})"/> registers.
  /// <code>app.MapPost("/orders", (MsgPackBody&lt;Order&gt; order) =&gt; MsgPackResults.MsgPack(Save(order.Value)));</code>
  /// <para>An empty body (or nil, or JSON null) is no value: a <c>MsgPackBody&lt;T&gt;?</c> parameter (or one with a default value) is then null, any other one is answered with 400 by ASP.NET Core.
  /// A body that cannot be read throws a <see cref="BadHttpRequestException"/> with 400, another Content-Type one with 415 (the server answers with that status code).
  /// With <see cref="LtMsgPackHttpOptions.ReportDifferences"/> the differences are in <see cref="ReadDifferencesExtensions.GetReadDifferences"/> and logged, as the input formatter does.</para>
  /// </summary>
  /// <typeparam name="T">The type the body is read into</typeparam>
  public sealed class MsgPackBody<T> : IBindableFromHttpContext<MsgPackBody<T>>, IEndpointParameterMetadataProvider
  {
    /// <summary>
    /// The content types of the body, in the order of MVC's formatters (JSON first).
    /// </summary>
    private static readonly string[] ContentTypes = MsgPackResult<T>.ContentTypes;

    /// <summary>
    /// The logger of the differences (<see cref="LtMsgPackHttpOptions.ReportDifferences"/>).
    /// </summary>
    private const string LoggerCategory = "LsMsgPackFormatters.MsgPackBody";

    public MsgPackBody(T value)
    {
      Value = value;
    }

    /// <summary>
    /// The value read.
    /// </summary>
    public T Value { get; }

    public static implicit operator T(MsgPackBody<T> body)
    {
      return body is null ? default(T) : body.Value;
    }

    /// <summary>
    /// Reads the body of the request (called by ASP.NET Core for a parameter of this type).
    /// </summary>
    /// <returns>null when there is no value</returns>
    /// <exception cref="BadHttpRequestException">The body cannot be read as <typeparamref name="T"/> (400), or has another Content-Type (415)</exception>
    public static async ValueTask<MsgPackBody<T>> BindAsync(HttpContext context, ParameterInfo parameter)
    {
      if (context is null)
        throw new ArgumentNullException(nameof(context));
      HttpRequest request = context.Request;
      if (request.ContentLength == 0 || context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == false)
        return null;

      string contentType = request.ContentType;
      if (MsgPackMediaTypes.IsSupported(contentType))
      {
        LtMsgPackHttpSerializer serializer = LsMsgPackFormatter.MinimalApiSerializer(context);
        return await MsgPackRequest.ReadAsync(context, (context, serializer), static (s, data) => data.Count == 0 ? null : Read(s.context, s.serializer, data));
      }
      if (request.HasJsonContentType())
        return await ReadJsonAsync(context);
      throw new BadHttpRequestException(string.Concat("The request body has the Content-Type '", contentType, "': expected ", string.Join(", ", ContentTypes), "."), StatusCodes.Status415UnsupportedMediaType);
    }

    /// <returns>null for nil</returns>
    private static MsgPackBody<T> Read(HttpContext context, LtMsgPackHttpSerializer serializer, ArraySegment<byte> data)
    {
      object model;
      try
      {
        model = serializer.DeserializeBody(typeof(T), data.Array, data.Offset, data.Count, context.Request.ContentType, out ReadDifferences differences);
        if (differences != null)
          MsgPackRequest.Report(context, serializer, typeof(T), differences, LoggerCategory);
      }
      catch (Exception ex) when (serializer.ReportsDifferences && MsgPackRequest.ReportFailure(context, serializer, typeof(T), ex, LoggerCategory)) // never true: reports the differences found until the exception
      {
        throw;
      }
      catch (MsgPackException ex)
      {
        throw new BadHttpRequestException(ex.Message, StatusCodes.Status400BadRequest, ex);
      }
      catch (Exception ex) when (!(ex is OperationCanceledException))
      {
        // The body is fully buffered, so anything thrown here is caused by the content (truncated data, unexpected types etc.)
        throw new BadHttpRequestException(CannotRead, StatusCodes.Status400BadRequest, ex);
      }

      if (model is null)
        return null;
      if (model is T value)
        return new MsgPackBody<T>(value);
      throw new BadHttpRequestException(CannotRead, StatusCodes.Status400BadRequest); // the deserializer passes through values it cannot convert (eg. a string where a map was expected)
    }

    private static readonly string CannotRead = "The request body could not be deserialized as " + typeof(T).Name + ".";

    private static async ValueTask<MsgPackBody<T>> ReadJsonAsync(HttpContext context)
    {
      JsonTypeInfo<T> info = (JsonTypeInfo<T>)LsMsgPackFormatter.MinimalApiJson(context).GetTypeInfo(typeof(T));
      T value;
      try
      {
        value = await context.Request.ReadFromJsonAsync(info, context.RequestAborted);
      }
      catch (JsonException ex)
      {
        throw new BadHttpRequestException(CannotRead, StatusCodes.Status400BadRequest, ex);
      }
      return value is null ? null : new MsgPackBody<T>(value);
    }

    /// <summary>
    /// The request body of the endpoint (for OpenAPI): optional for a nullable parameter or one with a default value, as ASP.NET Core decides.
    /// </summary>
    public static void PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
    {
      if (builder is null)
        throw new ArgumentNullException(nameof(builder));
      builder.Metadata.Add(new AcceptsMetadata(ContentTypes, typeof(T), IsOptional(parameter)));
    }

    private static bool IsOptional(ParameterInfo parameter)
    {
      if (parameter is null)
        return false;
      if (parameter.HasDefaultValue)
        return true;
      try
      {
        return new NullabilityInfoContext().Create(parameter).ReadState != NullabilityState.NotNull;
      }
      catch (InvalidOperationException) // NullabilityInfoContext is not supported (trimmed apps)
      {
        return false;
      }
    }
  }
}
#endif
