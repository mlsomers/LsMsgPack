using System;

namespace LsMsgPack.Types.Extensions
{
  /// <summary>
  /// Implementing the abstract members will not result in a fully working Extension. In addition the following will need to be overridden:
  /// <code>
  /// public override object Value{get;set:}
  /// public override MsgPackItem Read(MsgPackTypeId typeId, Stream data)
  /// public override byte[] ToBytes()
  /// </code>
  /// It's easier to inherit from <see cref="BaseCustomExt{TSelf,Ttype}"/> or <see cref="BaseCustomExtNonCached{TSelf,Ttype}"/>
  /// </summary>
  public abstract class AbstractCustomExt<TSelf> : MpExt, ICustomExt where TSelf : AbstractCustomExt<TSelf>, new()
  {
    public AbstractCustomExt() : base() { }

    public AbstractCustomExt(MsgPackSettings settings) : base(settings) { }

    public AbstractCustomExt(MpExt ext) : this()
    {
      CopyBaseDataFrom(ext);
    }

    public MsgPackItem Create(MsgPackSettings settings, MpExt value, object val)
    {
      TSelf ret = new TSelf() { Settings = settings };
      if (value is null)
        ret.Value = val;
      else { 
        ret.CopyBaseDataFrom(value);
      }
      return ret;
    }

    public override sbyte TypeSpecifier
    {
      get
      {
        if (base.TypeSpecifier == 0)
          base.TypeSpecifier = DefaultTypeSpecifier;
        return base.TypeSpecifier;
      }
    }

    /// <summary>
    /// Set a default specifier, note that it can be overridden globally in an application using the static CustomTypeSpecifier property
    /// </summary>
    protected abstract sbyte DefaultTypeSpecifier { get; }

    /// <summary>
    /// It may be desirable to override this if the type must be exact (not IsAssignableFrom())
    /// </summary>
    /// <param name="type">Type wanting to be serialized</param>
    /// <returns>True if this class supports serializing the specified type</returns>
    public abstract bool SupportsType(Type type);
  }
}
