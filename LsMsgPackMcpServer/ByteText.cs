using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LsMsgPackMcp
{
  /// <summary>
  /// Bytes written as text, as an agent passes them: what MsgPackExplorer accepts from the clipboard and the VS Code extension's bytesFromText.ts
  /// (hex, base64, delimited hex or decimal values, Python bytes literals).
  /// </summary>
  public static class ByteText
  {
    public const string SupportedFormats =
      "Supported formats: hex (0x1a4f... or 1A 4F, 0x1a,0x4f), base64 (also base64url), delimited decimal values (26, 79 or a copied array [26, 79]) and Python bytes literals (b'\\x1aO').";

    private static readonly char[] Separators = new char[] { '\r', '\n', ';', '\t', ' ', ',', '.', '-', '|', '[', ']', '{', '}', '(', ')' };

    /// <exception cref="FormatException">The text does not look like any of the formats</exception>
    public static byte[] Parse(string text)
    {
      string trimmed = (text ?? string.Empty).Trim();
      Match python = Regex.Match(trimmed, "^[bB](['\"])([\\s\\S]*)\\1$");
      if (python.Success)
        return FromPythonLiteral(python.Groups[2].Value);

      bool allHex = true;
      bool allNumeric = true;
      bool all2chars = true;
      bool hexPrefix = false;
      List<string> parts = new List<string>();
      foreach (string raw in trimmed.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
      {
        string part = raw;
        if (part.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
          part = part.Substring(2);
          hexPrefix = true;
        }
        if (part.Length == 0)
          continue;
        foreach (char ch in part)
        {
          if (ch >= '0' && ch <= '9')
            continue;
          allNumeric = false;
          if (!(ch >= 'A' && ch <= 'F' || ch >= 'a' && ch <= 'f'))
            allHex = false;
        }
        all2chars = all2chars && part.Length == 2;
        parts.Add(part);
      }

      if (parts.Count == 0)
        return new byte[0];

      if (parts.Count == 1) // one long string: hex or base64
        return allHex ? FromHex(parts[0].Length % 2 == 0 ? parts[0] : parts[0] + "0") : FromBase64(parts[0]);

      if (all2chars && allHex)
        return FromHex(string.Concat(parts));

      // Hex in groups of any even length (91 c4 30 000102...): a decimal byte has at most 3 digits
      if (allHex && !hexPrefix && parts.TrueForAll(p => p.Length % 2 == 0) && (!allNumeric || parts.Exists(p => p.Length > 3)))
        return FromHex(string.Concat(parts));

      if (hexPrefix && allHex) // delimited hex values not all written with two digits (0x92, 0x1): hex, not decimal
      {
        byte[] bytes = new byte[parts.Count];
        for (int t = 0; t < parts.Count; t++)
        {
          if (parts[t].Length > 2)
            throw new FormatException(string.Concat("Failure parsing delimited hex values: 0x", parts[t], " is not a byte (0x00..0xff)."));
          bytes[t] = Convert.ToByte(parts[t], 16);
        }
        return bytes;
      }

      if (allNumeric) // csv, or an array copied from a debugger
      {
        byte[] bytes = new byte[parts.Count];
        for (int t = 0; t < parts.Count; t++)
        {
          int value;
          if (!int.TryParse(parts[t], NumberStyles.None, CultureInfo.InvariantCulture, out value) || value > 255)
            throw new FormatException(string.Concat("Failure parsing delimited decimal values: ", parts[t], " is not a byte (0..255)."));
          bytes[t] = (byte)value;
        }
        return bytes;
      }

      // Base64 broken over several lines
      if (Regex.IsMatch(trimmed, "^[A-Za-z0-9+/=\\s]+$") && trimmed.IndexOfAny(new char[] { '\r', '\n' }) >= 0)
        return FromBase64(Regex.Replace(trimmed, "\\s+", string.Empty));

      throw new FormatException(string.Concat("The text does not seem to hold bytes. ", SupportedFormats));
    }

    public static byte[] FromHex(string hex)
    {
      if (hex.Length % 2 != 0 || !Regex.IsMatch(hex, "^[0-9a-fA-F]*$"))
        throw new FormatException("Failure parsing hex string: not an even number of hex digits.");
      return Convert.FromHexString(hex);
    }

    public static byte[] FromBase64(string text)
    {
      // Base64url too (- and _), padding is optional
      string normalized = text.Replace('-', '+').Replace('_', '/');
      string unpadded = normalized.TrimEnd('=');
      if (!Regex.IsMatch(normalized, "^[A-Za-z0-9+/]*={0,2}$") || unpadded.Length % 4 == 1)
        throw new FormatException(string.Concat("Failure parsing base64 encoded string. ", SupportedFormats));
      return Convert.FromBase64String(unpadded.PadRight(unpadded.Length + (4 - unpadded.Length % 4) % 4, '='));
    }

    /// <summary>
    /// The content of b'...': printable characters as they are, \xNN, octal and the usual escapes.
    /// </summary>
    private static byte[] FromPythonLiteral(string content)
    {
      List<byte> bytes = new List<byte>(content.Length);
      for (int t = 0; t < content.Length; t++)
      {
        char ch = content[t];
        if (ch != '\\')
        {
          if (ch > 255)
            throw new FormatException("A bytes literal holds only ASCII characters and escapes.");
          bytes.Add((byte)ch);
          continue;
        }
        if (t + 1 >= content.Length)
        {
          bytes.Add((byte)'\\');
          break;
        }
        char next = content[++t];
        switch (next)
        {
          case 'x':
            if (t + 2 >= content.Length || !Uri.IsHexDigit(content[t + 1]) || !Uri.IsHexDigit(content[t + 2]))
              throw new FormatException("Invalid \\x escape in bytes literal.");
            bytes.Add(Convert.ToByte(content.Substring(t + 1, 2), 16));
            t += 2;
            break;
          case 'n': bytes.Add(10); break;
          case 'r': bytes.Add(13); break;
          case 't': bytes.Add(9); break;
          case 'a': bytes.Add(7); break;
          case 'b': bytes.Add(8); break;
          case 'f': bytes.Add(12); break;
          case 'v': bytes.Add(11); break;
          case '\\': bytes.Add(92); break;
          case '\'': bytes.Add(39); break;
          case '"': bytes.Add(34); break;
          default:
            if (next >= '0' && next <= '7')
            {
              int length = 1;
              while (length < 3 && t + length < content.Length && content[t + length] >= '0' && content[t + length] <= '7')
                length++;
              bytes.Add((byte)(Convert.ToInt32(content.Substring(t, length), 8) & 0xff));
              t += length - 1;
            }
            else
            {
              bytes.Add(92); // an unknown escape keeps the backslash
              bytes.Add((byte)next);
            }
            break;
        }
      }
      return bytes.ToArray();
    }
  }
}
