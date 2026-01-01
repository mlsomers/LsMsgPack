using LsMsgPack;
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
    /// Adds the LsMsgPackInputFormatter and LsMsgPackOutputFormatter
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
    /// <param name="setupAction">Manipulate the settings here.</param>
    /// <returns>same Mvc Builder as input (for dasy-chaining)</returns>
    public static IMvcBuilder AddLsMsgPackSerializerFormatters(this IMvcBuilder builder, Action<MsgPackSettings> setupAction)
    {
      builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<MvcOptions>, LsMsgPackSettingsSetup>());

      return builder;
    }

    /// <summary>
    /// Adds the LsMsgPackInputFormatter and LsMsgPackOutputFormatter using default settings
    /// </summary>
    /// <param name="options">MvcOptions</param>
    /// <param name="settings">MsgPackSettings</param>
    /// <returns>THe same MvcOptions as the input (for dasy-chaining)</returns>
    public static MvcOptions AddLsMsgPackSerializerFormatters(this MvcOptions options)
    {
      MsgPackSettings settings=new MsgPackSettings();
      options.InputFormatters.Add(new LsMsgPackInputFormatter(settings));
      options.OutputFormatters.Add(new LsMsgPackOutputFormatter(settings));

      return options;
    }

    /// <summary>
    /// Adds the LsMsgPackInputFormatter and LsMsgPackOutputFormatter using the specified settings
    /// </summary>
    /// <param name="options">MvcOptions</param>
    /// <param name="settings">MsgPackSettings</param>
    /// <returns>THe same MvcOptions as the input (for dasy-chaining)</returns>
    public static MvcOptions AddLsMsgPackSerializerFormatters(this MvcOptions options, MsgPackSettings settings)
    {
      options.InputFormatters.Add(new LsMsgPackInputFormatter(settings));
      options.OutputFormatters.Add(new LsMsgPackOutputFormatter(settings));

      return options;
    }
  }

  public class LsMsgPackSettingsSetup:IConfigureOptions<MvcOptions>
  {
    private readonly MsgPackSettings _options;

    public LsMsgPackSettingsSetup(IOptions<MsgPackSettings> options)
    {
      _options = options.Value;
    }

    public void Configure(MvcOptions options)
    {
      options.InputFormatters.Add(new LsMsgPackInputFormatter(_options));
      options.OutputFormatters.Add(new LsMsgPackOutputFormatter(_options));
    }
  }
}
