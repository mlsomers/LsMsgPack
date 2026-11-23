#!/usr/bin/env bash
# Unity smoke test of LsMsgPack: .NET 10 writes golden.txt, Unity checks it in the editor (Mono JIT), in Linux players (Mono, IL2CPP per stripping level), in WebGL players (headless Firefox)
# and in Android players (a phone or emulator on adb).
# Usage: run.sh [editor|mono|il2cpp-<level>|webgl-<level>|android-<level>...]   (default: editor mono il2cpp-Minimal il2cpp-High il2cpp-High+keep webgl-Minimal, and android-Minimal when adb has a device; +keep: with a link.xml keeping the model)
set -uo pipefail
cd "$(dirname "$0")"
here=$(pwd)
unity=${UNITY:-$HOME/Unity/Hub/Editor/6000.6.10f1/Editor/Unity}
out="$here/out"
mkdir -p "$out"
adb=${ADB:-$HOME/Android/Sdk/platform-tools/adb}
[ -x "$adb" ] || adb=$(dirname "$unity")/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb
steps=("$@")
if [ ${#steps[@]} -eq 0 ]; then
  steps=(editor mono il2cpp-Minimal il2cpp-High il2cpp-High+keep webgl-Minimal)
  [ "$("$adb" get-state 2>/dev/null)" = device ] && steps+=(android-Minimal) || echo "no Android device on adb: android steps left out"
fi

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
# The analyzers the same way (LsMsgPack.Core's package has them next to the generator); AnalyzerSamples.cs has deliberate warnings, looked for in the editor's log
dotnet build ../LsMsgPack.Analyzers/LsMsgPack.Analyzers.csproj -c Release -v q -nologo > "$out/analyzers.log" || { cat "$out/analyzers.log"; exit 1; }
cmp -s ../LsMsgPack.Analyzers/bin/Release/netstandard2.0/LsMsgPack.Analyzers.dll Project/Assets/Plugins/LsMsgPack/Analyzers/LsMsgPack.Analyzers.dll || cp ../LsMsgPack.Analyzers/bin/Release/netstandard2.0/LsMsgPack.Analyzers.dll Project/Assets/Plugins/LsMsgPack/Analyzers/
dotnet run --project Golden/Golden.csproj -c Release -- "$here/golden.txt" > "$out/golden.log" || { cat "$out/golden.log"; exit 1; }

failed=0
rm -f "$out"/*.txt
for step in "${steps[@]}"; do
  case "$step" in
    editor)
      # A new stamp makes Unity compile the analyzer samples' assembly, so their warnings are in this log (Unity logs warnings only when it compiles)
      printf '// Written by run.sh: a change per run, so Unity compiles this assembly\nnamespace LsMsgPackUnitySmoke { internal static class AnalyzerStamp { internal const string Run = "%s"; } }\n' "$(date +%s%N)" > Project/Assets/AnalyzerSamples/Stamp.cs
      "$unity" -batchmode -nographics -projectPath Project -executeMethod LsMsgPackUnitySmoke.Editor.SmokeBuild.RunInEditor -golden "$here/golden.txt" -out "$out/editor.txt" -logFile "$out/editor.log"
      code=$?
      # The analyzers ran when Unity compiled the scripts: the deliberate warnings of AnalyzerSamples.cs
      for id in LSMP001 LSMP004 LSMP010; do
        if grep -q "AnalyzerSamples.cs([0-9,]*): warning $id" "$out/editor.log"; then echo "analyzers: $id: OK" >> "$out/editor.txt"
        else echo "analyzers: $id not in the editor's log (compiled with the analyzers?)" >> "$out/editor.txt"; code=1; fi
      done
      # Unity's engine structs are not classes of another project (LSMP008)
      if grep -q "warning LSMP008" "$out/editor.log"; then echo "analyzers: LSMP008 reported: $(grep -o "warning LSMP008: '[^']*'" "$out/editor.log" | sort -u | tr '\n' ' ')" >> "$out/editor.txt"; code=1
      else echo "analyzers: no LSMP008: OK" >> "$out/editor.txt"; fi;;
    webgl-*)
      keep=()
      stripping=${step#webgl-}
      if [[ "$stripping" == *+keep ]]; then stripping=${stripping%+keep}; keep=(-keep); fi
      player="$here/Builds/$step"
      "$unity" -batchmode -nographics -projectPath Project -executeMethod LsMsgPackUnitySmoke.Editor.SmokeBuild.Build -target WebGL -stripping $stripping "${keep[@]}" -player "$player" -logFile "$out/build-$step.log"
      code=$?
      if [ $code -ne 0 ]; then
        echo "$step: build failed ($out/build-$step.log)"; grep -E "error CS|Error|\[smoke\]" "$out/build-$step.log" | head -20
      else
        grep "\[smoke\] build" "$out/build-$step.log"
        # The page fetches golden.txt and posts its report to serve.py; Firefox writes the page's console to its output (a profile under out/: snap's Firefox has its own /tmp)
        rm -f "$out/port" && profile="$out/firefox-$step" && rm -rf "$profile" && mkdir -p "$profile"
        printf 'user_pref("devtools.console.stdout.content", true);\nuser_pref("browser.shell.checkDefaultBrowser", false);\nuser_pref("datareporting.policy.dataSubmissionEnabled", false);\n' > "$profile/user.js"
        python3 serve.py "$player" "$here/golden.txt" "$out/$step.txt" "$out/port" 600 2> "$out/serve-$step.log" &
        serve=$!
        for i in $(seq 50); do [ -s "$out/port" ] && break; sleep 0.1; done
        firefox --headless --no-remote --profile "$profile" "http://127.0.0.1:$(cat "$out/port")/index.html" > "$out/$step.log" 2>&1 &
        browser=$!
        wait $serve
        code=$?
        kill $browser 2>/dev/null; wait $browser 2>/dev/null
        [ $code -eq 3 ] && { echo "$step: no report within 10 minutes ($out/$step.log)"; grep -iE "error|exception|abort|\[smoke\]" "$out/$step.log" | head -20; }
      fi;;
    android-*)
      keep=()
      stripping=${step#android-}
      if [[ "$stripping" == *+keep ]]; then stripping=${stripping%+keep}; keep=(-keep); fi
      player="$here/Builds/$step.apk"
      "$unity" -batchmode -nographics -projectPath Project -executeMethod LsMsgPackUnitySmoke.Editor.SmokeBuild.Build -target Android -stripping $stripping "${keep[@]}" -player "$player" -logFile "$out/build-$step.log"
      code=$?
      if [ $code -ne 0 ]; then
        echo "$step: build failed ($out/build-$step.log)"; grep -E "error CS|Error|\[smoke\]" "$out/build-$step.log" | head -20
      elif ! "$adb" install -r "$player" > "$out/install-$step.log" 2>&1; then
        echo "$step: adb install failed"; cat "$out/install-$step.log"; code=1
      else
        grep "\[smoke\] build" "$out/build-$step.log"
        # The app fetches golden.txt and posts its report to 127.0.0.1:8765 on the phone (WebRunner.AndroidPort), forwarded to serve.py with adb reverse; Unity's log comes from logcat
        rm -f "$out/port"
        python3 serve.py "$out" "$here/golden.txt" "$out/$step.txt" "$out/port" 600 2> "$out/serve-$step.log" &
        serve=$!
        for i in $(seq 50); do [ -s "$out/port" ] && break; sleep 0.1; done
        "$adb" reverse tcp:8765 tcp:$(cat "$out/port") > /dev/null
        "$adb" logcat -c
        "$adb" logcat -s Unity:V CRASH:V AndroidRuntime:E DEBUG:V > "$out/$step.log" 2>&1 &
        logcat=$!
        "$adb" shell am force-stop com.lsmsgpack.smoke
        # Behind the lock screen Android pauses the app at once: wake the screen, and wait for someone to unlock it
        "$adb" shell input keyevent KEYCODE_WAKEUP
        for i in $(seq 120); do
          "$adb" shell dumpsys window | grep -q "isKeyguardShowing=true" || break
          [ $i -eq 1 ] && echo "$step: unlock the phone (waiting 2 minutes)"
          sleep 1
        done
        "$adb" shell monkey -p com.lsmsgpack.smoke -c android.intent.category.LAUNCHER 1 > /dev/null 2>&1
        wait $serve
        code=$?
        "$adb" shell am force-stop com.lsmsgpack.smoke
        "$adb" reverse --remove tcp:8765 > /dev/null 2>&1
        kill $logcat 2>/dev/null; wait $logcat 2>/dev/null
        [ $code -eq 3 ] && { echo "$step: no report within 10 minutes (phone locked? $out/$step.log)"; grep -iE "error|exception|abort|crash|\[smoke\]" "$out/$step.log" | head -20; }
      fi;;
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
