using Microsoft.VisualStudio.DebuggerVisualizers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DebuggerProxy
{
  [Serializable]
  public class MsgPackByteArray
  {
    public MsgPackByteArray() { }

    public MsgPackByteArray(byte[] value)
    {
      Value = value;
    }

    public MsgPackByteArray(IEnumerable<byte> value)
    {
      Value = value.ToArray();
    }

    public MsgPackByteArray(Stream value)
    {
      if(!value.CanRead)
        throw new ArgumentException("Stream must be readable!",nameof(value));

      if (value.CanSeek) // Great! we can read the whole stream and return it in it's original state.
      {
        long orgPos = value.Position;
        if (orgPos > 0)
          value.Seek(0, 0);
        Value = new byte[value.Length];
        value.Read(Value, 0, Value.Length);
        if (orgPos > 0)
          value.Seek(orgPos, 0);
      }
      else // we can debug, but we will not be able to continue the rest of the flow after analysing the MsgPack contents since this stream will have been read...
      {
        Value = new byte[value.Length];
        value.Read(Value, 0, Value.Length);
      }
    }

    /// <summary>
    /// According to the docs, it will be Base64 encoded by JSON.NET (https://www.newtonsoft.com/json/help/html/serializationguide.htm)
    /// </summary>
    public byte[] Value { get; set; }

    public static implicit operator MsgPackByteArray(byte[] value)
    {
      return new MsgPackByteArray(value);
    }

    public static implicit operator MsgPackByteArray(Stream value)
    {
      return new MsgPackByteArray(value);
    }

    public static implicit operator MsgPackByteArray(List<byte> value)
    {
      return new MsgPackByteArray(value);
    }

    public static implicit operator MsgPackByteArray(string base64)
    {
      return new MsgPackByteArray(System.Convert.FromBase64String(base64));
    }


    #region Converting back 

    public static implicit operator byte[](MsgPackByteArray d)
    {
      return d.Value;
    }

    public static implicit operator Stream(MsgPackByteArray d)
    {
      return new MemoryStream(d.Value);
    }

    public static implicit operator List<byte>(MsgPackByteArray d)
    {
      return new List<byte>(d.Value);
    }

    public static implicit operator string(MsgPackByteArray d)
    {
      return System.Convert.ToBase64String(d.Value);
    }

    #endregion
  }
}
