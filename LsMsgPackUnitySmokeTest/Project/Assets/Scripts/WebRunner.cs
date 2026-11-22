using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace LsMsgPackUnitySmoke
{
  /// <summary>
  /// The checks in a WebGL player, which has no command line or files: fetches golden.txt from the page's server (webgl-serve.py) and posts the report to its /result.
  /// </summary>
  public class WebRunner : MonoBehaviour
  {
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
    }

    private static string Url(string path)
    {
      return new System.Uri(new System.Uri(Application.absoluteURL), path).ToString();
    }
  }
}
