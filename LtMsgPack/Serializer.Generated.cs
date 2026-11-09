using LsMsgPack;
using LsMsgPack.Generated;
using LsMsgPack.Meta;
using LtMsgPack.Extensions;
using LtMsgPack.Generated;
using LtMsgPack.Reading;
using LtMsgPack.Writing;
using System;
using System.Diagnostics.CodeAnalysis;
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
      if (_useRegisteredContexts) // registered as the module initializer would: LsMsgPack.Core creates collections and objects with the registered code (needed where reflection does not see constructors: NativeAOT)
      {
        for (int t = 0; t < _contexts.Length; t++)
          GeneratedTypes.Register(_contexts[t]);
      }
    }

    /// <returns>The generated description of the type (the options' contexts first, then the registered ones), null when there is none</returns>
    internal GeneratedType Generated(Type type)
    {
      GeneratedType generated = _contexts.Length == 0 ? null : GeneratedTypes.Find(_contexts, type);
      if (generated is null && _useRegisteredContexts)
        generated = GeneratedTypes.Find(type);
      return generated;
    }

    /// <returns>The generated accessors of the property or field, null when there are none</returns>
    internal ILtGeneratedProperty GeneratedProperty(MemberInfo member)
    {
      return Generated(member.DeclaringType)?.FindMember(member) as ILtGeneratedProperty;
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

      bool unrolledIds = ids.Length == code.UnrolledIds.Length;
      for (int t = 0; unrolledIds && t < ids.Length; t++)
        unrolledIds = ids[t] == code.UnrolledIds[t];
      bool trim = Options._trimTrailingNulls;
      bool unrolled = unrolledIds && (code.Capabilities & GeneratedCapabilities.WriteUnrolled) != 0 && !trim;
      bool unrolledMap = unrolledIds && (code.Capabilities & GeneratedCapabilities.WriteUnrolledMap) != 0;
      bool unrolledTrimmed = unrolledIds && (code.Capabilities & GeneratedCapabilities.WriteUnrolledTrimmed) != 0 && trim;
      return new GeneratedWritePlan(this, code, ids, Filters == FilterMode.DefaultValues, Options._unspecifiedIsUtc, trim, DecimalCode(),
        GetTypeInfo(typeof(Guid)).Kind == TypeKind.GuidString, GetTypeInfo(typeof(decimal)).Kind == TypeKind.DecimalString, unrolled, unrolledMap, unrolledTrimmed, props);
    }

    /// <returns>The generated member of the property; -1 when there is none, or the generated code would write it otherwise than these options do (the runtime writes it)</returns>
    private int WriteMember(GeneratedObjectCode code, FullPropertyInfo info)
    {
      if (CallsShouldSerialize(info))
        return -1; // its PropWriter calls the method of the owner (FilterShouldSerialize)
      int id = FindMember(code, info);
      if (id < 0)
        return -1;
      GeneratedMember member = code.Members[id];
      if (member.Kind == GeneratedKind.None || !member.CanWrite)
        return id; // the generated code leaves it to the runtime (at this position)
      if (info.CustomAttributes.ContainsKey(nameof(System.ComponentModel.DefaultValueAttribute)) || !Fits(member, false, (code.Capabilities & GeneratedCapabilities.WriteFormats) != 0))
        return -1; // the generated code would write it otherwise than these options do
      return id;
    }

    /// <returns>The generated member of the property the generated code reads itself, -1 otherwise</returns>
    internal int ReadMember(GeneratedObjectCode code, FullPropertyInfo info)
    {
      if (code is null || (code.Capabilities & (GeneratedCapabilities.ReadValue | GeneratedCapabilities.ReadMap)) == 0)
        return -1;
      int id = FindMember(code, info);
      if (id < 0)
        return -1;
      GeneratedMember member = code.Members[id];
      return member.Kind != GeneratedKind.None && member.CanRead && Fits(member, true, (code.Capabilities & GeneratedCapabilities.ReadFormats) != 0) ? id : -1;
    }

    private static int FindMember(GeneratedObjectCode code, FullPropertyInfo info)
    {
      MemberInfo declared = info.Member; // a property or field (the runtime's compiled code describes both, the generator's code properties)
      GeneratedMember[] members = code.Members;
      for (int t = 0; t < members.Length; t++)
      {
        GeneratedMember member = members[t];
        if (member.Name == info.Name && member.PropertyType == info.DeclaredType && member.DeclaringType == declared.DeclaringType)
          return t;
      }
      return -1;
    }

    /// <summary>
    /// Whether these options write (and read) the member's type the way the generated code does (the same TypeKind, as the handlers and readers decide it).
    /// </summary>
    /// <param name="formats">The code writes (reads) Guids and decimals in the plan's format (<see cref="GeneratedCapabilities.WriteFormats"/>, <see cref="GeneratedCapabilities.ReadFormats"/>)</param>
    private bool Fits(GeneratedMember member, bool reading, bool formats)
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
        case GeneratedKind.Guid: return info.Kind == TypeKind.Guid || (formats && info.Kind == TypeKind.GuidString);
        case GeneratedKind.DateTime: return info.Kind == TypeKind.DateTime;
        case GeneratedKind.TimeSpan: return info.Kind == TypeKind.TimeSpan;
        case GeneratedKind.Enum: return info.Kind == TypeKind.Enum;
        case GeneratedKind.Decimal: // the built-in extension (DecimalHandler's and DecimalReader's fast paths)
          if (formats && info.Kind == TypeKind.DecimalString)
            return !reading || IsBuiltInDecimal(info.Extension); // writing: whatever the extension; reading: the built-in one, which DecimalStringReader reads in place too (GeneratedIO.TryReadDecimal)
          return info.Kind == TypeKind.Extension && info.Extension != null && info.Extension.GetType() == typeof(DecimalExtension) && (!reading || info.Extension.TypeCode != -1);
        default: return false;
      }
    }

    /// <summary>
    /// The built-in decimal extension with a type code it reads (-1 is the timestamp, read as such).
    /// </summary>
    private static bool IsBuiltInDecimal(LtExtension extension)
    {
      return extension != null && extension.GetType() == typeof(DecimalExtension) && extension.TypeCode != -1;
    }

    private byte DecimalCode()
    {
      WriteTypeInfo info = GetTypeInfo(typeof(decimal));
      return info.Extension is null ? (byte)1 : unchecked((byte)info.Extension.TypeCode);
    }

    private GeneratedReadPlan _generatedReadPlan;

    internal GeneratedReadPlan GeneratedReadPlan
    {
      get
      {
        return _generatedReadPlan ?? (_generatedReadPlan = new GeneratedReadPlan(DecimalCode(), GetTypeInfo(typeof(Guid)).Kind == TypeKind.GuidString, GetTypeInfo(typeof(decimal)).Kind == TypeKind.DecimalString));
      }
    }

    /// <summary>
    /// A generic handler or reader for <paramref name="handled"/>: created by its generated description (an instantiation compilers ahead of time know of),
    /// otherwise with <see cref="Type.MakeGenericType"/> (as before).
    /// </summary>
    /// <param name="handled">The type the handler or reader is for (the type of the description)</param>
    /// <param name="definition">E.g. <c>typeof(ListHandler&lt;&gt;)</c></param>
    /// <param name="argument">The type argument of <paramref name="definition"/> (the element type of a list...)</param>
    /// <param name="constructorArgument">What its constructor takes, null for none</param>
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = TrimReasons.Fallback)]
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = TrimReasons.Fallback)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = TrimReasons.Fallback)]
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

    /// <summary>
    /// A typed handler or reader of a set, dictionary or collection interface: created by its generated description, otherwise only where the runtime has dynamic code (<see cref="CanBindDelegates"/>).
    /// </summary>
    /// <param name="handled">The type the handler or reader is for</param>
    /// <param name="definition">E.g. <c>typeof(DictionaryHandler&lt;,&gt;)</c></param>
    /// <param name="arguments">Its type arguments</param>
    /// <returns>null when there is no such instantiation: written and read boxed (a context of an older version, a type it does not describe)</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = TrimReasons.Fallback)]
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = TrimReasons.Fallback)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = TrimReasons.Fallback)]
    private object InstantiateCollection(Type handled, Type definition, Type[] arguments, object constructorArgument)
    {
      if (Generated(handled) is IGeneratedInstantiations generated)
      {
        object instance = generated.Instantiate(definition, constructorArgument);
        if (instance != null)
          return instance;
      }
      if (!CanBindDelegates)
        return null;
      return Activator.CreateInstance(definition.MakeGenericType(arguments), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new object[] { constructorArgument }, null);
    }
  }
}
