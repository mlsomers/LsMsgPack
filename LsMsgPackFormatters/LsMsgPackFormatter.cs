using LtMsgPack;
using LtMsgPack.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics.CodeAnalysis;

namespace LsMsgPackFormatters
{
  /// <summary>
  /// Registers the formatters. Trimmed and NativeAOT apps: use the overloads taking the context of LtMsgPack's source generator (see <see cref="LtMsgPackSerializer(LtMsgPackContext, LtMsgPackOptions)"/>).
  /// </summary>
  public static partial class LsMsgPackFormatter
  {
    internal const string RequiresUnreferencedCodeMessage = "Without generated code the serializer reads the properties and constructors of the types by reflection, trimming may remove them. Use the overload taking the context of LtMsgPack's source generator.";
    internal const string RequiresDynamicCodeMessage = "Without generated code the serializer instantiates its generic code for the types at runtime, NativeAOT may not have it. Use the overload taking the context of LtMsgPack's source generator.";

    /// <summary>
    /// Adds the LsMsgPackInputFormatter and LsMsgPackOutputFormatter (LtMsgPack with the default <see cref="LtMsgPackHttpOptions"/>)
    /// </summary>
    /// <param name="builder">Mvc Builder</param>
    /// <returns>same Mvc Builder as input (for dasy-chaining)</returns>
#if NET8_0_OR_GREATER
    [RequiresUnreferencedCode(LsMsgPackFormatter.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LsMsgPackFormatter.RequiresDynamicCodeMessage)]
#endif
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
#if NET8_0_OR_GREATER
    [RequiresUnreferencedCode(LsMsgPackFormatter.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LsMsgPackFormatter.RequiresDynamicCodeMessage)]
#endif
    public static IMvcBuilder AddLsMsgPackSerializerFormatters(this IMvcBuilder builder, Action<LtMsgPackHttpOptions> setupAction)
    {
      builder.Services.Configure(setupAction);
      builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<MvcOptions>, LsMsgPackSettingsSetup>());

      return builder;
    }

    /// <summary>
    /// Adds the LsMsgPackInputFormatter and LsMsgPackOutputFormatter with the code of LtMsgPack's source generator (for trimmed and NativeAOT apps, see <see cref="LtMsgPackSerializer(LtMsgPackContext, LtMsgPackOptions)"/>).
    /// </summary>
    /// <param name="builder">Mvc Builder</param>
    /// <param name="context">E.g. <c>AppMsgPackContext.Default</c></param>
    /// <param name="setupAction">Manipulate the settings here (null: the defaults), e.g. <c>o =&gt; o.Plain = LtMsgPackPresets.MessagePackCSharp()</c>.</param>
    /// <returns>same Mvc Builder as input (for dasy-chaining)</returns>
    public static IMvcBuilder AddLsMsgPackSerializerFormatters(this IMvcBuilder builder, LtMsgPackContext context, Action<LtMsgPackHttpOptions> setupAction = null)
    {
      if (context is null)
        throw new ArgumentNullException(nameof(context));
      if (setupAction != null)
        builder.Services.Configure(setupAction);
      builder.Services.AddTransient<IConfigureOptions<MvcOptions>>(services => new LsMsgPackSettingsSetup(services.GetRequiredService<IOptions<LtMsgPackHttpOptions>>(), context));

      return builder;
    }

    /// <summary>
    /// Adds the LsMsgPackInputFormatter and LsMsgPackOutputFormatter using default settings
    /// </summary>
    /// <param name="options">MvcOptions</param>
    /// <returns>THe same MvcOptions as the input (for dasy-chaining)</returns>
#if NET8_0_OR_GREATER
    [RequiresUnreferencedCode(LsMsgPackFormatter.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LsMsgPackFormatter.RequiresDynamicCodeMessage)]
#endif
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
#if NET8_0_OR_GREATER
    [RequiresUnreferencedCode(LsMsgPackFormatter.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LsMsgPackFormatter.RequiresDynamicCodeMessage)]
#endif
    public static MvcOptions AddLsMsgPackSerializerFormatters(this MvcOptions options, LtMsgPackHttpOptions settings)
    {
      return options.AddFormatters(new LtMsgPackHttpSerializer(settings ?? new LtMsgPackHttpOptions()));
    }

    /// <summary>
    /// Adds the LsMsgPackInputFormatter and LsMsgPackOutputFormatter with the code of LtMsgPack's source generator (for trimmed and NativeAOT apps, see <see cref="LtMsgPackSerializer(LtMsgPackContext, LtMsgPackOptions)"/>).
    /// </summary>
    /// <param name="options">MvcOptions</param>
    /// <param name="context">E.g. <c>AppMsgPackContext.Default</c></param>
    /// <param name="settings">The settings per media type (null: the defaults)</param>
    /// <returns>THe same MvcOptions as the input (for dasy-chaining)</returns>
    public static MvcOptions AddLsMsgPackSerializerFormatters(this MvcOptions options, LtMsgPackContext context, LtMsgPackHttpOptions settings = null)
    {
      return options.AddFormatters(new LtMsgPackHttpSerializer(context, settings ?? new LtMsgPackHttpOptions()));
    }

    /// <param name="serializer">Shared, so both formatters use one schema store</param>
    private static MvcOptions AddFormatters(this MvcOptions options, LtMsgPackHttpSerializer serializer)
    {
      options.InputFormatters.Add(new LsMsgPackInputFormatter(serializer));
      options.OutputFormatters.Add(new LsMsgPackOutputFormatter(serializer));
      return options;
    }
  }

  public class LsMsgPackSettingsSetup:IConfigureOptions<MvcOptions>
  {
    private readonly LtMsgPackHttpOptions _options;
    private readonly LtMsgPackContext _context;

    public LsMsgPackSettingsSetup(IOptions<LtMsgPackHttpOptions> options)
    {
      _options = options.Value;
    }

    /// <param name="context">The code of LtMsgPack's source generator</param>
    internal LsMsgPackSettingsSetup(IOptions<LtMsgPackHttpOptions> options, LtMsgPackContext context)
    {
      _options = options.Value;
      _context = context;
    }

#if NET8_0_OR_GREATER
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Without a context this setup is only registered by the overloads of AddLsMsgPackSerializerFormatters that require unreferenced code.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Without a context this setup is only registered by the overloads of AddLsMsgPackSerializerFormatters that require dynamic code.")]
#endif
    public void Configure(MvcOptions options)
    {
      if (_context is null)
        options.AddLsMsgPackSerializerFormatters(_options);
      else
        options.AddLsMsgPackSerializerFormatters(_context, _options);
    }
  }
}
