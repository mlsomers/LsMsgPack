#!/usr/bin/env bash
# Rebuilds the "new" harness and alternates base and new runs (the machine noise is often 5-10%, compare the ranges).
# Usage: bench.sh <work dir> [seconds per run, default 8] [repeats, default 2]
set -euo pipefail
WORK=$(realpath "${1:?Usage: bench.sh <work dir> [seconds] [repeats]}")
SECONDS_PER_RUN=${2:-8}
REPEATS=${3:-2}
dotnet build "$WORK/bench-new" -c Release 2>&1 | grep -E " error |rror\(s\)"
for i in $(seq "$REPEATS"); do
  for mode in indexed named; do
    echo "base $(dotnet "$WORK/bench-base/bin/Release/net8.0/bench-base.dll" both "$SECONDS_PER_RUN" $mode)"
    echo "new  $(dotnet "$WORK/bench-new/bin/Release/net8.0/bench-new.dll" both "$SECONDS_PER_RUN" $mode)"
  done
done
