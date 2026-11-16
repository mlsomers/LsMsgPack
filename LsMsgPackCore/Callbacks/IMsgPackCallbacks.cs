namespace LsMsgPack
{
  /// <summary>
  /// Called by the serializers (LsMsgPackDebug, LsMsgPack and the web formatters) before the members of an object are written (docs/Callbacks.md).
  /// System.Text.Json's <c>IJsonOnSerializing</c> is called the same way when a class implements that one instead.
  /// </summary>
  public interface IMsgPackOnSerializing
  {
    /// <summary>
    /// Called before the members of this object are written: the values it sets here are written.
    /// </summary>
    void OnSerializing();
  }

  /// <summary>
  /// Called by the serializers after the members of an object are written (docs/Callbacks.md).
  /// System.Text.Json's <c>IJsonOnSerialized</c> is called the same way when a class implements that one instead.
  /// </summary>
  public interface IMsgPackOnSerialized
  {
    /// <summary>
    /// Called after the members of this object are written (also those of the objects below it).
    /// </summary>
    void OnSerialized();
  }

  /// <summary>
  /// Called by the serializers when an object is created, before its members are set (docs/Callbacks.md).
  /// System.Text.Json's <c>IJsonOnDeserializing</c> is called the same way when a class implements that one instead.
  /// </summary>
  public interface IMsgPackOnDeserializing
  {
    /// <summary>
    /// Called after the constructor, before the members of the data are set (for a constructor that takes values: right after it, before the other members).
    /// </summary>
    void OnDeserializing();
  }

  /// <summary>
  /// Called by the serializers after the members of an object are set (docs/Callbacks.md): the place to validate the object (see docs/security.md).
  /// An exception thrown here is passed on as it is, whatever <see cref="MsgPackOptions.ReadErrors"/> says.
  /// System.Text.Json's <c>IJsonOnDeserialized</c> is called the same way when a class implements that one instead.
  /// </summary>
  public interface IMsgPackOnDeserialized
  {
    /// <summary>
    /// Called after every member of the data is set (and the objects below it are done), with the extension data kept.
    /// </summary>
    void OnDeserialized();
  }
}
