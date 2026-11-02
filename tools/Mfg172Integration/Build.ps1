param([Parameter(Mandatory)][string]$UpstreamDirectory,[Parameter(Mandatory)][string]$GeneratedDirectory,[Parameter(Mandatory)][string]$DependencyDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
$deps=(Resolve-Path $DependencyDirectory).Path
$upstream=(Resolve-Path $UpstreamDirectory).Path
$generated=(Resolve-Path $GeneratedDirectory).Path
New-Item $out -ItemType Directory -Force|Out-Null
python "$PSScriptRoot/prepare172.py" --source "$upstream/src/addons/mfgunlock" --generated $generated --output "$out/source"
if($LASTEXITCODE -ne 0){throw 'UI integration preparation failed'}
python "$PSScriptRoot/tests/display_coverage.py" --prepared "$out/source" --report "$out/display-coverage.json"
if($LASTEXITCODE -ne 0){throw 'Visible UI translations are incomplete'}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'MSVC x64 tools required'}
$vc=Join-Path $vs 'VC/Auxiliary/Build/vcvarsall.bat'
foreach($path in @($out,$deps,$vc,$PSScriptRoot)){if($path -match '["%&|<>^!\r\n]'){throw 'Unsupported build path'}}
$batch=@"
@echo off
setlocal DisableDelayedExpansion
call "$vc" x64
if not "%errorlevel%"=="0" exit /b %errorlevel%
cd /d "$out"
cl /nologo /std:c++20 /EHsc /MT /O2 /DNOMINMAX /utf-8 /I"$out/source" "$PSScriptRoot/tests/runtime_policy_tests.cpp" /Fe:runtime_policy_tests.exe
if not "%errorlevel%"=="0" exit /b %errorlevel%
runtime_policy_tests.exe
if not "%errorlevel%"=="0" exit /b %errorlevel%
rc /nologo /fo mfg.res "$PSScriptRoot/addon.rc"
if not "%errorlevel%"=="0" exit /b %errorlevel%
cl /nologo /std:c++20 /EHsc /MT /O2 /DNDEBUG /DNOMINMAX /DMFGUNLOCK_LOCAL_LOW_OVERHEAD /DMFGUNLOCK_LOCAL_STABILITY /DMFGUNLOCK_CONTINUOUS_BORDER_AXES /DMFGUNLOCK_RELEASE_1_4_3 /DMFGUNLOCK_RELEASE_1_4_4 /DMFGUNLOCK_ENGINEERING_EXPERIMENTAL /DMFGUNLOCK_RELEASE_1_4_5 /bigobj /utf-8 /LD /I"$deps/external/Detours/include" /I"$deps/external/Streamline/include" /I"$deps/external/DLSS/include" /I"$deps/external/Streamline/external/ngx-sdk/include" /I"$deps/external/reshade" /I"$deps/external/json/include" "$out/source/addon.cpp" mfg.res "$deps/external/Detours/lib.X64/detours.lib" user32.lib gdi32.lib shell32.lib ole32.lib uuid.lib comdlg32.lib advapi32.lib version.lib /link /OUT:renodx-mfgunlock.addon64 /Brepro
exit /b %errorlevel%
"@
[IO.File]::WriteAllText("$out/build.cmd",$batch,[Text.Encoding]::Default)
& $env:ComSpec /d /c ('"'+$out+'/build.cmd"') 2>&1|Tee-Object "$out/build.log"
if($LASTEXITCODE -ne 0){throw 'MFG UI build failed'}
Get-FileHash "$out/renodx-mfgunlock.addon64"
