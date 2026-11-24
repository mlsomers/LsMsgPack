using LsMsgPack;
using LsMsgPack.Generated;
using LsMsgPack.Meta;
using LsMsgPack.Extensions;
using LsMsgPack.Reading;
using LsMsgPack.Writing;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LsMsgPack
{
  /// <summary>
  /// The code of the source generator (see <see cref="LsMsgPackContext"/>): asked when a handler, reader, plan or property accessor is created, not per value.
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
      return new GeneratedWritePlan(this, code, ids, Filters == FilterMode.DefaultValues, Options._unspecifiedIsUtc, !Options._nativeDateTime, trim, DecimalCode(),
        GetTypeInfo(typeof(Guid)).Kind == TypeKind.GuidString, GetTypeInfo(typeof(decimal)).Kind == TypeKind.DecimalString, unrolled, unrolledMap, unrolledTrimmed, props);
    }

    /// <returns>The generated member of the property; -1 when there is none, or the generated code would write it otherwise than these options do (the runtime writes it)</returns>
    private int WriteMember(GeneratedObjectCode code, FullPropertyInfo info)
    {
      if (CallsShouldSerialize(info) && !(code is ICompiledCode))
        return -1; // its PropWriter calls the method of the owner (FilterShouldSerialize); the compiled writers call it themselves or leave the member to the PropWriter (not CanWrite)
      int id = FindMember(code, info);
      if (id < 0)
        return -1;
      GeneratedMember member = code.Members[id];
      if (member.Kind == GeneratedKind.None || !member.CanWrite)
        return id; // the generated code leaves it to the runtime (at this position)
      if (info.CustomAttributes.ContainsKey(nameof(System.ComponentModel.DefaultValueAttribute)) || info.MemberConverter != null || OwnEnumFormat(info, out EnumFormat unused)
        || !Fits(member, false, (code.Capabilities & GeneratedCapabilities.WriteFormats) != 0, (code.Capabilities & GeneratedCapabilities.DateTimeFormats) != 0))
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
      return member.Kind != GeneratedKind.None && member.CanRead && info.MemberConverter is null && Fits(member, true, (code.Capabilities & GeneratedCapabilities.ReadFormats) != 0, true) ? id : -1;
    }

    /// <summary>
    /// Whether the properties of a read plan are the generated code's <see cref="GeneratedObjectCode.UnrolledIds"/>, in that order (the members whose names its ReadMap compares as constants).
    /// </summary>
    internal bool InUnrolledOrder(GeneratedObjectCode code, FullPropertyInfo[] infos)
    {
      int[] unrolled = code.UnrolledIds;
      if (infos.Length != unrolled.Length)
        return false;
      for (int t = infos.Length - 1; t >= 0; t--)
      {
        if (FindMember(code, infos[t]) != unrolled[t] || !string.Equals(infos[t].WrittenName, infos[t].Name, StringComparison.Ordinal)) // its constants are the .NET names (not PropertyNamingPolicy's)
          return false;
      }
      return true;
    }

    /// <summary>
    /// How a reader compiled at runtime (CompiledReader) reads values of the type itself: the kind the generated code has for it, when these options read it that way
    /// (<see cref="Fits"/>, with <see cref="GeneratedCapabilities.ReadFormats"/>); None for the types the runtime's readers read.
    /// </summary>
    /// <param name="type">The declared type of a property (also a nullable)</param>
    internal GeneratedKind ReadKind(Type type)
    {
      Type underlying = Nullable.GetUnderlyingType(type);
      GeneratedKind kind = CompiledWriter.KindOf(this, underlying ?? type);
      if (kind == GeneratedKind.None)
        return kind;
      return Fits(new GeneratedMember(null, null, type, kind, underlying != null, false, true), true, true, true) ? kind : GeneratedKind.None;
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
    /// <param name="dateFormats">The code writes DateTimes in the plan's format (<see cref="GeneratedCapabilities.DateTimeFormats"/>; the code of older versions writes timestamps). Readers read both formats.</param>
    private bool Fits(GeneratedMember member, bool reading, bool formats, bool dateFormats)
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
        case GeneratedKind.DateTime: return info.Kind == TypeKind.DateTimeTimestamp || (info.Kind == TypeKind.DateTime && (reading || dateFormats));
        case GeneratedKind.TimeSpan: return info.Kind == TypeKind.TimeSpan;
        case GeneratedKind.Enum: return info.Kind == TypeKind.Enum || (reading && info.Kind == TypeKind.EnumName); // the generated readers leave a name to the runtime's EnumReader
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
    private static bool IsBuiltInDecimal(LsExtension extension)
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
    /// otherwise with <see cref="Type.MakeGenericType"/>. Without dynamic code (NativeAOT) that works for reference type arguments (the shared code, the constructors kept
    /// by <see cref="ReflectionInstantiations"/>) and for the instantiations the application compiled anyway; for others it returns null and the caller writes or reads boxed.
    /// </summary>
    /// <param name="handled">The type the handler or reader is for (the type of the description)</param>
    /// <param name="definition">E.g. <c>typeof(ListHandler&lt;&gt;)</c></param>
    /// <param name="argument">The type argument of <paramref name="definition"/> (the element type of a list...)</param>
    /// <param name="constructorArgument">What its constructor takes, null for none</param>
    /// <returns>null when the runtime has no code for the instantiation (only without dynamic code)</returns>
    private object Instantiate(Type handled, Type definition, Type argument, object constructorArgument)
    {
      if (Generated(handled) is IGeneratedInstantiations generated)
      {
        object instance = generated.Instantiate(definition, constructorArgument);
        if (instance != null)
          return instance;
      }
      return CreateInstantiation(definition, new[] { argument }, constructorArgument is null ? new object[0] : new object[] { constructorArgument });
    }

    /// <returns>null when the runtime has no code for the instantiation: NativeAOT has no code for a value type argument the application did not use (NotSupportedException),
    /// nor the constructors of a definition <see cref="ReflectionInstantiations"/> did not keep (a serializer with a context: MissingMethodException)</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = TrimReasons.Fallback)]
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = TrimReasons.Fallback)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = TrimReasons.Fallback)]
    internal object CreateInstantiation(Type definition, Type[] arguments, object[] constructorArguments)
    {
      if (CanBindDelegates)
        return Activator.CreateInstance(definition.MakeGenericType(arguments), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, constructorArguments, null);
      try
      {
        return Activator.CreateInstance(definition.MakeGenericType(arguments), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, constructorArguments, null);
      }
      catch (Exception ex) when (ex is NotSupportedException || ex is MissingMethodException || ex is TypeLoadException)
      {
        return null;
      }
    }

    /// <summary>
    /// Keeps the constructors of the generic handlers and readers <see cref="Instantiate"/> creates, so NativeAOT can create them for the application's classes
    /// (reference type arguments share one compiled code) with a serializer without a context. Referenced only by those constructors (LsMsgPackSerializer, LsMsgPackHttpSerializer):
    /// an application that creates its serializers with a context does not get this code.
    /// </summary>
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicConstructors, typeof(ObjectHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicConstructors, typeof(ObjectReader<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicConstructors, typeof(BoxedHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors, typeof(BoxedReader<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicConstructors, typeof(ListHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicConstructors, typeof(ListReader<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicConstructors, typeof(ArrayHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicConstructors, typeof(ArrayReader<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicConstructors, typeof(ExtensionHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicConstructors, typeof(ExtensionReader<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(Creator<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicFields, typeof(System.Numerics.Vector2))] // framework structs written by their public fields (SerializationRules.IsFieldStruct)
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicFields, typeof(System.Numerics.Vector3))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicFields, typeof(System.Numerics.Vector4))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicFields, typeof(System.Numerics.Quaternion))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicFields, typeof(System.Numerics.Plane))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicFields, typeof(System.Numerics.Matrix3x2))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicFields, typeof(System.Numerics.Matrix4x4))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(System.Collections.Generic.List<>))] // created by Core: the concrete types of the collection interfaces, the collections read boxed
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(System.Collections.Generic.Dictionary<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(System.Collections.Generic.HashSet<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(PropWriter<,,>))] // the properties of classes whose type is a class (PropWriter.SharesCode)
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(PropReader<,>))]
    internal static void ReflectionInstantiations()
    {
#if NET8_0_OR_GREATER
      // Never run (kept by the DynamicDependency of the constructors without a context): the filter modes of PropWriter are structs, so NativeAOT makes its shared code
      // only for the instantiations named in the code. Without dynamic code the typed ShouldSerialize modes are not used (no typed delegate), BoxedFilter is.
      GC.KeepAlive(new PropWriter<object, object, NoFilter>(null, null, (MethodInfo)null));
      GC.KeepAlive(new PropWriter<object, object, DefaultValuesFilter>(null, null, (MethodInfo)null));
      GC.KeepAlive(new PropWriter<object, object, BoxedFilter>(null, null, (MethodInfo)null));
#endif
    }

    /// <summary>
    /// NativeAOT without generated code for the type: when the application does not keep its model, trimming removes the properties' metadata and a class would be written
    /// and read as an empty object, silently. A class without any property or field is refused instead (one without members at all can't be told apart from a trimmed one).
    /// Called once per plan; the test of <see cref="RuntimeFeature.IsDynamicCodeSupported"/> itself is a constant for ILC (nothing of this with a JIT).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    internal void ThrowIfTrimmed(Type type)
    {
#if NET8_0_OR_GREATER
      if (RuntimeFeature.IsDynamicCodeSupported || Generated(type) != null)
        return;
      const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
      if (type.GetProperties(members).Length != 0 || type.GetFields(members).Length != 0)
        return;
      string keep = TypeResolver.IsFrameworkAssembly(type.Assembly)
        ? $"Keep its members (e.g. [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields, typeof({type.Name}))] in the application)"
        : $"Keep the model (e.g. <TrimmerRootAssembly Include=\"{type.Assembly.GetName().Name}\" /> in the project file)";
      throw new MsgPackException($"{type} has no properties or fields: without dynamic code (NativeAOT) and without generated code for it, the members of a type the application does not keep are trimmed away. "
        + keep + ", or create the serializer with the context of LsMsgPack's source generator.");
#endif
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
      return CreateInstantiation(definition, arguments, new object[] { constructorArgument });
    }
  }
}
