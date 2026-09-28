param([Parameter(Mandatory)][string]$DependencyDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$deps=(Resolve-Path -LiteralPath $DependencyDirectory).Path
$out=[IO.Path]::GetFullPath($OutputDirectory)
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'MSVC x64 build tools required'}
$vc=Join-Path $vs 'VC/Auxiliary/Build/vcvarsall.bat'
foreach($path in @($PSScriptRoot,$deps,$out,$vc)){if($path -match '["%&|<>^!\r\n]'){throw 'Unsupported build path characters'}}
New-Item $out -ItemType Directory -Force | Out-Null
$imgui=Join-Path $deps 'external/reshade/deps/imgui'
$batch=@"
@echo off
setlocal DisableDelayedExpansion
call "$vc" x64
if errorlevel 1 exit /b 1
cd /d "$out"
cl /nologo /std:c++20 /EHsc /MT /O2 /utf-8 /I"$deps/external/reshade" "$PSScriptRoot/tests/menu_docking_tests.cpp" "$imgui/imgui.cpp" "$imgui/imgui_draw.cpp" "$imgui/imgui_tables.cpp" "$imgui/imgui_widgets.cpp" user32.lib imm32.lib /Fe:menu_docking_tests.exe
if errorlevel 1 exit /b 1
menu_docking_tests.exe
exit /b %errorlevel%
"@
$path=Join-Path $out 'test.cmd'
[IO.File]::WriteAllText($path,$batch,[Text.Encoding]::Default)
& $env:ComSpec /d /c ('"'+$path+'"') 2>&1 | Tee-Object (Join-Path $out 'test.log')
if($LASTEXITCODE -ne 0){throw 'Menu docking tests failed'}
