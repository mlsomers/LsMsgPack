using LtMsgPack.Http;
using System;
using System.ComponentModel;
using System.Web.Mvc;

namespace LsMsgPackMvc
{
  /// <summary>
  /// Provides the <see cref="LsMsgPackModelBinder"/> for complex types when the request Content-Type is a MsgPack media type.
  /// <para>Simple types (those that can be converted from a string, like int and string) are left to the other binders, so they can still come from the route or query string.</para>
  /// </summary>
  public class LsMsgPackModelBinderProvider : IModelBinderProvider
  {
    private readonly LsMsgPackModelBinder binder;

    public LsMsgPackModelBinderProvider(LtMsgPackHttpSerializer serializer)
    {
      binder = new LsMsgPackModelBinder(serializer);
    }

    public IModelBinder GetBinder(Type modelType)
    {
      if (TypeDescriptor.GetConverter(modelType).CanConvertFrom(typeof(string)))
        return null;
      return binder;
    }
  }
}
