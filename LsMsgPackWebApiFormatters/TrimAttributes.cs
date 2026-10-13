// The trim and AOT attributes for this .NET Standard build (ILLink and ILC recognize them by name): a trimmed HttpClient app gets the warnings of the constructors without a context, as with LtMsgPack.
namespace System.Diagnostics.CodeAnalysis
{
  [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class, Inherited = false)]
  internal sealed class RequiresUnreferencedCodeAttribute : Attribute
  {
    public RequiresUnreferencedCodeAttribute(string message)
    {
      Message = message;
    }

    public string Message { get; }

    public string Url { get; set; }
  }

  [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class, Inherited = false)]
  internal sealed class RequiresDynamicCodeAttribute : Attribute
  {
    public RequiresDynamicCodeAttribute(string message)
    {
      Message = message;
    }

    public string Message { get; }

    public string Url { get; set; }
  }
}
