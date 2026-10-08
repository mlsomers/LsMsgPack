using LsMsgPack.Meta;
using System;
using System.Reflection;

namespace LsMsgPack
{
  /// <summary>
  /// The type names the data can use (type ids and the names of the indexed schema), shared by LsMsgPack and LtMsgPack (see docs/schema.md, polymorphic class-hierarchy support).
  /// <para>The readers find the types reachable from the type they read (generic arguments, element types, base types, public properties) by themselves. Types that are not reachable,
  /// such as the implementations of an interface in another assembly or the classes of values declared as object, need their assembly registered here once (or allowed by an
  /// <see cref="TypeResolving.Types.AllowedTypesGuard"/>, which registers what it allows).</para>
  /// </summary>
  public static class MsgPackTypes
  {
    /// <summary>
    /// Makes the types of the assembly known by their names.
    /// </summary>
    public static void CacheAssemblyTypes(Assembly assembly)
    {
      if (assembly is null)
        throw new ArgumentNullException(nameof(assembly));
      TypeResolver.CacheAssembly(assembly, null);
    }

    /// <summary>
    /// Makes the types of the assembly of <paramref name="type"/> known by their names (see <see cref="CacheAssemblyTypes(Assembly)"/>).
    /// </summary>
    public static void CacheAssemblyTypes(Type type)
    {
      if (type is null)
        throw new ArgumentNullException(nameof(type));
      TypeResolver.CacheAssembly(type.Assembly, type.Name);
    }
  }
}
