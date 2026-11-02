param([string]$OutputDirectory='artifacts/tests/frame-modes')
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'MSVC x64 build tools required'}
$vc=Join-Path $vs 'VC/Auxiliary/Build/vcvarsall.bat'
foreach($path in @($out,$vc,$PSScriptRoot)){if($path -match '["%&|<>^!\r\n]'){throw 'Unsupported test build path'}}
New-Item -ItemType Directory -Path $out -Force | Out-Null
$batch=@"
@echo off
setlocal DisableDelayedExpansion
call "$vc" x64
if errorlevel 1 exit /b 1
cd /d "$out"
cl /nologo /std:c++20 /EHsc /MT /O2 /utf-8 "$PSScriptRoot/tests/frame_mode_tests.cpp" /Fe:frame_mode_tests.exe
if errorlevel 1 exit /b 1
frame_mode_tests.exe
exit /b %errorlevel%
"@
$command=Join-Path $out 'build.cmd'
[IO.File]::WriteAllText($command,$batch,[Text.Encoding]::Default)
& $env:ComSpec /d /c ('"'+$command+'"') 2>&1 | Tee-Object (Join-Path $out 'test.log')
if($LASTEXITCODE -ne 0){throw 'Frame mode persistence tests failed'}
