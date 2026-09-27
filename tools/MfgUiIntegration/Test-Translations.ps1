param([Parameter(Mandatory)][string]$TranslationsHeader,[Parameter(Mandatory)][string]$ImGuiDirectory,[string]$OutputDirectory=(Join-Path $PSScriptRoot '../../artifacts/tests/translations'))
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item -LiteralPath $TranslationsHeader -Destination (Join-Path $out 'translations.generated.hpp') -Force
$imgui=(Resolve-Path -LiteralPath $ImGuiDirectory).Path
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$installation){throw 'Visual C++ x64 build tools are required.'}
$vcvars=Join-Path $installation 'VC/Auxiliary/Build/vcvarsall.bat'
foreach($path in @($out,$imgui,$vcvars,$PSScriptRoot)){if($path -match '["%&|<>^!\r\n]'){throw 'Unsupported build path'}}
@"
@echo off
call "$vcvars" x64 >nul
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 /DNOMINMAX /utf-8 /I"$imgui" /I"$PSScriptRoot" /I"$out" "$PSScriptRoot/tests/translation_tests.cpp" /Fe:"$out/translation-tests.exe" /Fo:"$out/translation-tests.obj"
exit /b %errorlevel%
"@ | Set-Content -LiteralPath (Join-Path $out 'build.cmd') -Encoding ascii
& cmd /c (Join-Path $out 'build.cmd') 2>&1 | Tee-Object -FilePath (Join-Path $out 'build.log')
if($LASTEXITCODE -ne 0){throw 'Translation tests compilation failed.'}
& (Join-Path $out 'translation-tests.exe') 2>&1 | Tee-Object -FilePath (Join-Path $out 'test.log')
if($LASTEXITCODE -ne 0){throw 'Translation tests failed.'}
