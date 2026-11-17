using LsMsgPack;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using LsMsgPack.Meta;

namespace LsMsgPackDebug
{
  /// <summary>
  /// A DateTime: the native extension (type 6, <see cref="DateTimeFormat.Native"/>: <see cref="DateTime.ToBinary"/>, the ticks and the Kind) or a timestamp of the specification (type -1, a moment in UTC).
  /// New items take the format of their settings' <see cref="MsgPackOptions.DateTimeFormat"/>.
  /// </summary>
  public class MpDateTime : MpExt
  {

    public MpDateTime() : base() { TypeSpecifier = CodeOf(Settings._nativeDateTime); }
    public MpDateTime(MsgPackSettings settings) : base(settings) { TypeSpecifier = CodeOf(settings._nativeDateTime); }

    /// <param name="native">The native extension (type 6), otherwise a timestamp (type -1), whatever the settings say (DateTimeOffset values are timestamps)</param>
    internal MpDateTime(MsgPackSettings settings, bool native) : base(settings ?? new MsgPackSettings()) { TypeSpecifier = CodeOf(native); }

    public MpDateTime(MpExt ext) : base(ext.Settings) // not base(), that creates default settings that CopyBaseDataFrom replaces
    {
      CopyBaseDataFrom(ext);
      value = IsNative ? ConvertNative(Settings, ext) : ConvertExtUtc(Settings, ext);
    }

    private static sbyte CodeOf(bool native)
    {
      return native ? MsgPackOptions.DateTimeExtensionType : (sbyte)-1;
    }

    /// <summary>
    /// The native extension (type 6: the ticks and the Kind), otherwise a timestamp (type -1).
    /// </summary>
    public bool IsNative
    {
      get { return TypeSpecifier == MsgPackOptions.DateTimeExtensionType; }
    }

    private DateTime value; // a timestamp: the moment in UTC; the native extension: the DateTime as it is (its Kind)

    /// <summary>
    /// The DateTime: the native extension as it was written (a Local value in the reader's time zone), a timestamp with the Kind of <see cref="MsgPackOptions.ReadDateTimeKind"/> (UTC by default).
    /// A DateTime of Kind Unspecified is set into a timestamp as <see cref="MsgPackOptions.UnspecifiedDateTimeKind"/> says.
    /// </summary>
#if KEEPTRACK
    [TypeConverter(typeof(IsoDateTimeConverter))] // the property grids of the explorers: the culture's format showed neither the fraction of a second nor the Kind
#endif
    public override object Value
    {
      get
      {
        return IsNative ? value : Settings.FromTimestamp(value);
      }
      set
      {
        bool native = IsNative;
        if (value is DateTime)
          this.value = native ? (DateTime)value : Settings.ToTimestamp((DateTime)value);
        else if (value is DateTimeOffset)
          this.value = ((DateTimeOffset)value).UtcDateTime; // not IConvertible, Convert.ToDateTime would throw an InvalidCastException
        else
          this.value = native ? Convert.ToDateTime(value) : Settings.ToTimestamp(Convert.ToDateTime(value));

        base.Value = native ? NativeBytes(Settings, this.value) : ToBaseValue(Settings, this.value);
      }
    }

    public override T GetTypedValue<T>()
    {
      return (T)Value;
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, Stream data)
    {
      base.Read(typeId, data);
      value = IsNative ? ConvertNative(Settings, this) : ConvertExtUtc(Settings, this);
      return this;
    }

    public override byte[] ToBytes()
    {
      if (base.Value is null || ((byte[])base.Value).Length <= 0)
        return IsNative ? Native(Settings, value).ToBytes() : FromDateTime(Settings, value).ToBytes();

      return base.ToBytes();
    }

    internal override void WriteTo(ByteWriter target)
    {
      if (GetType() != typeof(MpDateTime)) // a derived type may override ToBytes
      {
        target.Write(ToBytes());
        return;
      }

      if (base.Value is null || ((byte[])base.Value).Length <= 0)
      {
        if (IsNative)
          Native(Settings, value).WriteTo(target);
        else
          FromDateTime(Settings, value).WriteTo(target);
      }
      else
        WriteExt(target);
    }

    public override string ToString()
    {
      if (IsNative)
        return $"DateTime ({GetOfficialTypeName(TypeId)}) extension type {TypeSpecifier} with the value {value.ToString("o", System.Globalization.CultureInfo.InvariantCulture)} ({value.Kind})";
      return
        $"DateTime ({GetOfficialTypeName(TypeId)}) extension type {TypeSpecifier} with the value {value.ToLongDateString()}";
    }

    /// <summary>
    /// The DateTime of the native extension (type 6): <see cref="DateTime.FromBinary"/> of its 8 bytes in the byte order of the settings (checked, see <see cref="MsgPackOptions.FromNative"/>).
    /// </summary>
    internal static DateTime ConvertNative(MsgPackSettings settings, MpExt ext)
    {
      byte[] bytes = (byte[])ext.Value;
      if (ext.TypeSpecifier != MsgPackOptions.DateTimeExtensionType || bytes.Length != 8)
#if KEEPTRACK
        throw new MsgPackException(string.Concat("The extension type ", ext.TypeSpecifier, " with ", bytes.Length, " bytes is not a native DateTime, expected type 6 with 8 bytes."), ext.StoredOffset + 1, ext.TypeId);
#else
        throw new MsgPackException($"The extension type {ext.TypeSpecifier} with {bytes.Length} bytes is not a native DateTime, expected type 6 with 8 bytes.", -1, ext.TypeId);
#endif
      return MsgPackOptions.FromNative(BitConverter.ToInt64(SwapIfLittleEndian(settings, bytes), 0));
    }

    private static byte[] NativeBytes(MsgPackSettings settings, DateTime value)
    {
      return SwapIfLittleEndian(settings, BitConverter.GetBytes(value.ToBinary())); // a new array, swapped or not
    }

    /// <summary>
    /// The native extension (type 6) of the DateTime as it is.
    /// </summary>
    public static MpDateTime Native(MsgPackSettings settings, DateTime dt)
    {
      return new MpDateTime(settings, true) { Value = dt };
    }

    /// <summary>
    /// The moment of a timestamp as local time (see <see cref="ConvertExtUtc"/> for the moment itself: local time is clamped at the first and last hours of the range of DateTime).
    /// </summary>
    public static DateTime ConvertExt(MsgPackSettings settings, MpExt ext)
    {
      return ConvertExtUtc(settings, ext).ToLocalTime();
    }

    /// <summary>
    /// The moment of a timestamp in UTC. Not by way of local time and back: within an offset of the ends of DateTime's range local time is clamped (east of Greenwich DateTime.MaxValue in UTC
    /// would not be read at all, adding its nanoseconds to the clamped value, and the last hour of year 9999 would come back an offset earlier; west of it the first hours of year 1 an offset later).
    /// </summary>
    internal static DateTime ConvertExtUtc(MsgPackSettings settings, MpExt ext)
    {
      if (ext.TypeSpecifier == MsgPackOptions.DateTimeExtensionType)
      {
        DateTime native = ConvertNative(settings, ext);
        return native.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(native, DateTimeKind.Utc) : native.ToUniversalTime(); // Unspecified as the UTC clock time, as a timestamp writes it
      }
      if (ext.TypeSpecifier != -1)
#if KEEPTRACK
        throw new MsgPackException(string.Concat("The extension type ", ext.TypeSpecifier, " is not a recognised DatTime or TimeStamp, expected type -1."), ext.StoredOffset + 1, ext.TypeId);
#else
        throw new MsgPackException(
          $"The extension type {ext.TypeSpecifier} is not a recognised DatTime or TimeStamp, expected type -1.", -1, ext.TypeId);
#endif

      switch (ext.TypeId)
      {
        case MsgPackTypeId.MpFExt4:
          uint seconds = BitConverter.ToUInt32(SwapIfLittleEndian(settings, (byte[])ext.Value), 0);
          return Zero.AddSeconds(seconds);

        case MsgPackTypeId.MpFExt8:
          // One big-endian 64 bit value: 30 bits nanoseconds followed by 34 bits seconds
          byte[] bitVal = SwapIfLittleEndian(settings, (byte[])ext.Value); // a copy when swapped, the value of the ext is not changed

          ulong bytes = BitConverter.ToUInt64(bitVal, 0);
          ulong nanoSecc;
          ulong sec;
          long ticks;
          unchecked
          {
            nanoSecc = bytes >> 34;
            ulong mask = 0x3FFFFFFFF;
            sec = (bytes & mask);
            ticks = (long)nanoSecc / 100;
          }

          return Zero.AddSeconds(sec).Add(new TimeSpan(ticks));
        case MsgPackTypeId.MpExt8:
          byte[] vall = (byte[])ext.Value;
          uint nanoSec = BitConverter.ToUInt32(SwapIfLittleEndian(settings, vall, 0, 4), 0);
          long sc = BitConverter.ToInt64(SwapIfLittleEndian(settings, vall, 4, 8), 0);
          long tick = nanoSec / 100;
          TimeSpan subSec = TimeSpan.FromTicks(tick);
          return Zero.AddSeconds(sc) + subSec; // the nanoseconds are added, also before 1970 (the seconds are rounded down, see DateTimeToEpoch)
      }

#if KEEPTRACK
      throw new MsgPackException(string.Concat("The extension type with base type ", GetOfficialTypeName(ext.TypeId), " is not recognised as a DatTime or TimeStamp. expected ", GetOfficialTypeName(MsgPackTypeId.MpFExt4), " or ", GetOfficialTypeName(MsgPackTypeId.MpFExt8), " or ", GetOfficialTypeName(MsgPackTypeId.MpExt8)), ext.StoredOffset, ext.TypeId);
#else
      throw new MsgPackException(
        $"The extension type with base type {GetOfficialTypeName(ext.TypeId)} is not recognised as a DatTime or TimeStamp. expected {GetOfficialTypeName(MsgPackTypeId.MpFExt4)} or {GetOfficialTypeName(MsgPackTypeId.MpFExt8)} or {GetOfficialTypeName(MsgPackTypeId.MpExt8)}", -1, ext.TypeId);
#endif
    }

    /// <summary>
    /// A timestamp (type -1) of the DateTime, whatever the settings' <see cref="MsgPackOptions.DateTimeFormat"/> (see <see cref="Native"/> for the native extension).
    /// </summary>
    public static MpDateTime FromDateTime(MsgPackSettings settings, DateTime dt, bool preserveFractionalSeconds = true)
    {
      dt = settings is null ? dt.ToUniversalTime() : settings.ToTimestamp(dt);

      if (dt < Zero || dt > MaxFExt8)
        return ToExt8(settings, dt); // lartgest 15 bytes

      if (dt > MaxFExt4 || (preserveFractionalSeconds && HasFractionalSeconds(dt)))
        return ToFExt8(settings, dt); // 10 bytes

      return ToFExt4(settings, dt); // 6 bytes
    }

    private static byte[] ToBaseValue(MsgPackSettings settings, DateTime dt, bool preserveFractionalSeconds = true)
    {
      dt = dt.ToUniversalTime();

      if (dt < Zero || dt > MaxFExt8)
        return ToExt8Bytes(settings, dt); // lartgest 15 bytes

      if (dt > MaxFExt4 || (preserveFractionalSeconds && HasFractionalSeconds(dt)))
        return ToFExt8Bytes(settings, dt); // 10 bytes

      return ToFExt4Bytes(settings, dt); // 6 bytes
    }

    private static bool HasFractionalSeconds(DateTime dt)
    {
      return (dt.Ticks % TimeSpan.TicksPerSecond) != 0;
    }

    private static byte[] ToFExt4Bytes(MsgPackSettings settings, DateTime dt)
    {
      long seconds = DateTimeToEpoch(dt);
      uint sec = (uint)seconds;
      return SwapIfLittleEndian(settings, BitConverter.GetBytes(sec));
    }

    private static MpDateTime ToFExt4(MsgPackSettings settings, DateTime dt)
    {
      MpDateTime mpdt = new MpDateTime(settings, false)
      {
        BaseValue = ToFExt4Bytes(settings, dt),
        Value = dt
      };
      return mpdt;
    }

    private static byte[] ToFExt8Bytes(MsgPackSettings settings, DateTime dt)
    {
      ulong seconds = (ulong)DateTimeToEpoch(dt);
      long ticksFractional = (dt.Ticks % TimeSpan.TicksPerSecond);
      ulong nanoSec = (ulong)ticksFractional * 100;

      ulong prepareBytes = nanoSec << 34;
      prepareBytes = prepareBytes | seconds;
      byte[] bitVal = BitConverter.GetBytes(prepareBytes);
      ReorderIfLittleEndian(settings, bitVal); // one big-endian 64 bit value: 30 bits nanoseconds followed by 34 bits seconds
      return bitVal;
    }

    private static MpDateTime ToFExt8(MsgPackSettings settings, DateTime dt)
    {
      MpDateTime mpdt = new MpDateTime(settings, false)
      {
        BaseValue = ToFExt8Bytes(settings, dt),
        Value = dt
      };
      return mpdt;
    }

    private static byte[] ToExt8Bytes(MsgPackSettings settings, DateTime dt)
    {
      long seconds = DateTimeToEpoch(dt);
      long ticksFractional = (dt.Ticks % TimeSpan.TicksPerSecond);
      uint nanoSec = (uint)ticksFractional * 100;
      byte[] bitVal = new byte[12];
      SwapIfLittleEndian(settings, BitConverter.GetBytes(nanoSec)).CopyTo(bitVal, 0);
      SwapIfLittleEndian(settings, BitConverter.GetBytes(seconds)).CopyTo(bitVal, 4);
      return bitVal;
    }

    private static MpDateTime ToExt8(MsgPackSettings settings, DateTime dt)
    {
      MpDateTime mpdt = new MpDateTime(settings, false)
      {
        BaseValue = ToExt8Bytes(settings, dt),
        Value = dt
      };
      return mpdt;
    }

    public static readonly DateTime Zero = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime MaxFExt4 = Zero.AddSeconds(uint.MaxValue);
    public static readonly DateTime MaxFExt8 = new DateTime(2514, 5, 30, 1, 53, 03, DateTimeKind.Utc).Add(TimeSpan.FromTicks(9999999));

    public static DateTime EpochToLocalDateTime(long secconds)
    {
      DateTime dt = Zero.AddSeconds(secconds).ToLocalTime();
      return dt;
    }

    /// <summary>
    /// Whole seconds since 1970-01-01 UTC, rounded down (also before 1970), so the fraction of a second is always positive as the timestamp format requires.
    /// </summary>
    public static long DateTimeToEpoch(DateTime dateTime)
    {
      DateTime uni = dateTime.ToUniversalTime();
      // Ticks are never negative so dividing them rounds down, Zero is a whole second. Do not use TotalSeconds, it has rounding errors!
      return uni.Ticks / TimeSpan.TicksPerSecond - Zero.Ticks / TimeSpan.TicksPerSecond;
    }
  }

#if KEEPTRACK
  /// <summary>
  /// A DateTime in the property grids as ISO 8601 with its Kind ("o": UTC with Z, Local with its offset, Unspecified without a zone), and read back with that Kind.
  /// </summary>
  internal sealed class IsoDateTimeConverter : DateTimeConverter
  {
    public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
    {
      if (destinationType == typeof(string) && value is DateTime time)
        return time.ToString("o", CultureInfo.InvariantCulture);
      return base.ConvertTo(context, culture, value, destinationType);
    }

    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
    {
      if (value is string text && DateTime.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime time))
        return time;
      return base.ConvertFrom(context, culture, value);
    }
  }
#endif
}
