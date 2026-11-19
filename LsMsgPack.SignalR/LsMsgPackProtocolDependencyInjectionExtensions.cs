using LsMsgPack;
using LsMsgPack.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Extensions.DependencyInjection
{
  /// <summary>
  /// Registers <see cref="LsMsgPackHubProtocol"/> on a SignalR server (<c>services.AddSignalR()</c>) or client (<c>new HubConnectionBuilder()</c>).
  /// In the namespace of <c>AddSignalR()</c> and <c>AddMessagePackProtocol()</c>, so it needs no using.
  /// </summary>
  public static class LsMsgPackProtocolDependencyInjectionExtensions
  {
    /// <summary>
    /// Adds the "lsmsgpack" protocol: LsMsgPack's values, for the TypeScript client <c>@lsmsgpack/client/signalr</c> and .NET clients with this package.
    /// </summary>
    /// <param name="configure">Changes the options (null for the defaults)</param>
    [RequiresUnreferencedCode(LsMsgPackHubProtocol.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LsMsgPackHubProtocol.RequiresDynamicCodeMessage)]
    public static TBuilder AddLsMsgPackProtocol<TBuilder>(this TBuilder builder, Action<LsMsgPackHubProtocolOptions> configure = null) where TBuilder : ISignalRBuilder
    {
      return Add<TBuilder, LsMsgPackHubProtocol>(builder, null, false, configure, (o, c) => new LsMsgPackHubProtocol(o, c));
    }

    /// <summary>
    /// Adds the "lsmsgpack" protocol with the code of LsMsgPack's source generator (trimmed and NativeAOT apps): the parameter and return types of the hub methods must be in <paramref name="context"/>.
    /// </summary>
    /// <param name="context">E.g. <c>AppMsgPackContext.Default</c></param>
    /// <param name="configure">Changes the options (null for the defaults)</param>
    public static TBuilder AddLsMsgPackProtocol<TBuilder>(this TBuilder builder, LsMsgPackContext context, Action<LsMsgPackHubProtocolOptions> configure = null) where TBuilder : ISignalRBuilder
    {
      return Add<TBuilder, LsMsgPackHubProtocol>(builder, NotNull(context), false, configure, (o, c) => new LsMsgPackHubProtocol(o, c));
    }

    /// <summary>
    /// Adds LsMsgPack as the "messagepack" protocol: the bytes of Microsoft's MessagePack protocol (<c>AddMessagePackProtocol()</c>, MessagePack-CSharp's contractless resolver with enums as strings),
    /// so its clients connect without a change. Use it instead of <c>AddMessagePackProtocol()</c> (one protocol per name); it can be added next to <see cref="AddLsMsgPackProtocol{TBuilder}(TBuilder, Action{LsMsgPackHubProtocolOptions})"/>.
    /// </summary>
    /// <param name="configure">Changes the options (null for the defaults; <see cref="LsMsgPackHubProtocolOptions.AsMessagePack"/> is set)</param>
    [RequiresUnreferencedCode(LsMsgPackHubProtocol.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LsMsgPackHubProtocol.RequiresDynamicCodeMessage)]
    public static TBuilder AddLsMsgPackAsMessagePackProtocol<TBuilder>(this TBuilder builder, Action<LsMsgPackHubProtocolOptions> configure = null) where TBuilder : ISignalRBuilder
    {
      return Add<TBuilder, MessagePackNamedProtocol>(builder, null, true, configure, (o, c) => new MessagePackNamedProtocol(o, c));
    }

    /// <summary>
    /// <see cref="AddLsMsgPackAsMessagePackProtocol{TBuilder}(TBuilder, Action{LsMsgPackHubProtocolOptions})"/> with the code of LsMsgPack's source generator (trimmed and NativeAOT apps).
    /// </summary>
    /// <param name="context">E.g. <c>AppMsgPackContext.Default</c></param>
    /// <param name="configure">Changes the options (null for the defaults; <see cref="LsMsgPackHubProtocolOptions.AsMessagePack"/> is set)</param>
    public static TBuilder AddLsMsgPackAsMessagePackProtocol<TBuilder>(this TBuilder builder, LsMsgPackContext context, Action<LsMsgPackHubProtocolOptions> configure = null) where TBuilder : ISignalRBuilder
    {
      return Add<TBuilder, MessagePackNamedProtocol>(builder, NotNull(context), true, configure, (o, c) => new MessagePackNamedProtocol(o, c));
    }

    /// <summary>
    /// One registration per protocol name: the options are named after the protocol (<see cref="LsMsgPackHubProtocol.Name"/>), so both protocols can be added with their own options.
    /// A second call adds its <paramref name="configure"/> (as <c>AddMessagePackProtocol()</c>); the context of the first call is used.
    /// </summary>
    private static TBuilder Add<TBuilder, TProtocol>(TBuilder builder, LsMsgPackContext context, bool asMessagePack, Action<LsMsgPackHubProtocolOptions> configure,
      Func<LsMsgPackHubProtocolOptions, LsMsgPackContext, TProtocol> create)
      where TBuilder : ISignalRBuilder where TProtocol : LsMsgPackHubProtocol
    {
      if (builder == null)
        throw new ArgumentNullException(nameof(builder));
      string name = asMessagePack ? LsMsgPackHubProtocol.MessagePackName : LsMsgPackHubProtocol.LsMsgPackName;
      if (asMessagePack)
        builder.Services.Configure<LsMsgPackHubProtocolOptions>(name, o => o.AsMessagePack = true);
      if (configure != null)
        builder.Services.Configure(name, configure);
      builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHubProtocol, TProtocol>(
        services => create(services.GetRequiredService<IOptionsMonitor<LsMsgPackHubProtocolOptions>>().Get(name), context)));
      return builder;
    }

    private static LsMsgPackContext NotNull(LsMsgPackContext context)
    {
      if (context is null)
        throw new ArgumentNullException(nameof(context));
      return context;
    }

    /// <summary>
    /// The registration of the "messagepack" name: a type of its own, so <c>TryAddEnumerable</c> keeps it next to the "lsmsgpack" one.
    /// </summary>
    private sealed class MessagePackNamedProtocol : LsMsgPackHubProtocol
    {
      internal MessagePackNamedProtocol(LsMsgPackHubProtocolOptions options, LsMsgPackContext context) : base(options, context)
      {
      }
    }
  }
}
