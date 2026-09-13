param([switch]$InstallSdk, [switch]$RunAfterBuild)
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
Set-Location $Root
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_NOLOGO='1'
$env:DOTNET_CLI_HOME=Join-Path $Root '.tools\dotnet-home'
$env:NUGET_PACKAGES=Join-Path $Root '.nuget\packages'
$env:TEMP=Join-Path $Root '.tmp'
$env:TMP=$env:TEMP
New-Item $env:TEMP -ItemType Directory -Force | Out-Null
New-Item (Join-Path $Root 'artifacts') -ItemType Directory -Force | Out-Null
$localDotnet=Join-Path $Root '.tools\dotnet\dotnet.exe'
$dotnet=$null
if(Test-Path $localDotnet){$dotnet=$localDotnet}
elseif(Get-Command dotnet -ErrorAction SilentlyContinue){$dotnet=(Get-Command dotnet).Source}
$hasSdk=$false
if($dotnet){$sdks=& $dotnet --list-sdks; $hasSdk=[bool]($sdks -match '^10\.')}
if(-not $hasSdk){
    if(-not $InstallSdk){throw '需要 .NET 10 SDK。双击 Build.cmd 可将 SDK 安装在当前项目的 .tools 目录，不需要 WSL。'}
    $tools=Join-Path $Root '.tools';New-Item $tools -ItemType Directory -Force|Out-Null
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    $install=Join-Path $tools 'dotnet-install.ps1'
    Write-Host 'Downloading the official Microsoft SDK installer...'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -UseBasicParsing -OutFile $install
    & $install -Version '10.0.100' -Architecture 'x64' -InstallDir (Join-Path $tools 'dotnet') -NoPath
    if(-not(Test-Path $localDotnet)){throw 'SDK installation failed.'}
    $dotnet=$localDotnet
}
Write-Host 'Running core regression tests...'
& $dotnet run --project '.\tests\WuWaFpsUnlock.Tests\WuWaFpsUnlock.Tests.csproj' -c Release 2>&1 | Tee-Object '.\artifacts\tests.log'
if($LASTEXITCODE -ne 0){throw 'Core tests failed; publish stopped.'}
Write-Host 'Running native Windows and local ReShade integration tests...'
& $dotnet run --project '.\tests\WuWaFpsUnlock.WindowsTests\WuWaFpsUnlock.WindowsTests.csproj' -c Release -- $Root 2>&1 | Tee-Object '.\artifacts\windows-tests.log'
if($LASTEXITCODE -ne 0){throw 'Windows integration tests failed; publish stopped.'}
Write-Host 'Publishing Windows x64 WPF application...'
& $dotnet publish '.\src\WuWaFpsUnlock\WuWaFpsUnlock.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:PublishAot=false -o '.\artifacts\win-x64' 2>&1 | Tee-Object '.\artifacts\build.log'
if($LASTEXITCODE -ne 0){throw 'WPF build/publish failed. Check artifacts\build.log.'}
Copy-Item '.\docs\WINDOWS_VALIDATION.md' '.\artifacts\win-x64\WINDOWS_VALIDATION.md' -Force
if(Test-Path '.\components\dotnet8\dotnet.exe'){ Copy-Item '.\components\dotnet8' '.\artifacts\win-x64\components' -Recurse -Force }
if(Test-Path '.\payload\manifest.json'){New-Item '.\artifacts\win-x64\payload' -ItemType Directory -Force|Out-Null; Copy-Item '.\payload\*' '.\artifacts\win-x64\payload' -Recurse -Force}
$exe=Join-Path $Root 'artifacts\win-x64\WuWaFpsUnlock.exe'
Get-FileHash $exe -Algorithm SHA256 | Format-List | Out-File '.\artifacts\BUILD_SHA256.txt' -Encoding utf8
Write-Host "Built: $exe"
Write-Host 'Build success is not a real-game compatibility test. Read WINDOWS_VALIDATION.md.'
if($RunAfterBuild){Start-Process $exe}
