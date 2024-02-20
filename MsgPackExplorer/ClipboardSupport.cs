using System;
using System.Collections.Generic;
using System.Text;

namespace MsgPackExplorer
{
  internal static class ClipboardSupport
  {

    public static byte[] GetBytes(string str)
    {
      bool allHex = true;
      bool allNumeric = true;
      bool all2chars = true;

      List<string> parts = new List<string>(str.Split('\r', '\n', ';', '\t', ' ', ',', '.', '-', '|', '[', ']'));
      for (int t = parts.Count - 1; t >= 0; t--)
      {
        string part = parts[t].Trim();
        if (string.IsNullOrWhiteSpace(part))
        {
          parts.RemoveAt(t);
          continue;
        }
        if (part.StartsWith("0x"))
          part = part.Substring(2);
        if (allHex)
        {
          for (int i = 0; i < part.Length; i++)
          {
            char ch = part[i];
            if (ch > 47 && ch < 58) // 0..9
              continue;
            allNumeric = false;
            if (ch > 64 && ch < 71) // A..F
              continue;
            if (ch > 97 && ch < 103) // a..f
              continue;
            allHex = false;
          }
        }
        if (all2chars)
          all2chars = part.Length == 2;

        parts[t] = part;
      }

      if (parts.Count == 1) // One long string, so either hex or base64
      {
        if (allHex)
        {
          try
          {
            if (!(parts[0].Length % 2 == 0))
              parts[0] = parts[0] + '0';
            return StringToByteArray(parts[0]);
          }
          catch (Exception ex)
          {
            throw new Exception("Faliure parsing hex string:\r\n" + ex.Message, ex);
          }
        }
        try
        {
          return Convert.FromBase64String(parts[0]);
        }
        catch (Exception ex)
        {
          throw new Exception("Faliure parsing base64 encoded string:\r\n" + ex.Message, ex);
        }
      }
      if (all2chars && allHex) // delimited hex 
        try
        {
          return StringToByteArray(string.Concat(parts));
        }
        catch (Exception ex)
        {
          throw new Exception("Faliure parsing delimited hex string:\r\n" + ex.Message, ex);
        }
      if (allNumeric) // csv, or copied an array from a debugger...
      {
        try
        {
          byte[] ret = new byte[parts.Count];
          for (int t = ret.Length - 1; t >= 0; t--)
            ret[t] = byte.Parse(parts[t]);
          return ret;
        }
        catch (Exception ex)
        {
          throw new Exception("Faliure parsing delimited decimal string:\r\n" + ex.Message, ex);
        }
      }
      throw new Exception("The string on the clipboard does not seem to represent a byte array.\r\nSupported formats are:\r\n  - hex strings (0x1a4f...)\r\n  - base64 encoded\r\n  - delimited hexadecimal velues\r\n  - delimited decimal values (between 0 and 255 inclusive each)");
    }

    // adapted from https://stackoverflow.com/a/9995303/659778, changed to loop backwards
    public static byte[] StringToByteArray(string hex)
    {
      int GetHexVal(int val)
      {
        return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
      }

      byte[] arr = new byte[hex.Length >> 1];

      for (int i = arr.Length; i >= 0; i--)
      {
        int idx = i << 1;
        arr[i] = (byte)((GetHexVal(hex[idx]) << 4) | GetHexVal(hex[idx + 1]));
      }

      return arr;
    }
  }
}
