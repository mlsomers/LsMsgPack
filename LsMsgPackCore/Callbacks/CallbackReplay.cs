namespace LsMsgPack.Meta
{
  /// <summary>
  /// The serialization callbacks of one write that may be repeated: a write with a cached schema (<see cref="Schema.SchemaStore"/>, a frozen session) that meets something the session does not have
  /// is done again with a grown copy (SchemaStore.RunWriter, LsMsgPack's Serializer.Serialize and SerializeBody). The repeat walks the same objects in the same order, so the callbacks are
  /// counted by position: a position whose callback was called by an earlier attempt is skipped, so each object gets OnSerializing and OnSerialized once (the values OnSerializing set are still there).
  /// <para>Without a repeat both counters move together and every callback is called (no reset needed between calls that are never repeated).</para>
  /// </summary>
  internal sealed class CallbackReplay
  {
    private int _serializingReached;
    private int _serializingCalled;
    private int _serializedReached;
    private int _serializedCalled;

    /// <summary>
    /// Before the first attempt of a write that may be repeated.
    /// </summary>
    internal void Start()
    {
      _serializingReached = 0;
      _serializingCalled = 0;
      _serializedReached = 0;
      _serializedCalled = 0;
    }

    /// <summary>
    /// Before each attempt after the first (calling it before the first too is harmless: nothing was reached yet).
    /// </summary>
    internal void Repeat()
    {
      _serializingReached = 0;
      _serializedReached = 0;
    }

    internal void OnSerializing(SerializationCallbacks callbacks, object instance)
    {
      if (_serializingReached++ < _serializingCalled)
        return; // called by an earlier attempt
      _serializingCalled = _serializingReached;
      callbacks.OnSerializing(instance);
    }

    internal void OnSerialized(SerializationCallbacks callbacks, object instance)
    {
      if (_serializedReached++ < _serializedCalled)
        return;
      _serializedCalled = _serializedReached;
      callbacks.OnSerialized(instance);
    }
  }
}
