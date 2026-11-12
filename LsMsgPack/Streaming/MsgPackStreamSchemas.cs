namespace LsMsgPack.Streaming
{
  /// <summary>
  /// Where the schemas of the objects of a stream go (<see cref="MsgPackStreamWriter.WriteObject{T}(T)"/>), with the indexed schema (<see cref="LsMsgPack.MsgPackOptions.UseInexedSchema"/>).
  /// Without it there is no schema: every mode writes the same.
  /// </summary>
  public enum MsgPackStreamSchemas
  {
    /// <summary>
    /// Every object is written as <see cref="LsMsgPackSerializer.Serialize{T}(T)"/> writes it with the schema inline: the schema, then the object. Each object can be read on its own.
    /// </summary>
    PerObject,

    /// <summary>
    /// A schema is written inline the first time an object of the stream has it, the later objects with that schema refer to it (<see cref="LsMsgPack.SchemaStore.ReferenceLength"/> bytes),
    /// for streams of objects of the same types. When the schema grows (a value needs a class or property it does not have yet), the grown schema is written inline once.
    /// <para>Needs a serializer with <see cref="LsMsgPack.MsgPackOptions.WriteSchemaReference"/> and a <see cref="LsMsgPack.SchemaStore"/>; the reader needs a serializer with a <see cref="LsMsgPack.SchemaStore"/> (it learns the schemas from the stream).</para>
    /// </summary>
    PerStream,

    /// <summary>
    /// The objects only refer to their schemas, the schemas stay in the <see cref="LsMsgPack.SchemaStore"/> of the serializer (e.g. to be stored apart, or appended to the stream later: see <see cref="MsgPackStreamWriter.ReferencedSchemas"/>).
    /// <para>Needs a serializer with <see cref="LsMsgPack.MsgPackOptions.WriteSchemaReference"/> and a <see cref="LsMsgPack.SchemaStore"/>; the reader needs a store that holds the schemas.</para>
    /// </summary>
    External
  }
}
