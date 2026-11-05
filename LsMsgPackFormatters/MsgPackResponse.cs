using LsMsgPack;
using LtMsgPack.Http;
using Microsoft.AspNetCore.Http;
using System;
#if NET8_0_OR_GREATER
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using LtMsgPack.Streaming;
#endif
using System.Threading.Tasks;

namespace LsMsgPackFormatters
{
  /// <summary>
  /// Writes a MsgPack response: what the output formatter and the minimal API result (<c>MsgPackResult&lt;T&gt;</c>) share.
  /// </summary>
  internal static class MsgPackResponse
  {
    /// <param name="mediaType">The Content-Type of the response (set by the caller), one of <see cref="MsgPackMediaTypes.All"/></param>
    /// <param name="varyAccept">The caller chose the format by the Accept header: <c>Vary: Accept</c> also without version 2 of the schema</param>
    internal static async Task WriteAsync(HttpContext context, LtMsgPackHttpSerializer serializer, object value, Type declaredType, string mediaType, bool varyAccept)
    {
      bool lsMsgPack = MsgPackMediaTypes.IsLsMsgPack(mediaType);
      bool negotiated = serializer.NegotiatesSchemas && lsMsgPack;
      bool describes = serializer.DescribesSchemas && lsMsgPack;
      HttpRequest request = context.Request;
      string clientSchemas = negotiated ? request.Headers[LtMsgPackHttpSerializer.SchemasHeader].ToString() : null;
      bool described = describes && MsgPackMediaTypes.AcceptsDescribedSchema(request.Headers["Accept"].ToString());
#if NET8_0_OR_GREATER
      Type element = AsyncElementType(value, declaredType);
      if (!(element is null))
      {
        AsyncBuffer buffer = Buffer(element);
        declaredType = buffer.ListType;
        varyAccept |= serializer.WritesStreams; // streamed or not by the Accept header
        if (!(value is null))
        {
          if (buffer.Stream != null && serializer.WritesStreams && MsgPackMediaTypes.AcceptsStream(request.Headers.Accept.ToString(), mediaType))
          {
            await StreamAsync(context, serializer, value, buffer, mediaType, clientSchemas, described, negotiated);
            return;
          }
          value = await buffer.Read(value, context.RequestAborted);
        }
      }
#endif

      // Serialize to a buffer and write it asynchronously, ASP.NET Core does not allow synchronous writes to the response stream.
      MsgPackPayload payload = serializer.Serialize(value, declaredType, mediaType, clientSchemas, described);

      HttpResponse response = context.Response;
      bool accept = describes || varyAccept; // version 2 of the schema depends on the Accept header
      if (negotiated || accept)
        response.Headers.Append("Vary", Vary(negotiated, accept));
      if (negotiated && !(payload.SchemaId is null))
        response.Headers[LtMsgPackHttpSerializer.SchemaHeader] = payload.SchemaId;
      response.ContentLength = payload.Length;
#if NETCOREAPP3_0_OR_GREATER
      System.IO.Pipelines.PipeWriter writer = response.BodyWriter; // copied into the buffers of the response, one flush
      if (payload.Schema.Count > 0)
        System.Buffers.BuffersExtensions.Write(writer, new ReadOnlySpan<byte>(payload.Schema.Array, payload.Schema.Offset, payload.Schema.Count));
      System.Buffers.BuffersExtensions.Write(writer, new ReadOnlySpan<byte>(payload.Body.Array, payload.Body.Offset, payload.Body.Count));
      await writer.FlushAsync(context.RequestAborted);
#else
      await payload.WriteToAsync(response.Body, context.RequestAborted);
#endif
    }

#if NET8_0_OR_GREATER
    private static readonly MethodInfo BufferMethod = typeof(MsgPackResponse).GetMethod(nameof(BufferAsync), BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly ConcurrentDictionary<Type, AsyncBuffer> Buffers = new ConcurrentDictionary<Type, AsyncBuffer>();

    private static readonly MethodInfo StreamMethod = typeof(MsgPackResponse).GetMethod(nameof(StreamItemsAsync), BindingFlags.NonPublic | BindingFlags.Static);

    private sealed class AsyncBuffer
    {
      internal Type ListType;
      internal Func<object, CancellationToken, Task<object>> Read;

      /// <summary>
      /// Writes the items as they come, null for pairs: a <c>List&lt;KeyValuePair&lt;K, V&gt;&gt;</c> is a map.
      /// </summary>
      internal Func<object, MsgPackStreamWriter, CancellationToken, Task> Stream;
    }

    /// <summary>
    /// Writes an <c>IAsyncEnumerable&lt;T&gt;</c> as a stream (the client asked for it, <see cref="MsgPackMediaTypes.AcceptsStream"/>): the head of <c>List&lt;T&gt;</c> (its schema), then an array of unknown length with the items as they come.
    /// With known lengths this would be the payload of the <c>List&lt;T&gt;</c>. The Content-Type gets <see cref="MsgPackMediaTypes.StreamParameter"/>, the response has no Content-Length.
    /// <para>The head and items go out when the next item is not there yet (after the first one: an exception before it still gets an error response), and every <see cref="MsgPackStreamWriter.FlushThreshold"/> bytes.
    /// An exception after that aborts the response: the array has no end, so the client finds the data cut off.</para>
    /// </summary>
    private static async Task StreamAsync(HttpContext context, LtMsgPackHttpSerializer serializer, object value, AsyncBuffer buffer, string mediaType, string clientSchemas, bool described, bool negotiated)
    {
      MsgPackStreamHead head = serializer.PrepareStreamHead(buffer.ListType, mediaType, clientSchemas, described);
      HttpResponse response = context.Response;
      response.ContentType = string.Concat(mediaType, "; ", MsgPackMediaTypes.StreamParameter);
      response.Headers.Append("Vary", Vary(negotiated, true));
      if (!(head.SchemaId is null))
        response.Headers[LtMsgPackHttpSerializer.SchemaHeader] = head.SchemaId;
      response.ContentLength = null;

      CancellationToken cancellationToken = context.RequestAborted;
      MsgPackStreamWriter writer = head.CreateWriter(response.Body, true); // asynchronous: nothing goes to the response before an async call
      writer.WriteArrayStart();
      await buffer.Stream(value, writer, cancellationToken);
      writer.WriteEnd();
      await writer.DisposeAsync(); // not after an exception: what was buffered is dropped, so an exception before the first item still gets an error response
    }

    private static async Task StreamItemsAsync<T>(object value, MsgPackStreamWriter writer, CancellationToken cancellationToken)
    {
      bool any = false;
      IAsyncEnumerator<T> items = ((IAsyncEnumerable<T>)value).GetAsyncEnumerator(cancellationToken);
      try
      {
        while (true)
        {
          ValueTask<bool> next = items.MoveNextAsync();
          if (any && !next.IsCompleted) // the source makes us wait: send what is there meanwhile
          {
            try
            {
              await writer.FlushAsync(cancellationToken);
            }
            catch
            {
              try
              {
                await next; // the enumerator is disposed after its move
              }
              catch { }
              throw;
            }
          }
          if (!await next)
            break;
          await writer.WriteObjectAsync(items.Current, cancellationToken); // the buffer goes out at FlushThreshold
          any = true;
        }
      }
      finally
      {
        await items.DisposeAsync();
      }
    }

    /// <summary>
    /// The T of an <c>IAsyncEnumerable&lt;T&gt;</c> to write as a <c>List&lt;T&gt;</c> (null: write the value as it is). MVC passes the value of an action returning one
    /// to the formatters as it is (System.Text.Json streams it, MVC's other formatters buffer it), and the serializers do not enumerate it: they wrote the compiler's
    /// iterator class, an object without properties. A MsgPack array has its length in front, so the items are read to the end first.
    /// <para>The declared type <c>IAsyncEnumerable&lt;T&gt;</c> decides, otherwise (<c>Ok(items)</c>, object) the value's class when it is not an <see cref="IEnumerable"/> too:
    /// an EF Core query declared as <c>IQueryable&lt;T&gt;</c> is written as before.</para>
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The interfaces of the value's class: an IAsyncEnumerable<T> implementation is kept, the action or endpoint returns the value as one.")]
    private static Type AsyncElementType(object value, Type declaredType)
    {
      Type element = ElementOf(declaredType);
      if (!(element is null) || value is null || value is IEnumerable)
        return element;
      Type[] interfaces = value.GetType().GetInterfaces();
      for (int i = 0; i < interfaces.Length; i++)
      {
        element = ElementOf(interfaces[i]);
        if (!(element is null))
          return element;
      }
      return null;
    }

    private static Type ElementOf(Type type)
    {
      return !(type is null) && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>) ? type.GetGenericArguments()[0] : null;
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Without dynamic code only items of a value type fail, with the NotSupportedException thrown here: List<T>, BufferAsync<T> and StreamItemsAsync<T> are shared by reference types.")]
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "BufferAsync<T> and StreamItemsAsync<T> ask nothing of T's members (reported by the .NET 10 tools).")]
    private static AsyncBuffer Buffer(Type element)
    {
      if (!RuntimeFeature.IsDynamicCodeSupported && element.IsValueType)
        throw new NotSupportedException(string.Concat("An IAsyncEnumerable<", element.Name, "> cannot be written as MsgPack without dynamic code (NativeAOT): return a List<", element.Name, "> or an array."));
      return Buffers.GetOrAdd(element, e => new AsyncBuffer
      {
        ListType = typeof(List<>).MakeGenericType(e),
        Read = BufferMethod.MakeGenericMethod(e).CreateDelegate<Func<object, CancellationToken, Task<object>>>(),
        Stream = e.IsGenericType && e.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) ? null : StreamMethod.MakeGenericMethod(e).CreateDelegate<Func<object, MsgPackStreamWriter, CancellationToken, Task>>()
      });
    }

    private static async Task<object> BufferAsync<T>(object value, CancellationToken cancellationToken)
    {
      List<T> items = new List<T>();
      await foreach (T item in ((IAsyncEnumerable<T>)value).WithCancellation(cancellationToken))
        items.Add(item);
      return items;
    }
#endif

    private const string VarySchemasAndAccept = LtMsgPackHttpSerializer.SchemasHeader + ", Accept";

    private static string Vary(bool negotiated, bool accept)
    {
      if (!negotiated)
        return "Accept";
      return accept ? VarySchemasAndAccept : LtMsgPackHttpSerializer.SchemasHeader;
    }
  }
}
