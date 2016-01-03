using System.ComponentModel;
using System.Xml.Serialization;

namespace LsMsgPack {
  public class MsgPackSettings {

    internal bool FileContainsErrors = false;

    internal bool _dynamicallyCompact = true;
    /// <summary>
    /// When true (default) will dynamically use the smallest possible datatype that the value fits in. When false, will always use the predefined type of integer.
    /// </summary>
    [XmlIgnore]
    [Category("Control")]
    [DisplayName("Dynamically Compact")]
    [Description("When true (default) will dynamically use the smallest possible datatype that the value fits in. When false, will use the predefined type of integer.")]
    [DefaultValue(true)]
    public bool DynamicallyCompact {
      get { return _dynamicallyCompact; }
      set { _dynamicallyCompact = value; }
    }

    internal bool _preservePackages = false;
    [XmlIgnore]
    [Category("Control")]
    [DisplayName("Preserve Packages")]
    [Description("Preserve the packaged (MsgPackItem) items in arrays and maps (in order to debug or inspect them in an editor)")]
    [DefaultValue(true)]
    public bool PreservePackages {
      get { return _preservePackages; }
      set { _preservePackages = value; }
    }

    internal bool _continueProcessingOnBreakingError = false;
    [XmlIgnore]
    [Category("Control")]
    [DisplayName("Continue Processing On Breaking Error")]
    [Description("If there is a breaking error (such as a non-existing MsgPack type) the reader will do a best effort to continue reading the rest of the file (it will search for the next valid MsgPack type in the stream and continue from there) This should never be done in production code, but for debugging it might help (in navigating or spotting multiple issues in one cycle).")]
    [DefaultValue(true)]
    public bool ContinueProcessingOnBreakingError {
      get { return _continueProcessingOnBreakingError; }
      set { _continueProcessingOnBreakingError = value; }
    }
  }
}
