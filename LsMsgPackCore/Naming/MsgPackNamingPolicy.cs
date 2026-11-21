using System;
using System.Globalization;
using System.Text;

namespace LsMsgPack
{
  /// <summary>
  /// Converts .NET names into the names that are written, as System.Text.Json's <c>JsonNamingPolicy</c>: for property names (<see cref="MsgPackOptions.PropertyNamingPolicy"/>),
  /// string dictionary keys (<see cref="MsgPackOptions.DictionaryKeyPolicy"/>) and enum names (<see cref="MsgPackOptions.EnumNamingPolicy"/>).
  /// The built-in policies give the same names as System.Text.Json's: <see cref="CamelCase"/> (<c>firstName</c>), <see cref="SnakeCaseLower"/> (<c>first_name</c>),
  /// <see cref="SnakeCaseUpper"/> (<c>FIRST_NAME</c>), <see cref="KebabCaseLower"/> (<c>first-name</c>), <see cref="KebabCaseUpper"/> (<c>FIRST-NAME</c>).
  /// <para>Derive from it for other names. <see cref="ConvertName"/> must give the same result for the same name every time and be safe to call from several threads:
  /// property and enum names are converted once per type and kept.</para>
  /// <para>Names given by attributes (<c>[JsonPropertyName]</c>, <c>[DataMember(Name = ...)]</c>, <c>[EnumMember(Value = ...)]</c>...) are written as they are, not converted.</para>
  /// </summary>
  public abstract class MsgPackNamingPolicy
  {
    /// <summary>
    /// The first word in lower case: <c>FirstName</c> is written as <c>firstName</c>, <c>URLValue</c> as <c>urlValue</c> (System.Text.Json's <c>JsonNamingPolicy.CamelCase</c>).
    /// </summary>
    public static MsgPackNamingPolicy CamelCase { get; } = new CamelCaseNamingPolicy();

    /// <summary>
    /// Words in lower case joined by underscores: <c>FirstName</c> is written as <c>first_name</c> (System.Text.Json's <c>JsonNamingPolicy.SnakeCaseLower</c>).
    /// </summary>
    public static MsgPackNamingPolicy SnakeCaseLower { get; } = new SeparatorNamingPolicy(true, '_', nameof(SnakeCaseLower));

    /// <summary>
    /// Words in upper case joined by underscores: <c>FirstName</c> is written as <c>FIRST_NAME</c> (System.Text.Json's <c>JsonNamingPolicy.SnakeCaseUpper</c>).
    /// </summary>
    public static MsgPackNamingPolicy SnakeCaseUpper { get; } = new SeparatorNamingPolicy(false, '_', nameof(SnakeCaseUpper));

    /// <summary>
    /// Words in lower case joined by hyphens: <c>FirstName</c> is written as <c>first-name</c> (System.Text.Json's <c>JsonNamingPolicy.KebabCaseLower</c>).
    /// </summary>
    public static MsgPackNamingPolicy KebabCaseLower { get; } = new SeparatorNamingPolicy(true, '-', nameof(KebabCaseLower));

    /// <summary>
    /// Words in upper case joined by hyphens: <c>FirstName</c> is written as <c>FIRST-NAME</c> (System.Text.Json's <c>JsonNamingPolicy.KebabCaseUpper</c>).
    /// </summary>
    public static MsgPackNamingPolicy KebabCaseUpper { get; } = new SeparatorNamingPolicy(false, '-', nameof(KebabCaseUpper));

    /// <summary>
    /// The name that is written for the .NET name <paramref name="name"/>.
    /// </summary>
    public abstract string ConvertName(string name);

    /// <summary>
    /// The name of a built-in policy (e.g. "CamelCase"), null for other ones: the source generator and the tools recognize these.
    /// </summary>
    internal virtual string BuiltInName { get { return null; } }

    /// <summary>
    /// The built-in policy of this name ("CamelCase", "SnakeCaseLower"...), null for none.
    /// </summary>
    internal static MsgPackNamingPolicy BuiltIn(string name)
    {
      switch (name)
      {
        case nameof(CamelCase): return CamelCase;
        case nameof(SnakeCaseLower): return SnakeCaseLower;
        case nameof(SnakeCaseUpper): return SnakeCaseUpper;
        case nameof(KebabCaseLower): return KebabCaseLower;
        case nameof(KebabCaseUpper): return KebabCaseUpper;
        default: return null;
      }
    }

    /// <summary>
    /// The name the policy gives, checked: a policy may not return null or an empty name.
    /// </summary>
    internal static string Convert(MsgPackNamingPolicy policy, string name)
    {
      string converted = policy.ConvertName(name);
      if (string.IsNullOrEmpty(converted))
        throw new MsgPackException($"The naming policy {policy.GetType().Name} gave no name for \"{name}\".");
      return converted;
    }

    /// <summary>
    /// System.Text.Json's JsonCamelCaseNamingPolicy: the upper case letters at the start in lower case, except the last one of several when a lower case letter follows it.
    /// </summary>
    private sealed class CamelCaseNamingPolicy : MsgPackNamingPolicy
    {
      internal override string BuiltInName { get { return nameof(CamelCase); } }

      public override string ConvertName(string name)
      {
        if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
          return name;

        char[] chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
          if (i == 1 && !char.IsUpper(chars[i]))
            break;

          bool hasNext = i + 1 < chars.Length;
          if (i > 0 && hasNext && !char.IsUpper(chars[i + 1])) // stop when the next one is already lower case
          {
            if (chars[i + 1] == ' ') // a space after it: this one in lower case too
              chars[i] = char.ToLowerInvariant(chars[i]);
            break;
          }
          chars[i] = char.ToLowerInvariant(chars[i]);
        }
        return new string(chars);
      }
    }

    /// <summary>
    /// System.Text.Json's JsonSeparatorNamingPolicy: words split before an upper case letter that follows a lower case letter or digit, and before the last of several upper case letters
    /// that a lower case letter follows (<c>XMLReader</c> → <c>xml_reader</c>, <c>SHA512Hash</c> → <c>sha512_hash</c>); spaces between words become one separator, other characters are kept
    /// and start a new word without a separator.
    /// </summary>
    private sealed class SeparatorNamingPolicy : MsgPackNamingPolicy
    {
      private readonly bool _lowercase;
      private readonly char _separator;
      private readonly string _builtInName;

      internal SeparatorNamingPolicy(bool lowercase, char separator, string builtInName)
      {
        _lowercase = lowercase;
        _separator = separator;
        _builtInName = builtInName;
      }

      internal override string BuiltInName { get { return _builtInName; } }

      private enum State
      {
        NotStarted,
        UppercaseLetter,
        LowercaseLetterOrDigit,
        SpaceSeparator,
      }

      public override string ConvertName(string name)
      {
        if (name is null)
          throw new ArgumentNullException(nameof(name));

        StringBuilder result = new StringBuilder(name.Length + name.Length / 4);
        State state = State.NotStarted;
        for (int i = 0; i < name.Length; i++)
        {
          char current = name[i];
          UnicodeCategory category = char.GetUnicodeCategory(current);
          switch (category)
          {
            case UnicodeCategory.UppercaseLetter:
              if (state == State.LowercaseLetterOrDigit || state == State.SpaceSeparator)
                result.Append(_separator); // an upper case letter after lower case letters or spaces starts a word
              else if (state == State.UppercaseLetter && i + 1 < name.Length && char.IsLower(name[i + 1]))
                result.Append(_separator); // the last of several upper case letters starts the next word when a lower case letter follows it
              result.Append(_lowercase ? char.ToLowerInvariant(current) : current);
              state = State.UppercaseLetter;
              break;

            case UnicodeCategory.LowercaseLetter:
            case UnicodeCategory.DecimalDigitNumber:
              if (state == State.SpaceSeparator)
                result.Append(_separator); // the spaces before it as one separator
              result.Append(!_lowercase && category == UnicodeCategory.LowercaseLetter ? char.ToUpperInvariant(current) : current);
              state = State.LowercaseLetterOrDigit;
              break;

            case UnicodeCategory.SpaceSeparator:
              if (state != State.NotStarted) // spaces at the start and the end are left out
                state = State.SpaceSeparator;
              break;

            default: // anything else (the separator itself, surrogates) is kept as it is and starts again
              result.Append(current);
              state = State.NotStarted;
              break;
          }
        }
        return result.ToString();
      }
    }
  }
}
