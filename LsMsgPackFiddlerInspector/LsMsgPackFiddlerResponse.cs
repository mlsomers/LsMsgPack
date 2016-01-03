using Fiddler;

namespace LsMsgPackFiddlerInspector {
  public class LsMsgPackFiddlerResponse: LsMsgPackFiddler, IResponseInspector2 {
    private HTTPResponseHeaders responseHdr;

    public HTTPResponseHeaders headers {
      get { return responseHdr; }
      set { responseHdr = value; }
    }
  }
}
