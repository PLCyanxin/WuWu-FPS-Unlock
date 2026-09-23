param(
    [Parameter(Mandatory)][string]$SourceDirectory,
    [Parameter(Mandatory)][string]$DependencyDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$deps = (Resolve-Path -LiteralPath $DependencyDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
# Paths are passed through a generated developer-command batch file.
foreach ($path in @($source,$deps,$output)) {
    if ($path -match '["%&|<>^\r\n]') { throw 'Build paths contain unsupported shell characters.' }
}
$locked = @{
    '' = '9b212edad4dde9bca2b823b1e045b712b1a8d854'
    'external/reshade' = '4a50d1eddace85734871d91792ff214f13f66c01'
    'external/reshade/deps/imgui' = '3912b3d9a9c1b3f17431aebafd86d2f40ee6e59c'
    'external/Streamline' = 'e8aaa6eaac968711fb62473d4ae8256dde20919b'
    'external/DLSS' = 'a291cc7d2cc642a51566f3dfd5376f635cd1b284'
    'external/Detours' = '9764cebcb1a75940e68fa83d6730ffaf0f669401'
    'external/json' = '55f93686c01528224f448c19128836e7df245f72'
}
foreach ($relative in $locked.Keys) {
    $checkout = if ($relative) { Join-Path $deps $relative } else { $deps }
    $head = & git -C $checkout rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $head -ne $locked[$relative]) { throw "Unpinned build dependency: $relative" }
}
& git -C $source merge-base --is-ancestor c3733d8afd51214c46a71d18feec520b0bf54864 HEAD
if ($LASTEXITCODE -ne 0) { throw 'The addon source must derive from upstream tag 1.1.' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Visual Studio C++ x64 build tools are required.' }
$vcvars = Join-Path $vs 'VC/Auxiliary/Build/vcvarsall.bat'
foreach ($file in @("$source/src/addons/mfgunlock/dynamicmax.hpp", "$deps/external/reshade/include/reshade.hpp",
    "$deps/external/reshade/deps/imgui/imgui.h", "$deps/external/Streamline/include/sl_dlss_g.h",
    "$deps/external/DLSS/include/nvsdk_ngx.h", "$deps/external/Detours/src/Makefile")) {
    if (!(Test-Path -LiteralPath $file)) { throw "Build dependency missing: $file" }
}
New-Item -ItemType Directory -Force -Path $output | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$resource = @'
#include <winver.h>
1 VERSIONINFO
FILEVERSION 1,1,0,2
PRODUCTVERSION 1,1,0,2
FILETYPE VFT_DLL
BEGIN
 BLOCK "StringFileInfo"
 BEGIN
  BLOCK "040904b0"
  BEGIN
   VALUE "OriginalFilename", "renodx-mfgunlock.addon64\0"
   VALUE "FileVersion", "1.1+WuWu.DynamicMax.Runtime.1\0"
   VALUE "ProductVersion", "1.1+WuWu.DynamicMax.Runtime.1\0"
   VALUE "FileDescription", "MFG Unlock 1.1 with experimental live Dynamic maximum support\0"
  END
 END
 BLOCK "VarFileInfo"
 BEGIN
  VALUE "Translation", 0x409, 1200
 END
END
'@
[IO.File]::WriteAllText((Join-Path $output 'mfgunlock.rc'), $resource, $utf8)
$includes = @('Detours/include','Streamline/include','DLSS/include','reshade','json/include') | ForEach-Object { '/I"' + (Join-Path "$deps/external" $_) + '"' }
$runtimeTests = @"
cl /nologo /std:c++20 /EHsc /MT /O2 /DNOMINMAX /utf-8 /I"$deps/external/Detours/include" "$source/tests/dynamiclive_tests.cpp" "$deps/external/Detours/lib.X64/detours.lib" /Fe:dynamiclive_tests.exe
if errorlevel 1 exit /b 1
dynamiclive_tests.exe
if errorlevel 1 exit /b 1
"@
$batch = @"
@echo off
call "$vcvars" x64
if errorlevel 1 exit /b 1
pushd "$deps/external/Detours/src"
nmake /nologo ARCH=X64
if errorlevel 1 exit /b 1
popd
cd /d "$output"
rc /nologo /fo mfgunlock.res mfgunlock.rc
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 /DNDEBUG /DNOMINMAX /bigobj /utf-8 /LD $($includes -join ' ') "$source/src/addons/mfgunlock/addon.cpp" mfgunlock.res "$deps/external/Detours/lib.X64/detours.lib" user32.lib /link /OUT:renodx-mfgunlock.addon64 /Brepro
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 /DNOMINMAX /utf-8 "$source/tests/dynamicmax_policy_tests.cpp" /Fe:dynamicmax_policy_tests.exe
if errorlevel 1 exit /b 1
dynamicmax_policy_tests.exe
if errorlevel 1 exit /b 1
$runtimeTests
exit /b %errorlevel%
"@
$batchPath = Join-Path $output 'build.cmd'
[IO.File]::WriteAllText($batchPath, $batch, [Text.Encoding]::Default)
& $env:ComSpec /d /c ('"' + $batchPath + '"') 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Addon build or test failed. See $output/build.log" }
Get-FileHash -LiteralPath (Join-Path $output 'renodx-mfgunlock.addon64') -Algorithm SHA256
