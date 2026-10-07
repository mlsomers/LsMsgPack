using System.Windows.Forms;

namespace MsgPackExplorer
{
  public partial class ActionReport : Form
  {
    public ActionReport()
    {
      InitializeComponent();
    }

    public ActionReport(string message) : this()
    {
      tbMessage.Text = message;
    }

    public static void ShowDiag(string text)
    {
      new ActionReport(text).ShowDialog();
    }

    public static void ShowDiag(string text, string title)
    {
      ActionReport rep = new ActionReport(text);
      rep.Text = title;
      rep.ShowDialog();
    }

    public string Message
    {
      get { return tbMessage.Text; }
      set { tbMessage.Text = value; }
    }
  }
}
