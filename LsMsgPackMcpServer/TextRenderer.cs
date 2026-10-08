using LsMsgPack;
using ObjectDebugger;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace LsMsgPackMcp
{
  public enum DecodeView
  {
    /// <summary>The objects, and the errors with their offsets</summary>
    Objects,
    /// <summary>The MsgPack items with their offsets and encodings</summary>
    Items,
    Both
  }

  public enum IssueLevel
  {
    /// <summary>Only errors (data that could not be read)</summary>
    Errors,
    /// <summary>Errors, warnings (e.g. a larger encoding than needed) and comments</summary>
    All,
    None
  }

  public class RenderOptions
  {
    public DecodeView View { get; set; } = DecodeView.Objects;

    /// <summary>
    /// Adds the offset of every value to the objects view.
    /// </summary>
    public bool Offsets { get; set; }

    /// <summary>
    /// The most values (objects view) or items (items view) written, the rest is summarized. Keeps the answer within an agent's context.
    /// </summary>
    public int MaxNodes { get; set; } = 1000;

    /// <summary>
    /// Longer strings are cut (their length is shown).
    /// </summary>
    public int MaxString { get; set; } = 200;

    /// <summary>
    /// The bytes of bin and ext values shown as hex.
    /// </summary>
    public int MaxBytes { get; set; } = 32;

    /// <summary>
    /// Only this part of the objects (e.g. "Lines[2]"), null or empty for all.
    /// </summary>
    public string Path { get; set; }

    /// <summary>
    /// Items view: only the items that hold bytes in this range.
    /// </summary>
    public long From { get; set; }
    public long To { get; set; } = long.MaxValue;

    public IssueLevel Issues { get; set; } = IssueLevel.Errors;
  }

  /// <summary>
  /// Writes a decoded payload as text for an AI (or a person in a terminal): the objects as JSON with comments for what JSON does not tell
  /// (types from the schema or type ids, timestamps, bin, extensions, errors), or the MsgPack items with their offsets and encodings.
  /// </summary>
  public static class TextRenderer
  {
    private static readonly JsonSerializerOptions JsonText = new JsonSerializerOptions() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Inline arrays and objects (all primitive, without comments) up to this length.
    /// </summary>
    private const int InlineWidth = 100;

    /// <summary>
    /// The levels of nesting written (objects) or indented (items) below the start.
    /// </summary>
    private const int MaxLevels = 32;

    public static string Render(PayloadDocument doc, RenderOptions options)
    {
      StringBuilder sb = new StringBuilder();
      WriteSummary(sb, doc);
      if (doc.Root is null)
        return sb.ToString();

      if (options.View != DecodeView.Items)
      {
        sb.Append('\n');
        WriteObjects(sb, doc, options);
      }
      if (options.View != DecodeView.Objects)
      {
        sb.Append('\n');
        WriteItems(sb, doc, options);
      }
      WriteIssues(sb, doc, options.Issues);
      return sb.ToString();
    }

    #region Summary

    private static void WriteSummary(StringBuilder sb, PayloadDocument doc)
    {
      sb.Append("MsgPack ");
      if (!string.IsNullOrEmpty(doc.Id))
        sb.Append('"').Append(doc.Id).Append("\" ");
      sb.Append(doc.Data.Length.ToString(CultureInfo.InvariantCulture)).Append(" bytes");
      if (!string.IsNullOrEmpty(doc.Source))
        sb.Append(" from ").Append(doc.Source);
      sb.Append(".").Append('\n');
      foreach (string note in doc.Notes)
        sb.Append("Note: ").Append(note).Append('\n');
      if (doc.Options.Endian != EndianAction.SwapIfCurrentSystemIsLittleEndian)
        sb.Append("Read with endian ").Append(doc.Options.Endian.ToString()).Append(" (not the byte order of the specification).").Append('\n');

      if (doc.Data.Length == 0)
      {
        sb.Append("There is no data.").Append('\n');
        return;
      }
      if (doc.ReadError != null)
      {
        sb.Append("The data could not be read: ").Append(doc.ReadError).Append('\n');
        sb.Append("Read it with continueOnError to see what can be read before and after the error.").Append('\n');
        return;
      }
      if (doc.Root is null)
        return;

      sb.Append("Structure: ").Append(DescribeStructure(doc)).Append('\n');
      long end = doc.Root.Offset + doc.Root.Length;
      if (end < doc.Data.Length)
        sb.Append("Bytes ").Append(Hex(end)).Append(" to ").Append(Hex(doc.Data.Length - 1)).Append(" are not part of any item.").Append('\n');

      int errors = 0;
      int warnings = 0;
      foreach (Issue issue in doc.Issues)
      {
        if (issue.IsError)
          errors++;
        else
          warnings++;
      }
      sb.Append(errors == 0 ? "No errors" : string.Concat(errors.ToString(CultureInfo.InvariantCulture), errors == 1 ? " error" : " errors"));
      sb.Append(", ").Append(warnings.ToString(CultureInfo.InvariantCulture)).Append(warnings == 1 ? " warning or comment." : " warnings or comments.").Append('\n');

      RootObject objects = doc.Objects;
      if (objects is null)
        return;
      foreach (SchemaInfo schema in Schemas(objects))
        WriteSchema(sb, schema);
      foreach (PrimitiveObject obj in PayloadDocument.Walk(objects))
        if (obj is RootObject root)
          foreach (string warning in root.Warnings)
            sb.Append("Warning: ").Append(warning).Append('\n');
    }

    private static IEnumerable<SchemaInfo> Schemas(RootObject objects)
    {
      if (objects.Schema != null)
        yield return objects.Schema;
      if (objects.Kind == ObjectKind.Sequence)
        foreach (PrimitiveObject member in objects.Members)
          if (member is RootObject payload && payload.Schema != null)
            yield return payload.Schema;
    }

    private static string DescribeStructure(PayloadDocument doc)
    {
      RootObject objects = doc.Objects;
      List<string> parts = new List<string>();
      if (objects != null && objects.Kind == ObjectKind.Sequence)
      {
        parts.Add(string.Concat(objects.Members.Count.ToString(CultureInfo.InvariantCulture), " payloads that follow each other"));
      }
      else if (objects != null && objects.Schema != null)
      {
        parts.Add(objects.Schema.Source == SchemaSource.Inline
          ? string.Concat("indexed schema (inline, ", objects.Schema.Types.Count.ToString(CultureInfo.InvariantCulture), " types) followed by the body")
          : string.Concat("reference to schema ", objects.Schema.Id, objects.Schema.IsAvailable ? " (found)" : " (not available)", " followed by the body"));
      }
      else if (doc.Root.Item is MpRoot root)
      {
        parts.Add(string.Concat(root.Count.ToString(CultureInfo.InvariantCulture), " items that follow each other"));
      }
      else if (doc.Root.Item is MpError error)
      {
        // An item that could not be read is replaced by an MpError (its TypeId is "never used"), the item is its PartialItem
        if (error.PartialItem is null)
          parts.Add("an error");
        else
          parts.Add(string.Concat("a single ", TypeName(error.PartialItem), error.IsInNestedItem ? " holding an error" : " that could not be read"));
      }
      else
      {
        parts.Add(string.Concat("a single ", TypeName(doc.Root.Item)));
      }
      if (objects != null && objects.Kind != ObjectKind.Sequence && objects.Schema is null && LooksLikeArrayLayout(objects))
        parts.Add("objects may be written as arrays of their values (ObjectLayout.Array): without a schema the property names are not in the data");
      return string.Join("; ", parts);
    }

    private static bool LooksLikeArrayLayout(RootObject objects)
    {
      return objects.Kind == ObjectKind.Collection && objects.Members.Exists(m => m is ComplexObject);
    }

    private static void WriteSchema(StringBuilder sb, SchemaInfo schema)
    {
      if (schema.Source == SchemaSource.Reference && !schema.IsAvailable)
      {
        sb.Append("Schema ").Append(schema.Id).Append(" is not available: property ids and type ids show as their index in it.").Append('\n');
        sb.Append("Pass the schema (or an export of the SchemaStore of the program) to resolve the names.").Append('\n');
        return;
      }
      if (schema.Types.Count == 0)
      {
        sb.Append("Schema (inline): empty, the body has no objects.").Append('\n');
        return;
      }
      sb.Append(schema.Source == SchemaSource.Inline ? "Schema (inline)" : string.Concat("Schema ", schema.Id))
        .Append(": type ids and property ids in the body are indexes into these lists.").Append('\n');
      foreach (SchemaType type in schema.Types)
      {
        sb.Append("  #").Append(type.Index.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(type.Name).Append(": ")
          .Append(string.Join(", ", type.Properties.ConvertAll(p => p ?? "(null)")));
        if (!type.IsUsed)
          sb.Append(" (no value matched)");
        sb.Append('\n');
      }
    }

    #endregion

    #region Objects

    private sealed class Budget
    {
      public int Left;

      /// <summary>
      /// Values that start after it were read after an error (-1: none).
      /// </summary>
      public long FirstError = -1;
    }

    private static void WriteObjects(StringBuilder sb, PayloadDocument doc, RenderOptions options)
    {
      PrimitiveObject start = doc.Objects;
      if (!string.IsNullOrWhiteSpace(options.Path))
      {
        start = doc.FindObject(options.Path);
        if (start is null)
        {
          sb.Append("No value at path \"").Append(options.Path).Append("\". Paths look like Lines[2].Product (as the objects view shows them).").Append('\n');
          return;
        }
      }
      if (start is null)
        return;

      sb.Append("Objects");
      if (start != doc.Objects)
        sb.Append(" at ").Append(start.Path);
      sb.Append(" (JSON with // comments: a type name comes from a type id, a name with ~ is inferred from the schema; dictionary keys that are not strings are not quoted):").Append('\n');

      Budget budget = new Budget() { Left = Math.Max(1, options.MaxNodes), FirstError = doc.FirstErrorOffset };
      if (budget.FirstError >= 0)
        sb.Append("The values after the first error (at ").Append(Hex(budget.FirstError)).Append(") were read on at the next byte: they may be wrong (marked \"after the error\").").Append('\n');
      string comment;
      string text = Value(start, options, 0, budget, false, out comment);
      sb.Append(text);
      AppendComment(sb, comment);
      sb.Append('\n');
    }

    /// <summary>
    /// The value as JSON-like text (several lines for objects and collections), and the comment for its first line.
    /// </summary>
    private static string Value(PrimitiveObject obj, RenderOptions options, int indent, Budget budget, bool parentGuessed, out string comment)
    {
      budget.Left--;
      bool guessed = parentGuessed || budget.FirstError >= 0 && !(obj.LastItemRef is null) && !(obj.LastItemRef is MpError) && obj.LastItemRef.StoredOffset > budget.FirstError;
      List<string> notes = new List<string>();
      string text;
      ComplexObject complex = obj as ComplexObject;
      if (complex != null && complex.Kind != ObjectKind.Value)
      {
        text = Complex(complex, options, indent, budget, guessed, notes);
      }
      else if (complex != null)
      {
        // A root holding a single value
        text = Primitive(complex.ValueKind, complex.Value, options, notes);
        if (complex.Type != null)
          notes.Insert(0, complex.Type);
      }
      else
      {
        text = Primitive(obj.ValueKind, obj.Value, options, notes);
        if (obj.Type != null)
          notes.Insert(0, obj.TypeIsGuess ? obj.Type + "~" : obj.Type);
      }

      if (obj.Error != null)
        notes.Add(string.Concat("ERROR: ", CleanError(obj.Error)));
      else if (guessed && !parentGuessed)
        notes.Add("after the error");
      if (options.Offsets && !(obj.LastItemRef is null))
        notes.Add(string.Concat("@", Hex(obj.LastItemRef.StoredOffset)));
      comment = string.Join(", ", notes);

      // The comment of an object or collection on several lines goes on its first line
      int newline = text.IndexOf('\n');
      if (newline >= 0 && comment.Length > 0)
      {
        text = string.Concat(text.Substring(0, newline).TrimEnd(), " // ", comment, "\n", text.Substring(newline + 1));
        comment = string.Empty;
      }
      return text;
    }

    private static string Complex(ComplexObject obj, RenderOptions options, int indent, Budget budget, bool guessed, List<string> notes)
    {
      bool isArray = obj.Kind == ObjectKind.Collection || obj.Kind == ObjectKind.Sequence;
      string count = obj.Members.Count.ToString(CultureInfo.InvariantCulture);
      switch (obj.Kind)
      {
        case ObjectKind.Collection:
          notes.Add(obj.Type != null ? string.Concat(obj.Type, ", ", count, " items") : string.Concat(count, " items"));
          break;
        case ObjectKind.Dictionary:
          notes.Add(string.Concat(obj.Type ?? "dictionary", ", ", count, obj.Members.Count == 1 ? " entry" : " entries"));
          break;
        case ObjectKind.Sequence:
          notes.Add(string.Concat(count, " payloads"));
          break;
        case ObjectKind.Entry:
          notes.Add("entry with a key that is not a primitive value");
          break;
        default:
          if (obj.Type != null)
            notes.Add(obj.TypeIsGuess ? obj.Type + "~" : obj.Type);
          break;
      }

      if (obj.Members.Count == 0)
        return isArray ? "[]" : "{}";

      // Deeper levels are asked for by their path: their indentation alone grows with the square of the depth (130 KB for 300 levels)
      if (indent >= MaxLevels)
      {
        notes.Add(string.Concat("nested deeper than ", MaxLevels.ToString(CultureInfo.InvariantCulture), " levels: ask for the path \"", obj.Path, "\""));
        return isArray ? "[...]" : "{...}";
      }

      // Members as (key, value, comment)
      List<string[]> lines = new List<string[]>(obj.Members.Count);
      int shown = 0;
      bool simple = true;
      foreach (PrimitiveObject member in obj.Members)
      {
        if (budget.Left <= 0)
          break;
        shown++;
        string memberComment;
        string value = Value(member, options, indent + 1, budget, guessed, out memberComment);
        if (member is ComplexObject c && c.Kind != ObjectKind.Value || value.IndexOf('\n') >= 0 || member.Error != null)
          simple = false;
        lines.Add(new string[] { isArray ? null : Key(obj, member), value, memberComment });
      }

      string open = isArray ? "[" : "{";
      string close = isArray ? "]" : "}";
      int hidden = obj.Members.Count - shown;
      if (simple && hidden == 0)
      {
        StringBuilder inline = new StringBuilder(open);
        if (!isArray)
          inline.Append(' ');
        for (int t = 0; t < lines.Count; t++)
        {
          if (t > 0)
            inline.Append(", ");
          if (lines[t][0] != null)
            inline.Append(lines[t][0]).Append(": ");
          inline.Append(lines[t][1]);
        }
        if (!isArray)
          inline.Append(' ');
        inline.Append(close);
        string merged = MergeComments(lines, isArray);
        if (inline.Length + indent * 2 <= InlineWidth && merged.Length <= InlineWidth)
        {
          if (merged.Length > 0)
            notes.Add(merged);
          return inline.ToString();
        }
      }

      StringBuilder sb = new StringBuilder(open);
      string pad = new string(' ', (indent + 1) * 2);
      for (int t = 0; t < lines.Count; t++)
      {
        sb.Append('\n');
        sb.Append(pad);
        if (lines[t][0] != null)
          sb.Append(lines[t][0]).Append(": ");
        sb.Append(lines[t][1]);
        if (t < lines.Count - 1 || hidden > 0)
          sb.Append(',');
        AppendComment(sb, lines[t][2]);
      }
      if (hidden > 0)
      {
        sb.Append('\n');
        sb.Append(pad).Append("// ... ").Append(hidden.ToString(CultureInfo.InvariantCulture)).Append(" more not shown (maxNodes reached): ask for the path ")
          .Append(string.IsNullOrEmpty(obj.Path) ? "of a member" : string.Concat("\"", obj.Path, "\"")).Append(" or a larger maxNodes");
      }
      sb.Append('\n');
      sb.Append(' ', indent * 2).Append(close);
      return sb.ToString();
    }

    /// <summary>
    /// The comments of the members of an inline array or object: "Price: decimal", or "each: timestamp" when all elements have the same.
    /// </summary>
    private static string MergeComments(List<string[]> lines, bool isArray)
    {
      List<string> parts = new List<string>();
      bool allSame = lines.Count > 1;
      for (int t = 0; t < lines.Count; t++)
      {
        if (lines[t][2] != lines[0][2])
          allSame = false;
        if (lines[t][2].Length > 0)
          parts.Add(string.Concat(isArray ? string.Concat("[", t.ToString(CultureInfo.InvariantCulture), "]") : lines[t][0].Trim('"'), ": ", lines[t][2]));
      }
      if (parts.Count == 0)
        return string.Empty;
      if (allSame)
        return string.Concat("each: ", lines[0][2]);
      return string.Join("; ", parts);
    }

    private static string Key(ComplexObject parent, PrimitiveObject member)
    {
      string name = member.Name ?? string.Empty;
      // Keys of dictionaries keep their kind: only string keys are quoted
      if (parent.Kind == ObjectKind.Dictionary && !(member.FirstItemRef is null) && member.FirstItemRef != member.LastItemRef && !(member.FirstItemRef is MpString))
        return name;
      if (parent.Kind == ObjectKind.Sequence || parent.Kind == ObjectKind.Dictionary && member is ComplexObject e && e.Kind == ObjectKind.Entry)
        return name;
      return Quote(name);
    }

    private static string Primitive(ValueKind kind, object value, RenderOptions options, List<string> notes)
    {
      switch (kind)
      {
        case ValueKind.Nil:
          return "null";
        case ValueKind.Bool:
          return (bool)value ? "true" : "false";
        case ValueKind.Int:
        case ValueKind.Float:
          return PrimitiveObject.FormatValue(value);
        case ValueKind.Decimal:
          notes.Add("decimal");
          return PrimitiveObject.FormatValue(value);
        case ValueKind.String:
          return QuoteLimited((string)value, options.MaxString, notes);
        case ValueKind.Timestamp:
          notes.Add("timestamp");
          return value is DateTime time ? Quote(time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture)) : Quote(PrimitiveObject.FormatValue(value));
        case ValueKind.Bin:
          {
            byte[] bytes = (byte[])value;
            string note = string.Concat("bin, ", bytes.Length.ToString(CultureInfo.InvariantCulture), " bytes");
            if (bytes.Length == 16)
              note = string.Concat(note, ", as Guid ", new Guid(bytes).ToString());
            notes.Add(note);
            return Quote(HexString(bytes, options.MaxBytes));
          }
        case ValueKind.Extension:
          {
            MpExt ext = value as MpExt;
            byte[] bytes = ext?.Value as byte[] ?? new byte[0];
            notes.Add(string.Concat("ext type ", ext?.TypeSpecifier.ToString(CultureInfo.InvariantCulture), " without a registered type, ", bytes.Length.ToString(CultureInfo.InvariantCulture), " bytes"));
            return Quote(HexString(bytes, options.MaxBytes));
          }
        case ValueKind.Other:
          notes.Add(string.Concat("custom extension ", value?.GetType().Name));
          return Quote(PrimitiveObject.FormatValue(value));
        case ValueKind.Error:
          return "null";
        default:
          return Quote(PrimitiveObject.FormatValue(value));
      }
    }

    #endregion

    #region Items

    private static void WriteItems(StringBuilder sb, PayloadDocument doc, RenderOptions options)
    {
      int width = Math.Max(4, (doc.Data.Length - 1).ToString("X", CultureInfo.InvariantCulture).Length);
      sb.Append("Items (offset, then the MsgPack encoding and value, nested items indented; map entries are a key followed by its value");
      if (options.From > 0 || options.To < long.MaxValue)
        sb.Append("; only the items holding bytes ").Append(Hex(options.From)).Append(" to ").Append(options.To == long.MaxValue ? "the end" : Hex(options.To));
      sb.Append("):").Append('\n');

      int budget = Math.Max(1, options.MaxNodes);
      int skipped = 0;
      foreach (ItemNode node in doc.Items)
      {
        long start = node.Offset;
        long end = start + Math.Max(1, node.Length) - 1;
        if (end < options.From || start > options.To)
          continue;
        if (budget <= 0)
        {
          skipped++;
          continue;
        }
        budget--;
        sb.Append("0x").Append(start < 0 ? new string('?', width) : start.ToString("X" + width.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)).Append(' ');
        sb.Append(' ', Math.Min(node.Depth, MaxLevels) * 2);
        if (node.Depth > MaxLevels)
          sb.Append("(level ").Append(node.Depth.ToString(CultureInfo.InvariantCulture)).Append(") ");
        if (node.Role != null && node.Depth > 0)
          sb.Append(node.Role).Append(' ');
        sb.Append(DescribeItem(node.Item, options));
        List<string> notes = new List<string>();
        if (node.IsSchema && (node.Parent is null || !node.Parent.IsSchema))
          notes.Add("indexed schema");
        if (!(node.Item is null) && node.Item.IsBestGuess)
          notes.Add("read after an error, a best guess");
        if (node.Depth == 0 || node.Parent != null && node.Parent.Depth == 0 && node.Parent.Item is MpRoot)
        {
          PrimitiveObject obj = doc.GetObject(node);
          if (obj != null && !string.IsNullOrEmpty(obj.Path))
            notes.Add(obj.Path);
        }
        else if (node.Role != "key" && node.Depth <= MaxLevels) // deeper paths are long, msgpack_explain_offset tells them
        {
          PrimitiveObject obj = doc.GetObject(node);
          if (obj != null && (obj.LastItemRef == node.Item) && !string.IsNullOrEmpty(obj.Path))
            notes.Add(obj.Path);
        }
        AppendComment(sb, string.Join(", ", notes));
        sb.Append('\n');
      }
      if (skipped > 0)
        sb.Append("... ").Append(skipped.ToString(CultureInfo.InvariantCulture)).Append(" more items not shown (maxNodes reached): ask for a range (from, to) or a larger maxNodes.").Append('\n');
    }

    /// <summary>
    /// The encoding and the value, e.g. <c>fixstr "abc"</c>, <c>uint 16 1000</c>, <c>fixarray, 3 items</c>.
    /// </summary>
    internal static string DescribeItem(MsgPackItem item, RenderOptions options)
    {
      if (item is null)
        return "(missing)";
      if (item is MpError error)
        return ErrorText(error);
      if (item is MpRoot root)
        return string.Concat("root, ", root.Count.ToString(CultureInfo.InvariantCulture), " items that follow each other");

      string type = TypeName(item);
      if (item is MpArray array)
        return string.Concat(type, ", ", array.PackedValues.Length.ToString(CultureInfo.InvariantCulture), " items");
      if (item is MpMap map)
        return string.Concat(type, ", ", map.PackedValues.Length.ToString(CultureInfo.InvariantCulture), " entries");
      if (item is MpNull)
        return "nil";
      if (item is MpString str)
        return string.Concat(type, " ", QuoteLimited(str.Value as string ?? string.Empty, options.MaxString, null));
      if (item is MpBin bin)
      {
        byte[] bytes = bin.Value as byte[] ?? new byte[0];
        return string.Concat(type, ", ", bytes.Length.ToString(CultureInfo.InvariantCulture), " bytes: ", HexString(bytes, options.MaxBytes, " "));
      }
      if (item is MpExt ext)
      {
        string head = string.Concat(type, ", type ", ext.TypeSpecifier.ToString(CultureInfo.InvariantCulture));
        if (item.GetType() == typeof(MpExt))
        {
          byte[] bytes = ext.Value as byte[] ?? new byte[0];
          if (ext.TypeSpecifier == SchemaStore.ReferenceExtensionType && bytes.Length == SchemaId.Length)
            return string.Concat(head, " (schema reference): ", new SchemaId(bytes).ToString());
          return string.Concat(head, ", ", bytes.Length.ToString(CultureInfo.InvariantCulture), " bytes: ", HexString(bytes, options.MaxBytes, " "));
        }
        object value = item.Value;
        if (value is DateTime time)
          return string.Concat(head, " (timestamp): ", time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
        if (value is decimal)
          return string.Concat(head, " (decimal): ", PrimitiveObject.FormatValue(value));
        return string.Concat(head, " (", item.GetType().Name, "): ", PrimitiveObject.FormatValue(value));
      }
      if (item is MpBool)
        return true.Equals(item.Value) ? "true" : "false";
      return string.Concat(type, " ", PrimitiveObject.FormatValue(item.Value));
    }

    private static string TypeName(MsgPackItem item)
    {
      if (item is null)
        return "nil";
      if (item is MpRoot)
        return "root";
      string name = MsgPackItem.GetOfficialTypeName(item.TypeId);
      return name;
    }

    #endregion

    #region Issues

    private static void WriteIssues(StringBuilder sb, PayloadDocument doc, IssueLevel level)
    {
      if (level == IssueLevel.None || doc.Issues.Count == 0)
        return;

      List<Issue> shown = doc.Issues.FindAll(i => level == IssueLevel.All || i.IsError);
      int others = doc.Issues.Count - shown.Count;
      if (shown.Count > 0)
      {
        sb.Append('\n');
        sb.Append(level == IssueLevel.All ? "Issues:" : "Errors:").Append('\n');
        const int max = 50;
        for (int t = 0; t < shown.Count && t < max; t++)
          sb.Append("  ").Append(DescribeIssue(doc, shown[t])).Append('\n');
        if (shown.Count > max)
          sb.Append("  ... ").Append((shown.Count - max).ToString(CultureInfo.InvariantCulture)).Append(" more.").Append('\n');
      }
      if (others > 0)
      {
        int wasted = 0;
        foreach (Issue issue in doc.Issues)
          if (!issue.IsError)
            wasted += issue.WastedBytes;
        sb.Append('\n');
        sb.Append(others.ToString(CultureInfo.InvariantCulture)).Append(others == 1 ? " warning or comment" : " warnings or comments").Append(" not listed (issues: all lists them)");
        if (wasted > 0)
          sb.Append(", smaller encodings would save ").Append(wasted.ToString(CultureInfo.InvariantCulture)).Append(" bytes");
        sb.Append(".").Append('\n');
      }
    }

    private static string DescribeIssue(PayloadDocument doc, Issue issue)
    {
      StringBuilder sb = new StringBuilder();
      sb.Append(issue.Severity == MsgPackValidation.ValidationSeverity.ReadAbortError ? "Error" : issue.Severity.ToString());
      MsgPackItem item = issue.Node.Item;
      long offset = item is MpError error && error.Value is MsgPackException ex && ex.Offset > 0 ? ex.Offset : issue.Node.Offset;
      sb.Append(" at ").Append(Hex(offset));
      PrimitiveObject obj = doc.GetObject(issue.Node);
      if (obj != null && !string.IsNullOrEmpty(obj.Path))
        sb.Append(" (").Append(obj.Path).Append(')');
      sb.Append(": ").Append(issue.Node.Item is MpError err ? ErrorMessage(err) : CleanError(issue.Message));
      return sb.ToString();
    }

    #endregion

    #region Explain an offset

    /// <summary>
    /// What the byte at the offset is: the items around it, the object it belongs to, the header and content bytes of its item and the bytes around it.
    /// </summary>
    public static string ExplainOffset(PayloadDocument doc, long offset, RenderOptions options)
    {
      StringBuilder sb = new StringBuilder();
      if (offset < 0 || offset >= doc.Data.Length)
      {
        sb.Append("Offset ").Append(Hex(offset)).Append(" is outside the data (").Append(doc.Data.Length.ToString(CultureInfo.InvariantCulture)).Append(" bytes).").Append('\n');
        return sb.ToString();
      }

      byte b = doc.Data[offset];
      sb.Append("Offset ").Append(Hex(offset)).Append(" (").Append(offset.ToString(CultureInfo.InvariantCulture)).Append(") of ").Append(doc.Data.Length.ToString(CultureInfo.InvariantCulture))
        .Append(" bytes: 0x").Append(b.ToString("X2", CultureInfo.InvariantCulture)).Append(", as a type byte: ").Append(DescribeTypeByte(b)).Append(".").Append('\n');

      ItemNode node = doc.FindItemAt(offset);
      if (node is null)
      {
        sb.Append("No item holds this byte (it comes after the data that could be read, or the data could not be read).").Append('\n');
      }
      else
      {
        List<ItemNode> chain = new List<ItemNode>();
        for (ItemNode n = node; n != null; n = n.Parent)
          chain.Insert(0, n);
        sb.Append("Items holding it (outermost first):").Append('\n');
        foreach (ItemNode n in chain)
        {
          sb.Append("  ").Append(Hex(n.Offset)).Append(" (").Append(n.Length.ToString(CultureInfo.InvariantCulture)).Append(" bytes) ");
          if (n.Role != null && n.Depth > 0)
            sb.Append(n.Role).Append(' ');
          sb.Append(DescribeItem(n.Item, options)).Append('\n');
        }

        PrimitiveObject obj = doc.GetObject(node);
        if (obj != null && obj != doc.Objects)
        {
          string comment;
          string value = Value(obj, new RenderOptions() { MaxNodes = 20, MaxString = options.MaxString, MaxBytes = options.MaxBytes }, 0, new Budget() { Left = 20, FirstError = doc.FirstErrorOffset }, false, out comment);
          sb.Append("Object: ").Append(string.IsNullOrEmpty(obj.Path) ? "(root)" : obj.Path).Append(" = ").Append(FirstLine(value));
          AppendComment(sb, comment);
          sb.Append('\n');
        }

        if (!(node.Item is MpRoot) && node.Offset >= 0)
        {
          int header = HeaderLength(node.Item, doc.Data, node.Offset);
          long length = Math.Min(node.Length, 64);
          sb.Append("Bytes of the innermost item");
          if (header > 0)
            sb.Append(" (header | content)");
          sb.Append(": ");
          for (long t = 0; t < length && node.Offset + t < doc.Data.Length; t++)
          {
            if (t > 0)
              sb.Append(t == header ? " | " : " ");
            sb.Append(doc.Data[node.Offset + t].ToString("X2", CultureInfo.InvariantCulture));
          }
          if (node.Length > length)
            sb.Append(" ...");
          sb.Append('\n');
        }
      }

      sb.Append("Bytes around it ([..] marks the offset):").Append('\n');
      long from = Math.Max(0, offset / 16 * 16 - 16);
      long to = Math.Min(doc.Data.Length, offset / 16 * 16 + 32);
      int width = Math.Max(4, (doc.Data.Length - 1).ToString("X", CultureInfo.InvariantCulture).Length);
      for (long line = from; line < to; line += 16)
      {
        sb.Append("  0x").Append(line.ToString("X" + width.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)).Append(':');
        for (long t = line; t < line + 16 && t < to; t++)
        {
          string hex = doc.Data[t].ToString("X2", CultureInfo.InvariantCulture);
          sb.Append(t == offset ? string.Concat("[", hex, "]") : string.Concat(t == offset + 1 ? string.Empty : " ", hex));
        }
        sb.Append('\n');
      }
      return sb.ToString();
    }

    /// <summary>
    /// The bytes before the content: the type byte and the length that follows it (and the type of an extension).
    /// </summary>
    private static int HeaderLength(MsgPackItem item, byte[] data, long offset)
    {
      if (item is null || item is MpError || offset >= data.Length)
        return 0;
      byte b = data[offset];
      if (b <= 0x7f || b >= 0xe0 || b == 0xc0 || b == 0xc2 || b == 0xc3)
        return 0; // the value is in the type byte
      if (b >= 0x80 && b <= 0xbf)
        return 1; // fixmap, fixarray, fixstr
      switch (b)
      {
        case 0xc4: case 0xd9: return 2;
        case 0xc5: case 0xda: case 0xdc: case 0xde: return 3;
        case 0xc6: case 0xdb: case 0xdd: case 0xdf: return 5;
        case 0xc7: return 3;
        case 0xc8: return 4;
        case 0xc9: return 6;
        case 0xd4: case 0xd5: case 0xd6: case 0xd7: case 0xd8: return 2;
        default: return 1; // numbers: the type byte, then the value
      }
    }

    /// <summary>
    /// What a byte means when an item starts there.
    /// </summary>
    internal static string DescribeTypeByte(byte b)
    {
      if (b <= 0x7f) return string.Concat("positive fixint ", b.ToString(CultureInfo.InvariantCulture));
      if (b <= 0x8f) return string.Concat("fixmap of ", (b & 0x0f).ToString(CultureInfo.InvariantCulture), " entries");
      if (b <= 0x9f) return string.Concat("fixarray of ", (b & 0x0f).ToString(CultureInfo.InvariantCulture), " items");
      if (b <= 0xbf) return string.Concat("fixstr of ", (b & 0x1f).ToString(CultureInfo.InvariantCulture), " bytes");
      if (b >= 0xe0) return string.Concat("negative fixint ", ((sbyte)b).ToString(CultureInfo.InvariantCulture));
      switch (b)
      {
        case 0xc0: return "nil";
        case 0xc1: return "never used (invalid)";
        case 0xc2: return "false";
        case 0xc3: return "true";
        case 0xc4: return "bin 8 (1 byte length follows)";
        case 0xc5: return "bin 16 (2 byte length follows)";
        case 0xc6: return "bin 32 (4 byte length follows)";
        case 0xc7: return "ext 8 (1 byte length and the extension type follow)";
        case 0xc8: return "ext 16 (2 byte length and the extension type follow)";
        case 0xc9: return "ext 32 (4 byte length and the extension type follow)";
        case 0xca: return "float 32";
        case 0xcb: return "float 64";
        case 0xcc: return "uint 8";
        case 0xcd: return "uint 16";
        case 0xce: return "uint 32";
        case 0xcf: return "uint 64";
        case 0xd0: return "int 8";
        case 0xd1: return "int 16";
        case 0xd2: return "int 32";
        case 0xd3: return "int 64";
        case 0xd4: return "fixext 1 (extension type follows)";
        case 0xd5: return "fixext 2 (extension type follows)";
        case 0xd6: return "fixext 4 (extension type follows, -1: timestamp 32)";
        case 0xd7: return "fixext 8 (extension type follows, -1: timestamp 64)";
        case 0xd8: return "fixext 16 (extension type follows, 2: LsMsgPack schema reference)";
        case 0xd9: return "str 8 (1 byte length follows)";
        case 0xda: return "str 16 (2 byte length follows)";
        case 0xdb: return "str 32 (4 byte length follows)";
        case 0xdc: return "array 16 (2 byte count follows)";
        case 0xdd: return "array 32 (4 byte count follows)";
        case 0xde: return "map 16 (2 byte count follows)";
        default: return "map 32 (4 byte count follows)";
      }
    }

    #endregion

    #region Search

    public static string Search(PayloadDocument doc, string text, bool matchCase, RenderOptions options)
    {
      StringBuilder sb = new StringBuilder();
      if (doc.Root is null || string.IsNullOrEmpty(text))
      {
        sb.Append("Nothing to search.").Append('\n');
        return sb.ToString();
      }

      List<MsgPackItem> matches = new List<MsgPackItem>();
      new ItemSearch(text, matchCase).FindAll(doc.Root.Item, matches);
      sb.Append(matches.Count.ToString(CultureInfo.InvariantCulture)).Append(matches.Count == 1 ? " item holds \"" : " items hold \"").Append(text)
        .Append("\" (strings containing it, and values it converts to):").Append('\n');
      int max = Math.Max(1, Math.Min(options.MaxNodes, 200));
      for (int t = 0; t < matches.Count && t < max; t++)
      {
        ItemNode node = doc.GetNode(matches[t]);
        sb.Append("  ").Append(Hex(matches[t].StoredOffset)).Append(' ');
        if (node != null && node.Role == "key")
          sb.Append("key ");
        sb.Append(DescribeItem(matches[t], options));
        PrimitiveObject obj = node is null ? null : doc.GetObject(node);
        if (obj != null && !string.IsNullOrEmpty(obj.Path))
          AppendComment(sb, obj.Path);
        if (node != null && node.IsSchema)
          AppendComment(sb, "in the indexed schema");
        sb.Append('\n');
      }
      if (matches.Count > max)
        sb.Append("  ... ").Append((matches.Count - max).ToString(CultureInfo.InvariantCulture)).Append(" more.").Append('\n');
      return sb.ToString();
    }

    #endregion

    #region Text helpers

    internal static string Hex(long offset)
    {
      return string.Concat("0x", offset.ToString("X", CultureInfo.InvariantCulture));
    }

    private static void AppendComment(StringBuilder sb, string comment)
    {
      if (!string.IsNullOrEmpty(comment))
        sb.Append(" // ").Append(comment);
    }

    internal static string Quote(string text)
    {
      return JsonSerializer.Serialize(text ?? string.Empty, JsonText);
    }

    private static string QuoteLimited(string text, int max, List<string> notes)
    {
      if (max > 0 && text.Length > max)
      {
        if (notes != null)
          notes.Add(string.Concat("string of ", text.Length.ToString(CultureInfo.InvariantCulture), " chars, the first ", max.ToString(CultureInfo.InvariantCulture), " shown"));
        else
          return string.Concat(Quote(text.Substring(0, max)), "... (", text.Length.ToString(CultureInfo.InvariantCulture), " chars)");
        return Quote(text.Substring(0, max) + "...");
      }
      return Quote(text);
    }

    private static string HexString(byte[] bytes, int max, string separator = "")
    {
      int count = max > 0 ? Math.Min(bytes.Length, max) : bytes.Length;
      StringBuilder sb = new StringBuilder(count * (2 + separator.Length) + 4);
      for (int t = 0; t < count; t++)
      {
        if (t > 0)
          sb.Append(separator);
        sb.Append(bytes[t].ToString("x2", CultureInfo.InvariantCulture));
      }
      if (count < bytes.Length)
        sb.Append("...");
      return sb.ToString();
    }

    /// <summary>
    /// The message of an error item, without the offset in decimal (shown in hex in front of it). An error around a partial item says so shortly.
    /// </summary>
    internal static string ErrorText(MpError error)
    {
      if (error.IsInNestedItem)
        return "ERROR inside: the item below was read up to the error";
      return string.Concat("ERROR: ", ErrorMessage(error));
    }

    internal static string ErrorMessage(MpError error)
    {
      Exception ex = error.Value as Exception;
      if (ex is null)
        return CleanError(error.ToString());
      string text = ex.Message;
      if (ex.InnerException != null)
        text = string.Concat(text, " (", ex.InnerException.Message, ")");
      return OneLine(text);
    }

    /// <summary>
    /// An error as <see cref="MpError.ToString"/> writes it, on one line with the offset in hex.
    /// </summary>
    internal static string CleanError(string text)
    {
      string clean = System.Text.RegularExpressions.Regex.Replace(text ?? string.Empty, @"\s*Offset = (\d+)", m => string.Concat(" (offset ", Hex(long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)), ")"));
      clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s*Type = ([^\r\n]+)", " (type $1)");
      clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s*InnerException = ([^\r\n]+)", " ($1)");
      return OneLine(clean).Trim();
    }

    private static string OneLine(string text)
    {
      return (text ?? string.Empty).Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
    }

    private static string FirstLine(string text)
    {
      int pos = text.IndexOf('\n');
      return pos < 0 ? text : string.Concat(text.Substring(0, pos).TrimEnd(), " ...");
    }

    #endregion
  }
}
