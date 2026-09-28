param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'MSVC x64 build tools required'}
$vc=Join-Path $vs 'VC/Auxiliary/Build/vcvarsall.bat'
foreach($path in @($PSScriptRoot,$out,$vc)){if($path -match '["%&|<>^!\r\n]'){throw 'Unsupported build path characters'}}
New-Item $out -ItemType Directory -Force | Out-Null
$batch=@"
@echo off
setlocal DisableDelayedExpansion
call "$vc" x64
if errorlevel 1 exit /b 1
cd /d "$out"
cl /nologo /std:c++20 /EHsc /MT /O2 /W4 /utf-8 /LD "$PSScriptRoot/dllmain.cpp" user32.lib /link /OUT:ww_fps_core_candidate.dll /Brepro
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 /W4 /utf-8 "$PSScriptRoot/tests/core_tests.cpp" user32.lib /Fe:fps_core_tests.exe
if errorlevel 1 exit /b 1
fps_core_tests.exe
exit /b %errorlevel%
"@
$path=Join-Path $out 'build.cmd'
[IO.File]::WriteAllText($path,$batch,[Text.Encoding]::Default)
& $env:ComSpec /d /c ('"'+$path+'"') 2>&1 | Tee-Object (Join-Path $out 'build.log')
if($LASTEXITCODE -ne 0){throw 'FPS candidate build/tests failed'}
Get-FileHash (Join-Path $out 'ww_fps_core_candidate.dll')
