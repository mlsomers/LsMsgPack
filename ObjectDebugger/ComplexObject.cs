using System.Collections.Generic;

namespace ObjectDebugger
{

  public class PrimitiveObject
  {
    public string Type { get; set; }
  }

  public class ComplexObject:PrimitiveObject
  {
    public List<PropertyAbstraction> Properties { get; set; }=new List<PropertyAbstraction>();
  }
  
  public class PropertyAbstraction: PrimitiveObject
  {
    public string Name { get; set; }
  }
}
