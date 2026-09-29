using LsMsgPack;
using LsMsgPackMvc;
using LtMsgPack.Http;
using System.Linq;
using NUnit.Framework;
using System.Web.Mvc;

namespace LsMsgPackMvcTests
{
  [TestFixture]
  public class ResultTests
  {
    private static readonly Order SampleOrder = new Order { Id = 42, Customer = "Infotopie", Quantity = 3, Amounts = new[] { 1.5, 2.25 } };
    private static readonly MsgPackSettings Plain = new MsgPackSettings { UseInexedSchema = false, AddTypeIdOptions = AddTypeIdOption.Never };

    private static FakeHttpContext Execute(ActionResult result, params string[] acceptTypes)
    {
      ControllerContext context = Fake.Context(new FakeRequest(null, null, acceptTypes));
      result.ExecuteResult(context);
      return (FakeHttpContext)context.HttpContext;
    }

    /// <summary>
    /// A different name on purpose: as an overload of Execute, Execute(result, "application/x-lsmsgpack") would pick this one (more declared parameters) and take the media type for the schemas.
    /// </summary>
    private static FakeHttpContext ExecuteWithSchemas(ActionResult result, string clientSchemas, params string[] acceptTypes)
    {
      FakeRequest request = new FakeRequest(null, null, acceptTypes);
      if (!(clientSchemas is null))
        request.Headers.Add(LtMsgPackHttpSerializer.SchemasHeader, clientSchemas);
      ControllerContext context = Fake.Context(request);
      result.ExecuteResult(context);
      return (FakeHttpContext)context.HttpContext;
    }

    [TestCase(new string[0], MsgPackMediaTypes.MsgPack, TestName = "Negotiates(no Accept header)")]
    [TestCase(new[] { "text/html", "*/*;q=0.8" }, MsgPackMediaTypes.MsgPack, TestName = "Negotiates(nothing supported)")]
    [TestCase(new[] { MsgPackMediaTypes.XMsgPack }, MsgPackMediaTypes.XMsgPack, TestName = "Negotiates(x-msgpack)")]
    [TestCase(new[] { MsgPackMediaTypes.XLsMsgPack }, MsgPackMediaTypes.XLsMsgPack, TestName = "Negotiates(x-lsmsgpack)")]
    [TestCase(new[] { "application/msgpack;q=0.5", "application/x-lsmsgpack" }, MsgPackMediaTypes.XLsMsgPack, TestName = "Negotiates(highest quality wins)")]
    [TestCase(new[] { "application/x-lsmsgpack;q=0", "application/msgpack" }, MsgPackMediaTypes.MsgPack, TestName = "Negotiates(q=0 is not acceptable)")]
    public void Negotiates(string[] acceptTypes, string expected)
    {
      FakeHttpContext context = Execute(new LsMsgPackResult(SampleOrder), acceptTypes);

      Assert.That(context.ResponseContentType, Is.EqualTo(expected));
    }

    [Test]
    public void PlainMsgPackHasNoSchema()
    {
      FakeHttpContext context = Execute(new LsMsgPackResult(SampleOrder), MsgPackMediaTypes.MsgPack);

      Assert.That(context.ResponseBody, Is.EqualTo(MsgPackSerializer.Serialize(SampleOrder, Plain)));
    }

    [Test]
    public void LsMsgPackUsesTheSettings()
    {
      FakeHttpContext context = Execute(new LsMsgPackResult(SampleOrder), MsgPackMediaTypes.XLsMsgPack);

      Assert.That(context.ResponseBody, Is.EqualTo(MsgPackSerializer.Serialize(SampleOrder, new MsgPackSettings())), "The root should not get a type id when it matches the declared type");
    }

    [Test]
    public void LsMsgPackAddsTypeIdForPolymorphicRoot()
    {
      FakeHttpContext context = Execute(new LsMsgPackResult(new Dog { Name = "Rex", Barks = 3 }) { DeclaredType = typeof(Animal) }, MsgPackMediaTypes.XLsMsgPack);

      Animal animal = MsgPackSerializer.Deserialize<Animal>(context.ResponseBody);
      Assert.That(animal, Is.TypeOf<Dog>());
    }

    [Test]
    public void ExplicitContentTypeWins()
    {
      FakeHttpContext context = Execute(new LsMsgPackResult(SampleOrder) { ContentType = MsgPackMediaTypes.XLsMsgPack }, MsgPackMediaTypes.MsgPack);

      Assert.That(context.ResponseContentType, Is.EqualTo(MsgPackMediaTypes.XLsMsgPack));
      Assert.That(MsgPackSerializer.Deserialize<Order>(context.ResponseBody).Customer, Is.EqualTo(SampleOrder.Customer));
    }

    [Test]
    public void NullIsWritten()
    {
      FakeHttpContext context = Execute(new LsMsgPackResult(null), MsgPackMediaTypes.MsgPack);

      Assert.That(context.ResponseBody, Is.EqualTo(new byte[] { 0xC0 }));
    }
    [Test]
    public void SchemaReferenceWhenTheClientHoldsTheSchema()
    {
      LtMsgPackHttpSerializer serializer = new LtMsgPackHttpSerializer();
      FakeHttpContext first = Execute(new LsMsgPackResult(SampleOrder) { Serializer = serializer }, MsgPackMediaTypes.XLsMsgPack);
      string schemaId = first.ResponseHeaders[LtMsgPackHttpSerializer.SchemaHeader];
      Assert.That(schemaId, Has.Length.EqualTo(32));
      Assert.That(first.ResponseHeaders["Vary"], Is.EqualTo(LtMsgPackHttpSerializer.SchemasHeader));
      Assert.That(first.ResponseBody, Is.EqualTo(MsgPackSerializer.Serialize(SampleOrder)));

      FakeHttpContext second = ExecuteWithSchemas(new LsMsgPackResult(SampleOrder) { Serializer = serializer }, schemaId, MsgPackMediaTypes.XLsMsgPack);
      byte[] reference = second.ResponseBody;
      Assert.That(reference.Take(2), Is.EqualTo(new byte[] { 0xD8, 2 }));
      Assert.That(MsgPackSerializer.Deserialize<Order>(reference, new MsgPackSettings() { SchemaStore = serializer.SchemaStore }).Customer, Is.EqualTo(SampleOrder.Customer));
    }
  }
}
