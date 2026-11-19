using LsMsgPack.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace LsMsgPack.SignalRTests
{
  public class ChatHub : Hub
  {
    public ChatMessage Echo(ChatMessage message)
    {
      return message;
    }

    public Shape EchoShape(Shape shape)
    {
      return shape;
    }

    public Task Broadcast(ChatMessage message)
    {
      return Clients.All.SendAsync("Receive", message, 7);
    }

    public async IAsyncEnumerable<ChatMessage> Count(int count, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      for (int i = 0; i < count; i++)
      {
        await Task.Yield();
        yield return ChatMessage.Sample(i);
      }
    }

    public async Task<int> Sum(ChannelReader<Point> points)
    {
      int sum = 0;
      while (await points.WaitToReadAsync())
      {
        while (points.TryRead(out Point point))
          sum += point.X + point.Y;
      }
      return sum;
    }

    /// <summary>
    /// A client result (ASP.NET Core 7 and up): the server invokes the client and waits for its answer.
    /// </summary>
    public async Task<string> AskName()
    {
      ChatMessage answer = await Clients.Caller.InvokeAsync<ChatMessage>("Name", ChatMessage.Sample(5), CancellationToken.None);
      return answer.User + "/" + answer.Text;
    }
  }

  [TestClass]
  public class HubTests
  {
    private enum ServerProtocols
    {
      Ours,
      Microsofts
    }

    private static async Task<IHost> StartServer(ServerProtocols protocols)
    {
      IHost host = new HostBuilder()
        .ConfigureWebHost(web => web
          .UseTestServer()
          .ConfigureServices(services =>
          {
            ISignalRServerBuilder signalR = services.AddSignalR(o => o.MaximumParallelInvocationsPerClient = 2);
            if (protocols == ServerProtocols.Ours)
              signalR.AddLsMsgPackProtocol().AddLsMsgPackAsMessagePackProtocol();
            else
              signalR.AddMessagePackProtocol();
            services.AddRouting();
          })
          .Configure(app =>
          {
            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapHub<ChatHub>("/chat"));
          }))
        .Build();
      await host.StartAsync();
      return host;
    }

    private static HubConnection Connect(IHost host, Action<IHubConnectionBuilder> protocol)
    {
      TestServer server = host.GetTestServer();
      IHubConnectionBuilder builder = new HubConnectionBuilder()
        .WithUrl(new Uri(server.BaseAddress, "chat"), o =>
        {
          o.HttpMessageHandlerFactory = _ => server.CreateHandler();
          o.Transports = HttpTransportType.LongPolling;
        });
      protocol(builder);
      return builder.Build();
    }

    [TestMethod]
    public Task LsMsgPack_Client_LsMsgPack_Server()
    {
      return Talk(ServerProtocols.Ours, b => b.AddLsMsgPackProtocol(), "lsmsgpack");
    }

    [TestMethod]
    public Task LsMsgPackAsMessagePack_Client_LsMsgPack_Server()
    {
      return Talk(ServerProtocols.Ours, b => b.AddLsMsgPackAsMessagePackProtocol(), "messagepack");
    }

    /// <summary>
    /// Microsoft's MessagePack client against "messagepack" of LsMsgPack: the existing clients connect without a change.
    /// </summary>
    [TestMethod]
    public Task MicrosoftClient_LsMsgPackAsMessagePack_Server()
    {
      return Talk(ServerProtocols.Ours, b => b.AddMessagePackProtocol(), "messagepack");
    }

    [TestMethod]
    public Task LsMsgPackAsMessagePack_Client_Microsoft_Server()
    {
      return Talk(ServerProtocols.Microsofts, b => b.AddLsMsgPackAsMessagePackProtocol(), "messagepack");
    }

    /// <summary>
    /// Invocations with results, a broadcast to the client, a stream from the server, a stream to the server and a client result.
    /// </summary>
    private static async Task Talk(ServerProtocols serverProtocols, Action<IHubConnectionBuilder> clientProtocol, string expectedName)
    {
      using IHost host = await StartServer(serverProtocols);
      HubConnection connection = Connect(host, clientProtocol);
      TaskCompletionSource<string> received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
      connection.On<ChatMessage, int>("Receive", (message, number) => received.TrySetResult(Json(message) + " " + number));
      connection.On<ChatMessage, ChatMessage>("Name", question => new ChatMessage() { User = "client", Text = question.Text });
      await connection.StartAsync();
      try
      {
        ChatMessage sample = ChatMessage.Sample(3);
        ChatMessage echoed = await connection.InvokeAsync<ChatMessage>("Echo", sample);
        Assert.AreEqual(Json(sample), Json(echoed));

        await connection.SendAsync("Broadcast", sample);
        Assert.AreEqual(Json(sample) + " 7", await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        List<string> streamed = new List<string>();
        await foreach (ChatMessage item in connection.StreamAsync<ChatMessage>("Count", 3))
          streamed.Add(item.User);
        CollectionAssert.AreEqual(new[] { "user0", "user1", "user2" }, streamed);

        Channel<Point> points = Channel.CreateUnbounded<Point>();
        Task<int> sum = connection.InvokeAsync<int>("Sum", points.Reader);
        for (int i = 1; i <= 4; i++)
          await points.Writer.WriteAsync(new Point() { X = i, Y = i * 10 });
        points.Writer.Complete();
        Assert.AreEqual(110, await sum);

        Assert.AreEqual("client/" + ChatMessage.Sample(5).Text, await connection.InvokeAsync<string>("AskName"));
      }
      finally
      {
        await connection.DisposeAsync();
        await host.StopAsync();
      }
    }

    /// <summary>
    /// An argument of a derived class declared as its base class: the type id is written below the root only (the arguments are written as their runtime type, as Microsoft's protocols),
    /// so the hub method's parameter of the base class reads the base class.
    /// </summary>
    [TestMethod]
    public async Task DerivedArgument_IsReadAsTheDeclaredType()
    {
      using IHost host = await StartServer(ServerProtocols.Ours);
      HubConnection connection = Connect(host, b => b.AddLsMsgPackProtocol());
      await connection.StartAsync();
      try
      {
        Shape shape = await connection.InvokeAsync<Shape>("EchoShape", new Circle() { Name = "c", Radius = 2 });
        Assert.AreEqual("c", shape.Name);
      }
      finally
      {
        await connection.DisposeAsync();
        await host.StopAsync();
      }
    }

    private static string Json(ChatMessage message)
    {
      return JsonSerializer.Serialize(message);
    }
  }
}
