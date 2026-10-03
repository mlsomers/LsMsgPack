using LsMsgPack;
using LsMsgPackMvc;
using NUnit.Framework;
using System.Web.Mvc;

namespace LsMsgPackMvcTests
{
  [TestFixture]
  public class ModelBinderTests
  {
    private static readonly Order SampleOrder = new Order { Id = 42, Customer = "Infotopie", Quantity = 3, Amounts = new[] { 1.5, 2.25 } };
    private static readonly MsgPackSettings Plain = new MsgPackSettings { UseInexedSchema = false, AddTypeIdOptions = AddTypeIdOption.Never, ObjectLayout = ObjectLayout.Map };

    private static (object model, ModelStateDictionary modelState) Bind<T>(byte[] body, string contentType)
    {
      ControllerContext context = Fake.Context(new FakeRequest(body, contentType));
      ModelBindingContext bindingContext = new ModelBindingContext
      {
        ModelName = "order",
        ModelMetadata = ModelMetadataProviders.Current.GetMetadataForType(null, typeof(T)),
        ModelState = new ModelStateDictionary(),
        ValueProvider = new NameValueCollectionValueProvider(new System.Collections.Specialized.NameValueCollection(), null)
      };
      object model = new LsMsgPackModelBinder().BindModel(context, bindingContext);
      return (model, bindingContext.ModelState);
    }

    [TestCase(MsgPackMediaTypes.MsgPack)]
    [TestCase(MsgPackMediaTypes.XMsgPack)]
    [TestCase("application/msgpack; charset=binary")]
    public void BindsPlainMsgPack(string contentType)
    {
      (object model, ModelStateDictionary modelState) = Bind<Order>(MsgPackSerializer.Serialize(SampleOrder, Plain), contentType);

      Assert.That(modelState.IsValid, Is.True);
      Order order = (Order)model;
      Assert.That(order.Id, Is.EqualTo(SampleOrder.Id));
      Assert.That(order.Customer, Is.EqualTo(SampleOrder.Customer));
      Assert.That(order.Amounts, Is.EqualTo(SampleOrder.Amounts));
    }

    [Test]
    public void BindsLsMsgPackWithSchema()
    {
      (object model, ModelStateDictionary modelState) = Bind<Order>(MsgPackSerializer.Serialize(SampleOrder), MsgPackMediaTypes.XLsMsgPack);

      Assert.That(modelState.IsValid, Is.True);
      Assert.That(((Order)model).Customer, Is.EqualTo(SampleOrder.Customer));
    }

    [Test]
    public void BindsPolymorphicLsMsgPack()
    {
      (object model, _) = Bind<Animal>(MsgPackSerializer.Serialize<Animal>(new Dog { Name = "Rex", Barks = 3 }), MsgPackMediaTypes.XLsMsgPack);

      Assert.That(model, Is.TypeOf<Dog>());
      Assert.That(((Dog)model).Barks, Is.EqualTo(3));
    }

    [TestCase(new byte[] { 0xC1 }, TestName = "MalformedInputIsAModelError(never used type)")]
    [TestCase(new byte[] { 0x83, 0xA2, 0x49 }, TestName = "MalformedInputIsAModelError(truncated)")]
    [TestCase(new byte[] { 0xA5, 0x68, 0x65, 0x6C, 0x6C, 0x6F }, TestName = "MalformedInputIsAModelError(string instead of map)")]
    public void MalformedInputIsAModelError(byte[] body)
    {
      (object model, ModelStateDictionary modelState) = Bind<Order>(body, MsgPackMediaTypes.MsgPack);

      Assert.That(model, Is.Null);
      Assert.That(modelState.IsValid, Is.False);
      Assert.That(modelState["order"].Errors[0].Exception, Is.Not.Null);
    }

    [Test]
    public void ModelIsValidated()
    {
      Order invalid = new Order { Id = 1, Customer = null, Quantity = 5000 };
      (object model, ModelStateDictionary modelState) = Bind<Order>(MsgPackSerializer.Serialize(invalid, Plain), MsgPackMediaTypes.MsgPack);

      Assert.That(model, Is.Not.Null);
      Assert.That(modelState.IsValid, Is.False);
      Assert.That(modelState.ContainsKey("order.Customer"), Is.True);
      Assert.That(modelState.ContainsKey("order.Quantity"), Is.True);
    }

    [Test]
    public void EmptyBodyGivesNull()
    {
      (object model, ModelStateDictionary modelState) = Bind<Order>(new byte[0], MsgPackMediaTypes.MsgPack);

      Assert.That(model, Is.Null);
      Assert.That(modelState.IsValid, Is.True);
    }

    [Test]
    public void OtherContentTypesUseTheDefaultBinder()
    {
      (object model, _) = Bind<Order>(new byte[] { 0x68, 0x69 }, "text/plain");

      // The default binder finds nothing in the (empty) value provider
      Assert.That(model, Is.Null);
    }

    [Test]
    public void RegisterAddsTheProviderForComplexTypesOnly()
    {
      LsMsgPackMvc.LsMsgPackMvc.Register();
      LsMsgPackMvc.LsMsgPackMvc.Register(); // Registering twice replaces the provider

      Assert.That(ModelBinderProviders.BinderProviders, Has.Exactly(1).TypeOf<LsMsgPackModelBinderProvider>());
      Assert.That(ModelBinders.Binders.GetBinder(typeof(Order)), Is.TypeOf<LsMsgPackModelBinder>());
      Assert.That(ModelBinders.Binders.GetBinder(typeof(int)), Is.Not.TypeOf<LsMsgPackModelBinder>());
      Assert.That(ModelBinders.Binders.GetBinder(typeof(string)), Is.Not.TypeOf<LsMsgPackModelBinder>());
    }
  }
}
