using LsMsgPack;
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

      List<Payload> payloads = SplitPayloads(items, schemaStore);
      if (payloads.Count == 1)
      {
        Reconstruct(payloads[0]);
        return;
      }

      Kind = ObjectKind.Sequence;
      for (int t = 0; t < payloads.Count; t++)
      {
        RootObject payload = new RootObject() { Name = string.Concat("[", t.ToString(), "]"), Parent = this };
        Members.Add(payload);
        payload.Reconstruct(payloads[t]);
      }
    }

    private void Reconstruct(Payload payload)
    {
      Schema = payload.Schema;
      FirstItemRef = payload.SchemaItem ?? payload.Body;
      LastItemRef = payload.Body;
      if (payload.Error != null)
        AddError(payload.Error);

      try
      {
        List<SchemaType> types = Schema != null && Schema.IsAvailable ? Schema.Types : null;
        Node body = Node.FromItem(payload.Body);
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

      List<Payload> payloads = SplitPayloads(new List<MsgPackItem>(root), null);
      for (int t = 0; t < payloads.Count; t++)
        if (payloads[t].SchemaItem != null)
          found.Add(payloads[t].SchemaItem);
      return found;
    }

    private sealed class Payload
    {
      public MsgPackItem SchemaItem;
      public MsgPackItem Body;
      public SchemaInfo Schema;
      public string Error;
    }

    /// <summary>
    /// A schema (or a reference to it) is followed by its body. Without a schema each item is a payload of its own.
    /// </summary>
    private static List<Payload> SplitPayloads(List<MsgPackItem> items, SchemaStore schemaStore)
    {
      List<Payload> payloads = new List<Payload>();
      int t = 0;
      while (t < items.Count)
      {
        Payload payload = new Payload() { Body = items[t] };
        // Only a map (the schema) or an extension (a reference) can start a payload with a schema
        if (t + 1 < items.Count && (items[t] is MpMap || items[t] is MpExt))
        {
          Node first = Node.FromItem(items[t]);
          SchemaInfo schema = ReadInlineSchema(first);
          if (schema is null)
            schema = ReadSchemaReference(first, schemaStore, out payload.Error);

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
