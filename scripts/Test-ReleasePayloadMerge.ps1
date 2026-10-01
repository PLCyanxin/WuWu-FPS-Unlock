$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $repo ('artifacts/tests/payload-merge-'+[guid]::NewGuid().ToString('N'))
$current=Join-Path $fixture 'checkout';$destination=Join-Path $fixture 'package'
New-Item "$current/files","$destination/files" -ItemType Directory -Force | Out-Null
Set-Content "$destination/files/vendor.dll" 'inert inherited runtime'
Set-Content "$destination/files/setup.exe" 'inert inherited setup'
$manifest=@{packageId='current';preferDynamic=$false;dynamicMinimumDriver=99999;mfgConfig=@{RuntimeSelectionMode='2'};
    reShade=@{localSetupPath='files/setup.exe';setupSha256=(Get-FileHash "$destination/files/setup.exe").Hash};
    files=@(@{source='files/vendor.dll';sha256=(Get-FileHash "$destination/files/vendor.dll").Hash;size=(Get-Item "$destination/files/vendor.dll").Length})}
$manifest | ConvertTo-Json -Depth 20 | Set-Content "$current/manifest.json"
Set-Content "$current/source-manifest.json" '{}';Set-Content "$current/payload-map.json" '[]'
Set-Content "$destination/manifest.json" '{"preferDynamic":true,"dynamicMinimumDriver":59541}'
& "$PSScriptRoot/Merge-ReleasePayload.ps1" -CheckoutPayload $current -DestinationPayload $destination
$merged=Get-Content "$destination/manifest.json" -Raw | ConvertFrom-Json
if($merged.preferDynamic -ne $false -or $merged.dynamicMinimumDriver -ne 99999 -or $merged.mfgConfig.RuntimeSelectionMode -ne '2'){throw 'Inherited policy overrides checkout'}
Write-Output 'PASS checkout policy replaces stale baseline policy'
if((Get-FileHash "$destination/files/vendor.dll").Hash -ne $manifest.files[0].sha256){throw 'Inherited bytes changed'}
Write-Output 'PASS pinned matching runtime bytes remain available'
Set-Content "$current/files/vendor.dll" 'inert newer checked-out runtime'
$manifest.files[0].sha256=(Get-FileHash "$current/files/vendor.dll").Hash;$manifest.files[0].size=(Get-Item "$current/files/vendor.dll").Length
$manifest | ConvertTo-Json -Depth 20 | Set-Content "$current/manifest.json"
& "$PSScriptRoot/Merge-ReleasePayload.ps1" -CheckoutPayload $current -DestinationPayload $destination
if((Get-FileHash "$destination/files/vendor.dll").Hash -ne $manifest.files[0].sha256){throw 'New checkout runtime not copied'}
Write-Output 'PASS explicit current runtime replaces baseline bytes'
Set-Content "$current/files/vendor.dll" 'corrupt runtime'
$rejected=$false;try{& "$PSScriptRoot/Merge-ReleasePayload.ps1" -CheckoutPayload $current -DestinationPayload $destination}catch{$rejected=$true}
if(!$rejected){throw 'Corrupt checkout runtime accepted'}
Write-Output 'PASS mismatching construction source rejected; user deployment hash policy unchanged'
Write-Output "RESULT: 4 passed; inert fixture $fixture, no package published."
