#!/usr/bin/env bash
# The speed of LsMsgPack under NativeAOT with and without the source generator, and with a JIT (docs/SourceGenerator.md "Measurements"):
# builds the JIT version (PublishAot off, so dynamic code is on) and publishes the NativeAOT one, runs each RUNS times (default 3) alternately and prints the medians.
# NativeAOT needs clang (or gcc, used when clang is missing). One dotnet step at a time (low memory machines).
set -euo pipefail
cd "$(dirname "$0")"
runs="${RUNS:-3}"
out="${TMPDIR:-/tmp}/lsmsgpack-aot-benchmark"
rm -rf "$out"
mkdir -p "$out"
dotnet build -c Release -p:PublishAot=false -o "$out/jit"
dotnet build-server shutdown > /dev/null
linker=()
if ! command -v clang > /dev/null; then
  linker=(-p:CppCompilerAndLinker=gcc)
fi
rm -rf obj/Release/net10.0/linux-x64
dotnet publish -c Release -r linux-x64 -o "$out/aot" "${linker[@]}"
dotnet build-server shutdown > /dev/null
for i in $(seq 1 "$runs"); do
  "$out/jit/LsMsgPackAotBenchmark" | tee "$out/jit$i.txt" | grep -v '^RESULT'
  "$out/aot/LsMsgPackAotBenchmark" | tee "$out/aot$i.txt" | grep -v '^RESULT'
done
echo
echo "Medians of $runs runs (ms for 100 invoices x 20 rounds; speed: times as fast as System.Text.Json, source generated, in the same mode)"
cat "$out"/jit*.txt "$out"/aot*.txt | python3 -c '
import sys, statistics, collections
times = collections.OrderedDict()
for line in sys.stdin:
    if line.startswith("RESULT|"):
        _, mode, name, w, r = line.strip().split("|")
        times.setdefault((mode, name), ([], []))
        times[(mode, name)][0].append(float(w))
        times[(mode, name)][1].append(float(r))
base = {k[0]: (statistics.median(v[0]), statistics.median(v[1])) for k, v in times.items() if k[1].startswith("System.Text.Json")}
for (mode, name), (w, r) in times.items():
    mw, mr = statistics.median(w), statistics.median(r)
    bw, br = base[mode]
    print(f"{mode:9} {name:45} write {mw:6.2f} ({bw / mw:4.2f}x)  read {mr:6.2f} ({br / mr:4.2f}x)")
'
