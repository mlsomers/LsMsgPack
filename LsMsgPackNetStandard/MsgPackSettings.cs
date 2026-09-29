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
#endif

    internal ICustomExt[] _customExtentionTypes = Default_CustomExtentionTypes;

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
