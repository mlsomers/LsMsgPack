#!/usr/bin/env bash
# NativeAOT smoke test of LtMsgPack's source generator: the JIT run writes golden.txt with the runtime plans (and checks the generated code against them),
# the NativeAOT binary checks the generated code against golden.txt. NativeAOT needs clang (or gcc, used when clang is missing) on Linux.
set -euo pipefail
cd "$(dirname "$0")"
out="${TMPDIR:-/tmp}/ltmsgpack-aot-smoke"
rm -rf "$out"
mkdir -p "$out"
dotnet run -c Release -- write "$out/golden.txt"
linker=()
if ! command -v clang > /dev/null; then
  linker=(-p:CppCompilerAndLinker=gcc)
fi
dotnet publish -c Release -r linux-x64 -o "$out/publish" "${linker[@]}"
"$out/publish/LtMsgPackAotSmokeTest" check "$out/golden.txt"
