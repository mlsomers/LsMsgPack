using LsMsgPack;
using LtMsgPack.Http;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace LsMsgPackMvc
{
  /// <summary>
  /// Deserializes the request body when the Content-Type is application/msgpack, application/x-msgpack (plain MsgPack) or application/x-lsmsgpack, with LtMsgPack (see <see cref="LtMsgPackHttpOptions"/>).
  /// Other requests are passed on to the binder MVC would have used otherwise.
  /// <para>Errors are added to the ModelState (check ModelState.IsValid) and the model is validated (DataAnnotations etc.) like the default model binder does.</para>
  /// </summary>
  public class LsMsgPackModelBinder : IModelBinder
  {
    private readonly LtMsgPackHttpSerializer Serializer;

    public LsMsgPackModelBinder() : this(new LtMsgPackHttpSerializer()) { }

    /// <param name="options">Read once, later changes have no effect.</param>
    public LsMsgPackModelBinder(LtMsgPackHttpOptions options) : this(new LtMsgPackHttpSerializer(options ?? new LtMsgPackHttpOptions())) { }

    public LsMsgPackModelBinder(LtMsgPackHttpSerializer serializer)
    {
      Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public object BindModel(ControllerContext controllerContext, ModelBindingContext bindingContext)
    {
      HttpRequestBase request = controllerContext.HttpContext.Request;
      if (!MsgPackMediaTypes.IsSupported(request.ContentType))
        return FallbackBinder(bindingContext.ModelType).BindModel(controllerContext, bindingContext);

      Stream input = request.InputStream;
      if (input.CanSeek)
      {
        if (input.Length == 0)
          return null;
        input.Position = 0; // Another parameter may have read it already
      }
      MemoryStream body = new MemoryStream();
      input.CopyTo(body);

      object model;
      try
      {
        model = Serializer.Deserialize(bindingContext.ModelType, body.GetBuffer(), 0, (int)body.Length, request.ContentType);
        if (!(model is null) && !bindingContext.ModelType.IsInstanceOfType(model)) // The deserializer passes through values it cannot convert (eg. a string where a map was expected)
          throw new MsgPackException("The request body could not be deserialized as " + bindingContext.ModelType.Name + ", it contains a " + model.GetType().Name + ".");
      }
      catch (Exception ex) when (!(ex is HttpException))
      {
        // Anything thrown here is caused by the content (truncated data, unexpected types etc.)
        bindingContext.ModelState.AddModelError(bindingContext.ModelName, ex);
        return null;
      }

      Validate(model, controllerContext, bindingContext);
      return model;
    }

    /// <summary>
    /// The binder MVC would have chosen without us: the other providers, then the registered binders, then a [ModelBinder] attribute on the type and finally the default binder.
    /// </summary>
    private static IModelBinder FallbackBinder(Type modelType)
    {
      foreach (IModelBinderProvider provider in ModelBinderProviders.BinderProviders)
      {
        if (provider is LsMsgPackModelBinderProvider)
          continue;
        IModelBinder binder = provider.GetBinder(modelType);
        if (!(binder is null))
          return binder;
      }

      if (ModelBinders.Binders.TryGetValue(modelType, out IModelBinder registered))
        return registered;

      CustomModelBinderAttribute attribute = TypeDescriptor.GetAttributes(modelType).OfType<CustomModelBinderAttribute>().FirstOrDefault();
      return attribute?.GetBinder() ?? ModelBinders.Binders.DefaultBinder;
    }

    private static void Validate(object model, ControllerContext controllerContext, ModelBindingContext bindingContext)
    {
      if (model is null)
        return;

      ModelMetadata metadata = ModelMetadataProviders.Current.GetMetadataForType(() => model, bindingContext.ModelType);
      foreach (ModelValidationResult result in ModelValidator.GetModelValidator(metadata, controllerContext).Validate(null))
      {
        string key = string.IsNullOrEmpty(result.MemberName) ? bindingContext.ModelName
          : string.IsNullOrEmpty(bindingContext.ModelName) ? result.MemberName
          : bindingContext.ModelName + "." + result.MemberName;
        bindingContext.ModelState.AddModelError(key, result.Message);
      }
    }
  }
}
