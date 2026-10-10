#if KEEPTRACK
using System.Collections.Generic;
using System.Linq;
using LsMsgPack.Types.Extensions;

namespace LsMsgPack
{
    public class MsgPackValidation
    {

        public static ValidationItem[] ValidateItem(MsgPackItem item, long displayLimit)
        {
            List<ValidationItem> issues = new List<ValidationItem>();
            if (item is MpInt) ValidateInt(item, issues);
            else if (item is MpError) ValidateError((MpError)item, issues);
            else if (item is MsgPackVarLen)
            {
                if (ValidateVarLength((MsgPackVarLen)item, issues)) return issues.ToArray(); // no use validating child aspects
                if (item is MpMap) ValidateMap(item, issues, displayLimit);
                else if (item is MpExt) ValidateExt((MpExt)item, issues);
            }
            return issues.ToArray();
        }

        /// <summary>
        /// The extension types LsMsgPack writes: timestamps (-1), big integers (-2) and decimals (1, <see cref="MpDecimal"/>), and the types the specification reserves.
        /// </summary>
        private static void ValidateExt(MpExt item, List<ValidationItem> issues)
        {
            if (item is MpDateTime) ValidateTimestamp(item, issues);
            else if (item is MpDecimal) ValidateDecimal((MpDecimal)item, issues);
            else if (item.TypeSpecifier == BigIntegerExtensionType) ValidateBigInteger(item, issues);
            else if (item.TypeSpecifier < 0)
            {
                issues.Add(new ValidationItem(item, ValidationSeverity.Warning, 0, "Extension type ", item.TypeSpecifier,
                  " is reserved by the specification for predefined types, but no type is defined for it (-1 is the timestamp, -2 the proposed big integer). Other implementations may refuse it, use a type from 0 to 127 for your own extensions."));
            }
        }

        /// <summary>
        /// FrameworkTypeInfo.BigIntegerExtensionType (internal in Core): BigInteger, Int128 and UInt128 beyond 64 bits, big-endian two's complement of minimal length.
        /// </summary>
        private const sbyte BigIntegerExtensionType = -2;

        private static void ValidateTimestamp(MpExt item, List<ValidationItem> issues)
        {
            byte[] bytes = item.BaseValue; // Value is the DateTime
            bool bigEndian = MsgPackItem.SwapEndianChoice(item.Settings, 4) == System.BitConverter.IsLittleEndian; // as MpDateTime.ConvertExt reads them
            ulong nanoseconds;
            long seconds;
            int used;
            if (item.TypeId == MsgPackTypeId.MpFExt8 && bytes.Length == 8)
            {
                ulong both = ReadUnsigned(bytes, 0, 8, bigEndian); // 30 bits nanoseconds followed by 34 bits seconds
                nanoseconds = both >> 34;
                seconds = (long)(both & 0x3FFFFFFFFUL);
                used = 10;
            }
            else if (item.TypeId == MsgPackTypeId.MpExt8 && bytes.Length == 12)
            {
                nanoseconds = ReadUnsigned(bytes, 0, 4, bigEndian);
                seconds = (long)ReadUnsigned(bytes, 4, 8, bigEndian);
                used = 15;
            }
            else
                return; // timestamp 32: seconds only, any value is valid (other lengths can not be read)

            if (nanoseconds > 999999999)
            {
                issues.Add(new ValidationItem(item, ValidationSeverity.Error, 0, "The nanoseconds of this timestamp (", nanoseconds,
                  ") are more than a second. The specification does not allow more than 999999999, other implementations may refuse it or read another moment."));
                return;
            }

            int needed;
            if (nanoseconds == 0 && seconds >= 0 && seconds <= uint.MaxValue) needed = 6; // timestamp 32
            else if (seconds >= 0 && seconds <= 0x3FFFFFFFFL) needed = 10; // timestamp 64
            else needed = 15; // timestamp 96
            if (needed < used)
            {
                issues.Add(new ValidationItem(item, ValidationSeverity.Warning, used - needed, "This timestamp should have been stored as a timestamp ", needed == 6 ? 32 : 64,
                  " (", needed, " bytes instead of ", used, ")."));
            }
        }

        private static ulong ReadUnsigned(byte[] bytes, int start, int count, bool bigEndian)
        {
            ulong value = 0;
            for (int t = 0; t < count; t++)
                value = (value << 8) | bytes[bigEndian ? start + t : start + count - 1 - t];
            return value;
        }

        private static void ValidateDecimal(MpDecimal item, List<ValidationItem> issues)
        {
            if (item.Count != 16)
                return; // not read
            int flags = decimal.GetBits((decimal)item.Value)[3];
            int scale = (flags >> 16) & 0xFF;
            if ((flags & 0x7F00FFFF) != 0 || scale > 28)
            {
                issues.Add(new ValidationItem(item, ValidationSeverity.Error, 0, "These 16 bytes are not a valid decimal (", scale > 28 ? string.Concat("a scale of ", scale, ", the maximum is 28") : "unused bits of the flags are set",
                  "). Reading it gives a corrupt decimal value: computations with it may fail or give wrong results."));
            }
        }

        /// <summary>
        /// Big-endian two's complement of minimal length, only when the value does not fit in 64 bits (an integer otherwise, see FrameworkTypeInfo.BigIntegerPlain).
        /// </summary>
        private static void ValidateBigInteger(MpExt item, List<ValidationItem> issues)
        {
            byte[] bytes = item.BaseValue;
            int redundant = 0;
            while (redundant < bytes.Length - 1 && (bytes[redundant] == 0x00 && bytes[redundant + 1] < 0x80 || bytes[redundant] == 0xFF && bytes[redundant + 1] >= 0x80))
                redundant++;
            int length = bytes.Length - redundant;

            bool negative = length > 0 && bytes[redundant] >= 0x80;
            bool fitsLong = length <= 8;
            bool fitsULong = length == 9 && bytes[redundant] == 0x00; // above long.MaxValue
            if (fitsLong || fitsULong)
            {
                int asInt = IntegerLength(bytes, redundant, length, negative);
                int used = ExtLength(item);
                issues.Add(new ValidationItem(item, ValidationSeverity.Warning, used > asInt ? used - asInt : 0,
                  "This big integer (extension type -2) fits in 64 bits and should have been stored as an integer (", asInt, " bytes instead of ", used,
                  "). LsMsgPack and LtMsgPack read both, other implementations may only read an integer here."));
                return;
            }

            if (redundant > 0)
            {
                issues.Add(new ValidationItem(item, ValidationSeverity.Warning, redundant, "This big integer (extension type -2) starts with ", redundant,
                  redundant == 1 ? " byte" : " bytes", " that only repeat the sign: the proposals for big integers in MsgPack (msgpack/msgpack#206) use the minimal length."));
            }
        }

        /// <summary>
        /// The length of the smallest integer encoding of a value of up to 9 bytes (two's complement, the 9th only for values above long.MaxValue).
        /// </summary>
        private static int IntegerLength(byte[] bytes, int start, int length, bool negative)
        {
            if (length == 9)
                return 9; // uint 64
            long value = negative ? -1L : 0L;
            for (int t = start; t < start + length; t++)
                value = (value << 8) | bytes[t];
            if (value >= -32 && value <= 127) return 1; // fixint
            if (value >= sbyte.MinValue && value <= byte.MaxValue) return 2;
            if (value >= short.MinValue && value <= ushort.MaxValue) return 3;
            if (value >= int.MinValue && value <= uint.MaxValue) return 5;
            return 9;
        }

        /// <summary>
        /// The header, the type and the content of an extension.
        /// </summary>
        private static int ExtLength(MpExt item)
        {
            switch (item.TypeId)
            {
                case MsgPackTypeId.MpExt8: return 3 + item.Count;
                case MsgPackTypeId.MpExt16: return 4 + item.Count;
                case MsgPackTypeId.MpExt32: return 6 + item.Count;
                default: return 2 + item.Count; // fixext
            }
        }

        private static void ValidateError(MpError item, List<ValidationItem> issues)
        {
            if (item.IsInNestedItem) return; // Only log an error for the exact place where it occurred (not for all parents)
            issues.Add(new ValidationItem(item, ValidationSeverity.ReadAbortError, 0, item.ToString()));
        }

        private static bool ValidateVarLength(MsgPackVarLen item, List<ValidationItem> issues)
        {
            if (item.Count == 0)
            {
                int waisted;
                switch (item.TypeId)
                {
                    case MsgPackTypeId.MpMap16: waisted = 2; break;
                    case MsgPackTypeId.MpMap32: waisted = 4; break;
                    case MsgPackTypeId.MpArray16: waisted = 2; break;
                    case MsgPackTypeId.MpArray32: waisted = 4; break;
                    case MsgPackTypeId.MpBin8: waisted = 1; break;
                    case MsgPackTypeId.MpBin16: waisted = 2; break;
                    case MsgPackTypeId.MpBin32: waisted = 4; break;
                    case MsgPackTypeId.MpExt8: waisted = 1; break;
                    case MsgPackTypeId.MpExt16: waisted = 2; break;
                    case MsgPackTypeId.MpExt32: waisted = 4; break;
                    case MsgPackTypeId.MpStr8: waisted = 1; break;
                    case MsgPackTypeId.MpStr16: waisted = 2; break;
                    case MsgPackTypeId.MpStr32: waisted = 4; break;
                    default: waisted = 0; break;
                }

                if(waisted > 0)
                {
                  issues.Add(new ValidationItem(item, ValidationSeverity.Warning, waisted, "Consider using a null (nil) value instead of an empty ", MsgPackItem.GetOfficialTypeName(item.TypeId), " instance."));
                  return true;
                }
            }
            return false;
        }

        private static void ValidateMap(MsgPackItem item, List<ValidationItem> issues, long displayLimit)
        {
            MpMap map = (MpMap)item;
            if(map.PackedValues.Length == 0)
            {
                issues.Add(new ValidationItem(item, ValidationSeverity.Comment, 0, "Map is empty."));
                return;
            }
            // LsMsgPack's type id ("" first) and the content of a wrapped value ("@") are string keys in maps that otherwise have the property ids
            // (indexes with the indexed schema): the other keys decide the family of the map
            bool typeIdMap = IsReservedKey(map.PackedValues[0].Key, string.Empty);
            int reference = 0;
            if (typeIdMap)
            {
                for (int t = 1; t < map.PackedValues.Length; t++)
                {
                    if (!IsReservedKey(map.PackedValues[t].Key, "@"))
                    {
                        reference = t;
                        break;
                    }
                }
            }
            MsgPackTypeId firstKeyType = map.PackedValues[reference].Key.TypeId;
            if (!MsgPackMeta.StrTypeFamily.Contains(firstKeyType) && !MsgPackMeta.IntTypeFamily.Contains(firstKeyType) && firstKeyType != MsgPackTypeId.MpNull)
            {
                issues.Add(new ValidationItem(item, ValidationSeverity.Comment, 0, "A key of type ", MsgPackItem.GetOfficialTypeName(firstKeyType),
                  " is rather unusual in a map. Some implementations might only support string or integer types as keys."));
            }

            for (int t = map.PackedValues.Length - 1; t >= 0; t--)
            {
                if (System.Math.Pow(map.PackedValues.Length - t, 2) > displayLimit)
                    return;

                if (ReferenceEquals(map.PackedValues[t].Key, null)) continue;
                bool reserved = typeIdMap && (t == 0 || IsReservedKey(map.PackedValues[t].Key, "@"));
                if (!reserved && map.PackedValues[t].Key.TypeId != firstKeyType && !MsgPackMeta.AreInSameFamily(map.PackedValues[t].Key.TypeId, firstKeyType))
                {
                    issues.Add(new ValidationItem(item, ValidationSeverity.Warning, 0,
                        "The types of keys in this map do not appear to be consistent. Item ", reference, " has a key of type ", MsgPackItem.GetOfficialTypeName(firstKeyType),
                        " while item ", t, " has a key of type ", MsgPackItem.GetOfficialTypeName(map.PackedValues[t].Key.TypeId),
                        ". Allthough the specs do not demand that keys are of the same type, it is likely that many implementations will assume that keys in a map are all of the same family."));
                }
                for (int i = t - 1; i >= 0; i--)
                {
                    if (map.PackedValues.Length - t > displayLimit)
                        return;

                    if (ReferenceEquals(map.PackedValues[t].Key, null) || ReferenceEquals(map.PackedValues[i].Key, null) || ReferenceEquals(map.PackedValues[t].Key.Value, null)) continue;
                    if (map.PackedValues[t].Key.Value.Equals(map.PackedValues[i].Key.Value))
                    {
                        issues.Add(new ValidationItem(item, ValidationSeverity.Warning, 0, "This map has multiple entries with identical keys (items ",
                          i, "='", map.PackedValues[i].Key.ToString(), "' and ", t, "='", map.PackedValues[t].Key.ToString(), "'). Allthough the specs do not demand unique keys, it is likely that many implementations will assume that keys in a map are unique."));
                    }
                }
            }
        }

        /// <summary>
        /// The key of LsMsgPack's type id ("") or of the content of a wrapped value ("@"): <c>{ "": typeId, "@": value }</c>, <c>{ "": typeId, 0: value, ... }</c>.
        /// </summary>
        private static bool IsReservedKey(MsgPackItem key, string name)
        {
            return key is MpString && string.Equals(key.Value as string, name, System.StringComparison.Ordinal);
        }

        private static void ValidateInt(MsgPackItem item, List<ValidationItem> issues)
        {
            try
            {
                MpInt intItem = (MpInt)item;
                item.Settings._dynamicallyCompact = true;
                MsgPackTypeId calcType = intItem.TypeId;
                if (calcType != intItem.PreservedType)
                {
                    int used = MpInt.GetByteLengthForType(intItem.PreservedType);
                    int potential = MpInt.GetByteLengthForType(calcType);
                    if (potential < used)
                    {
                        issues.Add(new ValidationItem(item, ValidationSeverity.Warning, (used - potential),
                          "This integer should have been stored in a smaller container (", MsgPackItem.GetOfficialTypeName(calcType), " instead of ",
                          MsgPackItem.GetOfficialTypeName(intItem.PreservedType), "). This would have saved ", (used - potential), " bytes."));
                    }
                    else if (MpInt.SignedTypeIds.Contains(intItem.PreservedType) && !MpInt.SignedTypeIds.Contains(calcType))
                    {
                        issues.Add(new ValidationItem(item, ValidationSeverity.Comment, 0,
                          "A positive integer has no need of a signing bit and can potentially be stored in a smaller container (in this case it did not matter)."));
                    }
                }
            }
            finally
            {
                item.Settings._dynamicallyCompact = false;
            }
        }

        public enum ValidationSeverity
        {
            Error = 0,
            Warning = 1,
            Comment = 2,
            ReadAbortError = 3,
            /// <summary>
            /// The data can make a reader create types it picks (e.g. a type id on a value of a property declared as object), see docs/security.md.
            /// </summary>
            SecurityVulnerability = 4
        }

        public class ValidationItem
        {
            public ValidationItem() { }
            public ValidationItem(MsgPackItem item, ValidationSeverity severity, int waistedBytes, params object[] message)
            {
                Item = item;
                Severity = severity;
                WaistedBytes = waistedBytes;
                Message = string.Concat(message);
            }

            /// <summary>
            /// How severe the issue is graded
            /// </summary>
            public ValidationSeverity Severity { get; set; }
            /// <summary>
            /// The number of bytes that could be spared by using an alternative way to encode the item.
            /// </summary>
            public int WaistedBytes { get; set; }
            /// <summary>
            /// Textual explanation of the validation issue
            /// </summary>
            public string Message { get; set; }
            /// <summary>
            /// The item being validated
            /// </summary>
            public MsgPackItem Item { get; set; }
        }
    }
}
#endif
