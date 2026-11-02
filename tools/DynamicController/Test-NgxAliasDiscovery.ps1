param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$source=$PSScriptRoot
$out=[IO.Path]::GetFullPath($OutputDirectory)
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'MSVC x64 build tools required'}
$vc=Join-Path $vs 'VC/Auxiliary/Build/vcvarsall.bat'
foreach($path in @($source,$out,$vc)){if($path -match '["%&|<>^!\r\n]'){throw 'Unsupported path'} }
$dllDir=Join-Path $out 'nvidia\ngx\models\sl_dlss_g_0\versions\134672\files'
$secondDir=Join-Path $out 'nvidia\ngx\models\sl_dlss_g_0\versions\134673\files'
New-Item -ItemType Directory -Path $dllDir,$secondDir -Force | Out-Null
$dll=Join-Path $dllDir '190_E658703.dll'
$second=Join-Path $secondDir '190_E658704.dll'
$ordinary=Join-Path $out 'sl.dlss_g.dll'
$runner=Join-Path $out 'dynamiclive_discovery_tests.exe'
$dllSrc=Join-Path $source 'tests\synthetic_ngx_alias.cpp'
$testSrc=Join-Path $source 'tests\dynamiclive_discovery_tests.cpp'
$cmd=Join-Path $out 'discovery_tests.cmd'
$batch=@"
@echo off
setlocal DisableDelayedExpansion
call "$vc" x64
if errorlevel 1 exit /b 1
cd /d "$out"
cl /nologo /std:c++20 /EHsc /MT /O2 /LD "$dllSrc" /link /OUT:"$dll"
if errorlevel 1 exit /b 1
copy /y "$dll" "$second" >nul
if errorlevel 1 exit /b 1
copy /y "$dll" "$ordinary" >nul
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 "$testSrc" /link /OUT:"$runner"
if errorlevel 1 exit /b 1
"$runner" "$dll" "$second" "$ordinary"
exit /b %errorlevel%
"@
[IO.File]::WriteAllText($cmd,$batch,[Text.Encoding]::Default)
& $env:ComSpec /d /c ('"'+$cmd+'"') 2>&1 | Tee-Object (Join-Path $out 'discovery-tests.log')
if($LASTEXITCODE -ne 0){throw 'Dynamic NGX alias discovery test failed'}
Write-Output 'DYNAMIC_NGX_ALIAS_DISCOVERY=PASS (benign synthetic modules; no vendor code)'
