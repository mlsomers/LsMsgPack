using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Types;
using LtMsgPack.Extensions;
using LtMsgPack.Writing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace LtMsgPack.Http
{
  /// <summary>
  /// The described schema of application/x-lsmsgpack (<see cref="MsgPackMediaTypes.DescribedSchemaParameter"/>): the schema with the kind and default of each property, for clients without the classes (docs/TypeScriptClient.md).
  /// <para>The same map as the schema (same keys in the same order, so the type ids of the body are the same), each value an array of property descriptions: <c>[name, type]</c> or <c>[name, type, default]</c>
  /// (the default of <see cref="DefaultValueAttribute"/>, encoded as the body encodes the value). An entry that is not an object (a collection or a value with a type id) has <c>["@", type]</c>,
  /// and a last entry <c>""</c> has <c>[["@", rootType]]</c>. Its id hashes <see cref="SchemaId.DescribedFormatVersion"/> instead of <see cref="SchemaId.FormatVersion"/>.</para>
  /// <para>Types: a kind (<c>string bool int long ulong float decimal char bigint guid bin uri datetime dto dto-clock timespan dateonly timeonly any</c>), an entry index (an object of that entry),
  /// or an array: <c>["?", T]</c> (Nullable), <c>["struct", k]</c> (a struct of entry k: its default is an object, not null), <c>["struct", [properties]]</c> (a struct without an entry,
  /// it was only written as its default so far: its properties as an entry has them), <c>["list", T]</c>, <c>["dict", K, V]</c>, <c>["pair", K, V]</c>, <c>["tuple", T...]</c>.</para>
  /// </summary>
  internal sealed class DescribedSchema
  {
    internal readonly byte[] Bytes;
    internal readonly SchemaId Id;
    internal readonly string Hex;
    internal readonly byte[] Reference;

    private DescribedSchema(byte[] bytes)
    {
      Bytes = bytes;
      Id = SchemaId.Compute(bytes, SchemaId.DescribedFormatVersion);
      Hex = Id.ToString();
      Reference = SchemaStore.WriteReference(Id);
    }

    /// <param name="resolver">The (frozen) resolver the schema was written with, its entries know their types</param>
    /// <param name="root">The root type of the writer</param>
    /// <param name="options">The options the body is written with</param>
    /// <param name="defaults">Writes the values of <see cref="DefaultValueAttribute"/> (options without the schema), made when first needed</param>
    internal static DescribedSchema Create(IndexedSchemaTypeResolver resolver, Type root, LtMsgPackOptions options, Func<LtMsgPackSerializer> defaults)
    {
      Writer writer = new Writer(resolver, options, defaults);
      return new DescribedSchema(writer.Write(root));
    }

    private sealed class Writer
    {
      private readonly IndexedSchemaTypeResolver _resolver;
      private readonly LtMsgPackOptions _options;
      private readonly Func<LtMsgPackSerializer> _defaults;
      private readonly ByteWriter _bytes = new ByteWriter(1024);
      private readonly HashSet<Type> _inlineStructs = new HashSet<Type>(); // being written: a struct with a (settable, not stored) property of its own type
      private static readonly MsgPackOptions Canonical = SchemaBytes.Canonical; // the byte order of the specification, as the schema

      internal Writer(IndexedSchemaTypeResolver resolver, LtMsgPackOptions options, Func<LtMsgPackSerializer> defaults)
      {
        _resolver = resolver;
        _options = options;
        _defaults = defaults;
      }

      internal byte[] Write(Type root)
      {
        List<ComplexTypeDef> defs = _resolver.ByTypeId;
        _bytes.WriteMapHeader(defs.Count + 1, Canonical);
        for (int t = 0; t < defs.Count; t++)
        {
          ComplexTypeDef def = defs[t];
          WriteName(def.TypeName);
          WriteEntry(def);
        }

        _bytes.WriteString(string.Empty, Canonical);
        _bytes.WriteArrayHeader(1, Canonical);
        WriteContent(root);
        return _bytes.ToArray();
      }

      private void WriteName(string name)
      {
        if (name is null)
          _bytes.Write((byte)MsgPackTypeId.MpNull);
        else
          _bytes.WriteString(name, Canonical);
      }

      private void WriteEntry(ComplexTypeDef def)
      {
        Type type = def.Type;
        List<string> names = def.Props;
        if (type is null) // not resolved (not for a writer): the names only
        {
          _bytes.WriteArrayHeader(names.Count, Canonical);
          for (int p = 0; p < names.Count; p++)
            WriteName(names[p]);
          return;
        }

        bool content = def.IsCollection || TypeKinds.Classify(type, _options._extensions, out LtExtension _) != TypeKind.Complex;
        _bytes.WriteArrayHeader(names.Count + (content ? 1 : 0), Canonical);
        if (content)
          WriteContent(type);

        if (names.Count == 0)
          return;
        FullPropertyInfo[] props = FullPropertyInfo.GetStaticallyIncludedProps(type, _options); // the shared cache of the static filters, as the session made them
        for (int p = 0; p < names.Count; p++)
          WriteProperty(names[p], Find(props, names[p]));
      }

      /// <summary>
      /// <c>["@", type]</c>: the content of a value wrapped with a type id (<c>{ "": typeId, "@": content }</c>), or the root.
      /// </summary>
      private void WriteContent(Type type)
      {
        _bytes.WriteArrayHeader(2, Canonical);
        _bytes.WriteString("@", Canonical);
        WriteType(type);
      }

      private static FullPropertyInfo Find(FullPropertyInfo[] props, string name)
      {
        for (int t = 0; t < props.Length; t++)
          if (string.Equals(props[t].PropertyInfo.Name, name, StringComparison.Ordinal))
            return props[t];
        return null;
      }

      private void WriteProperty(string name, FullPropertyInfo prop)
      {
        if (prop is null) // e.g. a property of a collection that is not found by name
        {
          WriteName(name);
          return;
        }

        object defaultValue = DefaultOf(prop);
        _bytes.WriteArrayHeader(defaultValue is null ? 2 : 3, Canonical);
        WriteName(name);
        Type type = prop.PropertyInfo.PropertyType;
        WriteType(type);
        if (!(defaultValue is null))
          _bytes.Write(_defaults().Serialize(defaultValue, type));
      }

      /// <summary>
      /// The value of [DefaultValue] when the filters leave out values equal to it (see FilterDefaultValues: only a value of the same type can be equal), otherwise null.
      /// </summary>
      private static object DefaultOf(FullPropertyInfo prop)
      {
        Dictionary<string, object> attributes = prop.CustomAttributes;
        if (attributes is null || attributes.Count == 0 || !attributes.TryGetValue(nameof(DefaultValueAttribute), out object attribute))
          return null;
        object value = (attribute as DefaultValueAttribute)?.Value;
        if (value is null)
          return null;
        Type type = prop.PropertyInfo.PropertyType;
        Type valueType = Nullable.GetUnderlyingType(type) ?? type;
        return value.GetType() == valueType ? value : null;
      }

      [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = TrimReasons.ModelTypes)]
      [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
      private void WriteType(Type type)
      {
        Type nullable = Nullable.GetUnderlyingType(type);
        if (!(nullable is null))
        {
          _bytes.WriteArrayHeader(2, Canonical);
          _bytes.WriteString("?", Canonical);
          WriteType(nullable);
          return;
        }

        if (type == typeof(decimal)) // a custom extension (DecimalExtension) or a string (DecimalFormat.String)
        {
          Kind("decimal");
          return;
        }

        switch (TypeKinds.Classify(type, _options._extensions, out LtExtension _))
        {
          case TypeKind.Bool: Kind("bool"); return;
          case TypeKind.SByte:
          case TypeKind.Int16:
          case TypeKind.Int32:
          case TypeKind.Byte:
          case TypeKind.UInt16:
          case TypeKind.UInt32: Kind("int"); return;
          case TypeKind.Int64: Kind("long"); return;
          case TypeKind.UInt64: Kind("ulong"); return;
          case TypeKind.Single:
          case TypeKind.Double: Kind("float"); return;
          case TypeKind.String: Kind("string"); return;
          case TypeKind.Bin: Kind("bin"); return;
          case TypeKind.Guid: Kind("guid"); return;
          case TypeKind.DateTime: Kind("datetime"); return;
          case TypeKind.DateTimeOffset: Kind(_options._dateTimeOffsetFormat == DateTimeOffsetFormat.ClockTimeAndOffset ? "dto-clock" : "dto"); return;
          case TypeKind.Char: Kind("char"); return;
          case TypeKind.TimeSpan: Kind("timespan"); return;
          case TypeKind.Uri: Kind("uri"); return;
          case TypeKind.DateOnly: Kind("dateonly"); return;
          case TypeKind.TimeOnly: Kind("timeonly"); return;
          case TypeKind.Enum: WriteType(Enum.GetUnderlyingType(type)); return;
          case TypeKind.Plain: WritePlain(type); return;
          case TypeKind.Pair:
            {
              Type[] arguments = type.GetGenericArguments();
              Composite("pair", arguments);
              return;
            }
          case TypeKind.Tuple: Composite("tuple", type.GetGenericArguments()); return; // the 8th item (Rest) is a tuple: a nested array
          case TypeKind.Map:
            {
              CollectionInfo info = CollectionInfo.Get(type);
              Type key = info.PairKey?.PropertyType ?? info.KeyType ?? typeof(object);
              Type value = info.PairValue?.PropertyType ?? info.ValueType ?? typeof(object);
              Composite("dict", new[] { key, value });
              return;
            }
          case TypeKind.Array: Composite("list", new[] { type.IsArray ? type.GetElementType() : CollectionInfo.Get(type).ElementType ?? typeof(object) }); return;
          case TypeKind.Complex:
            if (type.IsValueType && !_resolver.ByType.ContainsKey(type) && _inlineStructs.Add(type))
            {
              // Never written other than as its default (left out), so it has no entry: its properties inline, for the default (an object, as the readers of .NET make it)
              FullPropertyInfo[] props = FullPropertyInfo.GetStaticallyIncludedProps(type, _options);
              _bytes.WriteArrayHeader(2, Canonical);
              _bytes.WriteString("struct", Canonical);
              _bytes.WriteArrayHeader(props.Length, Canonical);
              for (int p = 0; p < props.Length; p++)
                WriteProperty(props[p].PropertyInfo.Name, props[p]);
              _inlineStructs.Remove(type);
              return;
            }
            if (!type.IsAbstract && !type.IsInterface && type != typeof(object) && _resolver.ByType.TryGetValue(type, out ComplexTypeDef def))
            {
              if (type.IsValueType)
              {
                _bytes.WriteArrayHeader(2, Canonical);
                _bytes.WriteString("struct", Canonical);
                WriteInt(def.TypeId);
              }
              else
                WriteInt(def.TypeId);
              return;
            }
            Kind("any"); // declared as object, an interface or abstract class (the value has a type id), or a class this schema never wrote (the value is nil or has a type id)
            return;
          default: // custom extensions, multidimensional arrays, raw extensions: as they are
            Kind("any");
            return;
        }
      }

      private void WritePlain(Type type)
      {
        switch (FrameworkTypeInfo.GetPlainForm(type))
        {
          case PlainForm.Half: Kind("float"); return;
          case PlainForm.Text: Kind("string"); return;
          case PlainForm.Rune: Kind("int"); return;
          case PlainForm.NInt: Kind("long"); return;
          case PlainForm.NUInt: Kind("ulong"); return;
          case PlainForm.Binary: Kind("bin"); return;
          case PlainForm.Complex: Composite("tuple", new[] { typeof(double), typeof(double) }); return;
          case PlainForm.BigInteger: Kind("bigint"); return;
          default: Kind("any"); return;
        }
      }

      private void Kind(string kind)
      {
        _bytes.WriteString(kind, Canonical);
      }

      private void Composite(string kind, Type[] items)
      {
        _bytes.WriteArrayHeader(items.Length + 1, Canonical);
        _bytes.WriteString(kind, Canonical);
        for (int t = 0; t < items.Length; t++)
          WriteType(items[t]);
      }

      private void WriteInt(int value)
      {
        if (value <= 0x7f)
          _bytes.Write((byte)value);
        else if (value <= 0xff)
        {
          _bytes.Write((byte)MsgPackTypeId.MpUByte);
          _bytes.Write((byte)value);
        }
        else if (value <= 0xffff)
        {
          _bytes.Write((byte)MsgPackTypeId.MpUShort);
          _bytes.Write((byte)(value >> 8));
          _bytes.Write((byte)value);
        }
        else
        {
          _bytes.Write((byte)MsgPackTypeId.MpUInt);
          _bytes.Write((byte)(value >> 24));
          _bytes.Write((byte)(value >> 16));
          _bytes.Write((byte)(value >> 8));
          _bytes.Write((byte)value);
        }
      }
    }
  }
}
