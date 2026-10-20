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
