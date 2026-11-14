#!/usr/bin/env bash
# NativeAOT smoke test of LsMsgPack's source generator: the JIT run writes golden.txt with the runtime plans (and checks the generated code against them),
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
