namespace LsMsgPack
{
  /// <summary>
  /// The data refers to a cached schema that is not in the <see cref="MsgPackSettings.SchemaStore"/> (and the <see cref="SchemaStore.SchemaProvider"/> did not provide it).
  /// <para>Get the schema from the writer (e.g. by its <see cref="SchemaId"/>), add it with <see cref="SchemaStore.Register"/> and read the data again.</para>
  /// </summary>
  public class MissingSchemaException : MsgPackException
  {
    public MissingSchemaException(SchemaId schemaId)
      : base($"The data refers to the cached schema {schemaId}, which is not in the {nameof(SchemaStore)}. Register the schema of the writer ({nameof(SchemaStore)}.{nameof(SchemaStore.Register)}) or set {nameof(SchemaStore)}.{nameof(SchemaStore.SchemaProvider)}.")
    {
      SchemaId = schemaId;
    }

    public SchemaId SchemaId { get; }
  }
}
