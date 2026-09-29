#!/usr/bin/env bash
# Builds A/B harnesses: the working tree ("new") against a git ref ("base", default origin/master).
# Usage: setup.sh <work dir outside the repo> [base ref]
set -euo pipefail
WORK=$(realpath -m "${1:?Usage: setup.sh <work dir> [base ref]}")
BASE_REF=${2:-origin/master}
SCRIPTS=$(cd "$(dirname "$0")" && pwd)
REPO=$(git -C "$SCRIPTS" rev-parse --show-toplevel)

mkdir -p "$WORK"
rm -rf "$WORK/baseline" && mkdir -p "$WORK/baseline"
# LsMsgPackCore (LsMsgPack.Core) exists since the library was split, older bases do not have it
PATHS="LsMsgPackNetStandard CommonAssemblyInfo.cs Packaging.props"
if [ -n "$(git -C "$REPO" ls-tree --name-only "$BASE_REF" LsMsgPackCore)" ]; then PATHS="$PATHS LsMsgPackCore"; fi
git -C "$REPO" archive "$BASE_REF" $PATHS | tar -x -C "$WORK/baseline"
echo "$BASE_REF ($(git -C "$REPO" rev-parse --short "$BASE_REF"))" > "$WORK/baseline/REF"

make_project() { # name, program, library project
  rm -rf "$WORK/$1" && mkdir -p "$WORK/$1"
  cp "$SCRIPTS/$2" "$WORK/$1/Program.cs"
  cat > "$WORK/$1/$1.csproj" <<PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$3" />
  </ItemGroup>
</Project>
PROJ
  dotnet build "$WORK/$1" -c Release 2>&1 | grep -E " error |rror\(s\)"
}

make_project bench-new Bench.cs "$REPO/LsMsgPackNetStandard/LsMsgPack.csproj"
make_project bench-base Bench.cs "$WORK/baseline/LsMsgPackNetStandard/LsMsgPack.csproj"
make_project equiv-new Equiv.cs "$REPO/LsMsgPackNetStandard/LsMsgPack.csproj"
make_project equiv-base Equiv.cs "$WORK/baseline/LsMsgPackNetStandard/LsMsgPack.csproj"
echo "Harnesses in $WORK, base: $(cat "$WORK/baseline/REF")"
