#!/usr/bin/env bash
# Rebuilds the "new" harness and diffs its output with the base (see setup.sh). No output from diff = identical.
# Usage: compare.sh <work dir>
set -euo pipefail
WORK=$(realpath "${1:?Usage: compare.sh <work dir>}")
dotnet build "$WORK/equiv-new" -c Release 2>&1 | grep -E " error |rror\(s\)"
[ -f "$WORK/base.txt" ] || dotnet "$WORK/equiv-base/bin/Release/net8.0/equiv-base.dll" > "$WORK/base.txt"
dotnet "$WORK/equiv-new/bin/Release/net8.0/equiv-new.dll" > "$WORK/new.txt"
if diff "$WORK/base.txt" "$WORK/new.txt" > "$WORK/equiv.diff"; then
  echo "Identical to $(cat "$WORK/baseline/REF") ($(wc -l < "$WORK/new.txt") lines)"
else
  echo "DIFFERENT from $(cat "$WORK/baseline/REF"), see $WORK/equiv.diff:"
  head -40 "$WORK/equiv.diff"
  exit 1
fi
