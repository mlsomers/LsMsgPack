@echo off
rem Collects the files of the standalone MsgPackExplorer release into bin\ReleaseKeepTrack\LsMsgPack.
rem Build the solution in the ReleaseKeepTrack configuration first.
rem Only what the explorer and its installer (Installer.cs) need is copied: the build folder also holds
rem Fiddler's files, the Visual Studio visualizer assembly, Newtonsoft.Json, the pdbs and the PluginTester (net472\).
rem The installer takes DebuggerProxy.dll from the explorer's folder and creates the netstandard2.0 folder itself.

setlocal
set "SRC=%~dp0bin\ReleaseKeepTrack"
set "DEST=%SRC%\LsMsgPack"

set FILES=MsgPackExplorer.exe MsgPackExplorer.exe.config LsMsgPack.dll LsMsgPack.Core.dll ObjectDebugger.dll LsMsgPackFiddlerInspector.dll LsMsgPackVisualStudioPlugin.dll DebuggerProxy.dll System.Memory.dll System.Buffers.dll System.Numerics.Vectors.dll System.Runtime.CompilerServices.Unsafe.dll

rem Check everything first, so a missing file does not leave half a release behind.
set MISSING=
for %%F in (%FILES%) do (
  if not exist "%SRC%\%%F" (
    echo Missing: %SRC%\%%F
    set MISSING=1
  )
)
if defined MISSING (
  echo Build the solution in the ReleaseKeepTrack configuration first.
  exit /b 1
)

rem Start from an empty folder, so files of an earlier release do not stay in it.
if exist "%DEST%" rmdir /s /q "%DEST%"
if exist "%DEST%" (
  echo Could not remove %DEST%, is a file in it still in use?
  exit /b 1
)
mkdir "%DEST%" || exit /b 1

set FAILED=
for %%F in (%FILES%) do (
  copy /y "%SRC%\%%F" "%DEST%\" >nul || (
    echo Could not copy %%F
    set FAILED=1
  )
)
if defined FAILED exit /b 1

echo Copied the release files to %DEST%
exit /b 0
