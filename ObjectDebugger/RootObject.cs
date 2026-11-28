using LsMsgPack;
using LsMsgPackDebug;
using LsMsgPack.Streaming;
using System;
using System.Collections.Generic;
using System.IO;

namespace ObjectDebugger
{
  /// <summary>
  /// The root of an object tree reconstructed from MsgPack data, without having the types it was written from.
  /// <para>Create it, then call <see cref="Reconstruct(MsgPackItem, SchemaStore)"/>: what was reconstructed before an error stays available.</para>
  /// </summary>
  public class RootObject : ComplexObject
  {
    /// <summary>
    /// The indexed schema of the payload, null when it has none.
    /// </summary>
    public SchemaInfo Schema { get; set; }

    /// <summary>
    /// Doubts about the reconstruction (e.g. types of the schema that no value was matched with, so inferred types may be wrong).
    /// </summary>
    public List<string> Warnings { get; } = new List<string>();

    /// <summary>
    /// Rebuilds the object tree from unpacked data (<see cref="MsgPackItem.UnpackMultiple(byte[], MsgPackSettings)"/>).
    /// <para>The data may start with the indexed schema (or a reference to it) followed by the body, several payloads that follow each other become a <see cref="ObjectKind.Sequence"/> of roots.</para>
    /// <para>Errors are kept with the value they occurred in (<see cref="PrimitiveObject.Error"/>), the rest of the tree is still reconstructed.</para>
    /// </summary>
    /// <param name="item">An <see cref="MpRoot"/> or a single item. With KEEPTRACK and PreservePackages every value refers to its items (offsets).</param>
    /// <param name="schemaStore">Holds the schemas the data may refer to (<c>WriteSchemaReference</c>), optional</param>
    public void Reconstruct(MsgPackItem item, SchemaStore schemaStore = null)
    {
      Reconstruct(item, schemaStore, (TrimmedData)null);
    }

    /// <param name="data">The bytes <paramref name="item"/> was read from (KEEPTRACK): a schema written inline is then also found by the references to it later in the data (a stream written with <c>MsgPackStreamSchemas.PerStream</c>)</param>
    public void Reconstruct(MsgPackItem item, SchemaStore schemaStore, byte[] data)
    {
      Reconstruct(item, schemaStore, data is null ? null : TrimmedData.FromBytes(data, null));
    }

    /// <param name="data">What was kept of the bytes <paramref name="item"/> was read from (KEEPTRACK): a schema written inline is then also found by the references to it later in the data (a stream written with <c>MsgPackStreamSchemas.PerStream</c>)</param>
    public void Reconstruct(MsgPackItem item, SchemaStore schemaStore, TrimmedData data)
    {
      Members.Clear();
      Warnings.Clear();
      Schema = null;
      Error = null;
      Type = null;
      TypeIsGuess = false;
      SchemaType = null;
      DeclaredType = null;
      DeclaredTypeText = null;
      Kind = ObjectKind.Object;
      FirstItemRef = item;
      LastItemRef = item;

      if (item is null)
      {
        Kind = ObjectKind.Value;
        ValueKind = ValueKind.Nil;
        return;
      }

      List<MsgPackItem> items = new List<MsgPackItem>();
      if (item is MpRoot root)
        items.AddRange(root);
      else
        items.Add(item);

      List<Payload> payloads = SplitPayloads(items, schemaStore, data);
      Dictionary<long, MsgPackStreamEntry> entries = EntriesByOffset(items);
      if (payloads.Count == 1)
      {
        Entry = EntryOf(payloads[0], entries);
        Reconstruct(payloads[0]);
        return;
      }

      Kind = ObjectKind.Sequence;
      for (int t = 0; t < payloads.Count; t++)
      {
        RootObject payload = new RootObject() { Name = string.Concat("[", t.ToString(), "]"), Parent = this, Entry = EntryOf(payloads[t], entries) };
        Members.Add(payload);
        payload.Reconstruct(payloads[t]);
      }
    }

    /// <summary>
    /// The entry of the index of a stream that starts where this payload starts (its schema or head included), null when the data has no index.
    /// </summary>
    public MsgPackStreamEntry Entry { get; set; }

    /// <summary>
    /// <see cref="Entry"/> as the explorers show it (see <see cref="DescribeEntry"/>), null without one.
    /// </summary>
    public string EntryText
    {
      get { return Entry is null ? null : DescribeEntry(Entry); }
    }

    /// <summary>
    /// <c>entry "orders"</c> for a string identifier, <c>entry 7</c> for an integer, <c>entry</c> without one.
    /// </summary>
    public static string DescribeEntry(MsgPackStreamEntry entry)
    {
      if (entry.Identifier is null)
        return "entry";
      if (entry.Identifier is string text)
        return string.Concat("entry \"", text, "\"");
      return string.Concat("entry ", Convert.ToString(entry.Identifier, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The entries of the valid indexes of a stream (<see cref="MpStreamIndex"/>) by their offset, null without them.
    /// </summary>
    private static Dictionary<long, MsgPackStreamEntry> EntriesByOffset(List<MsgPackItem> items)
    {
      Dictionary<long, MsgPackStreamEntry> entries = null;
      for (int t = 0; t < items.Count; t++)
      {
        MpStreamIndex index = items[t] as MpStreamIndex;
        if (index?.Index is null)
          continue;
        if (entries is null)
          entries = new Dictionary<long, MsgPackStreamEntry>();
        for (int e = 0; e < index.Index.Entries.Count; e++)
          entries[index.Index.Entries[e].Offset] = index.Index.Entries[e];
      }
      return entries;
    }

    private static MsgPackStreamEntry EntryOf(Payload payload, Dictionary<long, MsgPackStreamEntry> entries)
    {
#if KEEPTRACK
      MsgPackStreamEntry entry;
      if (entries != null && entries.TryGetValue((payload.SchemaItem ?? payload.Body).StoredOffset, out entry))
        return entry;
#endif
      return null;
    }

    /// <summary>
    /// The index of a stream, its tail and sidecars are no data (<see cref="MsgPackSettings.ReadStreams"/>), nor are the handshake and length prefixes of SignalR messages (<see cref="MsgPackSettings.ReadHubMessages"/>).
    /// </summary>
    internal static bool IsStreamPart(MsgPackItem item)
    {
      return item is MpStreamIndex || item is MpStreamTail || item is MpStreamSidecar || item is MpHubPrefix || item is MpHubHandshake;
    }

    private void Reconstruct(Payload payload)
    {
      if (payload.Body is MpHubMessage message)
      {
        ReconstructHubMessage(message, payload.IndexedHubValues);
        return;
      }
      Schema = payload.Schema;
      FirstItemRef = payload.SchemaItem ?? payload.Body;
      LastItemRef = payload.Body;
      if (payload.Error != null)
        AddError(payload.Error);
      FillBody(Node.FromItem(payload.Body));
    }

    /// <summary>
    /// The objects of a body with the <see cref="Schema"/> of this payload (none: inferred).
    /// </summary>
    private void FillBody(Node body)
    {
      try
      {
        List<SchemaType> types = Schema != null && Schema.IsAvailable ? Schema.Types : null;
        Node.LinkReferences(body);
        ObjectReconstructor reconstructor = new ObjectReconstructor(types);
        reconstructor.FillRoot(this, body, Schema != null && Schema.IsAvailable ? Schema.RootType : null);
        if (reconstructor.Suspects.Count > 0 && reconstructor.Unexplained > 0)
          RetryWithValues(body, types, reconstructor);

        if (Schema != null && Schema.Source == SchemaSource.Inline)
        {
          // An inline schema has exactly the types of this payload, so every type should have been found
          List<string> unused = new List<string>();
          for (int t = 0; t < Schema.Types.Count; t++)
            if (!Schema.Types[t].IsUsed)
              unused.Add(Schema.Types[t].Name);
          if (unused.Count > 0)
            Warnings.Add($"No value was matched with these types of the schema: {string.Join(", ", unused)}. Inferred types may be wrong.");
        }
      }
      catch (Exception ex)
      {
        AddError(ex.Message);
      }
    }

    /// <summary>
    /// A SignalR message: an object of its fields by name (<see cref="HubCapture.FieldName"/>), each value reconstructed on its own as the protocol writes it (an argument, a stream item,
    /// a result: each one a payload, its references point into it).
    /// </summary>
    /// <param name="indexed">"lsmsgpack-indexed": each value is nil, [schema, body] or [nil, value without a schema] (<see cref="HubValuesIndexed"/>)</param>
    private void ReconstructHubMessage(MpHubMessage message, bool indexed)
    {
      Kind = ObjectKind.Object;
      Type = "SignalR " + (message.MessageTypeName ?? "message");
      FirstItemRef = message;
      LastItemRef = message;
      Node body = Node.FromItem(message);
      if (body.Error != null)
        AddError(body.Error);
      if (body.Elements is null)
        return;

      int resultKind = message.ResultKind;
      for (int f = 0; f < body.Elements.Length; f++)
      {
        string name = HubCapture.FieldName(message.MessageType, f, resultKind) ?? string.Concat("[", f.ToString(), "]");
        Node node = body.Elements[f];
        if (name == "arguments" && node.Elements != null)
        {
          RootObject arguments = new RootObject() { Name = name, Parent = this, Kind = ObjectKind.Collection, Type = "arguments", FirstItemRef = node.Item, LastItemRef = node.Item };
          Members.Add(arguments);
          for (int a = 0; a < node.Elements.Length; a++)
            AddHubValue(arguments, string.Concat("[", a.ToString(), "]"), node.Elements[a], indexed);
          continue;
        }
        PrimitiveObject field = AddHubValue(this, name, node, indexed && IsHubValueField(name));
        if (name == "type")
          field.Type = message.MessageTypeName;
        else if (name == "resultKind")
          field.Type = HubCapture.ResultKindName(resultKind);
      }
    }

    /// <summary>
    /// A field or argument of a SignalR message: a primitive as a member, anything else reconstructed as a payload of its own (its references point into it).
    /// </summary>
    /// <param name="indexed">The value is nil, [schema, body] or [nil, value without a schema] ("lsmsgpack-indexed")</param>
    private static PrimitiveObject AddHubValue(ComplexObject parent, string name, Node node, bool indexed)
    {
      if (indexed && node.Kind == ValueKind.Array && node.Elements != null && node.Elements.Length == 2)
      {
        RootObject wrapped = new RootObject() { Name = name, Parent = parent, FirstItemRef = node.Item, LastItemRef = node.Item };
        parent.Members.Add(wrapped);
        Node first = node.Elements[0];
        SchemaInfo schema = first.Kind == ValueKind.Nil ? null : ReadInlineSchema(first);
        if (schema is null)
        {
          FillHubValue(wrapped, node.Elements[1]);
          return wrapped;
        }
        schema.ItemRef = first.Item;
        wrapped.Schema = schema;
        wrapped.FillBody(node.Elements[1]);
        return wrapped;
      }
      if (!node.IsContainer && node.Kind != ValueKind.Reference)
      {
        PrimitiveObject value = new PrimitiveObject() { Name = name, Parent = parent, FirstItemRef = node.Item, LastItemRef = node.Item, ValueKind = node.Kind, Value = node.Value };
        if (node.Error != null)
          value.AddError(node.Error);
        parent.Members.Add(value);
        return value;
      }
      RootObject root = new RootObject() { Name = name, Parent = parent };
      parent.Members.Add(root);
      FillHubValue(root, node);
      return root;
    }

    private static void FillHubValue(RootObject target, Node node)
    {
      try
      {
        Node.LinkReferences(node);
        new ObjectReconstructor(null).FillRoot(target, node);
      }
      catch (Exception ex)
      {
        target.AddError(ex.Message);
      }
    }

    /// <summary>
    /// The fields of a message that hold a value of the hub method (the elements of "arguments" too).
    /// </summary>
    private static bool IsHubValueField(string name)
    {
      return name == "item" || name == "result";
    }

    /// <summary>
    /// Whether the values of the messages are those of "lsmsgpack-indexed" (LsMsgPack.SignalR): each one nil, [schema, body] or [nil, value without a schema].
    /// The client's handshake names the protocol; without it (a capture of the messages only) every value must have that shape and at least one must hold a schema,
    /// which plain values almost never do (a list of two whose first element is nil or a map of names to arrays of names).
    /// </summary>
    internal static bool HubValuesIndexed(List<MsgPackItem> items)
    {
      bool schemas = false;
      for (int t = 0; t < items.Count; t++)
      {
        if (items[t] is MpHubHandshake handshake)
        {
          string protocol = HandshakeProtocol(handshake.Value as string);
          if (protocol != null) // the client's (the server's {} names none)
            return string.Equals(protocol, IndexedHubProtocol, StringComparison.Ordinal);
          continue;
        }
        if (!(items[t] is MpHubMessage message))
          continue;
        Node[] fields = Node.FromItem(message).Elements; // nodes: without KEEPTRACK an array holds values, not items
        if (fields is null)
          return false;
        for (int f = 0; f < fields.Length; f++)
        {
          string name = HubCapture.FieldName(message.MessageType, f, message.ResultKind);
          if (name == "arguments")
          {
            if (fields[f].Kind != ValueKind.Array || fields[f].Elements is null)
              return false;
            Node[] values = fields[f].Elements;
            for (int a = 0; a < values.Length; a++)
              if (!FitsIndexedHubValue(values[a], ref schemas))
                return false;
          }
          else if (IsHubValueField(name) && !FitsIndexedHubValue(fields[f], ref schemas))
            return false;
        }
      }
      return schemas;
    }

    /// <summary>
    /// Whether the data is a capture of a SignalR connection (<see cref="MsgPackSettings.ReadHubMessages"/>) with the values of "lsmsgpack-indexed": each one nil, [schema, body] or [nil, value].
    /// </summary>
    /// <param name="item">An <see cref="MpRoot"/> or a single item, as for <see cref="Reconstruct(MsgPackItem, SchemaStore)"/></param>
    public static bool IsIndexedHubCapture(MsgPackItem item)
    {
      if (item is MpHubMessage)
        return HubValuesIndexed(new List<MsgPackItem>() { item });
      return item is MpRoot root && HubValuesIndexed(new List<MsgPackItem>(root));
    }

    /// <summary>
    /// The name of the protocol of LsMsgPack.SignalR with the indexed schema in each value.
    /// </summary>
    internal const string IndexedHubProtocol = "lsmsgpack-indexed";

    private static bool FitsIndexedHubValue(Node value, ref bool schemas)
    {
      if (value.Kind == ValueKind.Nil)
        return true;
      if (value.Kind != ValueKind.Array || value.Elements is null || value.Elements.Length != 2)
        return false;
      Node first = value.Elements[0];
      if (first.Kind == ValueKind.Nil)
        return true;
      if (first.Kind != ValueKind.Map || ReadInlineSchema(first) is null)
        return false;
      schemas = true;
      return true;
    }

    /// <summary>
    /// The "protocol" of a handshake's JSON (<c>{"protocol":"messagepack","version":1}</c>), null when it names none.
    /// </summary>
    internal static string HandshakeProtocol(string json)
    {
      if (json is null)
        return null;
      int key = json.IndexOf("\"protocol\"", StringComparison.Ordinal);
      if (key < 0)
        return null;
      int colon = json.IndexOf(':', key + 10);
      int open = colon < 0 ? -1 : json.IndexOf('"', colon + 1);
      int close = open < 0 ? -1 : json.IndexOf('"', open + 1);
      return close < 0 ? null : json.Substring(open + 1, close - open - 1);
    }

    /// <summary>
    /// A DateTimeOffset (<c>[timestamp, offset]</c>) has the shape of an object of a type with two properties. Taken for one, the type seems to hold
    /// a timestamp and an integer, so its real objects later do not fit it (<see cref="ObjectReconstructor.Unexplained"/>). The places where that
    /// happened are tried as values, again with the places the next attempt suspects (the next DateTimeOffset is then taken for that type):
    /// the result with the fewest unexplained values is kept.
    /// </summary>
    private void RetryWithValues(Node body, List<SchemaType> types, ObjectReconstructor first)
    {
      const int maxAttempts = 8;
      bool[] used = new bool[types.Count];
      for (int t = 0; t < types.Count; t++)
        used[t] = types[t].IsUsed;

      int fewest = first.Unexplained;
      HashSet<string> best = null;
      HashSet<string> forced = new HashSet<string>(first.Suspects, StringComparer.Ordinal);
      for (int attempt = 0; attempt < maxAttempts; attempt++)
      {
        for (int t = 0; t < types.Count; t++)
          types[t].IsUsed = false;
        ObjectReconstructor retry = new ObjectReconstructor(types, forced);
        retry.FillRoot(new RootObject(), body, Schema?.RootType);
        if (retry.Unexplained < fewest)
        {
          fewest = retry.Unexplained;
          best = new HashSet<string>(forced, StringComparer.Ordinal);
        }
        int count = forced.Count;
        forced.UnionWith(retry.Suspects);
        if (fewest == 0 || forced.Count == count)
          break;
      }

      if (best is null)
      {
        for (int t = 0; t < types.Count; t++)
          types[t].IsUsed = used[t];
        return;
      }

      // Again into this object (the values refer to their parents)
      for (int t = 0; t < types.Count; t++)
        types[t].IsUsed = false;
      Members.Clear();
      Kind = ObjectKind.Object;
      Type = null;
      TypeIsGuess = false;
      TypeIdItemRef = null;
      SchemaType = null;
      ValueKind = ValueKind.Nil;
      Value = null;
      new ObjectReconstructor(types, best).FillRoot(this, body, Schema?.RootType);
    }

    /// <summary>
    /// The items holding a schema (or a reference to one) in the data, e.g. to show them differently. The same as <see cref="Reconstruct(MsgPackItem, SchemaStore)"/> recognizes them, without reconstructing the objects.
    /// </summary>
    /// <param name="item">An <see cref="MpRoot"/> or a single item (which is never a schema)</param>
    public static List<MsgPackItem> FindSchemaItems(MsgPackItem item)
    {
      List<MsgPackItem> found = new List<MsgPackItem>();
      if (!(item is MpRoot root))
        return found;

      List<Payload> payloads = SplitPayloads(new List<MsgPackItem>(root), null, null);
      for (int t = 0; t < payloads.Count; t++)
      {
        if (payloads[t].SchemaItem != null)
          found.Add(payloads[t].SchemaItem);
        else if (payloads[t].IndexedHubValues)
          AddHubSchemaItems((MpHubMessage)payloads[t].Body, found);
      }
      return found;
    }

    /// <summary>
    /// The schemas of the values of an "lsmsgpack-indexed" message (<see cref="HubValuesIndexed"/> found every value fits): the maps in front of the bodies.
    /// Without KEEPTRACK the values in a message have no items of their own (none found).
    /// </summary>
    private static void AddHubSchemaItems(MpHubMessage message, List<MsgPackItem> found)
    {
      Node[] fields = Node.FromItem(message).Elements;
      if (fields is null)
        return;
      for (int f = 0; f < fields.Length; f++)
      {
        string name = HubCapture.FieldName(message.MessageType, f, message.ResultKind);
        if (name == "arguments" && fields[f].Elements != null)
        {
          Node[] values = fields[f].Elements;
          for (int a = 0; a < values.Length; a++)
            AddHubSchemaItem(values[a], found);
        }
        else if (IsHubValueField(name))
          AddHubSchemaItem(fields[f], found);
      }
    }

    private static void AddHubSchemaItem(Node value, List<MsgPackItem> found)
    {
      if (value.Kind == ValueKind.Array && value.Elements != null && value.Elements.Length == 2 && value.Elements[0].Kind == ValueKind.Map && value.Elements[0].Item != null)
        found.Add(value.Elements[0].Item);
    }

    private sealed class Payload
    {
      public MsgPackItem SchemaItem;
      public MsgPackItem Body;
      public SchemaInfo Schema;
      public string Error;
      /// <summary>A SignalR message of "lsmsgpack-indexed" (<see cref="HubValuesIndexed"/>).</summary>
      public bool IndexedHubValues;
    }

    /// <summary>
    /// A schema (or a reference to it) is followed by its body. Without a schema each item is a payload of its own.
    /// </summary>
    /// <param name="data">The bytes of the items: the schemas written inline are kept in a store of this call, for the references to them later in the data (a stream)</param>
    private static List<Payload> SplitPayloads(List<MsgPackItem> items, SchemaStore schemaStore, TrimmedData data)
    {
      List<Payload> payloads = new List<Payload>();
      SchemaStore inline = null;
      int hubValues = 0; // 0 not decided yet, 1 "lsmsgpack-indexed", -1 plain
      int t = 0;
      while (t < items.Count)
      {
        if (IsStreamPart(items[t]))
        {
          t++;
          continue;
        }
        Payload payload = new Payload() { Body = items[t] };
        if (items[t] is MpHubMessage)
        {
          if (hubValues == 0)
            hubValues = HubValuesIndexed(items) ? 1 : -1;
          payload.IndexedHubValues = hubValues > 0;
          payloads.Add(payload);
          t++;
          continue;
        }
        // Only a map (the schema) or an extension (a reference) can start a payload with a schema
        if (t + 1 < items.Count && (items[t] is MpMap || items[t] is MpExt) && !IsStreamPart(items[t + 1]))
        {
          Node first = Node.FromItem(items[t]);
          SchemaInfo schema = ReadInlineSchema(first);
          if (schema != null && data != null)
            schema.Id = KeepInline(ref inline, items[t], data);
          if (schema is null && inline != null)
            schema = ReadSchemaReference(first, inline, out payload.Error, false);
          if (schema is null || !schema.IsAvailable)
            schema = ReadSchemaReference(first, schemaStore, out payload.Error) ?? schema;

          if (schema != null)
          {
            payload.Schema = schema;
            payload.SchemaItem = items[t];
            schema.ItemRef = items[t];
            payload.Body = items[t + 1];
            t++;
          }
        }
        payloads.Add(payload);
        t++;
      }
      return payloads;
    }

    /// <summary>
    /// Registers the bytes of a schema written inline (its id is computed from them), so a reference to it later in the data finds it.
    /// </summary>
    /// <returns>The id of the schema, null when it was not registered</returns>
    private static string KeepInline(ref SchemaStore inline, MsgPackItem item, TrimmedData data)
    {
#if KEEPTRACK
      if (item.StoredLength <= 0)
        return null;
      if (item.Settings != null && item.Settings.EndianAction != EndianAction.SwapIfCurrentSystemIsLittleEndian)
        return null; // the store keeps schemas in the byte order of the specification
      byte[] bytes = data.Copy(item.StoredOffset, item.StoredLength);
      if (bytes is null)
        return null;
      try
      {
        if (inline is null)
          inline = new SchemaStore();
        return inline.Register(bytes).ToString();
      }
      catch (MsgPackException)
      {
        // not a schema after all (the store reads it more strictly), or the store is full
      }
#endif
      return null;
    }

    /// <summary>
    /// A map of type names, each with an array of property names (see <c>IndexedSchemaTypeResolver.Pack</c>), or version 2 of it (see <see cref="ReadSchema"/>).
    /// </summary>
    /// <returns>Null when the node is not a schema</returns>
    internal static SchemaInfo ReadInlineSchema(Node node)
    {
      SchemaInfo schema = new SchemaInfo() { Source = SchemaSource.Inline, IsAvailable = true };
      return ReadSchema(node, schema) ? schema : null;
    }

    private const string RootKey = "";
    private const string ContentName = "@";

    /// <summary>
    /// Version 1: <c>{ typeName: [propertyName, ...], ... }</c>. Version 2 (<c>SchemaVersion.Described</c>): the properties are <c>[name, type]</c> or <c>[name, type, default]</c>,
    /// an entry that is not an object starts with <c>["@", type]</c>, and the last entry <c>""</c> holds <c>[["@", rootType]]</c>.
    /// </summary>
    /// <returns>False when the node is not a schema (the schema is then incomplete)</returns>
    private static bool ReadSchema(Node node, SchemaInfo schema)
    {
      if (node.Kind != ValueKind.Map)
        return false;

      bool described = false;
      int count = node.Entries.Length;
      if (count > 0 && node.Entries[count - 1].Key.IsString(RootKey))
      {
        Node root = node.Entries[count - 1].Value;
        if (root.Kind != ValueKind.Array || root.Elements.Length != 1 || !IsContentItem(root.Elements[0]))
          return false;
        schema.RootType = ReadDeclaredType(root.Elements[0].Elements[1], 0);
        schema.Version = 2;
        count--;
      }
      else
        schema.Version = 1;

      for (int t = 0; t < count; t++)
      {
        Node name = node.Entries[t].Key;
        Node props = node.Entries[t].Value;
        if (name.Kind != ValueKind.String || props.Kind != ValueKind.Array)
          return false;

        SchemaType type = new SchemaType() { Index = t, Name = (string)name.Value, NameItemRef = name.Item };
        if (!ReadProperties(props, type, ref described))
          return false;
        schema.Types.Add(type);
      }
      return !described || schema.Version == 2;
    }

    /// <param name="described">Set when an item of version 2 is found</param>
    private static bool ReadProperties(Node props, SchemaType type, ref bool described)
    {
      for (int p = 0; p < props.Elements.Length; p++)
      {
        Node prop = props.Elements[p];
        if (prop.Kind == ValueKind.String || prop.Kind == ValueKind.Nil)
        {
          type.Properties.Add((string)prop.Value);
          type.PropertyItemRefs.Add(prop.Item);
          type.PropertyTypes.Add(null);
          type.PropertyDefaults.Add(null);
          continue;
        }

        described = true;
        if (p == 0 && IsContentItem(prop))
        {
          type.ContentType = ReadDeclaredType(prop.Elements[1], 0);
          continue;
        }
        if (prop.Kind != ValueKind.Array || prop.Elements.Length < 2 || prop.Elements.Length > 3 || prop.Elements[0].Kind != ValueKind.String)
          return false;
        type.Properties.Add((string)prop.Elements[0].Value);
        type.PropertyItemRefs.Add(prop.Item);
        type.PropertyTypes.Add(ReadDeclaredType(prop.Elements[1], 0));
        Node value = prop.Elements.Length == 3 ? prop.Elements[2] : null;
        type.PropertyDefaults.Add(value is null ? null : new PrimitiveObject() { Name = (string)prop.Elements[0].Value, ValueKind = value.Kind, Value = value.Value, FirstItemRef = value.Item, LastItemRef = value.Item });
      }
      return true;
    }

    private static bool IsContentItem(Node item)
    {
      return item.Kind == ValueKind.Array && item.Elements.Length == 2 && item.Elements[0].IsString(ContentName);
    }

    /// <summary>
    /// A type of version 2: a kind, an index of a type of the schema, or <c>[composite, items...]</c>. Anything else is read as "any" (a later version may have more).
    /// </summary>
    private static DeclaredType ReadDeclaredType(Node node, int depth)
    {
      long index;
      if (node.Kind == ValueKind.String)
        return new DeclaredType() { Kind = (string)node.Value };
      if (node.TryGetIndex(out index) && index >= 0 && index <= int.MaxValue)
        return new DeclaredType() { Kind = DeclaredType.EntryKind, Entry = (int)index };
      if (node.Kind != ValueKind.Array || node.Elements.Length == 0 || node.Elements[0].Kind != ValueKind.String || depth > 32)
        return new DeclaredType() { Kind = "any" };

      DeclaredType type = new DeclaredType() { Kind = (string)node.Elements[0].Value };
      if (type.Kind == "struct" && node.Elements.Length == 2)
      {
        Node of = node.Elements[1];
        if (of.TryGetIndex(out index) && index >= 0 && index <= int.MaxValue)
          type.Entry = (int)index;
        else if (of.Kind == ValueKind.Array)
        {
          SchemaType inline = new SchemaType() { Index = -1 };
          bool described = false;
          if (ReadProperties(of, inline, ref described))
            type.InlineStruct = inline;
        }
        return type;
      }
      for (int t = 1; t < node.Elements.Length; t++)
        type.Items.Add(ReadDeclaredType(node.Elements[t], depth + 1));
      return type;
    }

    /// <summary>
    /// An extension of type <see cref="SchemaStore.ReferenceExtensionType"/> holding the <see cref="SchemaId"/>.
    /// </summary>
    /// <returns>Null when the node is not a schema reference</returns>
    private static SchemaInfo ReadSchemaReference(Node node, SchemaStore schemaStore, out string error)
    {
      return ReadSchemaReference(node, schemaStore, out error, true);
    }

    /// <param name="always">Return the reference also when the store does not have it (false: null then)</param>
    private static SchemaInfo ReadSchemaReference(Node node, SchemaStore schemaStore, out string error, bool always)
    {
      SchemaInfo schema = ReadReference(node, schemaStore, out error);
      return always || (schema != null && schema.IsAvailable) ? schema : null;
    }

    private static SchemaInfo ReadReference(Node node, SchemaStore schemaStore, out string error)
    {
      error = null;
      MpExt ext = node.Value as MpExt;
      if (node.Kind != ValueKind.Extension || ext is null || ext.TypeSpecifier != SchemaStore.ReferenceExtensionType)
        return null;

      byte[] idBytes = (byte[])ext.Value;
      if (idBytes.Length != SchemaId.Length)
        return null;

      SchemaId id = new SchemaId(idBytes);
      SchemaInfo schema = new SchemaInfo() { Source = SchemaSource.Reference, Id = id.ToString() };
      if (schemaStore is null)
        return schema;

      try
      {
        byte[] bytes = schemaStore.GetSchema(id) ?? schemaStore.SchemaProvider?.Invoke(id);
        if (bytes is null)
          return schema;

        // The store keeps the schema in the byte order of the specification, the default of the settings
        MsgPackItem unpacked = MsgPackItem.Unpack(new MemoryStream(bytes), new MsgPackSettings() { EndianAction = EndianAction.SwapIfCurrentSystemIsLittleEndian });
        SchemaInfo read = new SchemaInfo() { Source = SchemaSource.Reference, Id = schema.Id };
        if (!ReadSchema(Node.FromItem(unpacked), read))
        {
          error = $"The schema store returned something else than a schema for {id}.";
          return schema;
        }
        read.IsAvailable = true;
        return read;
      }
      catch (Exception ex)
      {
        error = $"Reading schema {id} from the store failed: {ex.Message}";
      }
      return schema;
    }
  }
}
