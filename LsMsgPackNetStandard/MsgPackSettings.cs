using LsMsgPack.Types.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace LsMsgPack
{
  /// <summary>
  /// The settings of LsMsgPack: the format settings (<see cref="MsgPackOptions"/>, shared with other serializers) and the ones of the <see cref="MsgPackItem"/>s (custom extensions, debugging).
  /// </summary>
  public class MsgPackSettings : MsgPackOptions
  {
#if KEEPTRACK

    /// <summary>
    /// Preserve the packaged (MsgPackItem) items in arrays and maps (in order to debug or inspect them in an editor)
    /// </summary>
    [IgnoreDataMember]
    public static bool Default_PreservePackages { get; set; } = false;

    /// <summary>
    /// If there is a breaking error (such as a non-existing MsgPack type) the reader will do a best effort to continue reading the rest of the file (it will search for the next valid MsgPack type in the stream and continue from there) This should never be done in production code, but for debugging it might help (in navigating or spotting multiple issues in one cycle).
    /// </summary>
    [IgnoreDataMember]
    public static bool Default_ContinueProcessingOnBreakingError { get; set; } = false;

#endif

    /// <summary>
    /// Should inherit from BaseCustomExt, BaseCustomExtNonCached or AbstractCustomExt
    /// </summary>
    [IgnoreDataMember]
    public static ICustomExt[] Default_CustomExtentionTypes = new ICustomExt[]
    {
      new MpDecimal((MsgPackSettings)null)
    };

#if KEEPTRACK
    internal bool _preservePackages = Default_PreservePackages;
    internal bool _continueProcessingOnBreakingError = Default_ContinueProcessingOnBreakingError;
    internal long _truncateValuesLargerThan = long.MaxValue;
    private readonly List<SkippedRange> _skippedRanges = new List<SkippedRange>();
#endif

    internal ICustomExt[] _customExtentionTypes = Default_CustomExtentionTypes;

    internal bool _readStreams;

    /// <summary>
    /// Read what the stream writers of LtMsgPack and the TypeScript client write (docs/Streaming.md): arrays, maps, str and bin of unknown length (the escape sequences of 0xC1 proposed in msgpack/msgpack#347),
    /// and at the root the index of a stream (<see cref="MpStreamIndex"/>), its tail (<see cref="MpStreamTail"/>) and sidecars (<see cref="MpStreamSidecar"/>). Off by default: 0xC1 is then an error, as the specification says.
    /// <para>The explorers turn it on. Writing is unchanged (lengths are always known).</para>
    /// </summary>
#if KEEPTRACK
    [Category("Control")]
    [DisplayName("Read Streams")]
    [Description("Read arrays, maps, str and bin of unknown length (0xC1 escape sequences, msgpack/msgpack#347) and the index of a stream, as LtMsgPack's MsgPackStreamWriter writes them.")]
    [DefaultValue(false)]
#endif
    public bool ReadStreams
    {
      get { return _readStreams; }
      set { _readStreams = value; }
    }

#if KEEPTRACK

    /// <summary>
    /// Preserve the packaged (MsgPackItem) items in arrays and maps (in order to debug or inspect them in an editor)
    /// </summary>
    [Category("Control")]
    [DisplayName("Preserve Packages")]
    [Description("Preserve the packaged (MsgPackItem) items in arrays and maps (in order to debug or inspect them in an editor)")]
    [DefaultValue(false)]
    public bool PreservePackages
    {
      get { return _preservePackages; }
      set { _preservePackages = value; }
    }

    /// <summary>
    /// If there is a breaking error (such as a non-existing MsgPack type) the reader will do a best effort to continue reading the rest of the file (it will search for the next valid MsgPack type in the stream and continue from there) This should never be done in production code, but for debugging it might help (in navigating or spotting multiple issues in one cycle).
    /// </summary>
    [Category("Control")]
    [DisplayName("Continue Processing On Breaking Error")]
    [Description("If there is a breaking error (such as a non-existing MsgPack type) the reader will do a best effort to continue reading the rest of the file (it will search for the next valid MsgPack type in the stream and continue from there) This should never be done in production code, but for debugging it might help (in navigating or spotting multiple issues in one cycle).")]
    [DefaultValue(false)]
    public bool ContinueProcessingOnBreakingError
    {
      get { return _continueProcessingOnBreakingError; }
      set { _continueProcessingOnBreakingError = value; }
    }

    /// <summary>
    /// What the explorers use for <see cref="TruncateValuesLargerThan"/>.
    /// </summary>
    public const long PracticalDebuggingLimit = 16 * 1024 * 1024;

    /// <summary>
    /// A str, bin or ext with a longer content in bytes only keeps its first and last 100 characters or bytes, the rest is passed without reading it into memory (<see cref="MsgPackVarLen.IsTruncated"/>, <see cref="SkippedRanges"/>).
    /// This is for looking at the data, not for reading objects from it. Off (<see cref="long.MaxValue"/>) by default: a value that does not fit a .NET array is then an error.
    /// </summary>
    [Category("Control")]
    [DisplayName("Truncate Values Larger Than")]
    [Description("A str, bin or ext with a longer content in bytes only keeps its first and last 100 characters or bytes, the rest is not read into memory.")]
    [DefaultValue(long.MaxValue)]
    public long TruncateValuesLargerThan
    {
      get { return _truncateValuesLargerThan; }
      set { _truncateValuesLargerThan = value; }
    }

    /// <summary>
    /// The bytes of the truncated values that were read with these settings and not kept, for <see cref="TrimmedData"/>.
    /// </summary>
    [Browsable(false)]
    public List<SkippedRange> SkippedRanges
    {
      get { return _skippedRanges; }
    }

#endif

    /// <summary>
    /// Should inherit from BaseCustomExt, BaseCustomExtNonCached or AbstractCustomExt
    /// </summary>
    public ICustomExt[] CustomExtentionTypes { get { return _customExtentionTypes; } set { _customExtentionTypes = value; } }

    /// <summary>
    /// A copy of the settings (the session caches of the indexed schema are not copied).
    /// </summary>
    public MsgPackSettings Clone()
    {
      return (MsgPackSettings)CloneOptions();
    }

    internal override bool HasCustomExtension(Type type)
    {
      ICustomExt[] extensions = _customExtentionTypes;
      for (int t = 0; t < extensions.Length; t++)
        if (extensions[t] != null && extensions[t].SupportsType(type))
          return true;
      return false;
    }

    internal override byte[] SerializePlainValue(object value, Type type, EndianAction endianAction)
    {
      MsgPackSettings plain = Clone();
      plain._useInexedSchema = false;
      plain._writeSchemaReference = false;
      plain._endianAction = endianAction;
      plain._differences = null;
      return MsgPackSerializer.Serialize(value, type, plain);
    }

    [ThreadStatic]
    private static Buffers _threadBuffers;

    /// <summary>
    /// Buffers used during (de)serialization so they do not need to be allocated for each instance.
    /// <para>One set per thread, since settings are often shared between threads.</para>
    /// </summary>
    [IgnoreDataMember]
    internal Buffers Buffers
    {
      get { return _threadBuffers ?? (_threadBuffers = new Buffers()); }
    }
  }

  /// <summary>
  /// Reusable buffers, reduce some allocations
  /// </summary>
  public class Buffers
  {
    private List<byte> _BytesList = new List<byte>(200);
    public List<byte> BytesList { 
      get {
        _BytesList.Clear(); 
        return _BytesList; 
      }
    }

    public byte[] Bytes2 = new byte[2];
    public byte[] Bytes4 = new byte[4];
    public byte[] Bytes8 = new byte[8];
  }
}
