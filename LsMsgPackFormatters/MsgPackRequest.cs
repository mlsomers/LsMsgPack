using LsMsgPack;
using LtMsgPack.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
#if NETCOREAPP3_0_OR_GREATER
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
#endif

namespace LsMsgPackFormatters
{
  /// <summary>
  /// Reads a MsgPack request body: what the input formatter and the minimal API body (<c>MsgPackBody&lt;T&gt;</c>) share.
  /// </summary>
  internal static class MsgPackRequest
  {
#if NETCOREAPP3_0_OR_GREATER
    /// <summary>
    /// Buffers the body asynchronously in the pipe of the request (the deserializer reads synchronously, which ASP.NET Core does not allow on the request stream)
    /// and passes it to <paramref name="read"/> in one piece: in place when it is in one buffer (LtMsgPack copies what it keeps), otherwise a pooled copy. Empty when the body is.
    /// </summary>
    internal static async Task<TResult> ReadAsync<TState, TResult>(HttpContext context, TState state, Func<TState, ArraySegment<byte>, TResult> read)
    {
      PipeReader reader = context.Request.BodyReader;
      ReadResult result = await reader.ReadAsync(context.RequestAborted);
      while (!result.IsCompleted && !result.IsCanceled)
      {
        reader.AdvanceTo(result.Buffer.Start, result.Buffer.End); // nothing consumed yet, wait for the rest
        result = await reader.ReadAsync(context.RequestAborted);
      }

      ReadOnlySequence<byte> body = result.Buffer;
      byte[] rented = null;
      try
      {
        if (body.Length == 0)
          return read(state, new ArraySegment<byte>(Array.Empty<byte>()));

        if (!body.IsSingleSegment || !MemoryMarshal.TryGetArray(body.First, out ArraySegment<byte> data))
        {
          rented = ArrayPool<byte>.Shared.Rent(checked((int)body.Length));
          body.CopyTo(rented);
          data = new ArraySegment<byte>(rented, 0, (int)body.Length);
        }
        return read(state, data);
      }
      finally
      {
        if (rented != null)
          ArrayPool<byte>.Shared.Return(rented);
        reader.AdvanceTo(body.End);
      }
    }
#endif

    /// <summary>
    /// The differences between the body and the classes (<see cref="LtMsgPackHttpOptions.ReportDifferences"/>): in the features of the request (<see cref="ReadDifferencesExtensions.GetReadDifferences"/>)
    /// and logged, as a warning with <see cref="LtMsgPackHttpOptions.LogDifferencesAsWarning"/>, otherwise at the Debug level (clients sending extra properties would fill the logs).
    /// </summary>
    /// <param name="category">Of the logger</param>
    internal static void Report(HttpContext context, LtMsgPackHttpSerializer serializer, Type type, ReadDifferences differences, string category)
    {
      context.Features.Set(differences);
      ILogger logger = context.RequestServices?.GetService<ILoggerFactory>()?.CreateLogger(category);
      LogLevel level = serializer.LogsDifferencesAsWarning ? LogLevel.Warning : LogLevel.Debug;
      if (logger != null && logger.IsEnabled(level)) // the report walks the objects that were read
      {
        string sanitizedMethod = (context.Request.Method ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
        string sanitizedPath = context.Request.Path.ToString().Replace("\r", string.Empty).Replace("\n", string.Empty);
        logger.Log(level, "The request body of {Method} {Path} did not match {Type}: {Differences}", sanitizedMethod, sanitizedPath, type.Name, differences.GenerateReport());
      }
    }

    /// <summary>
    /// Reports the differences an exception of <see cref="LtMsgPackHttpSerializer.DeserializeBody"/> carries.
    /// </summary>
    /// <returns>False (for an exception filter)</returns>
    internal static bool ReportFailure(HttpContext context, LtMsgPackHttpSerializer serializer, Type type, Exception ex, string category)
    {
      ReadDifferences differences = LtMsgPackHttpSerializer.DifferencesOf(ex);
      if (differences != null)
        Report(context, serializer, type, differences, category);
      return false;
    }
  }
}
