using LsMsgPack;
using System.Collections.Generic;

namespace ObjectDebugger
{

  public class PrimitiveObject
  {
    public string Type { get; set; }

    public MsgPackItem FirstItemRef { get; set; }
    public MsgPackItem LastItemRef { get; set; }

    
  }

  public class ComplexObject:PrimitiveObject
  {
    // shown as children in the treeview
    public List<ComplexObject> ComplexProperties { get; set; }=new List<ComplexObject>();

    // shown as children in treeview under complex props
    public List<ComplexObject> CollectionProperties { get; set; } = new List<ComplexObject>();// type will probably change

    // shown in the property grid
    public List<PrimitiveProperty> PrimitiveProperties { get; set; } = new List<PrimitiveProperty>();

    // a place to backreference a treeNode
    public object Tag { get; set; }
  }
  
  public class PrimitiveProperty: PrimitiveObject // may ofcourse also be a complex object or a collection
  {
    public string Name { get; set; }
  }

  // public class collection ...
}
