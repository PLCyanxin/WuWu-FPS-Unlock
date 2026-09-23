$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $repo ('.tmp/inventory-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$passed=0
function Check([bool]$ok,[string]$name){if(!$ok){throw $name};$script:passed++;Write-Output "PASS $name"}
function Put([string]$relative,[string]$value){$p=Join-Path $fixture $relative;New-Item (Split-Path $p -Parent) -ItemType Directory -Force | Out-Null;Set-Content -LiteralPath $p -Value $value -Encoding utf8}
foreach($name in @('manifest.json','source-manifest.json','payload-map.json')){New-Item "$fixture/payload" -ItemType Directory -Force | Out-Null;Copy-Item -LiteralPath (Join-Path $repo "payload/$name") -Destination "$fixture/payload/$name"}
Put 'renodx-mfgunlock.addon64' 'inert addon fixture'
$addon="$fixture/renodx-mfgunlock.addon64"
$source=@{length=(Get-Item $addon).Length;sha256=(Get-FileHash $addon).Hash;fileVersion='1.1.0.0';addonVersion='1.1+WuWu.DynamicMax.1';upstream=@{tag='1.1';commit='fixture'}}
$source|ConvertTo-Json|Set-Content "$fixture/source.json"
$vendorsBefore=@((Get-Content "$fixture/payload/manifest.json" -Raw|ConvertFrom-Json).files|Where-Object kind -eq 'Vendor'|ConvertTo-Json -Depth 30)
& "$PSScriptRoot/Sync-AddonInventory.ps1" -PayloadDirectory "$fixture/payload" -SourceRecord "$fixture/source.json" -AddonPath $addon -PublicInventory
$m=Get-Content "$fixture/payload/manifest.json" -Raw|ConvertFrom-Json
$s=Get-Content "$fixture/payload/source-manifest.json" -Raw|ConvertFrom-Json
$map=Get-Content "$fixture/payload/payload-map.json" -Raw|ConvertFrom-Json
Check ($m.addonVersion -eq $source.addonVersion) 'derived addon version synchronized'
Check (($m.files|Where-Object kind -eq 'Vendor'|ConvertTo-Json -Depth 30) -eq $vendorsBefore[0]) 'vendor inventory unchanged'
Check (($s.files|Where-Object name -eq 'renodx-mfgunlock.addon64').fileVersion -eq '1.1.0.0') 'provenance file version used'
Check (($map|Where-Object name -eq 'renodx-mfgunlock.addon64').sha256 -eq $source.sha256) 'mapping uses new addon hash'
$all=Get-Content "$fixture/payload/source-manifest.json","$fixture/payload/payload-map.json" -Raw
Check (!(($all -join '') -match '"[A-Za-z]:')) 'public inventories contain no drive-qualified paths'
Check (($s.sourceRoot -eq 'payload') -and ($s.destination -eq 'payload') -and @($map|Where-Object {$_.actualGameTargets.Count -ne 0}).Count -eq 0) 'public inventories use relative roots and empty game mappings'
$source.Remove('addonVersion');$source|ConvertTo-Json|Set-Content "$fixture/source.json"
& "$PSScriptRoot/Sync-AddonInventory.ps1" -PayloadDirectory "$fixture/payload" -SourceRecord "$fixture/source.json" -AddonPath $addon
Check ((Get-Content "$fixture/payload/manifest.json" -Raw|ConvertFrom-Json).addonVersion -eq '1.1+WuWu.DynamicMax.1') 'legacy source without addonVersion preserves existing version'
$before=(Get-FileHash "$fixture/payload/manifest.json").Hash
$source.sha256='0'*64;$source|ConvertTo-Json|Set-Content "$fixture/source.json"
$rejected=$false
try{& "$PSScriptRoot/Sync-AddonInventory.ps1" -PayloadDirectory "$fixture/payload" -SourceRecord "$fixture/source.json" -AddonPath $addon}catch{$rejected=$true}
Check ($rejected -and (Get-FileHash "$fixture/payload/manifest.json").Hash -eq $before) 'mismatched provenance rejects before inventory writes'
Write-Output "RESULT: $passed passed; isolated inventory fixtures retained under .tmp."
