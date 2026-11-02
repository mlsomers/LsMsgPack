#!/usr/bin/env bash
# Alternating base/new runs of the data harness, rebuilds bench-new first.
# Usage: data/ab.sh <work dir> <modes, comma separated, or all> [seconds per mode=2] [repeats=2]
set -euo pipefail
WORK=$(realpath "${1:?Usage: data/ab.sh <work dir> <modes> [seconds] [repeats]}")
dotnet build "$WORK/bench-new" -c Release 2>&1 | grep -E " error |rror\(s\)" | sort -u
for i in $(seq "${4:-2}"); do for v in base new; do
  dotnet "$WORK/bench-$v/bin/Release/net8.0/bench-$v.dll" "$2" "${3:-2}" </dev/null | sed "s/^/$v /"
done; done | sort -k2,2 -s
