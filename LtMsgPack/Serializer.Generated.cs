using LsMsgPack.Generated;
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
