using LsMsgPack;
using LsMsgPack.Http;
using LsMsgPack.Streaming;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
using System.Collections.Generic;
using System.Threading;
#endif

namespace LsMsgPackWebApiFormatters
{
  /// <summary>
  /// A request body that is a stream (docs/Streaming.md), written while it is sent (without a Content-Length: chunked): the callback gets an asynchronous <see cref="MsgPackStreamWriter"/> with the serializer of the media type.
  /// With application/x-lsmsgpack and schema references each schema is inline the first time and referred to after that (<see cref="MsgPackStreamSchemas.PerStream"/>), so the body needs no schema of the server.
  /// The Content-Type has <see cref="MsgPackMediaTypes.StreamParameter"/>. The server reads it with <see cref="LsMsgPackHttpSerializer.CreateStreamReader"/> (the formatters read only bodies of known length).
  /// <para>The callback runs each time the content is sent: once, unless a handler sends the request again.</para>
  /// </summary>
  public sealed class MsgPackStreamContent : HttpContent
  {
    private readonly LsMsgPackHttpSerializer _serializer;
    private readonly Func<MsgPackStreamWriter, Task> _write;
    private readonly string _mediaType;
    private readonly MsgPackStreamIndexOptions _index;

    /// <param name="formatter">Its serializer writes the body</param>
    /// <param name="write">Writes the values. Await <see cref="MsgPackStreamWriter.FlushIfFullAsync"/> (or <see cref="MsgPackStreamWriter.WriteObjectAsync{T}(T, System.Threading.CancellationToken)"/>) after each value or row,
    /// so the buffer goes out at <see cref="MsgPackStreamWriter.FlushThreshold"/> and the callback waits while the server is slow. Afterwards the writer is disposed asynchronously (the index, the rest of the buffer).</param>
    /// <param name="mediaType">application/x-lsmsgpack (default), application/msgpack or application/x-msgpack</param>
    /// <param name="index">Keep an index of the root values and write it at the end (see <see cref="MsgPackStreamIndexOptions"/>), null: none</param>
    public MsgPackStreamContent(LsMsgPackMediaTypeFormatter formatter, Func<MsgPackStreamWriter, Task> write, string mediaType = MsgPackMediaTypes.XLsMsgPack, MsgPackStreamIndexOptions index = null)
    {
      if (formatter is null)
        throw new ArgumentNullException(nameof(formatter));
      if (!MsgPackMediaTypes.IsSupported(mediaType))
        throw new ArgumentException($"Not a MsgPack media type: {mediaType}", nameof(mediaType));
      _serializer = formatter.HttpSerializer;
      _write = write ?? throw new ArgumentNullException(nameof(write));
      _mediaType = mediaType;
      _index = index;
      Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType + "; " + MsgPackMediaTypes.StreamParameter);
    }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
    /// <summary>
    /// The items as they come, as the ASP.NET Core formatters stream an <c>IAsyncEnumerable&lt;T&gt;</c>: a head of <c>List&lt;T&gt;</c> (its schema), then an array of unknown length.
    /// With known lengths this would be the payload of the <c>List&lt;T&gt;</c>. The server reads it with <see cref="MsgPackStreamReader.ReadHeadAsync{TRoot}"/> and <see cref="MsgPackStreamReader.ReadElementsAsync{T}"/>.
    /// </summary>
    /// <param name="formatter">Its serializer writes the body</param>
    /// <param name="mediaType">application/x-lsmsgpack (default), application/msgpack or application/x-msgpack</param>
    /// <param name="cancellationToken">Stops the enumeration of the items</param>
    public static MsgPackStreamContent Create<T>(LsMsgPackMediaTypeFormatter formatter, IAsyncEnumerable<T> items, string mediaType = MsgPackMediaTypes.XLsMsgPack, CancellationToken cancellationToken = default(CancellationToken))
    {
      if (items is null)
        throw new ArgumentNullException(nameof(items));
      return new MsgPackStreamContent(formatter, async writer =>
      {
        writer.WriteHead<List<T>>();
        writer.WriteArrayStart();
        await foreach (T item in items.WithCancellation(cancellationToken).ConfigureAwait(false))
          await writer.WriteObjectAsync(item, cancellationToken).ConfigureAwait(false);
        writer.WriteEnd();
      }, mediaType);
    }
#endif

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
    {
      MsgPackStreamWriter writer = _serializer.CreateStreamWriter(stream, _mediaType, _index, true);
      await _write(writer).ConfigureAwait(false);
      await writer.DisposeAsync().ConfigureAwait(false); // not after an exception: the request is aborted, the server does not get a stream that looks complete
    }

    protected override bool TryComputeLength(out long length)
    {
      length = 0;
      return false;
    }
  }
}
