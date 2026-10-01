param([string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$out=Join-Path $repo ('artifacts/tests/reshade-ini-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
function Checkout([string]$Url,[string]$Commit,[string]$Path){
    git init $Path | Out-Null
    git -C $Path remote add origin $Url
    git -C $Path fetch --depth=1 origin $Commit
    if($LASTEXITCODE -ne 0){throw 'Cannot fetch pinned parser dependency'}
    git -C $Path checkout --detach FETCH_HEAD
    if($LASTEXITCODE -ne 0){throw 'Cannot check out parser dependency'}
    if((git -C $Path rev-parse HEAD) -ne $Commit){throw 'Parser dependency pin mismatch'}
}
Checkout 'https://github.com/crosire/reshade.git' '18deaa52de0c425a78b329e9cb3c497281cd00ec' "$out/reshade"
$utf=(git -C "$out/reshade" ls-tree HEAD deps/utfcpp) -split '\s+'
if($utf[1] -ne 'commit' -or $utf[2] -notmatch '^[a-f0-9]{40}$'){throw 'UTF header dependency not pinned in ReShade'}
Checkout 'https://github.com/nemtrif/utfcpp.git' $utf[2] "$out/utfcpp"
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$vc=Join-Path $vs 'VC/Auxiliary/Build/vcvarsall.bat'
foreach($path in @($out,$repo,$vc)){if($path -match '["%&|<>^!\r\n]'){throw 'Unsupported parser build path'}}
$batch=@"
@echo off
setlocal DisableDelayedExpansion
call "$vc" x64
if errorlevel 1 exit /b 1
cd /d "$out"
cl /nologo /std:c++20 /EHsc /MT /O2 /utf-8 /I"$out/reshade/source" /I"$out/utfcpp/source" "$repo/tests/ReShade.IniTests/reader.cpp" /Fe:reader.exe
exit /b %errorlevel%
"@
$command=Join-Path $out 'build.cmd';[IO.File]::WriteAllText($command,$batch,[Text.Encoding]::Default)
& $env:ComSpec /d /c ('"'+$command+'"')
if($LASTEXITCODE -ne 0){throw 'Upstream INI reader build failed'}
& $Dotnet run --project "$repo/tests/ReShade.IniTests" -c Release -- "$out/reader.exe"
if($LASTEXITCODE -ne 0){throw 'INI round-trip disagrees with upstream ReShade parser'}
