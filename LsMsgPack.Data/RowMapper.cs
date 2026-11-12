using LsMsgPack;
using LsMsgPack.Generated;
using LsMsgPack.Meta;
using LsMsgPack.Reading;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LsMsgPack.Data
{
  /// <summary>
  /// Creates an object of <typeparamref name="T"/> per row and sets the properties the columns match: the properties the serializer reads (its static filters and creation),
  /// matched by <c>[Column(Name = ...)]</c> or the property id, then by name ignoring case, then ignoring case and underscores (<c>first_name</c>: FirstName); not the ones with <c>[NotMapped]</c>.
  /// One per serializer, type and columns (names and field types). A class created by the constructor that takes its values (<see cref="ObjectConstructor"/>, immutable classes) gets the values boxed.
  /// </summary>
  internal sealed class RowMapper<T> where T : class
  {
    private readonly ReadPlan _plan;
    private readonly string[] _names;
    private readonly Type[] _types;
    private readonly ColumnSetter<T>[] _setters;

    /// <summary>
    /// The constructor that takes the values, null when the object is created first and then set.
    /// </summary>
    private readonly ObjectConstructor _constructor;
    private readonly BoxedColumnSetter<T>[] _arguments;
    private readonly FullPropertyInfo[] _infos;
    private readonly MsgPackOptions _options;

    private RowMapper(Serializer serializer, ReadPlan plan, IDataRecord record)
    {
      _plan = plan;
      _options = serializer.Options;
      if (plan.WithValues != null)
      {
        _constructor = ObjectConstructor.Of(typeof(T));
        _infos = new FullPropertyInfo[plan.Props.Length];
        for (int t = 0; t < _infos.Length; t++)
          _infos[t] = plan.Props[t].Info;
      }
      int count = record.FieldCount;
      _names = new string[count];
      _types = new Type[count];
      for (int t = 0; t < count; t++)
      {
        _names[t] = record.GetName(t);
        _types[t] = record.GetFieldType(t);
      }

      PropReader[] props = plan.Props;
      List<ColumnSetter<T>> setters = new List<ColumnSetter<T>>(count);
      bool[] taken = new bool[props.Length];
      for (int t = 0; t < props.Length; t++)
        taken[t] = NotMapped(props[t].Info);
      for (int t = 0; t < count; t++)
      {
        int found = Match(props, taken, _names[t]);
        if (found < 0)
          continue; // a column without a property: not read (Dapper does the same)
        taken[found] = true;
        setters.Add(_constructor != null ? new BoxedColumnSetter<T>(serializer, props[found].Info, t) : ColumnSetter<T>.Create(serializer, props[found].Info, record, _types[t], t));
      }
      _setters = setters.ToArray();
      if (_constructor != null)
      {
        _arguments = new BoxedColumnSetter<T>[_setters.Length];
        for (int t = 0; t < _arguments.Length; t++)
          _arguments[t] = (BoxedColumnSetter<T>)_setters[t];
      }
    }

    internal static RowMapper<T> For(Serializer serializer, IDataRecord record, RowMapper<T> last)
    {
      if (last != null && last.Matches(record))
        return last;
      ReadPlan plan = serializer.GetReadPlan(typeof(T));
      if (plan is null)
        throw new MsgPackException($"{typeof(T).FullName} is not a class with properties that can be created, rows cannot be read into it.");
      return new RowMapper<T>(serializer, plan, record);
    }

    private bool Matches(IDataRecord record)
    {
      if (record.FieldCount != _names.Length)
        return false;
      for (int t = 0; t < _names.Length; t++)
        if (!string.Equals(record.GetName(t), _names[t], StringComparison.Ordinal) || record.GetFieldType(t) != _types[t])
          return false;
      return true;
    }

    /// <param name="check">IsDBNull on every column (see <see cref="NullChecks"/>)</param>
    internal T Map(IDataRecord record, bool check)
    {
      if (_constructor != null)
        return WithValues(record);
      T target = (T)_plan.Create();
      ColumnSetter<T>[] setters = _setters;
      for (int t = 0; t < setters.Length; t++)
        setters[t].Set(record, target, check);
      return target;
    }

    /// <summary>
    /// Collects the values for the constructor (and the other properties), then creates the object as the serializer does (missing values: what a nil sets, parameters without a property: their default).
    /// </summary>
    private T WithValues(IDataRecord record)
    {
      ObjectConstructor.Arguments arguments = _constructor.Collect();
      BoxedColumnSetter<T>[] setters = _arguments;
      for (int t = 0; t < setters.Length; t++)
        if (setters[t].TryRead(record, out object value))
          arguments.Set(setters[t].Info, value);
      return (T)arguments.Create(_infos, _options);
    }

    /// <returns>The index of the property the column sets, -1 for none (none, or several match equally)</returns>
    private static int Match(PropReader[] props, bool[] taken, string column)
    {
      for (int t = 0; t < props.Length; t++)
        if (!taken[t] && string.Equals(ColumnName(props[t].Info), column, StringComparison.Ordinal))
          return t;
      int found = Unique(props, taken, column, StringComparison.OrdinalIgnoreCase, false);
      if (found != -2)
        return found;
      found = Unique(props, taken, column, StringComparison.OrdinalIgnoreCase, true);
      return found < 0 ? -1 : found;
    }

    /// <returns>The one property matching, -1 when several do, -2 when none does</returns>
    private static int Unique(PropReader[] props, bool[] taken, string column, StringComparison comparison, bool withoutUnderscores)
    {
      string key = withoutUnderscores ? column.Replace("_", string.Empty) : column;
      int found = -2;
      for (int t = 0; t < props.Length; t++)
      {
        if (taken[t])
          continue;
        string name = ColumnName(props[t].Info);
        if (withoutUnderscores)
          name = name.Replace("_", string.Empty);
        if (!string.Equals(name, key, comparison))
          continue;
        if (found >= 0)
          return -1;
        found = t;
      }
      return found;
    }

    /// <summary>
    /// <c>[NotMapped]</c> (System.ComponentModel.DataAnnotations.Schema, Entity Framework): no column sets the property.
    /// </summary>
    private static bool NotMapped(FullPropertyInfo info)
    {
      return info.CustomAttributes != null && info.CustomAttributes.ContainsKey("NotMappedAttribute");
    }

    /// <summary>
    /// The column a property maps to: <c>[Column(Name = ...)]</c> (System.ComponentModel.DataAnnotations.Schema, Entity Framework, linq2db: found by its class name), otherwise the property id (the name, or what the property id resolvers give).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
    internal static string ColumnName(FullPropertyInfo info)
    {
      if (info.CustomAttributes != null && info.CustomAttributes.TryGetValue("ColumnAttribute", out object column))
      {
        string name = column.GetType().GetProperty("Name")?.GetValue(column) as string;
        if (!string.IsNullOrEmpty(name))
          return name;
      }
      return info.PropertyId as string ?? info.Name;
    }
  }

  /// <summary>
  /// Sets one property from one column.
  /// </summary>
  internal abstract class ColumnSetter<T> where T : class
  {
    protected readonly int Ordinal;

    protected ColumnSetter(int ordinal)
    {
      Ordinal = ordinal;
    }

    /// <summary>
    /// Sets the value of the column, leaves the property as the constructor made it for a DBNull (as for a nil in the array layout).
    /// </summary>
    /// <param name="check">IsDBNull before the getter, false: only for a column that had a NULL (see <see cref="NullChecks"/>)</param>
    internal abstract void Set(IDataRecord record, T target, bool check);

    /// <param name="record">On the first row: a column of another type than the property is read with the property type's getter when the provider converts its value (e.g. SQLite's GetInt32 of an INTEGER, GetDateTime of a TEXT)</param>
    internal static ColumnSetter<T> Create(Serializer serializer, FullPropertyInfo info, IDataRecord record, Type column, int ordinal)
    {
      MemberInfo member = info.Member; // a property or a field
      Type type = info.DeclaredType;
      Type underlying = Nullable.GetUnderlyingType(type);
      bool same = type == column || underlying == column;
      if ((same || ProviderConverts(record, ordinal, underlying ?? type)) && !member.DeclaringType.IsValueType)
      {
        ColumnSetter<T> typed = CreateTyped(serializer, member, type, underlying, ordinal, same && record is DbDataReader);
        if (typed != null)
          return typed;
      }
      return new BoxedColumnSetter<T>(serializer, info, ordinal);
    }

    /// <summary>
    /// The type has a typed getter of IDataRecord and it reads the value of the current row (decided once, on the first row; a DBNull there: the value is converted boxed).
    /// </summary>
    private static bool ProviderConverts(IDataRecord record, int ordinal, Type type)
    {
      if (record.IsDBNull(ordinal))
        return false;
      try
      {
        if (type == typeof(int)) record.GetInt32(ordinal);
        else if (type == typeof(long)) record.GetInt64(ordinal);
        else if (type == typeof(string)) record.GetString(ordinal);
        else if (type == typeof(short)) record.GetInt16(ordinal);
        else if (type == typeof(byte)) record.GetByte(ordinal);
        else if (type == typeof(bool)) record.GetBoolean(ordinal);
        else if (type == typeof(double)) record.GetDouble(ordinal);
        else if (type == typeof(float)) record.GetFloat(ordinal);
        else if (type == typeof(decimal)) record.GetDecimal(ordinal);
        else if (type == typeof(DateTime)) record.GetDateTime(ordinal);
        else if (type == typeof(Guid)) record.GetGuid(ordinal);
        else if (type == typeof(char)) record.GetChar(ordinal);
        else
          return false;
        return true;
      }
      catch (Exception) // e.g. InvalidCastException: a provider that only reads its own type (SqlClient)
      {
        return false;
      }
    }

    /// <summary>
    /// The column has the property's type (or the property is its Nullable): a typed getter and setter, null when they cannot be made (the value is converted boxed).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = TrimReasons.Fallback)]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.Fallback)]
    [UnconditionalSuppressMessage("Trimming", "IL2071", Justification = TrimReasons.Fallback)] // the struct constraint of NullableOf (reported by the .NET 10 tools), a failure converts the value boxed
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = TrimReasons.Fallback)]
    private static ColumnSetter<T> CreateTyped(Serializer serializer, MemberInfo property, Type type, Type underlying, int ordinal, bool dbReader)
    {
      try
      {
        string factory = underlying is null ? nameof(Typed) : nameof(NullableOf);
        MethodInfo method = typeof(ColumnSetter<T>).GetMethod(factory, BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(underlying ?? type);
        return (ColumnSetter<T>)method.Invoke(null, new object[] { serializer, property, ordinal, dbReader });
      }
      catch (Exception) // e.g. a runtime that cannot create the generic code, or a setter that cannot be bound
      {
        return null;
      }
    }

    private static ColumnSetter<T> Typed<TValue>(Serializer serializer, MemberInfo property, int ordinal, bool dbReader)
    {
      Action<T, TValue> set = Setter<TValue>(serializer, property);
      return set is null ? null : new TypedColumnSetter<T, TValue>(ordinal, Getter<TValue>(dbReader), set);
    }

    private static ColumnSetter<T> NullableOf<TValue>(Serializer serializer, MemberInfo property, int ordinal, bool dbReader) where TValue : struct
    {
      Action<T, TValue?> set = Setter<TValue?>(serializer, property);
      return set is null ? null : new NullableColumnSetter<T, TValue>(ordinal, Getter<TValue>(dbReader), set);
    }

    /// <summary>
    /// The setter of the source generator, otherwise a delegate bound to the set method (a field: compiled from an expression), null when there is none (a readonly field).
    /// </summary>
    private static Action<T, TValue> Setter<TValue>(Serializer serializer, MemberInfo member)
    {
      if (!member.DeclaringType.IsAssignableFrom(typeof(T)))
        return null;
      if (serializer.Generated(member.DeclaringType)?.FindMember(member) is GeneratedProperty<T, TValue> generated && generated.Set != null)
        return generated.Set;
      if (member is FieldInfo field)
        return serializer.CanBindDelegates && !field.IsStatic ? FieldDelegates.Setter<T, TValue>(field) : null;
      PropertyInfo property = (PropertyInfo)member;
      if (!serializer.CanBindDelegates)
        return null;
      MethodInfo set = property.GetSetMethod(true);
      if (set is null || set.IsStatic)
        return null;
      return (Action<T, TValue>)Delegate.CreateDelegate(typeof(Action<T, TValue>), set);
    }

    /// <summary>
    /// The typed getter of IDataRecord for the type, <see cref="DbDataReader.GetFieldValue{T}"/> or a cast of GetValue otherwise.
    /// </summary>
    private static Func<IDataRecord, int, TValue> Getter<TValue>(bool dbReader)
    {
      Type type = typeof(TValue);
      object getter;
      if (type == typeof(int)) getter = (Func<IDataRecord, int, int>)((r, i) => r.GetInt32(i));
      else if (type == typeof(long)) getter = (Func<IDataRecord, int, long>)((r, i) => r.GetInt64(i));
      else if (type == typeof(string)) getter = (Func<IDataRecord, int, string>)((r, i) => r.GetString(i));
      else if (type == typeof(short)) getter = (Func<IDataRecord, int, short>)((r, i) => r.GetInt16(i));
      else if (type == typeof(byte)) getter = (Func<IDataRecord, int, byte>)((r, i) => r.GetByte(i));
      else if (type == typeof(bool)) getter = (Func<IDataRecord, int, bool>)((r, i) => r.GetBoolean(i));
      else if (type == typeof(double)) getter = (Func<IDataRecord, int, double>)((r, i) => r.GetDouble(i));
      else if (type == typeof(float)) getter = (Func<IDataRecord, int, float>)((r, i) => r.GetFloat(i));
      else if (type == typeof(decimal)) getter = (Func<IDataRecord, int, decimal>)((r, i) => r.GetDecimal(i));
      else if (type == typeof(DateTime)) getter = (Func<IDataRecord, int, DateTime>)((r, i) => r.GetDateTime(i));
      else if (type == typeof(Guid)) getter = (Func<IDataRecord, int, Guid>)((r, i) => r.GetGuid(i));
      else if (type == typeof(char)) getter = (Func<IDataRecord, int, char>)((r, i) => r.GetChar(i));
      else if (dbReader) getter = (Func<IDataRecord, int, TValue>)((r, i) => ((DbDataReader)r).GetFieldValue<TValue>(i));
      else getter = (Func<IDataRecord, int, TValue>)((r, i) => (TValue)r.GetValue(i));
      return (Func<IDataRecord, int, TValue>)getter;
    }
  }

  internal sealed class TypedColumnSetter<T, TValue> : ColumnSetter<T> where T : class
  {
    private readonly Func<IDataRecord, int, TValue> _get;
    private readonly Action<T, TValue> _set;

    /// <summary>
    /// The column had a NULL (in any call with this mapper): it is checked on every row. Only ever set to true (by any thread).
    /// </summary>
    private bool _hadNull;

    internal TypedColumnSetter(int ordinal, Func<IDataRecord, int, TValue> get, Action<T, TValue> set) : base(ordinal)
    {
      _get = get;
      _set = set;
    }

    internal override void Set(IDataRecord record, T target, bool check)
    {
      int ordinal = Ordinal;
      if (!check && !_hadNull)
      {
        SetUnchecked(record, target, ordinal);
        return;
      }
      if (record.IsDBNull(ordinal))
      {
        if (!_hadNull)
          FirstNull(record, ordinal);
        return;
      }
      _set(target, _get(record, ordinal));
    }

    private void FirstNull(IDataRecord record, int ordinal)
    {
      _hadNull = true;
      NullChecks.Learn(record, ordinal, _get);
    }

    /// <summary>
    /// The getter without IsDBNull (apart from Set: a method with a try block is not inlined, and the checked path stays as it was).
    /// </summary>
    private void SetUnchecked(IDataRecord record, T target, int ordinal)
    {
      try
      {
        _set(target, _get(record, ordinal));
      }
      catch (Exception) when (record.IsDBNull(ordinal)) // a NULL after all: the property stays as it was
      {
        _hadNull = true;
      }
    }
  }

  internal sealed class NullableColumnSetter<T, TValue> : ColumnSetter<T> where T : class where TValue : struct
  {
    private readonly Func<IDataRecord, int, TValue> _get;
    private readonly Action<T, TValue?> _set;

    /// <summary>
    /// The column had a NULL (in any call with this mapper): it is checked on every row. Only ever set to true (by any thread).
    /// </summary>
    private bool _hadNull;

    internal NullableColumnSetter(int ordinal, Func<IDataRecord, int, TValue> get, Action<T, TValue?> set) : base(ordinal)
    {
      _get = get;
      _set = set;
    }

    internal override void Set(IDataRecord record, T target, bool check)
    {
      int ordinal = Ordinal;
      if (!check && !_hadNull)
      {
        SetUnchecked(record, target, ordinal);
        return;
      }
      if (record.IsDBNull(ordinal))
      {
        if (!_hadNull)
          FirstNull(record, ordinal);
        return;
      }
      _set(target, _get(record, ordinal));
    }

    private void FirstNull(IDataRecord record, int ordinal)
    {
      _hadNull = true;
      NullChecks.Learn(record, ordinal, _get);
    }

    /// <summary>
    /// The getter without IsDBNull (apart from Set: a method with a try block is not inlined, and the checked path stays as it was).
    /// </summary>
    private void SetUnchecked(IDataRecord record, T target, int ordinal)
    {
      try
      {
        _set(target, _get(record, ordinal));
      }
      catch (Exception) when (record.IsDBNull(ordinal)) // a NULL after all: the property stays as it was
      {
        _hadNull = true;
      }
    }
  }

  /// <summary>
  /// A column of another type than the property (e.g. SQLite's long into an int, an integer into an enum): the boxed value, converted as the serializer converts a value of another type.
  /// </summary>
  internal sealed class BoxedColumnSetter<T> : ColumnSetter<T> where T : class
  {
    internal readonly FullPropertyInfo Info;
    private readonly Type _type;
    private readonly MsgPackOptions _options;

    internal BoxedColumnSetter(Serializer serializer, FullPropertyInfo info, int ordinal) : base(ordinal)
    {
      Info = info;
      _type = info.DeclaredType;
      _options = serializer.Options;
    }

    internal override void Set(IDataRecord record, T target, bool check)
    {
      if (TryRead(record, out object value))
        Info.SetValue(target, value);
    }

    /// <returns>false for DBNull</returns>
    internal bool TryRead(IDataRecord record, out object value)
    {
      value = record.GetValue(Ordinal);
      if (value is DBNull || value is null)
        return false;
      value = ValueConverter.ConvertDeserializeValue(value, _type, _options, Info);
      return true;
    }
  }
}
