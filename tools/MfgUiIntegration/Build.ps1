param([Parameter(Mandatory)][string]$UpstreamDirectory,[Parameter(Mandatory)][string]$GeneratedDirectory,[Parameter(Mandatory)][string]$DependencyDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
$deps=(Resolve-Path $DependencyDirectory).Path
$upstream=(Resolve-Path $UpstreamDirectory).Path
$generated=(Resolve-Path $GeneratedDirectory).Path
New-Item $out -ItemType Directory -Force|Out-Null
python "$PSScriptRoot/prepare.py" --source "$upstream/src/addons/mfgunlock" --generated $generated --output "$out/source"
if($LASTEXITCODE -ne 0){throw 'UI integration preparation failed'}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'MSVC x64 tools required'}
$vc=Join-Path $vs 'VC/Auxiliary/Build/vcvarsall.bat'
foreach($path in @($out,$deps,$vc,$PSScriptRoot)){if($path -match '["%&|<>^!\r\n]'){throw 'Unsupported build path'}}
$batch=@"
@echo off
setlocal DisableDelayedExpansion
call "$vc" x64
if errorlevel 1 exit /b 1
cd /d "$out"
rc /nologo /fo mfg.res "$PSScriptRoot/addon.rc"
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 /DNDEBUG /DNOMINMAX /bigobj /utf-8 /LD /I"$deps/external/Detours/include" /I"$deps/external/Streamline/include" /I"$deps/external/DLSS/include" /I"$deps/external/reshade" /I"$deps/external/json/include" "$out/source/addon.cpp" mfg.res "$deps/external/Detours/lib.X64/detours.lib" user32.lib /link /OUT:renodx-mfgunlock.addon64 /Brepro
exit /b %errorlevel%
"@
[IO.File]::WriteAllText("$out/build.cmd",$batch,[Text.Encoding]::Default)
& $env:ComSpec /d /c ('"'+$out+'/build.cmd"') 2>&1|Tee-Object "$out/build.log"
if($LASTEXITCODE -ne 0){throw 'MFG UI build failed'}
Get-FileHash "$out/renodx-mfgunlock.addon64"
