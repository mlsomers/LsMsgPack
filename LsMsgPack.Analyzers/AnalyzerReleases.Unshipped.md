; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
LSMP001 | LsMsgPack.Defaults | Warning | Initialized property without [DefaultValue]
LSMP002 | LsMsgPack.Defaults | Warning | [DefaultValue] differs from the initial value
LSMP003 | LsMsgPack.Defaults | Warning | [DefaultValue] of another type than the property
LSMP004 | LsMsgPack.Security | Warning | Property holds any type the data picks
LSMP005 | LsMsgPack.Security | Warning | Data is read into any type it picks
LSMP006 | LsMsgPack.Defaults | Warning | Null is read back as the initial value
LSMP007 | LsMsgPack.Reading | Warning | Objects are read without their property names
LSMP008 | LsMsgPack.Coverage | Warning | Serialized class of another project is not checked
