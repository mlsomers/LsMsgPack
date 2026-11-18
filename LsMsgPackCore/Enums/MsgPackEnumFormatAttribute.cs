using System;

namespace LsMsgPack
{
  /// <summary>
  /// How the values of this enum, or of this property or field, are written: by number or by name (docs/Enums.md). Wins over <see cref="MsgPackOptions.EnumFormat"/>;
  /// on a member it is about the member's own value (its type is the enum or a nullable of it, not a collection of them) and wins over the enum's attribute.
  /// <para>The attributes of other serializers that mean the same are recognized too: System.Text.Json's and Json.NET's <c>[JsonConverter]</c> with their string enum converters,
  /// MessagePack-CSharp's <c>[MessagePackFormatter(typeof(EnumAsStringFormatter&lt;T&gt;))]</c>, MsgPack.Cli's <c>[MessagePackEnum]</c> and <c>[MessagePackEnumMember]</c>.</para>
  /// <para>Readers take both forms whatever is written.</para>
  /// </summary>
  [AttributeUsage(AttributeTargets.Enum | AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
  public sealed class MsgPackEnumFormatAttribute : Attribute
  {
    /// <param name="format">Number or name</param>
    public MsgPackEnumFormatAttribute(EnumFormat format)
    {
      Format = format;
    }

    /// <summary>
    /// Number or name.
    /// </summary>
    public EnumFormat Format { get; }
  }
}
