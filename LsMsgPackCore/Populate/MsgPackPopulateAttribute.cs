using System;

namespace LsMsgPack
{
  /// <summary>
  /// Whether reading fills the instance this property or field already holds instead of replacing it (docs/Populate.md): an object is read into it (the members the data lacks keep
  /// their values), a collection is cleared and filled. On a class, struct or interface it applies to the members it declares. Wins over <see cref="MsgPackOptions.Populate"/>;
  /// a member's attribute wins over its class's.
  /// <para>Opts in a property without a setter whose getter is written by hand (<c>public Settings Settings =&gt; _settings;</c>), which the option leaves alone (it may return shared state),
  /// and a settable member; <c>[MsgPackPopulate(false)]</c> opts out (a get-only member is then skipped when read, a settable one gets a new instance).</para>
  /// <para>System.Text.Json's <c>[JsonObjectCreationHandling]</c> and Json.NET's <c>[JsonProperty(ObjectCreationHandling = ...)]</c> are recognized too.</para>
  /// </summary>
  [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
  public sealed class MsgPackPopulateAttribute : Attribute
  {
    /// <param name="populate">True: read into the instance the member holds; false: replace it (or skip a member without a setter)</param>
    public MsgPackPopulateAttribute(bool populate = true)
    {
      Populate = populate;
    }

    /// <summary>
    /// True: read into the instance the member holds; false: replace it (or skip a member without a setter).
    /// </summary>
    public bool Populate { get; }
  }
}
