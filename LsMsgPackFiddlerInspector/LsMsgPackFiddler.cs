using Fiddler;
using MsgPackExplorer;
using System.Drawing;
using System.Windows.Forms;

namespace LsMsgPackFiddlerInspector
{
  public class LsMsgPackFiddler: Inspector2 {

    private bool _readonly = false;
    private readonly LsMsgPackExplorer explorer;
    private readonly FiddlerWrapper wrapper;
    private byte[] orgBody;

    public LsMsgPackFiddler() {
      wrapper = new FiddlerWrapper();
      explorer = wrapper.lsMsgPackExplorer1;
    }

    public override int ScoreForContentType(string sMIMEType) {
      string mimeLower = sMIMEType.ToLowerInvariant();
      if(mimeLower == "application/msgpack" || mimeLower == "application/x-msgpack") return 1000;
      if(mimeLower.Contains("msgpack")) return 500;
      return base.ScoreForContentType(sMIMEType);
    }

    public override void ShowAboutBox() {
      new AboutBox().ShowDialog();
      base.ShowAboutBox();
    }

    public override void AddToTab(TabPage o) {
      Image img = explorer.GetIcon();
      if(!ReferenceEquals(img, null)) {
        FiddlerApplication.UI.imglSessionIcons.Images.Add(explorer.GetIcon());
        o.ImageIndex = FiddlerApplication.UI.imglSessionIcons.Images.Count - 1;
      }
      o.Text = "MsgPack";
      o.ToolTipText = "MsgPack Explorer and Validator";
      o.Controls.Add(wrapper);
      wrapper.Dock = DockStyle.Fill;
    }

    public void Clear() {
      explorer.Clear();
    }

    public override int GetOrder() {
      return 10; // Todo: find out what this is, just return 10 for now
    }
    
    public bool bDirty {
      get {
        return orgBody == explorer.Data;
      }
    }

    public byte[] body {
      get {
        if(_readonly) return orgBody;
        return explorer.Data;
      }
      set {
        orgBody = value;
        explorer.Data = value;
      }
    }

    public bool bReadOnly {
      get { return _readonly; }
      set { _readonly = value; }
    }
    
    
  }
}
