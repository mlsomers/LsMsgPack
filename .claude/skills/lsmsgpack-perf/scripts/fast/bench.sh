#!/usr/bin/env bash
# Rebuilds the "new" LsMsgPack benchmark and alternates base and new runs of one or more candidates (see LsBench.cs for the names).
# Usage: fast/bench.sh <work dir> <write|read|both|small1> [seconds=5] [repeats=2] [candidates="ls-default ls-map-named"]
set -euo pipefail
WORK=$(realpath "${1:?Usage: fast/bench.sh <work dir> <mode> [seconds] [repeats] [candidates]}")
MODE=${2:?mode}
SECONDS_PER_RUN=${3:-5}
REPEATS=${4:-2}
CANDIDATES=${5:-"ls-default ls-map-named"}
dotnet build "$WORK/bench-new" -c Release 2>&1 | grep -E " error |rror\(s\)"
for i in $(seq "$REPEATS"); do
  for c in $CANDIDATES; do
    for v in base new; do
      printf '%-4s %s\n' "$v" "$(dotnet "$WORK/bench-$v/bin/Release/net8.0/bench-$v.dll" "$MODE" "$c" "$SECONDS_PER_RUN")"
    done
  done
done
