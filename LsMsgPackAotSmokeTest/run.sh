#!/usr/bin/env bash
# NativeAOT smoke test of LsMsgPack's source generator (and, at the end, of the serializer without it and of F# types): the JIT run writes golden.txt with the runtime plans (and checks the generated code against them),
# the NativeAOT binary checks the generated code against golden.txt. The publish must not report trim or AOT warnings. NativeAOT needs clang (or gcc, used when clang is missing) on Linux.
set -euo pipefail
cd "$(dirname "$0")"
out="${TMPDIR:-/tmp}/lsmsgpack-aot-smoke"
rm -rf "$out"
mkdir -p "$out"
dotnet run -c Release -- write "$out/golden.txt"
linker=()
if ! command -v clang > /dev/null; then
  linker=(-p:CppCompilerAndLinker=gcc)
fi
# Every warning separately (not one IL2104 per assembly), compiled again so the warnings are reported again
rm -rf obj/Release/net10.0/linux-x64
dotnet publish -c Release -r linux-x64 -o "$out/publish" -p:TrimmerSingleWarn=false -p:IlcGenerateMapFile=true "${linker[@]}" | tee "$out/publish.log"
# No trim or AOT warnings: the libraries fix or justify theirs, the app creates its serializer with the context (its deliberate uses without one are suppressed)
if grep -E "warning IL[0-9]{4}" "$out/publish.log"; then
  echo "FAILED: trim or AOT warnings (above)"
  exit 1
fi
# Only the element types of the context's collections are written and read in bulk (Serializer.CreateInBulk): the app has no collections of these types
map=obj/Release/net10.0/linux-x64/native/LsMsgPackAotSmokeTest.map.xml
if grep -oE 'ConstructedEEType Name="[^"]*Number(Array|List|Set)(Handler|Reader)_1&lt;(SByte|Int16|UInt16|UInt32|UInt64|Boolean)&gt;' "$map"; then
  echo "FAILED: collections in bulk of element types the app does not use (above)"
  exit 1
fi
if ! grep -q 'ConstructedEEType Name="[^"]*NumberArrayHandler_1&lt;Int32&gt;' "$map"; then
  echo "FAILED: the int[] of the context is not written in bulk"
  exit 1
fi
"$out/publish/LsMsgPackAotSmokeTest" check "$out/golden.txt"

# Without the source generator (LsMsgPackAotReflectionTest: this Program.cs with NO_GENERATOR, the model kept by a descriptor): the same bytes and values by reflection
rm -rf ../LsMsgPackAotReflectionTest/obj/Release/net10.0/linux-x64
dotnet publish ../LsMsgPackAotReflectionTest/LsMsgPackAotReflectionTest.csproj -c Release -r linux-x64 -o "$out/reflection" -p:TrimmerSingleWarn=false "${linker[@]}" | tee "$out/reflection.log"
# The app suppresses the warnings of its serializers without a context (deliberately reflection), the libraries report none of their own
if grep -E "warning IL[0-9]{4}" "$out/reflection.log"; then
  echo "FAILED: trim or AOT warnings without the source generator (above)"
  exit 1
fi
"$out/reflection/LsMsgPackAotReflectionTest" check "$out/golden.txt"

# F# types (LsMsgPackAotFSharpTest: F# projects can't run the source generator, so always by reflection): a JIT build with dynamic code writes the expected bytes (the generic converters),
# the NativeAOT binary writes and reads the same (the boxed converters, FSharp.Core's members kept by LsMsgPack.FSharp.xml) and maps rows of a data reader into F# records
dotnet build-server shutdown > /dev/null
dotnet build ../LsMsgPackAotFSharpTest/LsMsgPackAotFSharpTest.csproj -c Release -p:PublishAot=false -o "$out/fsharp-jit"
"$out/fsharp-jit/LsMsgPackAotFSharpTest" write "$out/fsharp.txt"
dotnet build-server shutdown > /dev/null
rm -rf ../LsMsgPackAotFSharpTest/obj/Release/net10.0/linux-x64
dotnet publish ../LsMsgPackAotFSharpTest/LsMsgPackAotFSharpTest.csproj -c Release -r linux-x64 -o "$out/fsharp" -p:TrimmerSingleWarn=false "${linker[@]}" | tee "$out/fsharp.log"
# FSharp.Core reports trim and AOT warnings of its own (its reflection and printf, in every F# NativeAOT application): only the others count
if grep -E "warning IL[0-9]{4}" "$out/fsharp.log" | grep -vE "warning IL[0-9]{4}: Microsoft\.FSharp\."; then
  echo "FAILED: trim or AOT warnings of the F# test outside FSharp.Core (above)"
  exit 1
fi
"$out/fsharp/LsMsgPackAotFSharpTest" check "$out/fsharp.txt"
