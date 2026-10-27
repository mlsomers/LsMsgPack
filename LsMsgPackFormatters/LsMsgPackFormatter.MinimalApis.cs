#if NET8_0_OR_GREATER
using LtMsgPack;
using LtMsgPack.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace LsMsgPackFormatters
{
  public static partial class LsMsgPackFormatter
  {
    /// <summary>
    /// Registers the serializer of minimal APIs (<see cref="MsgPackResults"/>, <see cref="MsgPackBody{T}"/>): an <see cref="LtMsgPackHttpSerializer"/> singleton with the <see cref="LtMsgPackHttpOptions"/> of the services
    /// (the ones <c>AddLsMsgPackSerializerFormatters</c> configures too), unless one is registered already.
    /// </summary>
    /// <param name="services">The services of the application</param>
    /// <param name="setupAction">Manipulate the settings here (null: the defaults), e.g. <c>o =&gt; o.Plain = LtMsgPackPresets.MessagePackCSharp()</c>.</param>
    /// <returns>The same services (for daisy-chaining)</returns>
    [RequiresUnreferencedCode(LsMsgPackFormatter.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LsMsgPackFormatter.RequiresDynamicCodeMessage)]
    public static IServiceCollection AddLsMsgPack(this IServiceCollection services, Action<LtMsgPackHttpOptions> setupAction = null)
    {
      return AddMinimalApiSerializer(services, null, setupAction);
    }

    /// <summary>
    /// Registers the serializer of minimal APIs (<see cref="MsgPackResults"/>, <see cref="MsgPackBody{T}"/>) with the code of LtMsgPack's source generator (for trimmed and NativeAOT apps, see <see cref="LtMsgPackSerializer(LtMsgPackContext, LtMsgPackOptions)"/>):
    /// an <see cref="LtMsgPackHttpSerializer"/> singleton with the <see cref="LtMsgPackHttpOptions"/> of the services, unless one is registered already.
    /// </summary>
    /// <param name="services">The services of the application</param>
    /// <param name="context">E.g. <c>AppMsgPackContext.Default</c></param>
    /// <param name="setupAction">Manipulate the settings here (null: the defaults).</param>
    /// <returns>The same services (for daisy-chaining)</returns>
    public static IServiceCollection AddLsMsgPack(this IServiceCollection services, LtMsgPackContext context, Action<LtMsgPackHttpOptions> setupAction = null)
    {
      if (context is null)
        throw new ArgumentNullException(nameof(context));
      return AddMinimalApiSerializer(services, context, setupAction);
    }

    /// <param name="context">null for none (the public overload without it requires unreferenced and dynamic code)</param>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Without a context only the overload of AddLsMsgPack that requires unreferenced code passes null.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Without a context only the overload of AddLsMsgPack that requires dynamic code passes null.")]
    private static IServiceCollection AddMinimalApiSerializer(IServiceCollection services, LtMsgPackContext context, Action<LtMsgPackHttpOptions> setupAction)
    {
      if (services is null)
        throw new ArgumentNullException(nameof(services));
      if (setupAction != null)
        services.Configure(setupAction);
      services.TryAddSingleton(provider =>
      {
        LtMsgPackHttpOptions options = provider.GetRequiredService<IOptions<LtMsgPackHttpOptions>>().Value;
        return context is null ? new LtMsgPackHttpSerializer(options) : new LtMsgPackHttpSerializer(context, options);
      });
      return services;
    }

    /// <summary>
    /// The serializer <see cref="AddLsMsgPack(IServiceCollection, Action{LtMsgPackHttpOptions})"/> registers.
    /// </summary>
    internal static LtMsgPackHttpSerializer MinimalApiSerializer(HttpContext context)
    {
      LtMsgPackHttpSerializer serializer = context.RequestServices?.GetService<LtMsgPackHttpSerializer>();
      if (serializer is null)
        throw new InvalidOperationException("No LtMsgPackHttpSerializer is registered: MsgPackResults and MsgPackBody<T> need builder.Services.AddLsMsgPack().");
      return serializer;
    }

    private static JsonSerializerOptions _defaultJson;

    /// <summary>
    /// The JSON options of minimal APIs (<c>ConfigureHttpJsonOptions</c>), for the clients that do not ask for MsgPack.
    /// </summary>
    internal static JsonSerializerOptions MinimalApiJson(HttpContext context)
    {
      JsonSerializerOptions options = context.RequestServices?.GetService<IOptions<HttpJsonOptions>>()?.Value.SerializerOptions;
      return options ?? (_defaultJson ?? (_defaultJson = new HttpJsonOptions().SerializerOptions));
    }
  }
}
#endif
