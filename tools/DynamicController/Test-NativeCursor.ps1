param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/tests/native-cursor'))
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$installation) { throw 'Visual C++ x64 build tools are required.' }
$vcvars = Join-Path $installation 'VC/Auxiliary/Build/vcvarsall.bat'
$source = Join-Path $PSScriptRoot 'native_cursor_tests.cpp'
$binary = Join-Path $OutputDirectory 'native-cursor-tests.exe'
$object = Join-Path $OutputDirectory 'native-cursor-tests.obj'
$command = Join-Path $OutputDirectory 'build-tests.cmd'
@"
@echo off
call "$vcvars" x64 >nul
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 /DNOMINMAX /utf-8 "$source" /Fe:"$binary" /Fo:"$object"
exit /b %errorlevel%
"@ | Set-Content -LiteralPath $command -Encoding ascii
& cmd /c $command 2>&1 | Tee-Object -FilePath (Join-Path $OutputDirectory 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'Native cursor test compilation failed.' }
& $binary 2>&1 | Tee-Object -FilePath (Join-Path $OutputDirectory 'test.log')
if ($LASTEXITCODE -ne 0) { throw 'Native cursor tests failed.' }
