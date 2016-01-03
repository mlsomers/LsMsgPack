using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace LsMsgPack {
  [Serializable]
  public class MpError: MsgPackItem {

    internal MpError() : base() { }
    internal MpError(MsgPackSettings settings):base(settings) {
      settings.FileContainsErrors = true;
    }

    internal MpError(MsgPackSettings settings, Exception ex):this(settings) {
      value = ex;
    }

    internal MpError(MsgPackSettings settings, MsgPackItem partialItemWithNestedError) : this(settings, partialItemWithNestedError.StoredOffset, partialItemWithNestedError.TypeId,
      string.Concat("A nested item contains an error. ", settings.ContinueProcessingOnBreakingError 
        ? "Inspect the PartialItem to view the part of the message that could be read. Since the option 'ContinueProcessingOnBreakingError' is used, the 'IsBestGuess' property of each subitem will indicate if it was read before or after the error."
        : "Inspect the PartialItem to view the part of the message that could be read.")) {
      PartialItem = partialItemWithNestedError;
    }

    internal MpError(MsgPackSettings settings, params object[] exceptionMessage):this(settings, 0, MsgPackTypeId.NeverUsed, exceptionMessage) { }

    internal MpError(MsgPackSettings settings, long offset, MsgPackTypeId typeId, params object[] exceptionMessage):this(settings) {
      storedOffset = offset;
      value = new MsgPackException(string.Concat(exceptionMessage), offset, typeId);
    }

    [XmlText]
    [Category("Data")]
    [DisplayName("Partial Data")]
    [Description("Part of the data up to the point that it could be read.")]
    public MsgPackItem PartialItem { get; set; }

    public override MsgPackTypeId TypeId {
      get {
        return MsgPackTypeId.NeverUsed;
      }
    }

    Exception value;
    [XmlElement]
    public override object Value {
      get {
        return value;
      }
      set {
        if(value is Exception) {
          this.value = (Exception)value;
        } else {
          this.value = new MsgPackException(value.ToString());
        }
      }
    }

    public override byte[] ToBytes() {
      throw new MsgPackException("An error may be produced when decoding a MsgPack message. It cannot be written to a new package as-is.");
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, Stream data) {
      throw new MsgPackException("An error may be produced when decoding a MsgPack message. It cannot be read directly from a stream.");
    }

    public override string ToString() {
      StringBuilder sb= new StringBuilder(value.Message);
      if(value is MsgPackException) {
        MsgPackException mpEx = (MsgPackException)value;
        if(mpEx.Offset > 0) sb.Append(Environment.NewLine).Append("  Offset = ").Append(mpEx.Offset);
        if(mpEx.TypeId != MsgPackTypeId.NeverUsed) sb.Append(Environment.NewLine).Append("  Type = ").Append(GetOfficialTypeName(mpEx.TypeId));
      }
      if(!ReferenceEquals(value.InnerException,null)) sb.Append(Environment.NewLine).Append("  InnerException = ").Append(value.InnerException.Message);
      return sb.ToString();
    }
  }
}
