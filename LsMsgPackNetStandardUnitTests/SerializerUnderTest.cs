using LsMsgPack;
using LsMsgPack.Types.Extensions;
using LtMsgPack;
using LtMsgPack.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// A serializer the shared tests run against: LsMsgPack (<see cref="LsSerializer"/>) or LtMsgPack (<see cref="LtSerializer"/>).
  /// <para>The tests describe the settings with <see cref="MsgPackSettings"/>, the adapter of LtMsgPack copies the format settings (<see cref="MsgPackOptions"/>) into LtMsgPackOptions.
  /// The other overloads of MsgPackSerializer are extension methods (<see cref="SerializerUnderTestExtensions"/>), so a test calls <c>Serializer.Serialize(...)</c> like it called <c>MsgPackSerializer.Serialize(...)</c>.</para>
  /// </summary>
  public interface ISerializerUnderTest
  {
    string Name { get; }

    /// <param name="settings">null: the defaults</param>
    byte[] Serialize<T>(T item, MsgPackSettings settings);

    void Serialize<T>(T item, Stream target, MsgPackSettings settings);

    byte[] Serialize(object item, Type assignedTo, MsgPackSettings settings);

    void Serialize(object item, Type assignedTo, Stream target, MsgPackSettings settings);

    T Deserialize<T>(byte[] source, MsgPackSettings settings);

    T Deserialize<T>(Stream source, MsgPackSettings settings);

    object Deserialize(Type type, byte[] source, MsgPackSettings settings);

    object Deserialize(Type type, Stream source, MsgPackSettings settings);

    T Deserialize<T>(byte[] source, MsgPackSettings settings, out ReadDifferences differences);

    object Deserialize(Type type, byte[] source, MsgPackSettings settings, out ReadDifferences differences);

    /// <summary>
    /// Whether the value is an extension the serializer has no custom extension for (LsMsgPack: MpExt, LtMsgPack: MsgPackExtension).
    /// </summary>
    bool IsUnknownExtension(object value, out sbyte typeCode, out byte[] data);
  }

  /// <summary>
  /// The overloads of MsgPackSerializer (the same defaults).
  /// </summary>
  public static class SerializerUnderTestExtensions
  {
    public static byte[] Serialize<T>(this ISerializerUnderTest serializer, T item, bool dynamicallyCompact = true)
    {
      return serializer.Serialize(item, new MsgPackSettings() { DynamicallyCompact = dynamicallyCompact });
    }

    public static void Serialize<T>(this ISerializerUnderTest serializer, T item, Stream target, bool dynamicallyCompact = true)
    {
      serializer.Serialize(item, target, new MsgPackSettings() { DynamicallyCompact = dynamicallyCompact });
    }

    public static T Deserialize<T>(this ISerializerUnderTest serializer, byte[] source)
    {
      return serializer.Deserialize<T>(source, (MsgPackSettings)null);
    }

    public static T Deserialize<T>(this ISerializerUnderTest serializer, Stream source)
    {
      return serializer.Deserialize<T>(source, (MsgPackSettings)null);
    }

    public static object Deserialize(this ISerializerUnderTest serializer, Type type, byte[] source)
    {
      return serializer.Deserialize(type, source, (MsgPackSettings)null);
    }

    public static object Deserialize(this ISerializerUnderTest serializer, Type type, Stream source)
    {
      return serializer.Deserialize(type, source, (MsgPackSettings)null);
    }
  }

  public static class Serializers
  {
    public static readonly ISerializerUnderTest Ls = new LsSerializer();
    public static readonly ISerializerUnderTest Lt = new LtSerializer();
  }

  public sealed class LsSerializer : ISerializerUnderTest
  {
    public string Name { get { return "LsMsgPack"; } }

    public byte[] Serialize<T>(T item, MsgPackSettings settings) { return MsgPackSerializer.Serialize(item, settings); }
    public void Serialize<T>(T item, Stream target, MsgPackSettings settings) { MsgPackSerializer.Serialize(item, target, settings); }
    public byte[] Serialize(object item, Type assignedTo, MsgPackSettings settings) { return MsgPackSerializer.Serialize(item, assignedTo, settings); }
    public void Serialize(object item, Type assignedTo, Stream target, MsgPackSettings settings) { MsgPackSerializer.Serialize(item, assignedTo, target, settings); }
    public T Deserialize<T>(byte[] source, MsgPackSettings settings) { return MsgPackSerializer.Deserialize<T>(source, settings); }
    public T Deserialize<T>(Stream source, MsgPackSettings settings) { return MsgPackSerializer.Deserialize<T>(source, settings); }
    public object Deserialize(Type type, byte[] source, MsgPackSettings settings) { return MsgPackSerializer.Deserialize(type, source, settings); }
    public object Deserialize(Type type, Stream source, MsgPackSettings settings) { return MsgPackSerializer.Deserialize(type, source, settings); }
    public T Deserialize<T>(byte[] source, MsgPackSettings settings, out ReadDifferences differences) { return MsgPackSerializer.Deserialize<T>(source, settings, out differences); }
    public object Deserialize(Type type, byte[] source, MsgPackSettings settings, out ReadDifferences differences) { return MsgPackSerializer.Deserialize(type, source, settings, out differences); }

    public bool IsUnknownExtension(object value, out sbyte typeCode, out byte[] data)
    {
      if (value != null && value.GetType() == typeof(MpExt))
      {
        MpExt ext = (MpExt)value;
        typeCode = ext.TypeSpecifier;
        data = (byte[])ext.Value;
        return true;
      }
      typeCode = 0;
      data = null;
      return false;
    }

    public override string ToString() { return Name; }
  }

  public sealed class LtSerializer : ISerializerUnderTest
  {
    private readonly ConcurrentDictionary<object, LtMsgPackSerializer> _serializers = new ConcurrentDictionary<object, LtMsgPackSerializer>();

    public string Name { get { return "LtMsgPack"; } }

    /// <summary>
    /// A serializer with the format settings of the MsgPackSettings (cached per combination of their values, a serializer builds its plans once).
    /// </summary>
    internal LtMsgPackSerializer For(MsgPackSettings settings)
    {
      if (settings is null)
        settings = new MsgPackSettings();

      LtExtension[] extensions = Extensions(settings.CustomExtentionTypes);
      object key = (settings.UseInexedSchema, settings.DynamicallyCompact, settings.EndianAction, settings.AddTypeIdOptions,
        settings.TypeResolvers, settings.StaticFilters, settings.DynamicFilters, settings.PropertyNameResolvers,
        (settings.SchemaStore, settings.WriteSchemaReference, settings.CustomExtentionTypes, settings.PropertyOrder, settings.ObjectLayout, settings.TrimTrailingNulls, settings.TypeGuard, settings.MaxDepth, settings.ObjectCreation), (settings.UnspecifiedDateTimeKind, settings.ReadDateTimeKind));
      return _serializers.GetOrAdd(key, k => new LtMsgPackSerializer(new LtMsgPackOptions()
      {
        UseInexedSchema = settings.UseInexedSchema,
        DynamicallyCompact = settings.DynamicallyCompact,
        EndianAction = settings.EndianAction,
        AddTypeIdOptions = settings.AddTypeIdOptions,
        TypeResolvers = settings.TypeResolvers,
        StaticFilters = settings.StaticFilters,
        DynamicFilters = settings.DynamicFilters,
        PropertyNameResolvers = settings.PropertyNameResolvers,
        SchemaStore = settings.SchemaStore,
        WriteSchemaReference = settings.WriteSchemaReference,
        PropertyOrder = settings.PropertyOrder,
        ObjectLayout = settings.ObjectLayout,
        TrimTrailingNulls = settings.TrimTrailingNulls,
        TypeGuard = settings.TypeGuard,
        MaxDepth = settings.MaxDepth,
        ObjectCreation = settings.ObjectCreation,
        UnspecifiedDateTimeKind = settings.UnspecifiedDateTimeKind,
        ReadDateTimeKind = settings.ReadDateTimeKind,
        Extensions = extensions
      }));
    }

    /// <summary>
    /// The LtExtension that writes and reads the same bytes as a custom extension of LsMsgPack (null: none known). The decimal extension is known, tests add their own.
    /// </summary>
    public static readonly List<Func<ICustomExt, LtExtension>> ExtensionEquivalents = new List<Func<ICustomExt, LtExtension>>()
    {
      ext => ext is MpDecimal dec ? new DecimalExtension(dec.TypeSpecifier) : null
    };

    private static LtExtension[] Extensions(ICustomExt[] custom)
    {
      if (custom is null)
        return new LtExtension[0];

      LtExtension[] extensions = new LtExtension[custom.Length];
      for (int t = 0; t < custom.Length; t++)
      {
        foreach (Func<ICustomExt, LtExtension> equivalent in ExtensionEquivalents)
          if ((extensions[t] = equivalent(custom[t])) != null)
            break;
        if (extensions[t] is null)
          Assert.Inconclusive($"LtMsgPack has no equivalent of the custom extension {custom[t].GetType().Name} (add one to {nameof(LtSerializer)}.{nameof(ExtensionEquivalents)}).");
      }
      return extensions;
    }

    public byte[] Serialize<T>(T item, MsgPackSettings settings) { return For(settings).Serialize(item); }
    public void Serialize<T>(T item, Stream target, MsgPackSettings settings) { For(settings).Serialize(item, target); }
    public byte[] Serialize(object item, Type assignedTo, MsgPackSettings settings) { return For(settings).Serialize(item, assignedTo); }
    public void Serialize(object item, Type assignedTo, Stream target, MsgPackSettings settings) { For(settings).Serialize(item, assignedTo, target); }
    public T Deserialize<T>(byte[] source, MsgPackSettings settings) { return For(settings).Deserialize<T>(source); }
    public T Deserialize<T>(Stream source, MsgPackSettings settings) { return For(settings).Deserialize<T>(source); }
    public object Deserialize(Type type, byte[] source, MsgPackSettings settings) { return For(settings).Deserialize(type, source); }
    public object Deserialize(Type type, Stream source, MsgPackSettings settings) { return For(settings).Deserialize(type, source); }
    public T Deserialize<T>(byte[] source, MsgPackSettings settings, out ReadDifferences differences) { return For(settings).Deserialize<T>(source, out differences); }
    public object Deserialize(Type type, byte[] source, MsgPackSettings settings, out ReadDifferences differences) { return For(settings).Deserialize(type, source, out differences); }

    public bool IsUnknownExtension(object value, out sbyte typeCode, out byte[] data)
    {
      if (value is MsgPackExtension ext)
      {
        typeCode = ext.TypeCode;
        data = ext.Data;
        return true;
      }
      typeCode = 0;
      data = null;
      return false;
    }

    public override string ToString() { return Name; }
  }
}
