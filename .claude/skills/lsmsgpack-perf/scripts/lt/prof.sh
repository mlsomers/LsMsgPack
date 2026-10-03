#!/usr/bin/env bash
# perf profile of the "new" LtMsgPack benchmark harness, writes <out>.self.txt (self time) and <out>.children.txt (inclusive) next to <out>.data.
# Needs kernel.perf_event_paranoid <= 1 (ask the user). W^X must be off, otherwise the JIT code is double mapped and perf shows no symbols.
# Usage: lt/prof.sh <work dir> <out name> <bench arguments...>   e.g. lt/prof.sh $W write write lt-default 8
set -euo pipefail
WORK=$(realpath "${1:?Usage: lt/prof.sh <work dir> <out name> <bench args...>}")
OUT=$2; shift 2
mkdir -p "$WORK/perf" && cd "$WORK/perf"
DOTNET_PerfMapEnabled=1 DOTNET_EnableWriteXorExecute=0 perf record -F 2999 -g -o "$OUT.data" dotnet "$WORK/bench-new/bin/Release/net8.0/bench-new.dll" "$@" 2>&1 | grep -vE "^\[ perf|kernel|kallsyms|vmlinux|relocation|kexec|root|^$" || true
perf report -i "$OUT.data" --no-children --sort symbol --stdio -g none 2>/dev/null | grep -E "^ +[0-9]" | sed -E 's/ +- +- *$//; s/\[OptimizedTier1\]//' | cut -c1-200 > "$OUT.self.txt"
perf report -i "$OUT.data" --children --sort symbol --stdio -g none 2>/dev/null | grep -E "^ +[0-9]" | sed -E 's/ +- +- *$//; s/\[OptimizedTier1\]//' | cut -c1-220 > "$OUT.children.txt"
head -30 "$OUT.self.txt"
