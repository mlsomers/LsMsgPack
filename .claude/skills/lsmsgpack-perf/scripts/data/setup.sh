#!/usr/bin/env bash
# A/B harness for LsMsgPack.Data and the stream writer/reader: the working tree ("new") against a git ref ("base", default origin/master).
# Usage: data/setup.sh <work dir outside the repo> [base ref]
set -euo pipefail
WORK=$(realpath -m "${1:?Usage: data/setup.sh <work dir> [base ref]}")
BASE_REF=${2:-origin/master}
SRC=$(cd "$(dirname "$0")" && pwd)
REPO=$(git -C "$SRC" rev-parse --show-toplevel)
git -C "$REPO" cat-file -e "$BASE_REF:LsMsgPack.Data" 2>/dev/null || { echo "$BASE_REF has no LsMsgPack.Data"; exit 1; }
mkdir -p "$WORK"
rm -rf "$WORK/baseline" && mkdir -p "$WORK/baseline"
git -C "$REPO" archive "$BASE_REF" LsMsgPackCore LsMsgPack LsMsgPack.Data LsMsgPack.DataTests LsMsgPack.Analyzers LsMsgPack.Analyzers.CodeFixes CommonAssemblyInfo.cs Packaging.props | tar -x -C "$WORK/baseline"
echo "$BASE_REF ($(git -C "$REPO" rev-parse --short "$BASE_REF"))" > "$WORK/baseline/REF"
for v in new base; do
  ROOT=$REPO; [ $v = base ] && ROOT=$WORK/baseline
  rm -rf "$WORK/bench-$v" && mkdir -p "$WORK/bench-$v"
  cp "$SRC/DataBench.cs" "$ROOT/LsMsgPack.DataTests/Data.cs" "$ROOT/LsMsgPack.DataTests/ColumnarReader.cs" "$ROOT/LsMsgPack.DataTests/Models.cs" "$WORK/bench-$v/"
  cat > "$WORK/bench-$v/bench-$v.csproj" <<PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>disable</Nullable><ImplicitUsings>disable</ImplicitUsings></PropertyGroup>
  <ItemGroup><ProjectReference Include="$ROOT/LsMsgPack.Data/LsMsgPack.Data.csproj" /></ItemGroup>
</Project>
PROJ
  dotnet build "$WORK/bench-$v" -c Release 2>&1 | grep -E " error |rror\(s\)" | sort -u
done
echo "Harness in $WORK, base: $(cat "$WORK/baseline/REF")"
