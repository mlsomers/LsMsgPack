using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace LsMsgPackUnitySmoke
{
  /// <summary>
  /// The checks in a WebGL or Android player, which get no command line or files: fetches golden.txt from run.sh's server (serve.py) and posts the report to its /result.
  /// WebGL: the page's own server; Android: port <see cref="AndroidPort"/> on the phone, which run.sh forwards to the server with adb reverse.
  /// </summary>
  public class WebRunner : MonoBehaviour
  {
    public const int AndroidPort = 8765;

    private IEnumerator Start()
    {
      string golden = null;
      string error = null;
      using (UnityWebRequest get = UnityWebRequest.Get(Url("golden.txt")))
      {
        yield return get.SendWebRequest();
        if (get.result == UnityWebRequest.Result.Success)
          golden = get.downloadHandler.text;
        else
          error = "FAILED: golden.txt: " + get.error + "\n";
      }
      string report;
      int failures = UnityRunner.Run(golden == null ? null : new System.Func<string>(() => golden), out report);
      if (error != null)
        report = error + report;
      using (UnityWebRequest post = new UnityWebRequest(Url("result?failures=" + failures), "POST"))
      {
        post.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(report));
        post.downloadHandler = new DownloadHandlerBuffer();
        yield return post.SendWebRequest();
        if (post.result != UnityWebRequest.Result.Success)
          Debug.Log("[smoke] posting the report failed: " + post.error);
      }
#if UNITY_ANDROID
      Application.Quit(failures == 0 ? 0 : 1);
#endif
    }

    private static string Url(string path)
    {
#if UNITY_ANDROID
      return $"http://127.0.0.1:{AndroidPort}/{path}";
#else
      return new System.Uri(new System.Uri(Application.absoluteURL), path).ToString();
#endif
    }
  }
}
