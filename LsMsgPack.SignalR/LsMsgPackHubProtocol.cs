using LsMsgPack.IO;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;

namespace LsMsgPack.SignalR
{
  /// <summary>
  /// A SignalR hub protocol on LsMsgPack (server and .NET client): the envelope of Microsoft's MessagePack protocol (each message a length prefix and an array, the same message types and fields),
  /// the values (arguments, stream items, results) written and read by <see cref="LsMsgPackSerializer"/> with the options of <see cref="LsMsgPackHubProtocolOptions"/>.
  /// <para>Named "lsmsgpack" (<see cref="LsMsgPackName"/>) with LsMsgPack's values, for its TypeScript client (<c>@lsmsgpack/client/signalr</c>) and .NET clients,
  /// or "messagepack" (<see cref="LsMsgPackHubProtocolOptions.AsMessagePack"/>) with the bytes of Microsoft's MessagePack protocol, for the clients of that one.</para>
  /// <para>Register it with <c>AddLsMsgPackProtocol()</c> on <c>AddSignalR()</c> (server) or on a <c>HubConnectionBuilder</c> (client). Thread-safe.</para>
  /// <para>Trimmed and NativeAOT apps: create it with the code of LsMsgPack's source generator (<see cref="LsMsgPackHubProtocol(LsMsgPackContext, LsMsgPackHubProtocolOptions)"/>):
  /// the hub methods' parameter and return types must be in the context.</para>
  /// </summary>
  public class LsMsgPackHubProtocol : IHubProtocol
  {
    /// <summary>
    /// The name of the protocol with LsMsgPack's values.
    /// </summary>
    public const string LsMsgPackName = "lsmsgpack";

    /// <summary>
    /// The name of Microsoft's MessagePack protocol, used with <see cref="LsMsgPackHubProtocolOptions.AsMessagePack"/>.
    /// </summary>
    public const string MessagePackName = "messagepack";

    /// <summary>
    /// Version 2: the messages of stateful reconnect (Ack, Sequence), as Microsoft's protocols of ASP.NET Core 8.
    /// </summary>
    private const int ProtocolVersion = 2;

    internal const string RequiresUnreferencedCodeMessage = "Without generated code the serializer reads the properties and constructors of the types by reflection, trimming may remove them. Use the overload taking the context of LsMsgPack's source generator, with the parameter and return types of the hub methods.";
    internal const string RequiresDynamicCodeMessage = "Without generated code the serializer instantiates its generic code for the types at runtime, NativeAOT may not have it. Use the overload taking the context of LsMsgPack's source generator, with the parameter and return types of the hub methods.";

    private readonly string _name;
    private readonly LsMsgPackSerializer _serializer;
    private readonly Serializer _reader;
    private readonly LsMsgPackOptions _options;

    /// <summary>
    /// "lsmsgpack" with the default options.
    /// </summary>
    [RequiresUnreferencedCode(LsMsgPackHubProtocol.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LsMsgPackHubProtocol.RequiresDynamicCodeMessage)]
    public LsMsgPackHubProtocol() : this(new LsMsgPackHubProtocolOptions(), null) { }

    /// <param name="options">Read once, later changes do not apply</param>
    [RequiresUnreferencedCode(LsMsgPackHubProtocol.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LsMsgPackHubProtocol.RequiresDynamicCodeMessage)]
    public LsMsgPackHubProtocol(LsMsgPackHubProtocolOptions options) : this(options, null) { }

    /// <summary>
    /// With the code of LsMsgPack's source generator: the serializer asks <paramref name="context"/> first (see <see cref="LsMsgPackSerializer(LsMsgPackContext, LsMsgPackOptions)"/>).
    /// </summary>
    /// <param name="context">E.g. <c>AppMsgPackContext.Default</c>, with the parameter and return types of the hub methods</param>
    /// <param name="options">Null for the defaults; read once, later changes do not apply</param>
    public LsMsgPackHubProtocol(LsMsgPackContext context, LsMsgPackHubProtocolOptions options = null) : this(options ?? new LsMsgPackHubProtocolOptions(), NotNull(context)) { }

    /// <param name="context">null for none: the public constructors without a context require unreferenced and dynamic code, callers of this one (the registration) do the same</param>
    internal LsMsgPackHubProtocol(LsMsgPackHubProtocolOptions options, LsMsgPackContext context)
    {
      if (options is null)
        throw new ArgumentNullException(nameof(options));
      _name = options.AsMessagePack ? MessagePackName : LsMsgPackName;
      LsMsgPackOptions values = options.SerializerOptions?.Clone() ?? LsMsgPackHubProtocolOptions.DefaultSerializerOptions(options.AsMessagePack);
      values.UseInexedSchema = false; // a message is written once for every connection it goes to (see LsMsgPackHubProtocolOptions.SerializerOptions)
      values.WriteSchemaReference = false;
      values.MaxObjects = options.MaxObjects;
      values.MaxItems = options.MaxItems;
      values.MaxStringLength = options.MaxStringLength;
      values.MaxBinLength = options.MaxBinLength;
      _options = values;
      _serializer = new LsMsgPackSerializer(values, context);
      _reader = _serializer.Implementation;
    }

    private static LsMsgPackContext NotNull(LsMsgPackContext context)
    {
      if (context is null)
        throw new ArgumentNullException(nameof(context));
      return context;
    }

    /// <summary>
    /// "lsmsgpack" or "messagepack" (<see cref="LsMsgPackHubProtocolOptions.AsMessagePack"/>): what the client asks for in its handshake.
    /// </summary>
    public string Name
    {
      get { return _name; }
    }

    public int Version
    {
      get { return ProtocolVersion; }
    }

    public TransferFormat TransferFormat
    {
      get { return TransferFormat.Binary; }
    }

    /// <summary>
    /// The serializer of the values (a copy of the options: <see cref="LsMsgPackSerializer.Options"/>).
    /// </summary>
    public LsMsgPackSerializer Serializer
    {
      get { return _serializer; }
    }

    public bool IsVersionSupported(int version)
    {
      return version <= ProtocolVersion;
    }

    #region Writing

    public void WriteMessage(HubMessage message, IBufferWriter<byte> output)
    {
      if (output is null)
        throw new ArgumentNullException(nameof(output));
      HubFrame frame = HubFrame.Rent();
      try
      {
        WriteMessage(message, frame);
        int start = frame.Framed();
        output.Write(new ReadOnlySpan<byte>(frame.Buf, start, frame.Pos - start));
      }
      finally
      {
        HubFrame.Return(frame);
      }
    }

    public ReadOnlyMemory<byte> GetMessageBytes(HubMessage message)
    {
      HubFrame frame = HubFrame.Rent();
      try
      {
        WriteMessage(message, frame);
        int start = frame.Framed();
        byte[] bytes = new byte[frame.Pos - start];
        Buffer.BlockCopy(frame.Buf, start, bytes, 0, bytes.Length);
        return bytes;
      }
      finally
      {
        HubFrame.Return(frame);
      }
    }

    /// <summary>
    /// The message without its length prefix: Microsoft's MessagePack protocol, field by field.
    /// </summary>
    private void WriteMessage(HubMessage message, HubFrame w)
    {
      InvocationMessage invocation = message as InvocationMessage;
      if (invocation != null)
      {
        w.ArrayHeader(6);
        w.Integer(HubEnvelope.Invocation);
        WriteHeaders(invocation.Headers, w);
        w.String(string.IsNullOrEmpty(invocation.InvocationId) ? null : invocation.InvocationId);
        w.String(invocation.Target);
        WriteArguments(invocation.Arguments, w);
        WriteStreamIds(invocation.StreamIds, w);
        return;
      }

      StreamItemMessage item = message as StreamItemMessage;
      if (item != null)
      {
        w.ArrayHeader(4);
        w.Integer(HubEnvelope.StreamItem);
        WriteHeaders(item.Headers, w);
        w.String(item.InvocationId);
        WriteValue(item.Item, w);
        return;
      }

      CompletionMessage completion = message as CompletionMessage;
      if (completion != null)
      {
        int kind = completion.Error != null ? HubEnvelope.ErrorResult : completion.HasResult ? HubEnvelope.NonVoidResult : HubEnvelope.VoidResult;
        w.ArrayHeader(kind == HubEnvelope.VoidResult ? 4 : 5);
        w.Integer(HubEnvelope.Completion);
        WriteHeaders(completion.Headers, w);
        w.String(completion.InvocationId);
        w.Integer(kind);
        if (kind == HubEnvelope.ErrorResult)
          w.String(completion.Error);
        else if (kind == HubEnvelope.NonVoidResult)
          WriteValue(completion.Result, w);
        return;
      }

      StreamInvocationMessage streamInvocation = message as StreamInvocationMessage;
      if (streamInvocation != null)
      {
        w.ArrayHeader(6);
        w.Integer(HubEnvelope.StreamInvocation);
        WriteHeaders(streamInvocation.Headers, w);
        w.String(streamInvocation.InvocationId);
        w.String(streamInvocation.Target);
        WriteArguments(streamInvocation.Arguments, w);
        WriteStreamIds(streamInvocation.StreamIds, w);
        return;
      }

      CancelInvocationMessage cancel = message as CancelInvocationMessage;
      if (cancel != null)
      {
        w.ArrayHeader(3);
        w.Integer(HubEnvelope.CancelInvocation);
        WriteHeaders(cancel.Headers, w);
        w.String(cancel.InvocationId);
        return;
      }

      if (message is PingMessage)
      {
        w.ArrayHeader(1);
        w.Integer(HubEnvelope.Ping);
        return;
      }

      CloseMessage close = message as CloseMessage;
      if (close != null)
      {
        w.ArrayHeader(3);
        w.Integer(HubEnvelope.Close);
        w.String(string.IsNullOrEmpty(close.Error) ? null : close.Error);
        w.Bool(close.AllowReconnect);
        return;
      }

      AckMessage ack = message as AckMessage;
      if (ack != null)
      {
        w.ArrayHeader(2);
        w.Integer(HubEnvelope.Ack);
        w.Integer(ack.SequenceId);
        return;
      }

      SequenceMessage sequence = message as SequenceMessage;
      if (sequence != null)
      {
        w.ArrayHeader(2);
        w.Integer(HubEnvelope.Sequence);
        w.Integer(sequence.SequenceId);
        return;
      }

      throw new InvalidDataException($"Unexpected message type: {message?.GetType().Name ?? "null"}");
    }

    private static void WriteHeaders(IDictionary<string, string> headers, HubFrame w)
    {
      if (headers is null || headers.Count == 0)
      {
        w.MapHeader(0);
        return;
      }
      w.MapHeader(headers.Count);
      foreach (KeyValuePair<string, string> header in headers) // an IDictionary of unknown kind
      {
        w.String(header.Key);
        w.String(header.Value);
      }
    }

    private void WriteArguments(object[] arguments, HubFrame w)
    {
      if (arguments is null)
      {
        w.ArrayHeader(0);
        return;
      }
      w.ArrayHeader(arguments.Length);
      for (int i = 0; i < arguments.Length; i++)
        WriteValue(arguments[i], w);
    }

    private static void WriteStreamIds(string[] streamIds, HubFrame w)
    {
      if (streamIds is null)
      {
        w.ArrayHeader(0);
        return;
      }
      w.ArrayHeader(streamIds.Length);
      for (int i = 0; i < streamIds.Length; i++)
        w.String(streamIds[i]);
    }

    /// <summary>
    /// A value as its runtime type (as Microsoft's protocols: the declared types of a hub method are not known here); a <see cref="RawResult"/> (a client's result passed on by the server) as its bytes.
    /// </summary>
    private void WriteValue(object value, HubFrame w)
    {
      if (value is null)
      {
        w.Nil();
        return;
      }
      RawResult raw = value as RawResult;
      if (raw != null)
      {
        w.Raw(raw.RawSerializedData);
        return;
      }
      _serializer.Serialize(value, value.GetType(), w);
    }

    #endregion

    #region Reading

    public bool TryParseMessage(ref ReadOnlySequence<byte> input, IInvocationBinder binder, out HubMessage message)
    {
      if (!HubFrame.TryRead(ref input, out ReadOnlySequence<byte> payload))
      {
        message = null;
        return false;
      }

      if (payload.IsSingleSegment && MemoryMarshal.TryGetArray(payload.First, out ArraySegment<byte> segment)) // read in place (Kestrel's and the client's pipes are arrays)
        message = ParseMessage(segment.Array, segment.Offset, segment.Offset + segment.Count, binder);
      else
        message = ParseCopy(payload, binder);
      return message != null;
    }

    /// <summary>
    /// A message in several segments (or not in an array) is read from a copy in an array of the shared pool, cleared before it goes back (the reader copies what it keeps).
    /// </summary>
    private HubMessage ParseCopy(in ReadOnlySequence<byte> payload, IInvocationBinder binder)
    {
      int length = (int)payload.Length; // at most 2 GB (the prefix)
      byte[] copy = ArrayPool<byte>.Shared.Rent(length);
      try
      {
        payload.CopyTo(copy);
        return ParseMessage(copy, 0, length, binder);
      }
      finally
      {
        new Span<byte>(copy, 0, length).Clear();
        ArrayPool<byte>.Shared.Return(copy);
      }
    }

    /// <summary>
    /// One message (Microsoft's MessagePack protocol: the same fields, checks and errors). Null for an unknown message type (SignalR ignores it).
    /// </summary>
    private HubMessage ParseMessage(byte[] buf, int pos, int end, IInvocationBinder binder)
    {
      int itemCount = HubEnvelope.ReadArrayHeader(buf, ref pos, end, "message");
      int messageType = HubEnvelope.ReadInt32(buf, ref pos, end, "messageType");
      switch (messageType)
      {
        case HubEnvelope.Invocation:
          return ReadInvocation(buf, pos, end, binder, itemCount, false);
        case HubEnvelope.StreamInvocation:
          return ReadInvocation(buf, pos, end, binder, itemCount, true);
        case HubEnvelope.StreamItem:
          return ReadStreamItem(buf, pos, end, binder);
        case HubEnvelope.Completion:
          return ReadCompletion(buf, pos, end, binder);
        case HubEnvelope.CancelInvocation:
          {
            Dictionary<string, string> headers = ReadHeaders(buf, ref pos, end);
            string invocationId = HubEnvelope.ReadString(buf, ref pos, end, "invocationId");
            ThrowIfNullOrEmpty(invocationId, "invocation ID for CancelInvocation message");
            return ApplyHeaders(headers, new CancelInvocationMessage(invocationId));
          }
        case HubEnvelope.Ping:
          return PingMessage.Instance;
        case HubEnvelope.Close:
          {
            string error = HubEnvelope.ReadString(buf, ref pos, end, "error");
            bool allowReconnect = itemCount > 2 && HubEnvelope.ReadBoolean(buf, ref pos, end, "allowReconnect");
            if (error is null && !allowReconnect)
              return CloseMessage.Empty;
            return new CloseMessage(error, allowReconnect);
          }
        case HubEnvelope.Ack:
          return new AckMessage(HubEnvelope.ReadInt64(buf, ref pos, end, "sequenceId"));
        case HubEnvelope.Sequence:
          return new SequenceMessage(HubEnvelope.ReadInt64(buf, ref pos, end, "sequenceId"));
        default:
          return null; // a newer message type: ignored, as Microsoft's protocols do
      }
    }

    private HubMessage ReadInvocation(byte[] buf, int pos, int end, IInvocationBinder binder, int itemCount, bool streaming)
    {
      Dictionary<string, string> headers = ReadHeaders(buf, ref pos, end);
      string invocationId = HubEnvelope.ReadString(buf, ref pos, end, "invocationId");
      if (streaming)
        ThrowIfNullOrEmpty(invocationId, "invocation ID for StreamInvocation message");
      else if (string.IsNullOrEmpty(invocationId))
        invocationId = null;
      string target = ReadTarget(buf, ref pos, end, binder);
      ThrowIfNullOrEmpty(target, streaming ? "target for StreamInvocation message" : "target for Invocation message");

      object[] arguments;
      try
      {
        IReadOnlyList<Type> parameterTypes = binder.GetParameterTypes(target);
        arguments = ReadArguments(buf, ref pos, end, parameterTypes);
      }
      catch (Exception ex)
      {
        return new InvocationBindingFailureMessage(invocationId, target, ExceptionDispatchInfo.Capture(ex));
      }

      string[] streams = itemCount > 5 ? ReadStreamIds(buf, ref pos, end) : null;
      if (streaming)
        return ApplyHeaders(headers, new StreamInvocationMessage(invocationId, target, arguments, streams));
      return ApplyHeaders(headers, new InvocationMessage(invocationId, target, arguments, streams));
    }

    /// <summary>
    /// The target, as the binder's string when it knows the name (no string made per message, .NET 7 and up), as Microsoft's protocol.
    /// </summary>
    private static string ReadTarget(byte[] buf, ref int pos, int end, IInvocationBinder binder)
    {
      try
      {
        if (!HubEnvelope.TryReadStringBytes(buf, ref pos, end, out int start, out int length))
          return null;
#if NET5_0_OR_GREATER
        string known = binder.GetTarget(new ReadOnlySpan<byte>(buf, start, length));
        if (known != null)
          return known;
#endif
        return Encoding.UTF8.GetString(buf, start, length);
      }
      catch (Exception ex)
      {
        throw new InvalidDataException("Reading 'target' as String failed.", ex);
      }
    }

    private HubMessage ReadStreamItem(byte[] buf, int pos, int end, IInvocationBinder binder)
    {
      Dictionary<string, string> headers = ReadHeaders(buf, ref pos, end);
      string invocationId = HubEnvelope.ReadString(buf, ref pos, end, "invocationId");
      ThrowIfNullOrEmpty(invocationId, "invocation ID for StreamItem message");
      object value;
      try
      {
        Type itemType = binder.GetStreamItemType(invocationId);
        value = ReadValue(buf, ref pos, end, itemType, "item");
      }
      catch (Exception ex)
      {
        return new StreamBindingFailureMessage(invocationId, ExceptionDispatchInfo.Capture(ex));
      }
      return ApplyHeaders(headers, new StreamItemMessage(invocationId, value));
    }

    private HubMessage ReadCompletion(byte[] buf, int pos, int end, IInvocationBinder binder)
    {
      Dictionary<string, string> headers = ReadHeaders(buf, ref pos, end);
      string invocationId = HubEnvelope.ReadString(buf, ref pos, end, "invocationId");
      ThrowIfNullOrEmpty(invocationId, "invocation ID for Completion message");
      int resultKind = HubEnvelope.ReadInt32(buf, ref pos, end, "resultKind");
      string error = null;
      object result = null;
      bool hasResult = false;
      switch (resultKind)
      {
        case HubEnvelope.ErrorResult:
          error = HubEnvelope.ReadString(buf, ref pos, end, "error");
          break;
        case HubEnvelope.NonVoidResult:
          hasResult = true;
          Type itemType = TryGetReturnType(binder, invocationId);
          if (itemType is null)
            Skip(buf, pos, end);
          else if (itemType == typeof(RawResult)) // the server passes a client's result on (scale-out), as its bytes
            result = new RawResult(new ReadOnlySequence<byte>(ValueBytes(buf, pos, end)));
          else
          {
            try
            {
              result = ReadValue(buf, ref pos, end, itemType, "argument");
            }
            catch (Exception ex)
            {
              error = $"Error trying to deserialize result to {itemType.Name}. {ex.Message}";
              hasResult = false;
            }
          }
          break;
        case HubEnvelope.VoidResult:
          break;
        default:
          throw new InvalidDataException("Invalid invocation result kind.");
      }
      return ApplyHeaders(headers, new CompletionMessage(invocationId, error, result, hasResult));
    }

    private static Type TryGetReturnType(IInvocationBinder binder, string invocationId)
    {
      try
      {
        return binder.GetReturnType(invocationId);
      }
      catch (Exception) // as Microsoft's ProtocolHelper: an invocation the binder does not know (e.g. cancelled), the result is skipped
      {
        return null;
      }
    }

    private object[] ReadArguments(byte[] buf, ref int pos, int end, IReadOnlyList<Type> parameterTypes)
    {
      int argumentCount = HubEnvelope.ReadArrayHeader(buf, ref pos, end, "arguments");
      if (parameterTypes.Count != argumentCount)
        throw new InvalidDataException($"Invocation provides {argumentCount} argument(s) but target expects {parameterTypes.Count}.");
      try
      {
        object[] arguments = new object[argumentCount];
        for (int i = 0; i < arguments.Length; i++)
          arguments[i] = ReadValue(buf, ref pos, end, parameterTypes[i], "argument");
        return arguments;
      }
      catch (Exception ex)
      {
        throw new InvalidDataException("Error binding arguments. Make sure that the types of the provided values match the types of the hub method being invoked.", ex);
      }
    }

    /// <summary>
    /// One value in place: the serializer reads it from the message's bytes and tells where it ended.
    /// </summary>
    private object ReadValue(byte[] buf, ref int pos, int end, Type type, string field)
    {
      try
      {
        object value = _reader.Deserialize(type, buf, pos, end, out int consumed);
        pos = consumed;
        return value;
      }
      catch (Exception ex)
      {
        throw new InvalidDataException($"Deserializing object of the `{type.Name}` type for '{field}' failed.", ex);
      }
    }

    private void Skip(byte[] buf, int pos, int end)
    {
      new MsgPackReader(buf, pos, end, _options).Skip(0);
    }

    /// <summary>
    /// A copy of the bytes of the value at <paramref name="pos"/> (the message's buffer is reused once it is read).
    /// </summary>
    private byte[] ValueBytes(byte[] buf, int pos, int end)
    {
      MsgPackReader reader = new MsgPackReader(buf, pos, end, _options);
      reader.Skip(0);
      byte[] bytes = new byte[reader.Pos - pos];
      Buffer.BlockCopy(buf, pos, bytes, 0, bytes.Length);
      return bytes;
    }

    private static Dictionary<string, string> ReadHeaders(byte[] buf, ref int pos, int end)
    {
      int headerCount = HubEnvelope.ReadMapHeader(buf, ref pos, end, "headers");
      if (headerCount == 0)
        return null;
      Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.Ordinal);
      for (int i = 0; i < headerCount; i++)
      {
        string key = HubEnvelope.ReadString(buf, ref pos, end, "headers.Key");
        ThrowIfNullOrEmpty(key, "key in header");
        string value = HubEnvelope.ReadString(buf, ref pos, end, "headers.Value");
        ThrowIfNullOrEmpty(value, "value in header");
        headers.Add(key, value);
      }
      return headers;
    }

    private static string[] ReadStreamIds(byte[] buf, ref int pos, int end)
    {
      int count = HubEnvelope.ReadArrayHeader(buf, ref pos, end, "streamIds");
      if (count == 0)
        return null;
      string[] streams = new string[count];
      for (int i = 0; i < streams.Length; i++)
      {
        string id = HubEnvelope.ReadString(buf, ref pos, end, "streamIds");
        ThrowIfNullOrEmpty(id, "value in streamIds received");
        streams[i] = id;
      }
      return streams;
    }

    private static T ApplyHeaders<T>(Dictionary<string, string> headers, T message) where T : HubInvocationMessage
    {
      if (headers != null)
        message.Headers = headers;
      return message;
    }

    private static void ThrowIfNullOrEmpty(string value, string what)
    {
      if (string.IsNullOrEmpty(value))
        throw new InvalidDataException($"Null or empty {what}.");
    }

    #endregion
  }
}
