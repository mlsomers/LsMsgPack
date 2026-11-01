using LtMsgPack.Streaming;
using System;
using System.IO;

namespace LtMsgPack.Http
{
  /// <summary>
  /// A response body that is a stream with a head (<see cref="LtMsgPackHttpSerializer.PrepareStreamHead"/>): its schema id for the response headers, then the writer of the body.
  /// </summary>
  public sealed class MsgPackStreamHead
  {
    private readonly LtMsgPackSerializer _serializer;
    private readonly Serializer.WriterHead _head; // null without the indexed schema
    private readonly MsgPackStreamSchemas _schemas;

    internal MsgPackStreamHead(LtMsgPackSerializer serializer, Type rootType, Serializer.WriterHead head, MsgPackStreamSchemas schemas, string schemaId, bool isReference)
    {
      _serializer = serializer;
      RootType = rootType;
      _head = head;
      _schemas = schemas;
      SchemaId = schemaId;
      IsReference = isReference;
    }

    /// <summary>
    /// The declared type of the root value after the head.
    /// </summary>
    public Type RootType { get; }

    /// <summary>
    /// The value of the response header <see cref="LtMsgPackHttpSerializer.SchemaHeader"/>: the id of the head's schema when schemas are negotiated, otherwise null.
    /// </summary>
    public string SchemaId { get; }

    /// <summary>
    /// The head refers to the schema (the client holds it) instead of containing it.
    /// </summary>
    public bool IsReference { get; }

    /// <summary>
    /// Creates the writer of the body and writes the head (nothing without the indexed schema). Then write the root value, whose objects are written without their schema
    /// (<see cref="MsgPackStreamWriter.WriteObject{T}(T)"/> with the declared type of their place in <see cref="RootType"/>). The stream is left open.
    /// <para>Set the response headers first: a synchronous writer may pass the head to the stream at once (<see cref="MsgPackStreamWriter.FlushThreshold"/>).</para>
    /// </summary>
    /// <param name="body">The response body</param>
    /// <param name="asynchronous">The writer writes to the body only in its async methods (ASP.NET Core does not allow synchronous writes to a response)</param>
    public MsgPackStreamWriter CreateWriter(Stream body, bool asynchronous = true)
    {
      MsgPackStreamWriter writer = new MsgPackStreamWriter(body, _serializer, _schemas, true, null, asynchronous);
      writer.WriteHead(RootType, _head);
      return writer;
    }
  }
}
