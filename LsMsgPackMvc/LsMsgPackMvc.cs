using LsMsgPack.Http;
using System.Web.Mvc;

namespace LsMsgPackMvc
{
  /// <summary>
  /// Registers MsgPack model binding for ASP.NET MVC 5, call <see cref="Register(LsMsgPackHttpOptions)"/> from Application_Start (LsMsgPack).
  /// </summary>
  public static class LsMsgPackMvc
  {
    private static LsMsgPackHttpSerializer _serializer = new LsMsgPackHttpSerializer();

    /// <summary>
    /// The serializers used by the model binder and by <see cref="LsMsgPackResult"/> (unless the result has its own).
    /// </summary>
    public static LsMsgPackHttpSerializer Serializer => _serializer;

    /// <summary>
    /// Binds complex action parameters from the request body when the Content-Type is application/msgpack, application/x-msgpack or application/x-lsmsgpack.
    /// Other requests are bound as usual.
    /// </summary>
    /// <param name="options">The settings per media type (e.g. <c>Plain = LsMsgPackPresets.MessagePackCSharp()</c>), read once so later changes have no effect.</param>
    public static void Register(LsMsgPackHttpOptions options = null)
    {
      _serializer = new LsMsgPackHttpSerializer(options ?? new LsMsgPackHttpOptions());

      ModelBinderProviderCollection providers = ModelBinderProviders.BinderProviders;
      for (int t = providers.Count - 1; t >= 0; t--)
        if (providers[t] is LsMsgPackModelBinderProvider)
          providers.RemoveAt(t);
      providers.Insert(0, new LsMsgPackModelBinderProvider(_serializer));
    }

    /// <summary>
    /// Returns the data as MsgPack, the media type is chosen from the request's Accept header (application/msgpack when there is no match).
    /// </summary>
    public static LsMsgPackResult MsgPack(this Controller controller, object data)
    {
      return new LsMsgPackResult(data);
    }
  }
}
