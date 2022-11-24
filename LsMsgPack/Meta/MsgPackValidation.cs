using System.Collections.Generic;
using System.Linq;

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
            }
            return issues.ToArray();
        }

        private static void ValidateError(MpError item, List<ValidationItem> issues)
        {
            if (!ReferenceEquals(item.PartialItem, null)) return; // Only log an error for the exact place where it occurred (not for all parents)
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

                issues.Add(new ValidationItem(item, ValidationSeverity.Warning, waisted, "Consider using a null (nil) value instead of an empty ", MsgPackItem.GetOfficialTypeName(item.TypeId), " instance."));
                return true;
            }
            return false;
        }

        private static void ValidateMap(MsgPackItem item, List<ValidationItem> issues, long displayLimit)
        {
            MpMap map = (MpMap)item;
            MsgPackTypeId firstKeyType = map.PackedValues[0].Key.TypeId;
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
                if (map.PackedValues[t].Key.TypeId != firstKeyType && !MsgPackMeta.AreInSameFamily(map.PackedValues[t].Key.TypeId, firstKeyType))
                {
                    issues.Add(new ValidationItem(item, ValidationSeverity.Warning, 0,
                        "The types of keys in this map do not appear to be consistent. Item 0 has a key of type ", MsgPackItem.GetOfficialTypeName(firstKeyType),
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

        private static void ValidateInt(MsgPackItem item, List<ValidationItem> issues)
        {
            try
            {
                MpInt intItem = (MpInt)item;
                item.Settings.DynamicallyCompact = true;
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
                item.Settings.DynamicallyCompact = false;
            }
        }

        public enum ValidationSeverity
        {
            Error = 0,
            Warning = 1,
            Comment = 2,
            ReadAbortError = 3
        }

        public class ValidationItem
        {
            public ValidationItem() { }
            public ValidationItem(MsgPackItem item, ValidationSeverity severity, int waistedBytes, params object[] message)
            {
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
