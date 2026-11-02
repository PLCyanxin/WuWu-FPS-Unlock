param([Parameter(Mandatory)][string]$Prepared,[Parameter(Mandatory)][string]$DependencyDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$preparedPath=(Resolve-Path -LiteralPath $Prepared).Path
$deps=(Resolve-Path -LiteralPath $DependencyDirectory).Path
$out=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $out -Force | Out-Null
python "$PSScriptRoot/tests/display_coverage.py" --prepared $preparedPath --report "$out/coverage.json"
if($LASTEXITCODE -ne 0){throw 'Display source coverage incomplete'}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$vc=Join-Path $vs 'VC/Auxiliary/Build/vcvarsall.bat'
$imgui=Join-Path $deps 'external/reshade/deps/imgui'
$batch=@"
@echo off
call "$vc" x64
if not "%errorlevel%"=="0" exit /b %errorlevel%
cd /d "$out"
cl /nologo /std:c++20 /EHsc /MT /O2 /DNOMINMAX /utf-8 /I"$deps/external/reshade" /I"$preparedPath" "$PSScriptRoot/tests/visible_ui_tests.cpp" "$imgui/imgui.cpp" "$imgui/imgui_draw.cpp" "$imgui/imgui_tables.cpp" "$imgui/imgui_widgets.cpp" user32.lib imm32.lib /Fe:visible_ui_tests.exe
if not "%errorlevel%"=="0" exit /b %errorlevel%
visible_ui_tests.exe
exit /b %errorlevel%
"@
[IO.File]::WriteAllText("$out/test.cmd",$batch,[Text.Encoding]::Default)
& $env:ComSpec /d /c ('"'+$out+'/test.cmd"') 2>&1 | Tee-Object "$out/test.log"
if($LASTEXITCODE -ne 0){throw 'Real UI translation or widget identity test failed'}
