#if KEEPTRACK
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace LsMsgPack
{
  public class MsgPackTypeConverter:TypeConverter {

    private static readonly Type[] supportedTypes = new Type[] { typeof(string), typeof(MsgPackTypeId), typeof(byte) };
    
    public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) {
      return supportedTypes.Contains(sourceType);
    }

    public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType) {
      return supportedTypes.Contains(destinationType);
    }

    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value) {
      if(value is string) {
        MsgPackMeta.PackDef def;
        if(MsgPackMeta.FromName.TryGetValue(value.ToString().ToLowerInvariant(), out def)) return def.TypeId;
        MsgPackTypeId ret;
        if(Enum.TryParse(value.ToString(), true, out ret)) return ret;
      }
      if(value is MsgPackTypeId) return value;
      if(value is byte) return Convert.ChangeType(value, typeof(MsgPackTypeId));
      return base.ConvertFrom(context, culture, value);
    }

    public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType) {
      if(destinationType == typeof(MsgPackTypeId)) return ConvertFrom(context, culture, value);
      if(destinationType == typeof(string)) {
        if(value is MsgPackTypeId) return MsgPackMeta.FromTypeId[(MsgPackTypeId)value].OfficialName;
        MsgPackTypeId item = (MsgPackTypeId)ConvertFrom(context, culture, value);
        return MsgPackMeta.FromTypeId[item].OfficialName;
      }
      if(destinationType == typeof(byte)) {
        MsgPackTypeId item = (MsgPackTypeId)ConvertFrom(context, culture, value);
        return Convert.ChangeType(item, typeof(byte));
      }
      return base.ConvertTo(context, culture, value, destinationType);
    }


    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context) {
      return new StandardValuesCollection(MsgPackMeta.AllPacks.Select(p => p.OfficialName).ToArray());
    }

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) {
      return true;
    }

    public override bool GetStandardValuesSupported(ITypeDescriptorContext context) {
      return true;
    }

    public override bool IsValid(ITypeDescriptorContext context, object value) {
      MsgPackTypeId item = (MsgPackTypeId)ConvertFrom(context, CultureInfo.InvariantCulture, value);
      return Enum.IsDefined(typeof(MsgPackTypeId), item);
    }


    public override bool GetPropertiesSupported(ITypeDescriptorContext context) {
      return true;
    }

    public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object value, Attribute[] attributes) {
      return new PropertyDescriptorCollection(
        new PropertyDescriptor[] {
          new MsgPackTypeDescriptor(typeof(MsgPackTypeId), "Description", typeof(string), new Attribute[] { new ReadOnlyAttribute(true) })
        });
    }

    class MsgPackTypeDescriptor: SimplePropertyDescriptor {

      public MsgPackTypeDescriptor(Type componentType, string name, Type propertyType) : base(componentType, name, propertyType) { }
      public MsgPackTypeDescriptor(Type componentType, string name, Type propertyType, Attribute[] attributes) : base(componentType, name, propertyType, attributes) { }

      public override object GetValue(object component) {
        MsgPackTypeId item;
        if(component is MsgPackTypeId) item = (MsgPackTypeId)component;
        else return null;
        return MsgPackItem.GetTypeDescriptor(item).Description;
      }

      public override void SetValue(object component, object value) {
        return; // ignore setting descriptions
      }
    }
  }
}
#endif