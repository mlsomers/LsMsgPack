#!/usr/bin/env bash
# Runs a command in its own systemd scope with a memory limit, so running out of memory kills that command only, not VS Code with everything it started.
# Why: on this 7 GB machine the kernel's global OOM killer picked VS Code's processes (oom_score_adj 300) over the dotnet process that used the memory,
# and systemd then stopped VS Code's whole scope (the terminals, Claude Code, every build). In a scope of its own with MemoryMax the kernel kills
# inside that scope when it is full (cgroup OOM, exit 137), and .NET sees the limit (its GC heap hard limit is 75% of it: an OutOfMemoryException first).
# oom_score_adj 1000 makes the command the first choice if the machine runs out anyway (children inherit it).
# Usage: scripts/limit.sh [-m 3G] [-s 256M] <command...>     MEM / SWAP in the environment work too
#   scripts/limit.sh dotnet build X.csproj -c Release -nr:false
#   scripts/limit.sh -m 2500M dotnet $W/webmany-new/bin/Release/net8.0/webmany-new.dll 3
set -euo pipefail
MEM=${MEM:-3G}
SWAP=${SWAP:-256M}
while getopts "m:s:" o; do
  case $o in
    m) MEM=$OPTARG ;;
    s) SWAP=$OPTARG ;;
    *) exit 2 ;;
  esac
done
shift $((OPTIND - 1))
[ $# -gt 0 ] || { echo "Usage: limit.sh [-m 3G] [-s 256M] <command...>" >&2; exit 2; }
exec systemd-run --user --scope --quiet --collect -p MemoryMax="$MEM" -p MemorySwapMax="$SWAP" choom -n 1000 -- "$@"
