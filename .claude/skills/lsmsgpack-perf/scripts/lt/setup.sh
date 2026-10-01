#!/usr/bin/env bash
# A/B harnesses for LtMsgPack (and the ASP.NET Core formatters): the working tree ("new") against a git ref ("base", default origin/master).
# Usage: lt/setup.sh <work dir outside the repo> [base ref]
set -euo pipefail
WORK=$(realpath -m "${1:?Usage: lt/setup.sh <work dir> [base ref]}")
BASE_REF=${2:-origin/master}
SRC=$(cd "$(dirname "$0")" && pwd)
REPO=$(git -C "$SRC" rev-parse --show-toplevel)
mkdir -p "$WORK"
rm -rf "$WORK/baseline" "$WORK/base.txt" && mkdir -p "$WORK/baseline"
git -C "$REPO" archive "$BASE_REF" LsMsgPackNetStandard LsMsgPackCore LtMsgPack LsMsgPackFormatters CommonAssemblyInfo.cs Packaging.props | tar -x -C "$WORK/baseline"
echo "$BASE_REF ($(git -C "$REPO" rev-parse --short "$BASE_REF"))" > "$WORK/baseline/REF"
sed 's/CreateInvoices(5)/CreateInvoices(100)/' "$SRC/EquivModel.cs" > "$WORK/WebModel.cs"

make_project() { # name, files, repo root, extra project content
  rm -rf "$WORK/$1" && mkdir -p "$WORK/$1"
  for f in $2; do cp "$f" "$WORK/$1/"; done
  cat > "$WORK/$1/$1.csproj" <<PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
$4
  </ItemGroup>
</Project>
PROJ
  dotnet build "$WORK/$1" -c Release 2>&1 | grep -E " error |rror\(s\)" | sort -u
}
for v in new base; do
  ROOT=$REPO; [ $v = base ] && ROOT=$WORK/baseline
  LIBS="    <ProjectReference Include=\"$ROOT/LtMsgPack/LtMsgPack.csproj\" />
    <ProjectReference Include=\"$ROOT/LsMsgPackNetStandard/LsMsgPack.csproj\" />"
  make_project bench-$v "$SRC/LtBench.cs" "$ROOT" "$LIBS"
  make_project equiv-$v "$SRC/LtEquiv.cs $SRC/EquivModel.cs" "$ROOT" "$LIBS"
  make_project web-$v "$SRC/WebBench.cs $WORK/WebModel.cs" "$ROOT" "    <FrameworkReference Include=\"Microsoft.AspNetCore.App\" />
    <PackageReference Include=\"Microsoft.AspNetCore.TestHost\" Version=\"8.0.20\" />
    <ProjectReference Include=\"$ROOT/LsMsgPackFormatters/LsMsgPackFormatters.csproj\" />"
done
echo "Harnesses in $WORK, base: $(cat "$WORK/baseline/REF")"
