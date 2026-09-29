using LtMsgPack.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System;

namespace LsMsgPackFormatters
{
  public static class LsMsgPackFormatter
  {
    /// <summary>
    /// Adds the LsMsgPackInputFormatter and LsMsgPackOutputFormatter (LtMsgPack with the default <see cref="LtMsgPackHttpOptions"/>)
    /// </summary>
    /// <param name="builder">Mvc Builder</param>
    /// <returns>same Mvc Builder as input (for dasy-chaining)</returns>
    public static IMvcBuilder AddLsMsgPackSerializerFormatters(this IMvcBuilder builder) {

      builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<MvcOptions>, LsMsgPackSettingsSetup>());

      return builder;

    }

    /// <summary>
    /// Adds the LsMsgPackInputFormatter and LsMsgPackOutputFormatter
    /// </summary>
    /// <param name="builder">Mvc Builder</param>
    /// <param name="setupAction">Manipulate the settings here, e.g. <c>o =&gt; o.Plain = LtMsgPackPresets.MessagePackCSharp()</c>.</param>
    /// <returns>same Mvc Builder as input (for dasy-chaining)</returns>
    public static IMvcBuilder AddLsMsgPackSerializerFormatters(this IMvcBuilder builder, Action<LtMsgPackHttpOptions> setupAction)
    {
      builder.Services.Configure(setupAction);
      builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<MvcOptions>, LsMsgPackSettingsSetup>());

      return builder;
    }

    /// <summary>
    /// Adds the LsMsgPackInputFormatter and LsMsgPackOutputFormatter using default settings
    /// </summary>
    /// <param name="options">MvcOptions</param>
    /// <returns>THe same MvcOptions as the input (for dasy-chaining)</returns>
    public static MvcOptions AddLsMsgPackSerializerFormatters(this MvcOptions options)
    {
      return options.AddLsMsgPackSerializerFormatters(new LtMsgPackHttpOptions());
    }

    /// <summary>
    /// Adds the LsMsgPackInputFormatter and LsMsgPackOutputFormatter using the specified settings
    /// </summary>
    /// <param name="options">MvcOptions</param>
    /// <param name="settings">The settings per media type</param>
    /// <returns>THe same MvcOptions as the input (for dasy-chaining)</returns>
    public static MvcOptions AddLsMsgPackSerializerFormatters(this MvcOptions options, LtMsgPackHttpOptions settings)
    {
      LtMsgPackHttpSerializer serializer = new LtMsgPackHttpSerializer(settings ?? new LtMsgPackHttpOptions()); // shared, so both formatters use one schema store
      options.InputFormatters.Add(new LsMsgPackInputFormatter(serializer));
      options.OutputFormatters.Add(new LsMsgPackOutputFormatter(serializer));

      return options;
    }
  }

  public class LsMsgPackSettingsSetup:IConfigureOptions<MvcOptions>
  {
    private readonly LtMsgPackHttpOptions _options;

    public LsMsgPackSettingsSetup(IOptions<LtMsgPackHttpOptions> options)
    {
      _options = options.Value;
    }

    public void Configure(MvcOptions options)
    {
      options.AddLsMsgPackSerializerFormatters(_options);
    }
  }
}
