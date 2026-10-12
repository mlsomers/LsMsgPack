using LsMsgPack;
using LsMsgPack.Generated;
using LsMsgPack.Meta;
using LtMsgPack.Extensions;
using LtMsgPack.Generated;
using LtMsgPack.Reading;
using LtMsgPack.Writing;
using System;
using System.Reflection;

namespace LtMsgPack
{
  /// <summary>
  /// The code of the source generator (see <see cref="LtMsgPackContext"/>): asked when a handler, reader, plan or property accessor is created, not per value.
  /// </summary>
  internal sealed partial class Serializer
  {
    private GeneratedContext[] _contexts;
    private bool _useRegisteredContexts;

    private void InitGenerated()
    {
      _contexts = (GeneratedContext[])Options._contexts.Clone(); // the array of the options may be changed later
      _useRegisteredContexts = Options._useRegisteredContexts;
    }

    /// <returns>The generated description of the type (the options' contexts first, then the registered ones), null when there is none</returns>
    internal GeneratedType Generated(Type type)
    {
      GeneratedType generated = _contexts.Length == 0 ? null : GeneratedTypes.Find(_contexts, type);
      if (generated is null && _useRegisteredContexts)
        generated = GeneratedTypes.Find(type);
      return generated;
    }

    /// <returns>The generated accessors of the property, null when there are none</returns>
    internal ILtGeneratedProperty GeneratedProperty(PropertyInfo property)
    {
      return Generated(property.DeclaringType)?.FindProperty(property) as ILtGeneratedProperty;
    }

    /// <returns>The generated writer and reader of the type, null when there is none</returns>
    internal GeneratedObjectCode GeneratedCode(Type type)
    {
      return (Generated(type) as IGeneratedObject)?.Code;
    }

    /// <summary>
    /// How the generated writer of the type writes the properties of a plan with these options, null when it is not used (custom dynamic filters, type ids on every value).
    /// </summary>
    internal GeneratedWritePlan GeneratedWritePlan(GeneratedObjectCode code, FullPropertyInfo[] infos, PropWriter[] props)
    {
      if (code is null || (code.Capabilities & GeneratedCapabilities.Write) == 0 || Filters == FilterMode.Custom || _alwaysTypeId)
        return null;

      int[] ids = new int[infos.Length];
      for (int t = 0; t < ids.Length; t++)
        ids[t] = WriteMember(code, infos[t]);

      bool unrolled = (code.Capabilities & GeneratedCapabilities.WriteUnrolled) != 0 && !Options._trimTrailingNulls && ids.Length == code.UnrolledIds.Length;
      for (int t = 0; unrolled && t < ids.Length; t++)
        unrolled = ids[t] == code.UnrolledIds[t];
      return new GeneratedWritePlan(ids, Filters == FilterMode.DefaultValues, Options._unspecifiedIsUtc, Options._trimTrailingNulls, DecimalCode(), unrolled, props);
    }

    /// <returns>The generated member of the property; -1 when there is none, or the generated code would write it otherwise than these options do (the runtime writes it)</returns>
    private int WriteMember(GeneratedObjectCode code, FullPropertyInfo info)
    {
      int id = FindMember(code, info);
      if (id < 0)
        return -1;
      GeneratedMember member = code.Members[id];
      if (member.Kind == GeneratedKind.None || !member.CanWrite)
        return id; // the generated code leaves it to the runtime (at this position)
      if (info.CustomAttributes.ContainsKey(nameof(System.ComponentModel.DefaultValueAttribute)) || !Fits(member, false))
        return -1; // the generated code would write it otherwise than these options do
      return id;
    }

    /// <returns>The generated member of the property the generated code reads itself, -1 otherwise</returns>
    internal int ReadMember(GeneratedObjectCode code, FullPropertyInfo info)
    {
      if (code is null || (code.Capabilities & GeneratedCapabilities.ReadValue) == 0)
        return -1;
      int id = FindMember(code, info);
      if (id < 0)
        return -1;
      GeneratedMember member = code.Members[id];
      return member.Kind != GeneratedKind.None && member.CanRead && Fits(member, true) ? id : -1;
    }

    private static int FindMember(GeneratedObjectCode code, FullPropertyInfo info)
    {
      PropertyInfo property = info.PropertyInfo;
      GeneratedMember[] members = code.Members;
      for (int t = 0; t < members.Length; t++)
      {
        GeneratedMember member = members[t];
        if (member.Name == property.Name && member.PropertyType == property.PropertyType && member.DeclaringType == property.DeclaringType)
          return t;
      }
      return -1;
    }

    /// <summary>
    /// Whether these options write (and read) the member's type the way the generated code does (the same TypeKind, as the handlers and readers decide it).
    /// </summary>
    private bool Fits(GeneratedMember member, bool reading)
    {
      Type type = member.Nullable ? Nullable.GetUnderlyingType(member.PropertyType) : member.PropertyType;
      if (type is null)
        return false;
      WriteTypeInfo info = GetTypeInfo(type);
      switch (member.Kind)
      {
        case GeneratedKind.String: return info.Kind == TypeKind.String;
        case GeneratedKind.Boolean: return info.Kind == TypeKind.Bool;
        case GeneratedKind.SByte: return info.Kind == TypeKind.SByte;
        case GeneratedKind.Int16: return info.Kind == TypeKind.Int16;
        case GeneratedKind.Int32: return info.Kind == TypeKind.Int32;
        case GeneratedKind.Int64: return info.Kind == TypeKind.Int64;
        case GeneratedKind.Byte: return info.Kind == TypeKind.Byte;
        case GeneratedKind.UInt16: return info.Kind == TypeKind.UInt16;
        case GeneratedKind.UInt32: return info.Kind == TypeKind.UInt32;
        case GeneratedKind.UInt64: return info.Kind == TypeKind.UInt64;
        case GeneratedKind.Single: return info.Kind == TypeKind.Single;
        case GeneratedKind.Double: return info.Kind == TypeKind.Double;
        case GeneratedKind.Char: return info.Kind == TypeKind.Char;
        case GeneratedKind.Guid: return info.Kind == TypeKind.Guid;
        case GeneratedKind.DateTime: return info.Kind == TypeKind.DateTime;
        case GeneratedKind.TimeSpan: return info.Kind == TypeKind.TimeSpan;
        case GeneratedKind.Enum: return info.Kind == TypeKind.Enum;
        case GeneratedKind.Decimal: // the built-in extension (DecimalHandler's and DecimalReader's fast paths)
          return info.Kind == TypeKind.Extension && info.Extension != null && info.Extension.GetType() == typeof(DecimalExtension) && (!reading || info.Extension.TypeCode != -1);
        default: return false;
      }
    }

    private byte DecimalCode()
    {
      WriteTypeInfo info = GetTypeInfo(typeof(decimal));
      return info.Extension is null ? (byte)1 : unchecked((byte)info.Extension.TypeCode);
    }

    private GeneratedReadPlan _generatedReadPlan;

    internal GeneratedReadPlan GeneratedReadPlan
    {
      get { return _generatedReadPlan ?? (_generatedReadPlan = new GeneratedReadPlan(DecimalCode())); }
    }

    /// <summary>
    /// A generic handler or reader for <paramref name="handled"/>: created by its generated description (an instantiation compilers ahead of time know of),
    /// otherwise with <see cref="Type.MakeGenericType"/> (as before).
    /// </summary>
    /// <param name="handled">The type the handler or reader is for (the type of the description)</param>
    /// <param name="definition">E.g. <c>typeof(ListHandler&lt;&gt;)</c></param>
    /// <param name="argument">The type argument of <paramref name="definition"/> (the element type of a list...)</param>
    /// <param name="constructorArgument">What its constructor takes, null for none</param>
    private object Instantiate(Type handled, Type definition, Type argument, object constructorArgument)
    {
      if (Generated(handled) is IGeneratedInstantiations generated)
      {
        object instance = generated.Instantiate(definition, constructorArgument);
        if (instance != null)
          return instance;
      }
      return Activator.CreateInstance(definition.MakeGenericType(argument), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
        constructorArgument is null ? new object[0] : new object[] { constructorArgument }, null);
    }
  }
}
