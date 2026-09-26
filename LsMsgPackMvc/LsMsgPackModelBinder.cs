using LsMsgPack;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace LsMsgPackMvc
{
  /// <summary>
  /// Deserializes the request body when the Content-Type is application/msgpack, application/x-msgpack (plain MsgPack) or application/x-lsmsgpack (using the given settings).
  /// Other requests are passed on to the binder MVC would have used otherwise.
  /// <para>Errors are added to the ModelState (check ModelState.IsValid) and the model is validated (DataAnnotations etc.) like the default model binder does.</para>
  /// </summary>
  public class LsMsgPackModelBinder : IModelBinder
  {
    private readonly MsgPackSettings Settings;
    private readonly MsgPackSettings PlainSettings;

    public LsMsgPackModelBinder() : this(new MsgPackSettings()) { }

    /// <param name="settings">Used for application/x-lsmsgpack, a copy is taken so later changes have no effect.</param>
    public LsMsgPackModelBinder(MsgPackSettings settings)
    {
      Settings = (settings ?? new MsgPackSettings()).Clone();
      PlainSettings = MsgPackMediaTypes.ToPlain(Settings);
    }

    public object BindModel(ControllerContext controllerContext, ModelBindingContext bindingContext)
    {
      HttpRequestBase request = controllerContext.HttpContext.Request;
      if (!MsgPackMediaTypes.IsSupported(request.ContentType))
        return FallbackBinder(bindingContext.ModelType).BindModel(controllerContext, bindingContext);

      Stream body = request.InputStream;
      if (body.CanSeek)
      {
        if (body.Length == 0)
          return null;
        body.Position = 0; // Another parameter may have read it already
      }

      // A copy per request, since deserializing may flag errors on the settings (KEEPTRACK builds)
      MsgPackSettings settings = (MsgPackMediaTypes.IsLsMsgPack(request.ContentType) ? Settings : PlainSettings).Clone();

      object model;
      try
      {
        model = MsgPackSerializer.Deserialize(bindingContext.ModelType, body, settings);
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
