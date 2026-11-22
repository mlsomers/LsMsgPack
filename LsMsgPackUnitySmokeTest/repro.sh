#!/usr/bin/env bash
# Minimal repro of IL2CPP's internal error on Unsafe.Unbox<T> (Repro/Assets/Scripts/UnboxRepro.cs): one IL2CPP build per variant
cd "$(dirname "$0")"; here=$(pwd); unity=${UNITY:-$HOME/Unity/Hub/Editor/6000.6.10f1/Editor/Unity}; mkdir -p out
for v in "$@"; do  # a variant may combine defines: "UNBOX;ONLY_PLAIN"
  "$unity" -batchmode -nographics -projectPath Repro -executeMethod ReproBuild.Build -variant $v -player "$here/Builds/repro-${v//;/_}/Repro.x86_64" -logFile "out/repro-build-${v//;/_}.log"
  if [ $? -ne 0 ]; then echo "$v: build failed"; grep -m2 "Internal Error" "out/repro-build-${v//;/_}.log" | cut -c1-300; continue; fi
  rm -f "out/repro-${v//;/_}.txt"; "$here/Builds/repro-${v//;/_}/Repro.x86_64" -batchmode -nographics -out "$here/out/repro-${v//;/_}.txt" -logFile "out/repro-run-${v//;/_}.log"
  echo "$v: $(cat out/repro-${v//;/_}.txt 2>/dev/null | head -c 300)"
done
