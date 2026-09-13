[CmdletBinding()]
param([string]$Version='8.0.31', [switch]$UseOfficialBlobMirror, [switch]$ReuseMetadata, [switch]$DesktopOnlyMirror,
      [string]$Destination=(Join-Path (Split-Path $PSScriptRoot -Parent) 'components/dotnet8'))
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
$cache=Join-Path $workspace '.tools/dotnet8-runtime'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$metadataUrl='https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json'
$metadataPath=Join-Path $cache 'releases.json'
if(-not($ReuseMetadata -and (Test-Path -LiteralPath $metadataPath))){Invoke-WebRequest -Uri $metadataUrl -OutFile $metadataPath -TimeoutSec 120}
$metadata=Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$release=@($metadata.releases | Where-Object 'release-version' -eq $Version)
if($release.Count -ne 1){throw "官方元数据未找到唯一版本 $Version"}
$packages=@($release[0].runtime.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -eq 'dotnet-runtime-win-x64.zip' }) + @($release[0].windowsdesktop.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -eq 'windowsdesktop-runtime-win-x64.zip' })
if($packages.Count -ne 2){throw '缺少唯一NETCore与WindowsDesktop x64 ZIP'}
$records=@()
foreach($package in $packages){
    $uri=[Uri]$package.url
    if($uri.Scheme -ne 'https' -or $uri.Host -ne 'builds.dotnet.microsoft.com'){throw '非Microsoft官方构建源'}
    $mirror=$UseOfficialBlobMirror -or ($DesktopOnlyMirror -and $package.name -like 'windowsdesktop*'); $downloadUrl=if($mirror){$uri.AbsoluteUri.Replace('https://builds.dotnet.microsoft.com/','https://dotnetcli.blob.core.windows.net/')}else{$uri.AbsoluteUri}
    $archive=Join-Path $cache ([IO.Path]::GetFileName($uri.AbsolutePath))
    if($mirror){$archive=$archive.Replace('.zip','.blob.zip')}
    if(-not(Test-Path -LiteralPath $archive)){Invoke-WebRequest -Uri $downloadUrl -OutFile $archive -TimeoutSec 600}
    $hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash.ToLowerInvariant()
    if($hash -cne $package.hash.ToLowerInvariant()){throw "SHA512不匹配：$archive；不解压。"}
    Write-Output "VERIFIED $($package.name) $Version SHA512=$hash"
    Expand-Archive -LiteralPath $archive -DestinationPath $Destination -Force
    $records+=[ordered]@{version=$Version;rid='win-x64';url=$uri.AbsoluteUri;downloadUrl=$downloadUrl;sha512=$hash;metadataSha512=$package.hash;archiveLength=(Get-Item -LiteralPath $archive).Length;verified=$true}
}
$dotnet=Join-Path $Destination 'dotnet.exe'
if(-not(Test-Path -LiteralPath $dotnet)){throw '缺少dotnet host'}
$runtimes=@(& $dotnet --list-runtimes)
if($LASTEXITCODE -ne 0){throw 'dotnet --list-runtimes失败'}
if(-not($runtimes -match "Microsoft.NETCore.App $([regex]::Escape($Version)) ") -or -not($runtimes -match "Microsoft.WindowsDesktop.App $([regex]::Escape($Version)) ")){throw '运行时列表缺少所需版本'}
$runtimes | Write-Output
$provenance=[ordered]@{createdAt=[DateTimeOffset]::Now.ToString('o');metadataUrl=$metadataUrl;metadataSha256=(Get-FileHash -LiteralPath $metadataPath -Algorithm SHA256).Hash;metadataLatestRuntime=$metadata.'latest-runtime';pinnedVersion=$Version;packages=$records;runtimeList=$runtimes;unlockerExecuted=$false}
$provenance | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Destination 'PROVENANCE.json') -Encoding utf8
