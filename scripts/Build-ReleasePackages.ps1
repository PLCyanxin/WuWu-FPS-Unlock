param([Parameter(Mandatory)][string]$Version,[string]$BaselineZip,[string]$BaselineSha256)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if($Version -notmatch '^\d+\.\d+\.\d+(?:RC\d*)?$'){throw 'Invalid release version'}
$baseline=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'release-baseline.json') -Raw | ConvertFrom-Json
if(!$BaselineZip){
    gh release download $baseline.tag --repo $baseline.repository --pattern $baseline.asset --dir baseline
    if($LASTEXITCODE -ne 0){throw 'Component package download failed'}
    $BaselineZip=Join-Path baseline $baseline.asset
}
if(!$BaselineSha256){$BaselineSha256=$baseline.sha256}
if($BaselineSha256 -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-FileHash -LiteralPath $BaselineZip).Hash -ne $BaselineSha256){throw 'Build baseline archive digest mismatch'}
Expand-Archive -LiteralPath $BaselineZip -DestinationPath baseline/extracted
New-Item package -ItemType Directory | Out-Null
foreach($name in @('payload','components','licenses','App.ico')){Copy-Item "baseline/extracted/$name" package -Recurse}
& "$PSScriptRoot/Merge-ReleasePayload.ps1" -CheckoutPayload (Join-Path $repo 'payload') -DestinationPayload package/payload
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
# A complete release must contain deployment sources; an update-only layout is insufficient.
$payloadRoot=(Resolve-Path -LiteralPath 'package/payload').Path
$materialManifest=Get-Content -LiteralPath (Join-Path $payloadRoot 'manifest.json') -Raw | ConvertFrom-Json
$materialSources=@($materialManifest.files | ForEach-Object { $_.source }) + @($materialManifest.reShade.localSetupPath)
foreach($relative in $materialSources){
    if([string]::IsNullOrWhiteSpace($relative)){throw 'Empty deployment material path'}
    $material=[IO.Path]::GetFullPath((Join-Path $payloadRoot $relative))
    if(!$material.StartsWith($payloadRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw "Deployment source escapes package: $relative"}
    if(!(Test-Path -LiteralPath $material -PathType Leaf) -or (Get-Item -LiteralPath $material).Length -eq 0){throw "Missing deployment source in full package: $relative"}
}
$expectedPolicy=Get-Content -LiteralPath (Join-Path $repo 'payload/manifest.json') -Raw | ConvertFrom-Json
foreach($field in $expectedPolicy.PSObject.Properties.Name | Where-Object {$_ -notin @('packageId','files','addonVersion')}){
    if((ConvertTo-Json -InputObject $materialManifest.$field -Depth 50 -Compress) -cne (ConvertTo-Json -InputObject $expectedPolicy.$field -Depth 50 -Compress)){throw "Packaged deployment policy differs from checkout: $field"}
}
$fullName="WuWaFPSUnlock-$Version-win-x64.zip"
$updateName="WuWaFPSUnlock-$Version-update.zip"
dotnet run --project (Join-Path $repo 'tests/WuWaFpsUnlock.Tests') -c Release -- --validate-materials $payloadRoot
if($LASTEXITCODE -ne 0){throw 'Complete package deployment material validation failed'}
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
