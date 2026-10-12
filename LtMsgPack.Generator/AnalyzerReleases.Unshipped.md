; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
LTMP001 | LtMsgPack.Generator | Error | Context class is not partial
LTMP002 | LtMsgPack.Generator | Error | Context class does not derive from LtMsgPackContext
LTMP003 | LtMsgPack.Generator | Warning | Type is not accessible to the generated code
LTMP004 | LtMsgPack.Generator | Info | Property is set by reflection
LTMP005 | LtMsgPack.Generator | Error | Context class is not supported
LTMP006 | LtMsgPack.Generator | Error | Context name is taken
LTMP007 | LtMsgPack.Generator | Error | Context class declares a parameterless constructor
LTMP008 | LtMsgPack.Generator | Warning | Open generic type
LTMP009 | LtMsgPack.Generator | Info | Context is not registered automatically
