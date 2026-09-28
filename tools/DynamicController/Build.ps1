param([Parameter(Mandatory)][string]$DependencyDirectory,[Parameter(Mandatory)][string]$OutputDirectory,[string]$ReferenceDll)
$ErrorActionPreference='Stop'
$source=$PSScriptRoot
$deps=(Resolve-Path -LiteralPath $DependencyDirectory).Path
$out=[IO.Path]::GetFullPath($OutputDirectory)
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'MSVC x64 build tools required'}
$vc=Join-Path $vs 'VC/Auxiliary/Build/vcvarsall.bat'
foreach($path in @($source,$deps,$out,$vc,$ReferenceDll)){if($path -match '["%&|<>^!\r\n]'){throw 'Unsupported build path characters'}}
foreach($path in @("$deps/external/Detours/include/detours.h","$deps/external/Detours/lib.X64/detours.lib","$deps/external/reshade/include/reshade.hpp","$deps/external/reshade/deps/imgui/imgui.h")){if(!(Test-Path -LiteralPath $path)){throw "Prepared dependency missing: $path"}}
New-Item $out -ItemType Directory -Force | Out-Null
$testArgs=if($ReferenceDll){'"'+(Resolve-Path -LiteralPath $ReferenceDll).Path+'"'}else{''}
$batch=@"
@echo off
setlocal DisableDelayedExpansion
call "$vc" x64
if errorlevel 1 exit /b 1
cd /d "$out"
rc /nologo /fo companion.res "$source/addon.rc"
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 /DNOMINMAX /utf-8 /LD /I"$deps/external/Detours/include" /I"$deps/external/reshade" "$source/addon.cpp" companion.res "$deps/external/Detours/lib.X64/detours.lib" user32.lib /link /OUT:wuwa-dynamicmax.addon64 /Brepro
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 /DNOMINMAX /utf-8 /I"$deps/external/Detours/include" "$source/tests/dynamiclive_tests.cpp" "$deps/external/Detours/lib.X64/detours.lib" /Fe:dynamiclive_tests.exe
if errorlevel 1 exit /b 1
dynamiclive_tests.exe $testArgs
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 /DNOMINMAX /utf-8 /I"$deps/external/Detours/include" /I"$deps/external/reshade" "$source/tests/config_wiring_tests.cpp" "$deps/external/Detours/lib.X64/detours.lib" user32.lib /Fe:config_wiring_tests.exe
if errorlevel 1 exit /b 1
config_wiring_tests.exe
exit /b %errorlevel%
"@
$path=Join-Path $out 'build.cmd'
[IO.File]::WriteAllText($path,$batch,[Text.Encoding]::Default)
& $env:ComSpec /d /c ('"'+$path+'"') 2>&1 | Tee-Object (Join-Path $out 'build.log')
if($LASTEXITCODE -ne 0){throw 'Companion build or tests failed'}
& "$source/Test-NativeCursor.ps1" -DependencyDirectory $deps -OutputDirectory (Join-Path $out 'cursor-tests')
Get-FileHash (Join-Path $out 'wuwa-dynamicmax.addon64')
