[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
$testRoot=Join-Path $workspace ('.tmp/migration-tests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$script=Join-Path $PSScriptRoot 'Migrate-UserData.ps1'
$passed=0
function Check([bool]$condition,[string]$label){if(-not $condition){throw "FAIL $label"};$script:passed++;Write-Output "PASS $label"}
function Fixture([string]$name){
    $source=Join-Path $testRoot "$name/旧包 中文";$dest=Join-Path $testRoot "$name/新包 空格"
    foreach($dir in @("$source/data/deployments","$source/data/logs","$source/data/provenance","$source/payload","$dest/payload")){New-Item -ItemType Directory -Path $dir -Force | Out-Null}
    [IO.File]::WriteAllText("$source/payload/manifest.json",'{}');[IO.File]::WriteAllText("$dest/payload/manifest.json",'{}')
    [IO.File]::WriteAllText("$source/data/deployments/owned.json",'{"UnknownReceiptField":{"retain":true},"Files":[]}')
    [IO.File]::WriteAllText("$source/data/logs/run.log",'用户日志');[IO.File]::WriteAllText("$source/data/provenance/source.json",'{"unknown":123}')
    [IO.File]::WriteAllText("$source/do-not-copy.txt",'outside data')
    $escaped=(Join-Path $source 'payload/manifest.json').Replace('\','\\')
    [IO.File]::WriteAllText("$source/data/settings.json",('{"PackageManifest":"'+$escaped+'","GameRoot":"D:\\game","FpsEnabled":false,"Future":{"Big":12345678901234567890123456789,"Text":"保留"}}'))
    [PSCustomObject]@{source=$source;dest=$dest}
}
function Snapshot([string]$dir){@{}+$(@{};)}
function Reject([scriptblock]$action,[string]$label){$failed=$false;try{& $action}catch{$failed=$true};Check $failed $label}
$x=Fixture 'normal'
$sourceBefore=(Get-FileHash -LiteralPath "$($x.source)/data/settings.json").Hash
& $script -SourceDirectory $x.source -DestinationDirectory $x.dest
$settings=[System.Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText("$($x.dest)/data/settings.json"))
Check ($settings['PackageManifest'].ToString() -eq [IO.Path]::GetFullPath("$($x.dest)/payload/manifest.json")) 'internal manifest relocated only to new package'
Check ($settings['Future']['Big'].ToJsonString() -eq '12345678901234567890123456789') 'unknown large numeric JSON preserved exactly'
Check ($settings['Future']['Text'].ToString() -eq '保留' -and $settings['FpsEnabled'].ToJsonString() -eq 'false') 'unknown fields and FPS disabled preserved'
Check ((Get-FileHash -LiteralPath "$($x.source)/data/settings.json").Hash -eq $sourceBefore) 'source settings untouched'
Check ((Get-FileHash -LiteralPath "$($x.source)/data/deployments/owned.json").Hash -eq (Get-FileHash -LiteralPath "$($x.dest)/data/deployments/owned.json").Hash) 'deployment receipt byte-identical'
Check ((Test-Path "$($x.dest)/data/logs/run.log") -and (Test-Path "$($x.dest)/data/provenance/source.json") -and -not(Test-Path "$($x.dest)/do-not-copy.txt")) 'logs and provenance copied, outside data excluded'
& $script -SourceDirectory $x.source -DestinationDirectory $x.dest
Check ((Get-ChildItem "$($x.dest)/migration-reports" -File).Count -eq 2) 'repeat migration skips same files with new report'
$x=Fixture 'external';$external=Join-Path $testRoot 'external-manifest.json';[IO.File]::WriteAllText($external,'{}')
$raw='{"PackageManifest":"'+$external.Replace('\','\\')+'","Future": 123}'
[IO.File]::WriteAllText("$($x.source)/data/settings.json",$raw)
& $script -SourceDirectory $x.source -DestinationDirectory $x.dest
Check ((Get-FileHash "$($x.source)/data/settings.json").Hash -eq (Get-FileHash "$($x.dest)/data/settings.json").Hash) 'external selected manifest settings remain byte-identical'
$x=Fixture 'conflict';New-Item -ItemType Directory -Path "$($x.dest)/data" -Force | Out-Null;[IO.File]::WriteAllText("$($x.dest)/data/settings.json",'{"user":"keep"}')
Reject {& $script -SourceDirectory $x.source -DestinationDirectory $x.dest} 'different destination blocks migration'
Check ([IO.File]::ReadAllText("$($x.dest)/data/settings.json") -eq '{"user":"keep"}' -and -not(Test-Path "$($x.dest)/data/deployments/owned.json")) 'conflict causes no data writes or overwrite'
$x=Fixture 'missingmanifest';Remove-Item -LiteralPath "$($x.dest)/payload/manifest.json"
Reject {& $script -SourceDirectory $x.source -DestinationDirectory $x.dest} 'missing target manifest blocks relocation'
$x=Fixture 'badjson';[IO.File]::WriteAllText("$($x.source)/data/settings.json",'{broken')
Reject {& $script -SourceDirectory $x.source -DestinationDirectory $x.dest} 'bad settings JSON never silently reset'
Reject {& $script -SourceDirectory $x.source -DestinationDirectory $x.source} 'same package refused'
Reject {& $script -SourceDirectory $x.source -DestinationDirectory (Join-Path $x.source 'nested')} 'nested package refused'
Reject {& $script -SourceDirectory 'relative' -DestinationDirectory $x.dest} 'relative package root refused'
$x=Fixture 'sourcelink';$outside=Join-Path $testRoot 'outside-link-target';New-Item -ItemType Directory -Path $outside -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $outside 'private.txt'),'must not copy')
New-Item -ItemType Junction -Path "$($x.source)/data/redirect" -Target $outside | Out-Null
Reject {& $script -SourceDirectory $x.source -DestinationDirectory $x.dest} 'source data junction refused'
Check (-not(Test-Path "$($x.dest)/data/settings.json")) 'source junction preflight blocks all data copy'
$x=Fixture 'destlink';New-Item -ItemType Junction -Path "$($x.dest)/data" -Target $outside | Out-Null
Reject {& $script -SourceDirectory $x.source -DestinationDirectory $x.dest} 'destination data junction refused'
Check (-not(Test-Path (Join-Path $outside 'settings.json'))) 'destination junction never receives data writes'
Write-Output "RESULT $passed checks passed. Fixture-only; actual portable data untouched; no program executed. Fixtures retained at $testRoot"
