#!/usr/bin/env bash
# Unity smoke test of LsMsgPack: .NET 10 writes golden.txt, Unity checks it in the editor (Mono JIT) and in Linux players (Mono, IL2CPP per stripping level).
# Usage: run.sh [editor|mono|il2cpp-<level>...]   (default: editor mono il2cpp-Minimal il2cpp-High il2cpp-High+keep; +keep: with a link.xml keeping the model)
set -uo pipefail
cd "$(dirname "$0")"
here=$(pwd)
unity=${UNITY:-$HOME/Unity/Hub/Editor/6000.6.10f1/Editor/Unity}
out="$here/out"
mkdir -p "$out"
steps=("$@")
[ ${#steps[@]} -eq 0 ] && steps=(editor mono il2cpp-Minimal il2cpp-High il2cpp-High+keep)

# The libraries' netstandard2.1 builds into Assets/Plugins (what a Unity project takes from NuGet)
dotnet build Libs/Libs.csproj -c Release -v q -nologo > "$out/libs.log" || { cat "$out/libs.log"; exit 1; }
mkdir -p Project/Assets/Plugins/LsMsgPack
# Not System.Runtime.CompilerServices.Unsafe: Unity has its own (10.0), the NuGet copy (6.0) made IL2CPP fail on Unsafe.Unbox
for dll in LsMsgPack.Core LsMsgPack LsMsgPackDebug; do
  cmp -s "Libs/bin/Release/netstandard2.1/$dll.dll" "Project/Assets/Plugins/LsMsgPack/$dll.dll" || cp "Libs/bin/Release/netstandard2.1/$dll.dll" Project/Assets/Plugins/LsMsgPack/
done
# The source generator as a Roslyn analyzer of the project (its .meta: label RoslynAnalyzer, no platform)
dotnet build ../LsMsgPack.Generator/LsMsgPack.Generator.csproj -c Release -v q -nologo > "$out/generator.log" || { cat "$out/generator.log"; exit 1; }
cmp -s ../LsMsgPack.Generator/bin/Release/netstandard2.0/LsMsgPack.Generator.dll Project/Assets/Plugins/LsMsgPack/Analyzers/LsMsgPack.Generator.dll || cp ../LsMsgPack.Generator/bin/Release/netstandard2.0/LsMsgPack.Generator.dll Project/Assets/Plugins/LsMsgPack/Analyzers/
dotnet run --project Golden/Golden.csproj -c Release -- "$here/golden.txt" > "$out/golden.log" || { cat "$out/golden.log"; exit 1; }

failed=0
rm -f "$out"/*.txt
for step in "${steps[@]}"; do
  case "$step" in
    editor)
      "$unity" -batchmode -nographics -projectPath Project -executeMethod LsMsgPackUnitySmoke.Editor.SmokeBuild.RunInEditor -golden "$here/golden.txt" -out "$out/editor.txt" -logFile "$out/editor.log"
      code=$?;;
    mono|il2cpp-*)
      keep=()
      if [ "$step" = mono ]; then backend=Mono; stripping=Disabled; else backend=IL2CPP; stripping=${step#il2cpp-}; fi
      if [[ "$stripping" == *+keep ]]; then stripping=${stripping%+keep}; keep=(-keep); fi
      player="$here/Builds/$step/Smoke.x86_64"
      "$unity" -batchmode -nographics -projectPath Project -executeMethod LsMsgPackUnitySmoke.Editor.SmokeBuild.Build -backend $backend -stripping $stripping "${keep[@]}" -player "$player" -logFile "$out/build-$step.log"
      code=$?
      if [ $code -ne 0 ]; then
        echo "$step: build failed ($out/build-$step.log)"; grep -E "error CS|Error|\[smoke\]" "$out/build-$step.log" | head -20
      else
        grep "\[smoke\] build" "$out/build-$step.log"
        "$player" -batchmode -nographics -golden "$here/golden.txt" -out "$out/$step.txt" -logFile "$out/$step.log"
        code=$?
      fi;;
    *) echo "unknown step $step"; exit 2;;
  esac
  echo "== $step: exit $code"
  [ -f "$out/$step.txt" ] && grep -vE ": OK$|^\s+at " "$out/$step.txt" | cut -c1-400
  [ $code -ne 0 ] && failed=1
done
exit $failed
