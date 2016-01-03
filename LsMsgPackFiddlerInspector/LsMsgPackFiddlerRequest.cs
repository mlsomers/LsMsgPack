using Fiddler;

namespace LsMsgPackFiddlerInspector {
  public class LsMsgPackFiddlerRequest: LsMsgPackFiddler, IRequestInspector2 {
    HTTPRequestHeaders requestHdr;

    public HTTPRequestHeaders headers {
      get { return requestHdr; }
      set { requestHdr = value; }
    }
  }
}
