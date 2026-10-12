using LsMsgPack.Generated;
using LsMsgPack.Meta;
using LtMsgPack.Extensions;
using LtMsgPack.Reading;
using LtMsgPack.Writing;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace LtMsgPack.Generated
{
  /// <summary>
  /// The factories the code of the source generator calls to describe its types (see <see cref="LtMsgPackContext"/>). For generated code: may change between releases (see <see cref="GeneratedContract"/>).
  /// <para>Each description creates the serializer's generic handlers and readers of its type with <c>new</c>, so compilers ahead of time (NativeAOT, WebAssembly AOT) see the instantiations.</para>
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public static class GeneratedValues
  {
    /// <summary>
    /// A class, abstract class or interface with properties (or without: then only the instantiations).
    /// </summary>
    /// <param name="creator">Calls the accessible parameterless constructor, null when there is none</param>
    /// <param name="properties">The properties the type declares (<see cref="Property{TTarget, TValue}"/>)</param>
    public static GeneratedType Object<T>(Func<T> creator, params GeneratedProperty[] properties) where T : class
    {
      return new GeneratedObjectType<T>(creator, properties);
    }

    /// <param name="get">null when the generated code cannot call the getter</param>
    /// <param name="set">null when the generated code cannot call the setter (not accessible, or init-only)</param>
    public static GeneratedProperty Property<TTarget, TValue>(string name, Func<TTarget, TValue> get, Action<TTarget, TValue> set) where TTarget : class
    {
      return new LtGeneratedProperty<TTarget, TValue>(name, get, set);
    }

    public static GeneratedType Enum<T>() where T : struct
    {
      return new GeneratedEnumType<T>();
    }

    /// <summary>
    /// <c>T?</c> (the description of T is added separately).
    /// </summary>
    public static GeneratedType Nullable<T>() where T : struct
    {
      return new GeneratedNullableType<T>();
    }

    public static GeneratedType List<T>()
    {
      return new GeneratedListType<T>();
    }

    /// <summary>
    /// <c>T[]</c>.
    /// </summary>
    public static GeneratedType Array<T>()
    {
      return new GeneratedArrayType<T>();
    }

    /// <summary>
    /// Another collection or dictionary (filled by the runtime as before).
    /// </summary>
    /// <param name="creator">Calls the accessible parameterless constructor, null when there is none</param>
    public static GeneratedType Collection<T>(Func<T> creator)
    {
      return new LtGeneratedType<T>(creator);
    }

    /// <summary>
    /// Any other type the serializer writes and reads boxed (structs with properties, framework types...).
    /// </summary>
    public static GeneratedType Boxed<T>()
    {
      return new LtGeneratedType<T>(null);
    }
  }

  /// <summary>
  /// The generic instantiations of the serializer a generated type creates without <see cref="Type.MakeGenericType"/>.
  /// </summary>
  internal interface IGeneratedInstantiations
  {
    /// <param name="definition">The generic type definition of the handler or reader (<c>typeof(EnumHandler&lt;&gt;)</c>), instantiated for the generated type (or its element or underlying type)</param>
    /// <param name="argument">What the constructor takes (the serializer, an inner handler or reader, an extension), null for none</param>
    /// <returns>null when the type does not provide it</returns>
    object Instantiate(Type definition, object argument);
  }

  /// <summary>
  /// A generated type (see <see cref="GeneratedValues.Boxed{T}"/>): the instantiations every type may need.
  /// </summary>
  internal class LtGeneratedType<T> : GeneratedType, IGeneratedInstantiations
  {
    internal LtGeneratedType(Func<T> creator, GeneratedProperty[] properties = null) : base(typeof(T), Box(creator), properties)
    {
    }

    private static Func<object> Box(Func<T> creator)
    {
      if (creator is null)
        return null;
      return () => creator();
    }

    public virtual object Instantiate(Type definition, object argument)
    {
      if (definition == typeof(BoxedHandler<>))
        return new BoxedHandler<T>((Serializer)argument);
      if (definition == typeof(BoxedReader<>))
        return new BoxedReader<T>();
      if (definition == typeof(ExtensionHandler<>))
        return new ExtensionHandler<T>((LtExtension)argument);
      if (definition == typeof(ExtensionReader<>))
        return argument is LtExtension<T> extension ? new ExtensionReader<T>(extension) : null;
      return null;
    }
  }

  internal sealed class GeneratedObjectType<T> : LtGeneratedType<T> where T : class
  {
    internal GeneratedObjectType(Func<T> creator, GeneratedProperty[] properties) : base(creator, properties)
    {
    }

    public override object Instantiate(Type definition, object argument)
    {
      if (definition == typeof(ObjectHandler<>))
        return new ObjectHandler<T>((Serializer)argument);
      if (definition == typeof(ObjectReader<>))
        return new ObjectReader<T>((Serializer)argument);
      return base.Instantiate(definition, argument);
    }
  }

  internal sealed class GeneratedEnumType<T> : LtGeneratedType<T> where T : struct
  {
    internal GeneratedEnumType() : base(null)
    {
    }

    public override object Instantiate(Type definition, object argument)
    {
      if (definition == typeof(EnumHandler<>))
        return new EnumHandler<T>();
      if (definition == typeof(EnumReader<>))
        return new EnumReader<T>();
      return base.Instantiate(definition, argument);
    }
  }

  internal sealed class GeneratedNullableType<T> : LtGeneratedType<T?> where T : struct
  {
    internal GeneratedNullableType() : base(null)
    {
    }

    public override object Instantiate(Type definition, object argument)
    {
      if (definition == typeof(NullableHandler<>))
        return new NullableHandler<T>((ValueHandler<T>)argument);
      if (definition == typeof(NullableReader<>))
        return new NullableReader<T>((ValueReader<T>)argument);
      return base.Instantiate(definition, argument);
    }
  }

  internal sealed class GeneratedListType<T> : LtGeneratedType<List<T>>
  {
    internal GeneratedListType() : base(() => new List<T>())
    {
    }

    public override object Instantiate(Type definition, object argument)
    {
      if (definition == typeof(ListHandler<>))
        return new ListHandler<T>((Serializer)argument);
      if (definition == typeof(ListReader<>))
        return new ListReader<T>((Serializer)argument);
      return base.Instantiate(definition, argument);
    }
  }

  internal sealed class GeneratedArrayType<T> : LtGeneratedType<T[]>
  {
    internal GeneratedArrayType() : base(null)
    {
    }

    public override object Instantiate(Type definition, object argument)
    {
      if (definition == typeof(ArrayHandler<>))
        return new ArrayHandler<T>((Serializer)argument);
      if (definition == typeof(ArrayReader<>))
        return new ArrayReader<T>((Serializer)argument);
      return base.Instantiate(definition, argument);
    }
  }

  /// <summary>
  /// The generated accessors of a property, creating the serializer's typed property writer and reader.
  /// </summary>
  internal sealed class LtGeneratedProperty<TTarget, TValue> : GeneratedProperty<TTarget, TValue> where TTarget : class
  {
    internal LtGeneratedProperty(string name, Func<TTarget, TValue> get, Action<TTarget, TValue> set) : base(name, get, set)
    {
    }

    /// <returns>null without a generated getter</returns>
    internal PropWriter CreateWriter(Serializer serializer, FullPropertyInfo info)
    {
      return Get is null ? null : new PropWriter<TTarget, TValue>(serializer, info, Get);
    }

    /// <returns>null without a generated setter</returns>
    internal PropReader CreateReader(Serializer serializer, FullPropertyInfo info)
    {
      return Set is null ? null : new PropReader<TTarget, TValue>(serializer, info, Set);
    }
  }
}
