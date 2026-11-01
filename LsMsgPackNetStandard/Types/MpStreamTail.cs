using LsMsgPack.Streaming;

#if KEEPTRACK
using System.ComponentModel;
using System.Xml.Serialization;
#endif

namespace LsMsgPack
{
  /// <summary>
  /// The tail of the index of a stream (<see cref="MpStreamIndex"/>), the value right after it: <c>[offset of the index, its length, CRC-32 of the index, "LSIX"]</c> written with fixed types (<see cref="MsgPackStreamIndex.TailLength"/> bytes),
  /// so a reader finds the index from the end of the stream. Read with <see cref="MsgPackSettings.ReadStreams"/>.
  /// </summary>
  public class MpStreamTail : MpArray
  {
    internal MpStreamTail(MpArray array, long indexOffset, long indexLength, uint crc, bool crcMatches) : base(array.Settings)
    {
      CopyFrom(array);
      IndexOffset = indexOffset;
      IndexLength = indexLength;
      Crc = crc;
      CrcMatches = crcMatches;
    }

    /// <summary>
    /// Where the index starts, as the writer counted (the stream may have been moved since, see <see cref="MpStreamIndex.Delta"/>).
    /// </summary>
#if KEEPTRACK
    [XmlIgnore]
    [Category("Stream index")]
    [DisplayName("Index offset")]
    [Description("Where the index starts, as the writer counted (the stream may have been moved since).")]
#endif
    public long IndexOffset { get; private set; }

#if KEEPTRACK
    [XmlIgnore]
    [Category("Stream index")]
    [DisplayName("Index length")]
    [Description("The length of the index in bytes (its whole extension value).")]
#endif
    public long IndexLength { get; private set; }

#if KEEPTRACK
    [XmlIgnore]
    [Category("Stream index")]
    [DisplayName("CRC-32")]
    [Description("The CRC-32 (IEEE) of the index's bytes.")]
#endif
    public uint Crc { get; private set; }

    /// <summary>
    /// The bytes before the tail (<see cref="IndexLength"/> of them) match <see cref="Crc"/> and are the index.
    /// </summary>
#if KEEPTRACK
    [XmlIgnore]
    [Category("Stream index")]
    [DisplayName("CRC-32 matches")]
    [Description("The bytes before the tail match the CRC-32 and are the index (false: the index is damaged).")]
#endif
    public bool CrcMatches { get; private set; }

    /// <summary>
    /// The index this tail belongs to.
    /// </summary>
#if KEEPTRACK
    [XmlIgnore]
    [Browsable(false)]
#endif
    public MpStreamIndex Index { get; internal set; }

    public override string ToString()
    {
      return $"Tail of the stream index at {IndexOffset} ({IndexLength} bytes), CRC-32 0x{Crc:X8} {(CrcMatches ? "matches" : "does not match")}.";
    }
  }
}
