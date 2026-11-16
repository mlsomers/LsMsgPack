using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Types;
using LsMsgPack.IO;
using LsMsgPack.Writing;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;

namespace LsMsgPack
{
  internal sealed partial class Serializer
  {
    /// <summary>
    /// The entry of every root type written (types do not go stale): behind the recent ones of each thread (<see cref="WriteContext.Entries"/>), so any number of root types allocates nothing after their first call.
    /// </summary>
    private readonly ConcurrentDictionary<Type, WriteRootEntry> _rootEntries = new ConcurrentDictionary<Type, WriteRootEntry>();

    [ThreadStatic]
    private static WriteState _threadState;

    /// <summary>
    /// The writers and context of the calls on one thread, reused (taken while a call runs, so a nested call gets its own).
    /// </summary>
    internal sealed class WriteState
    {
      internal Serializer Owner;
      internal WriteContext Context;
      internal MsgPackWriter Body = new MsgPackWriter(1024);
      internal MsgPackWriter Schema = new MsgPackWriter(256);
      internal InlineSchema Inline = new InlineSchema();

      /// <summary>
      /// The slot of the last struct root type (see <see cref="RootSlot"/>, LsMsgPackSerializer.TypedWriter).
      /// </summary>
      internal RootSlot Slot;

      private BufferSink _sink;

      /// <summary>
      /// The sink of this thread that copies the payload into <paramref name="target"/> (no sink per call). The caller sets its <see cref="BufferSink.Target"/> to null at the end.
      /// </summary>
      internal BufferSink SinkFor(IBufferWriter<byte> target)
      {
        BufferSink sink = _sink ?? (_sink = new BufferSink());
        sink.Target = target;
        return sink;
      }
    }

    /// <summary>
    /// The state of this thread (taken until the call returns it), or a new one. The Serialize overloads taking a state return it.
    /// </summary>
    internal WriteState RentState()
    {
      WriteState state = _threadState;
      _threadState = null;
      if (state is null)
        state = new WriteState();
      if (!ReferenceEquals(state.Owner, this))
      {
        state.Owner = this;
        state.Context = new WriteContext(this) { Inline = state.Inline };
      }
      return state;
    }

    private static void ReturnState(WriteState state)
    {
      if (state.Body.Buf.Length > 1024 * 1024) // do not keep a huge buffer alive
        state.Body = new MsgPackWriter(1024);
      _threadState = state;
    }

    /// <summary>
    /// What the calls look up for one declared root type, one per type and serializer (<see cref="_rootEntries"/>), the recent ones of a thread in <see cref="WriteContext.Entry"/> and <see cref="WriteContext.Entries"/>
    /// (the hash of a <see cref="Type"/> is a call into the runtime). Threads share it: each field is set once (the same value, a race sets it twice) or checked by the reader before it is used.
    /// </summary>
    internal sealed class WriteRootEntry
    {
      internal readonly Type AssignedTo;
      internal readonly FullPropertyInfo Info;

      /// <summary>
      /// The handler of the typed writer, a ValueHandler&lt;T&gt; of <see cref="AssignedTo"/> (see LsMsgPackSerializer.TypedWriter), null until it is first used.
      /// </summary>
      internal object Handler;

      /// <summary>
      /// The info of the value's type of the boxed writer (<see cref="WriteRoot"/>), last seen (checked: a value of another type replaces it).
      /// </summary>
      internal WriteTypeInfo TypeInfo;

      /// <summary>
      /// Schema references: the writer sessions of the root type in the store (the store's session state of a root type never changes; checked against the options' store)
      /// and the shared settings of the session written with last (checked: a grown one replaces it).
      /// </summary>
      internal WriterHit Writer;
      internal SessionShared Shared;

      internal WriteRootEntry(Type type)
      {
        AssignedTo = type;
        Info = new FullPropertyInfo(type);
      }
    }

    private const int WriteRootEntryCount = 8;

    /// <summary>
    /// The entry of <paramref name="assignedTo"/>: one the context's thread used recently, otherwise the serializer's (made at the first call of the type), which takes a slot of the thread's (the oldest goes).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)] // the calls of one root type never get here
    private WriteRootEntry EntryOf(WriteContext c, Type assignedTo)
    {
      WriteRootEntry previous = c.Entry;
      WriteRootEntry[] entries = c.Entries;
      if (entries != null)
      {
        for (int t = 0; t < entries.Length; t++) // forwards: they are filled in order, the first empty one ends the search
        {
          WriteRootEntry candidate = entries[t];
          if (candidate is null)
            break;
          if (ReferenceEquals(candidate.AssignedTo, assignedTo))
            return c.Entry = candidate;
        }
      }

      if (!_rootEntries.TryGetValue(assignedTo, out WriteRootEntry entry))
        entry = _rootEntries.GetOrAdd(assignedTo, t => new WriteRootEntry(t));
      if (previous != null) // a second root type on this thread: from now on the recent ones are kept
      {
        if (entries is null)
        {
          c.Entries = entries = new WriteRootEntry[WriteRootEntryCount];
          entries[0] = previous;
          c.NextEntry = 1;
        }
        int next = c.NextEntry;
        entries[next] = entry;
        c.NextEntry = next + 1 == entries.Length ? 0 : next + 1;
      }
      return c.Entry = entry;
    }

    /// <summary>
    /// Writes the payload: the value (names mode), or the schema (or a reference to it) followed by the value.
    /// </summary>
    /// <param name="value">The value, written by <paramref name="write"/> (it is <see cref="WriteContext.Root"/>): a struct of a typed call is in a <see cref="RootSlot{T}"/></param>
    /// <param name="write">Writes the value (typed or boxed), may run twice with a schema reference (when the cached schema grows)</param>
    /// <param name="sink">Gets the payload instead of <paramref name="target"/> or <paramref name="result"/> (the stream writer)</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] // no call more for the calls without a state
    internal void Serialize(Type assignedTo, object value, Action<WriteContext, FullPropertyInfo> write, Stream target, out byte[] result, IPayloadSink sink = null)
    {
      Serialize(RentState(), assignedTo, value, write, target, out result, sink);
    }

    /// <summary>
    /// Writes the payload into <paramref name="target"/> when it is complete (nothing when writing fails), through the <see cref="BufferSink"/> of the thread's state.
    /// </summary>
    internal void Serialize(Type assignedTo, object value, Action<WriteContext, FullPropertyInfo> write, IBufferWriter<byte> target)
    {
      WriteState state = RentState();
      BufferSink sink = state.SinkFor(target);
      try
      {
        Serialize(state, assignedTo, value, write, null, out byte[] unused, sink);
      }
      finally
      {
        sink.Target = null; // the state of the thread does not keep the target alive
      }
    }

    /// <inheritdoc cref="Serialize(Type, object, Action{WriteContext, FullPropertyInfo}, Stream, out byte[], IPayloadSink)"/>
    /// <param name="state">From <see cref="RentState"/>, returned at the end (LsMsgPackSerializer.TypedWriter puts a struct root in its slot first)</param>
    internal void Serialize(WriteState state, Type assignedTo, object value, Action<WriteContext, FullPropertyInfo> write, Stream target, out byte[] result, IPayloadSink sink = null)
    {
      result = null;
      bool isNull = value is null;
      try
      {
        WriteContext c = state.Context;
        c.Root = value;
        c.Depth = 0;
        if (c.PathCount != 0)
          c.ClearPath();
        c.ByNames = false;
        if (!ReferenceEquals(c.W, state.Body)) // the fields of the context mostly keep their values: a comparison is cheaper than storing a reference (a write barrier)
          c.W = state.Body;
        c.W.Reset(Options);
        WriteRootEntry entry = c.Entry;
        if (entry is null || entry.AssignedTo != assignedTo) // calls on a thread usually write the same root type
          entry = EntryOf(c, assignedTo);
        FullPropertyInfo root = entry.Info;

        if (!Options._useInexedSchema || isNull) // null is serialized without a schema
        {
          c.Mode = IdMode.Names;
          if (!ReferenceEquals(c.IdSettings, Options))
            c.IdSettings = Options;
          write(c, root);
          Output(null, 0, false, c.W, target, sink, out result);
          return;
        }

        if (Options._writeSchemaReference)
        {
          SchemaStore store = Options._schemaStore;
          if (store is null)
            throw new MsgPackException("MsgPackSettings.WriteSchemaReference needs a MsgPackSettings.SchemaStore to keep the schema in.");

          // The published session of the root type, shared with other calls (as SchemaStore.RunWriter, without its allocations)
          WriterHit hit = entry.Writer;
          if (hit is null || !ReferenceEquals(hit.Store, store))
            entry.Writer = hit = new WriterHit(store, store.GetWriter(assignedTo, Options)); // once per root type
          SchemaSession current = hit.State.Current;
          c.Replay.Start(); // the work may be repeated (WriteGrowing): the callbacks are called once per object
          if (current != null)
          {
            try
            {
              SessionShared shared = entry.Shared;
              if (shared is null || !ReferenceEquals(shared.Session, current))
                entry.Shared = shared = WriterShared(current);
              c.Mode = IdMode.Session;
              c.IdSettings = shared.Settings(Options);
              write(c, root);
              Output(current.Reference, current.Reference.Length, true, c.W, target, sink, out result);
              return;
            }
            catch (SchemaGrowthException) // something the session does not have yet, RunWriter grows it
            {
            }
          }

          SchemaSession session = WriteGrowing(store, assignedTo, c, root, write);
          Output(session.Reference, session.Reference.Length, true, c.W, target, sink, out result);
          return;
        }

        MsgPackWriter schema = state.Schema;
        schema.Reset(Options);
        if (!CustomPropertyIds && Filters != FilterMode.Custom)
        {
          // The schema of this call is built while writing (the same order as LsMsgPack's session)
          c.Mode = IdMode.Inline;
          c.Inline.Clear();
          if (!ReferenceEquals(c.IdSettings, Options))
            c.IdSettings = Options;
          write(c, root);
          if (Options._schemaVersion == SchemaVersion.Described)
            schema.Raw(_described.Get(c.Inline, assignedTo, Options));
          else
            c.Inline.WriteTo(schema);
        }
        else
        {
          // Custom property ids or filters see the ids of a session, as in LsMsgPack
          IndexedSchemaTypeResolver resolver = new IndexedSchemaTypeResolver();
          SchemaSession session = new SchemaSession(resolver, Options);
          c.Mode = IdMode.Session;
          c.IdSettings = session.Apply(Options);
          write(c, root);
          schema.Raw(resolver.Pack(Options, assignedTo, Options));
        }
        Output(schema.Buf, schema.Pos, false, c.W, target, sink, out result);
      }
      finally
      {
        state.Context.Root = null;
        ReturnState(state);
      }
    }

    /// <summary>
    /// Writes with the writer session of the root type under the store's lock, which grows it (in a method of its own: a lambda that captures parameters allocates its closure where the method starts, on every call).
    /// </summary>
    private SchemaSession WriteGrowing(SchemaStore store, Type assignedTo, WriteContext c, FullPropertyInfo root, Action<WriteContext, FullPropertyInfo> write)
    {
      SchemaSession session;
      store.RunWriter(assignedTo, Options, (s, sessionSettings) =>
      {
        c.Replay.Repeat(); // the callbacks of an earlier attempt are not called again (the first attempt when there was no session yet: nothing reached)
        c.Depth = 0;
        if (c.PathCount != 0)
          c.ClearPath();
        c.ByNames = false;
        c.W.Reset(Options);
        c.Mode = IdMode.Session;
        c.IdSettings = sessionSettings;
        write(c, root);
        return true;
      }, out session);
      return session;
    }

    #region Schema heads (the stream writer)

    private MsgPackOptions _completeOptions; // the options with CompleteSchemas, for the heads
    private SchemaStore _headStore; // the schemas of the heads, when the options have no store

    /// <summary>
    /// A schema written on its own, for the body that follows it in a stream (<see cref="Streaming.MsgPackStreamWriter.WriteHead{TRoot}"/>): written in parts, each with this schema.
    /// </summary>
    internal sealed class WriterHead
    {
      internal readonly SchemaStore Store;
      internal readonly Type Root;

      /// <summary>
      /// The options with <see cref="MsgPackOptions.CompleteSchemas"/>.
      /// </summary>
      internal readonly MsgPackOptions Settings;
      internal readonly SchemaId Id;

      /// <summary>
      /// Frozen, the schema of <see cref="Id"/>: replaced by a copy with more cache entries (never more schema).
      /// </summary>
      internal SchemaSession Session;

      internal WriterHead(SchemaStore store, Type root, MsgPackOptions settings, SchemaSession session)
      {
        Store = store;
        Root = root;
        Settings = settings;
        Session = session;
        Id = session.Id;
      }
    }

    /// <summary>
    /// The head of a root type: the complete schema of the root type (<see cref="MsgPackOptions.CompleteSchemas"/>, whatever the options say), published in the options' store or a store of this serializer
    /// (<see cref="SchemaStore.GetInlineSchema"/> has it), so the body after the head has every class it can hold.
    /// </summary>
    internal WriterHead PrepareHead(Type root)
    {
      SchemaStore store = HeadStore();

      MsgPackOptions settings = Options;
      if (!settings._completeSchemas)
      {
        settings = _completeOptions;
        if (settings is null)
        {
          settings = Options.CloneOptions(); // shares the arrays, so the session key is the same every time
          settings._completeSchemas = true;
          _completeOptions = settings;
        }
      }

      store.PrepareWriter(root, settings);
      return new WriterHead(store, root, settings, store.GetWriter(root, settings).Current); // the prepared session, or a grown one of the same root type
    }

    /// <summary>
    /// Where the schemas of the heads are kept: the options' store, or one of this serializer.
    /// </summary>
    internal SchemaStore HeadStore()
    {
      SchemaStore store = Options._schemaStore;
      if (store is null)
      {
        store = _headStore;
        if (store is null)
          _headStore = store = new SchemaStore();
      }
      return store;
    }

    /// <summary>
    /// Writes a value of the body of a schema head, as <see cref="Serialize"/> writes the body of a payload with that schema.
    /// </summary>
    /// <param name="assignedTo">The declared type of the value (a type id is added when the value's type differs)</param>
    /// <param name="depth">The nesting of the value in the stream (for <see cref="MsgPackOptions.MaxDepth"/>)</param>
    /// <exception cref="InvalidOperationException">The value needs a class or property the schema of the head does not have, nothing was written</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SerializeBody(WriterHead head, Type assignedTo, object value, Action<WriteContext, FullPropertyInfo> write, int depth, IPayloadSink sink)
    {
      SerializeBody(RentState(), head, assignedTo, value, write, depth, sink);
    }

    /// <inheritdoc cref="SerializeBody(WriterHead, Type, object, Action{WriteContext, FullPropertyInfo}, int, IPayloadSink)"/>
    /// <param name="state">From <see cref="RentState"/>, returned at the end</param>
    internal void SerializeBody(WriteState state, WriterHead head, Type assignedTo, object value, Action<WriteContext, FullPropertyInfo> write, int depth, IPayloadSink sink)
    {
      try
      {
        WriteContext c = state.Context;
        c.Root = value;
        WriteRootEntry entry = c.Entry;
        if (entry is null || entry.AssignedTo != assignedTo)
          entry = EntryOf(c, assignedTo);
        FullPropertyInfo root = entry.Info;
        try
        {
          c.Replay.Start();
          WriteBody(c, state, depth, WriterShared(head.Session).Settings(Options), write, root);
        }
        catch (SchemaGrowthException) // the frozen session needs more: cache entries (the first value of a class), or classes the head does not have
        {
          SchemaSession grown = head.Session.Thaw(head.Settings);
          c.Replay.Repeat();
          WriteBody(c, state, depth, grown.Apply(head.Settings), write, root);
          byte[] schema = grown.Resolver.Pack(SchemaBytes.Canonical, head.Root, head.Settings); // as SchemaStore.PublishWriter
          if (SchemaId.Compute(schema, head.Settings._schemaVersion) != head.Id)
            throw new InvalidOperationException($"The {(value is null ? "value" : RootSlot.TypeOf(value).Name)} declared as {assignedTo.Name} needs a class the schema written in the head does not have: a value declared as object, or a class of an assembly that {head.Root.Name} does not reach (see {nameof(MsgPackOptions.CompleteSchemas)}). Nothing was written for it.");
          grown.Freeze();
          head.Session = grown;
        }
        sink.Write(null, 0, false, c.W);
      }
      finally
      {
        state.Context.Root = null;
        ReturnState(state);
      }
    }

    private static void WriteBody(WriteContext c, WriteState state, int depth, MsgPackOptions sessionSettings, Action<WriteContext, FullPropertyInfo> write, FullPropertyInfo root)
    {
      c.Depth = depth;
      if (c.PathCount != 0)
        c.ClearPath();
      c.ByNames = false;
      if (!ReferenceEquals(c.W, state.Body))
        c.W = state.Body;
      c.W.Reset(c.Serializer.Options);
      c.Mode = IdMode.Session;
      c.IdSettings = sessionSettings;
      write(c, root);
    }

    #endregion

    /// <summary>
    /// The inline schemas of <see cref="SchemaVersion.Described"/>, made once per root type and sequence of types.
    /// </summary>
    private readonly DescribedInlineSchemas _described = new DescribedInlineSchemas();

    /// <summary>
    /// The writer sessions of a root type in a store (<see cref="WriteRootEntry.Writer"/>): SchemaStore.GetWriter finds them by a key of the settings, which do not change.
    /// </summary>
    internal sealed class WriterHit
    {
      internal readonly SchemaStore Store;
      internal readonly SchemaStore.SessionState State;

      internal WriterHit(SchemaStore store, SchemaStore.SessionState state)
      {
        Store = store;
        State = state;
      }
    }

    private volatile SessionShared _lastWriterSession; // apart from the one of reading, a serializer may do both

    private SessionShared WriterShared(SchemaSession session)
    {
      SessionShared last = _lastWriterSession;
      if (last != null && ReferenceEquals(last.Session, session))
        return last;
      last = _sessions.GetValue(session, s => new SessionShared(s));
      _lastWriterSession = last;
      return last;
    }

    /// <param name="reference">The head is a reference to a schema of the store (<see cref="SchemaStore.ReferenceLength"/> bytes)</param>
    private static void Output(byte[] head, int headLength, bool reference, MsgPackWriter body, Stream target, IPayloadSink sink, out byte[] result)
    {
      if (sink != null)
      {
        result = null;
        sink.Write(head, headLength, reference, body);
        return;
      }

      if (target != null)
      {
        result = null;
        if (headLength > 0)
          target.Write(head, 0, headLength);
        body.CopyTo(target);
        return;
      }

      result = MsgPackWriter.NewBytes(headLength + body.Pos); // filled below
      if (headLength > 0)
        new ReadOnlySpan<byte>(head, 0, headLength).CopyTo(result); // Buffer.BlockCopy checks the array types first
      new ReadOnlySpan<byte>(body.Buf, 0, body.Pos).CopyTo(new Span<byte>(result, headLength, body.Pos));
    }
  }

  /// <summary>
  /// Takes the payload of a Serialize call: the head (the schema, a reference to it, or nothing) and the body (<see cref="Streaming.MsgPackStreamWriter"/>).
  /// </summary>
  internal interface IPayloadSink
  {
    void Write(byte[] head, int headLength, bool reference, MsgPackWriter body);
  }

  /// <summary>
  /// Copies the payload into an <see cref="IBufferWriter{T}"/> (LsMsgPackSerializer.Serialize to an IBufferWriter): in one piece when the target gives a span that large, otherwise in the pieces it gives.
  /// </summary>
  internal sealed class BufferSink : IPayloadSink
  {
    internal IBufferWriter<byte> Target;

    public void Write(byte[] head, int headLength, bool reference, MsgPackWriter body)
    {
      IBufferWriter<byte> target = Target;
      int total = headLength + body.Pos;
      Span<byte> span = target.GetSpan(total);
      if (span.Length >= total)
      {
        if (headLength > 0)
          new ReadOnlySpan<byte>(head, 0, headLength).CopyTo(span);
        new ReadOnlySpan<byte>(body.Buf, 0, body.Pos).CopyTo(span.Slice(headLength));
        target.Advance(total);
        return;
      }
      Put(target, new ReadOnlySpan<byte>(head, 0, headLength));
      Put(target, new ReadOnlySpan<byte>(body.Buf, 0, body.Pos));
    }

    private static void Put(IBufferWriter<byte> target, ReadOnlySpan<byte> data)
    {
      while (data.Length > 0)
      {
        Span<byte> span = target.GetSpan(data.Length);
        if (span.Length == 0)
          throw new InvalidOperationException($"The {nameof(IBufferWriter<byte>)} returned an empty span.");
        int count = Math.Min(span.Length, data.Length);
        data.Slice(0, count).CopyTo(span);
        target.Advance(count);
        data = data.Slice(count);
      }
    }
  }
}
