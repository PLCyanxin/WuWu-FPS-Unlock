[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [Parameter(Mandatory)][string]$SourceRecord,
    [Parameter(Mandatory)][string]$AddonPath,
    [string]$PackageId,
    [switch]$PublicInventory
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $PayloadDirectory).Path
$provenance=Get-Content -LiteralPath $SourceRecord -Raw | ConvertFrom-Json
if((Get-FileHash -LiteralPath $AddonPath -Algorithm SHA256).Hash -ne $provenance.sha256 -or (Get-Item -LiteralPath $AddonPath).Length -ne $provenance.length){throw 'Addon differs from its source record'}
function Set-Field($Object,[string]$Name,$Value){$Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force}
$manifest=Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
$source=Get-Content -LiteralPath (Join-Path $root 'source-manifest.json') -Raw | ConvertFrom-Json
$map=@(Get-Content -LiteralPath (Join-Path $root 'payload-map.json') -Raw | ConvertFrom-Json)
$entry=@($manifest.files | Where-Object source -eq 'files/addon/renodx-mfgunlock.addon64')
$record=@($source.files | Where-Object name -eq 'renodx-mfgunlock.addon64')
$mapped=@($map | Where-Object name -eq 'renodx-mfgunlock.addon64')
if($entry.Count -ne 1 -or $record.Count -ne 1 -or $mapped.Count -ne 1){throw 'Inventory must contain one addon entry in each document'}
if($PackageId){$manifest.packageId=$PackageId}
$entry[0].sha256=$provenance.sha256;$entry[0].size=$provenance.length
$record[0].length=$provenance.length;$record[0].sha256=$provenance.sha256;$record[0].copySha256=$provenance.sha256
Set-Field $record[0] 'copyVerified' $true
$record[0].sourcePath='payload/addon-source.json';$record[0].copiedPath='payload/files/addon/renodx-mfgunlock.addon64'
$record[0].fileVersion=if($provenance.fileVersion){$provenance.fileVersion}else{(Get-Item -LiteralPath $AddonPath).VersionInfo.FileVersion}
Set-Field $record[0] 'signatureStatus' 'NotChecked'
Set-Field $record[0] 'signatureMessage' 'Signature not revalidated for this build.'
Set-Field $record[0] 'signer' $null
Set-Field $record[0] 'certificateThumbprint' $null
$mapped[0].sha256=$provenance.sha256;$mapped[0].source='payload/files/addon/renodx-mfgunlock.addon64'
if($provenance.addonVersion){
    Set-Field $manifest 'addonVersion' $provenance.addonVersion
    Set-Field $record[0] 'addonVersion' $provenance.addonVersion
    Set-Field $mapped[0] 'addonVersion' $provenance.addonVersion
}
if($PublicInventory){
    $paths=@{}
    foreach($file in $manifest.files){$paths[[IO.Path]::GetFileName($file.source)]='payload/'+$file.source.Replace('\','/')}
    $setup=$manifest.reShade.localSetupPath
    if($setup){$paths[[IO.Path]::GetFileName($setup)]='payload/'+$setup.Replace('\','/')}
    Set-Field $source 'sourceRoot' 'payload'
    Set-Field $source 'destination' 'payload'
    foreach($file in $source.files){
        if(!$paths.ContainsKey($file.name)){throw "Inventory source is not in the package manifest: $($file.name)"}
        foreach($field in @('sourcePath','copiedPath','copyPath','source')){
            if($field -in @('sourcePath','copiedPath') -or $file.PSObject.Properties[$field]){Set-Field $file $field $paths[$file.name]}
        }
    }
    foreach($file in $map){
        if(!$paths.ContainsKey($file.name)){throw "Inventory map is not in the package manifest: $($file.name)"}
        $file.source=$paths[$file.name]
        Set-Field $file 'actualGameTargets' @()
        Set-Field $file 'gameMappingStatus' 'Targets are resolved within the selected game directory during deployment.'
    }
}
$manifest | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath (Join-Path $root 'manifest.json') -Encoding utf8
$source | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath (Join-Path $root 'source-manifest.json') -Encoding utf8
ConvertTo-Json -InputObject $map -Depth 50 | Set-Content -LiteralPath (Join-Path $root 'payload-map.json') -Encoding utf8
