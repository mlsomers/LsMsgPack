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
        ObjectReconstructor reconstructor = new ObjectReconstructor(Schema != null && Schema.IsAvailable ? Schema.Types : null);
        reconstructor.FillRoot(this, Node.FromItem(payload.Body));

        if (Schema != null && Schema.Source == SchemaSource.Inline)
        {
          // An inline schema has exactly the types of this payload, so every type should have been found
          List<string> unused = new List<string>();
          foreach (SchemaType type in Schema.Types)
            if (!type.IsUsed)
              unused.Add(type.Name);
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
    /// The items holding a schema (or a reference to one) in the data, e.g. to show them differently. The same as <see cref="Reconstruct(MsgPackItem, SchemaStore)"/> recognizes them, without reconstructing the objects.
    /// </summary>
    /// <param name="item">An <see cref="MpRoot"/> or a single item (which is never a schema)</param>
    public static List<MsgPackItem> FindSchemaItems(MsgPackItem item)
    {
      List<MsgPackItem> found = new List<MsgPackItem>();
      if (!(item is MpRoot root))
        return found;

      foreach (Payload payload in SplitPayloads(new List<MsgPackItem>(root), null))
        if (payload.SchemaItem != null)
          found.Add(payload.SchemaItem);
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
    /// A map of type names, each with an array of property names (see <c>IndexedSchemaTypeResolver.Pack</c>).
    /// </summary>
    /// <returns>Null when the node is not a schema</returns>
    internal static SchemaInfo ReadInlineSchema(Node node)
    {
      List<SchemaType> types = ReadSchemaTypes(node);
      if (types is null)
        return null;

      SchemaInfo schema = new SchemaInfo() { Source = SchemaSource.Inline, IsAvailable = true };
      schema.Types.AddRange(types);
      return schema;
    }

    private static List<SchemaType> ReadSchemaTypes(Node node)
    {
      if (node.Kind != ValueKind.Map)
        return null;

      List<SchemaType> types = new List<SchemaType>(node.Entries.Length);
      for (int t = 0; t < node.Entries.Length; t++)
      {
        Node name = node.Entries[t].Key;
        Node props = node.Entries[t].Value;
        if (name.Kind != ValueKind.String || props.Kind != ValueKind.Array)
          return null;

        SchemaType type = new SchemaType() { Index = t, Name = (string)name.Value };
        for (int p = 0; p < props.Elements.Length; p++)
        {
          Node prop = props.Elements[p];
          if (prop.Kind != ValueKind.String && prop.Kind != ValueKind.Nil)
            return null;
          type.Properties.Add((string)prop.Value);
        }
        types.Add(type);
      }
      return types;
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
        List<SchemaType> types = ReadSchemaTypes(Node.FromItem(unpacked));
        if (types is null)
        {
          error = $"The schema store returned something else than a schema for {id}.";
          return schema;
        }
        schema.Types.AddRange(types);
        schema.IsAvailable = true;
      }
      catch (Exception ex)
      {
        error = $"Reading schema {id} from the store failed: {ex.Message}";
      }
      return schema;
    }
  }
}
