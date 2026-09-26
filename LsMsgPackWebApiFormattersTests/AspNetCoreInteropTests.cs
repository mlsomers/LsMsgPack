using LsMsgPack;
using LsMsgPackFormatters;
using LsMsgPackWebApiFormatters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace LsMsgPackWebApiFormattersTests
{
  [ApiController]
  [Route("core")]
  public class AspNetCoreTestController : ControllerBase
  {
    [HttpPost("echo")]
    public Order Echo(Order order) => order;
  }

  /// <summary>
  /// HttpClient with the Web API formatter talking to ASP.NET Core with the LsMsgPackFormatters, both packages should produce and accept the same format
  /// </summary>
  [TestFixture]
  public class AspNetCoreInteropTests
  {
    private static readonly Order SampleOrder = new Order { Id = 42, Customer = "Infotopie", Amounts = new[] { 1.5, 2.25 } };
    private static readonly LsMsgPackMediaTypeFormatter Formatter = new LsMsgPackMediaTypeFormatter();

    private IHost host;
    private HttpClient client;

    [OneTimeSetUp]
    public async Task StartServer()
    {
      host = await new HostBuilder()
        .ConfigureWebHost(web => web
          .UseTestServer()
          .ConfigureServices(services => services.AddControllers().AddApplicationPart(typeof(AspNetCoreTestController).Assembly).AddLsMsgPackSerializerFormatters())
          .Configure(app => app.UseRouting().UseEndpoints(endpoints => endpoints.MapControllers())))
        .StartAsync();
      client = host.GetTestClient();
    }

    [OneTimeTearDown]
    public void StopServer()
    {
      client.Dispose();
      host.Dispose();
    }

    [TestCase(MsgPackMediaTypes.MsgPack)]
    [TestCase(MsgPackMediaTypes.XMsgPack)]
    [TestCase(MsgPackMediaTypes.XLsMsgPack)]
    public async Task RoundTrips(string mediaType)
    {
      HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "core/echo") { Content = new ObjectContent<Order>(SampleOrder, Formatter, mediaType) };
      request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
      HttpResponseMessage response = await client.SendAsync(request);

      Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
      Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo(mediaType));
      Order order = await response.Content.ReadAsAsync<Order>(new MediaTypeFormatter[] { Formatter });
      Assert.That(order.Id, Is.EqualTo(SampleOrder.Id));
      Assert.That(order.Customer, Is.EqualTo(SampleOrder.Customer));
      Assert.That(order.Amounts, Is.EqualTo(SampleOrder.Amounts));
    }
  }
}
