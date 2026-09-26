using LsMsgPack;
using System.Linq;
using System.Web.Mvc;

namespace LsMsgPackMvc
{
  /// <summary>
  /// Registers MsgPack model binding for ASP.NET MVC 5, call <see cref="Register(MsgPackSettings)"/> from Application_Start.
  /// </summary>
  public static class LsMsgPackMvc
  {
    private static MsgPackSettings _settings = new MsgPackSettings();

    /// <summary>
    /// The settings used for application/x-lsmsgpack by the model binder and by <see cref="LsMsgPackResult"/> (unless the result has its own settings).
    /// </summary>
    public static MsgPackSettings Settings => _settings;

    /// <summary>
    /// Binds complex action parameters from the request body when the Content-Type is application/msgpack, application/x-msgpack or application/x-lsmsgpack.
    /// Other requests are bound as usual.
    /// </summary>
    /// <param name="settings">Used for application/x-lsmsgpack, a copy is taken so later changes have no effect.</param>
    public static void Register(MsgPackSettings settings = null)
    {
      _settings = (settings ?? new MsgPackSettings()).Clone();

      ModelBinderProviderCollection providers = ModelBinderProviders.BinderProviders;
      foreach (LsMsgPackModelBinderProvider existing in providers.OfType<LsMsgPackModelBinderProvider>().ToList())
        providers.Remove(existing);
      providers.Insert(0, new LsMsgPackModelBinderProvider(_settings));
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
