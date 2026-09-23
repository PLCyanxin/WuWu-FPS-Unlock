param([Parameter(Mandatory)][string]$Version,[string]$BaselineZip)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if($Version -notmatch '^\d+\.\d+\.\d+(?:RC\d*)?$'){throw 'Invalid release version'}
if(!$BaselineZip){
    gh release download v1.1RC --repo $env:GITHUB_REPOSITORY --pattern WuWaFPSUnlock-1.1RC-win-x64.zip --dir baseline
    if($LASTEXITCODE -ne 0){throw 'Component package download failed'}
    $BaselineZip='baseline/WuWaFPSUnlock-1.1RC-win-x64.zip'
}
Expand-Archive -LiteralPath $BaselineZip -DestinationPath baseline/extracted
New-Item package -ItemType Directory | Out-Null
foreach($name in @('payload','components','licenses','App.ico')){Copy-Item "baseline/extracted/$name" package -Recurse}
Get-ChildItem build | Where-Object Extension -ne '.pdb' | Copy-Item -Destination package -Recurse -Force
$addon=Join-Path $repo 'release-assets/mfg/renodx-mfgunlock.addon64'
$sourceRecord=Join-Path $repo 'release-assets/mfg/source.json'
$provenance=Get-Content -LiteralPath $sourceRecord -Raw | ConvertFrom-Json
if((Get-FileHash $addon).Hash -ne $provenance.sha256 -or (Get-Item $addon).Length -ne $provenance.length){throw 'Addon differs from its release source record'}
Copy-Item $addon package/payload/files/addon/renodx-mfgunlock.addon64 -Force
Copy-Item -LiteralPath $sourceRecord -Destination package/payload/addon-source.json
& "$PSScriptRoot/Sync-AddonInventory.ps1" -PayloadDirectory package/payload -SourceRecord $sourceRecord -AddonPath $addon -PackageId "wuwa-fps-unlock-$Version" -PublicInventory
Copy-Item -LiteralPath (Join-Path $repo 'README.md') -Destination package
New-Item package/docs -ItemType Directory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'docs/DYNAMIC_MAX_MULTIPLIER.md') -Destination package/docs
$allowedRoot=@('WuWaFpsUnlock.exe','App.ico','README.md','payload','components','licenses','docs')
foreach($item in Get-ChildItem package -Force){
    if($item.Name -notin $allowedRoot){throw "Unexpected full-package item (possible local user data): $($item.Name)"}
}
if(Test-Path package/data){throw 'Full package must not include user configuration, deployment records or logs'}
$fullName="WuWaFPSUnlock-$Version-win-x64.zip"
$updateName="WuWaFPSUnlock-$Version-update.zip"
Compress-Archive package/* $fullName
New-Item update/update-payload/components/fps,update/update-payload/payload/files/addon -ItemType Directory -Force | Out-Null
Copy-Item build/WuWaFpsUnlock.exe update/update-payload
Copy-Item build/components/fps/ww_plugin_base.dll update/update-payload/components/fps
Copy-Item build/components/PROVENANCE.json update/update-payload/components
Copy-Item build/licenses update/update-payload -Recurse
Copy-Item $addon update/update-payload/payload/files/addon
Copy-Item -LiteralPath $sourceRecord -Destination update/update-payload/payload/addon-source.json
Copy-Item updater-build/WuWaUpdater.exe update/更新.exe
Copy-Item -LiteralPath (Join-Path $repo 'tools/OfflineUpdater/更新说明.txt') -Destination update
& "$PSScriptRoot/New-UpdateManifest.ps1" -PackageDirectory update -Version $Version
& ./updater-build/WuWaUpdater.exe --validate-package ((Resolve-Path update).Path) $Version
if($LASTEXITCODE -ne 0){throw 'Update package protocol validation failed'}
Compress-Archive update/* $updateName
Get-FileHash $fullName,$updateName | ForEach-Object {'{0}  {1}' -f $_.Hash.ToLowerInvariant(),(Split-Path $_.Path -Leaf)} | Set-Content SHA256SUMS.txt -Encoding ascii
