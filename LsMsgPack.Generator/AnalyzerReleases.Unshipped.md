; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
LSMP101 | LsMsgPack.Generator | Error | Context class is not partial
LSMP102 | LsMsgPack.Generator | Error | Context class does not derive from LsMsgPackContext
LSMP103 | LsMsgPack.Generator | Warning | Type is not accessible to the generated code
LSMP104 | LsMsgPack.Generator | Info | Property is set by reflection
LSMP105 | LsMsgPack.Generator | Error | Context class is not supported
LSMP106 | LsMsgPack.Generator | Error | Context name is taken
LSMP107 | LsMsgPack.Generator | Error | Context class declares a parameterless constructor
LSMP108 | LsMsgPack.Generator | Warning | Open generic type
LSMP109 | LsMsgPack.Generator | Info | Context is not registered automatically
LSMP110 | LsMsgPack.Generator | Warning | Values of types only known at runtime (only when trimmed or AOT)
LSMP111 | LsMsgPack.Generator | Info | Generated code is not optimized (a Debug build)
