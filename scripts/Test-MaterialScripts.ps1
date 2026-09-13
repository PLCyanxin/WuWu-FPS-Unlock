[CmdletBinding()]
param([string]$MaterialsDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'input/desktop-materials'))
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
$testRoot=Join-Path $workspace ('.tmp/material-script-tests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$materials=(Resolve-Path -LiteralPath $MaterialsDirectory).Path
$common=@{MfgAddon=(Join-Path $materials 'renodx-mfgunlock.addon64');LocalReShadeSetup=(Join-Path $materials 'ReShade_Setup_6.8.0_Addon.exe');ProxyApi='dxgi'}
function MustReject([string]$name,[scriptblock]$action,[string]$expected){
    $thrown=$false
    try { & $action } catch { $thrown=$true; if($_.Exception.Message -notlike "*$expected*"){throw}; Write-Output "PASS $name : $($_.Exception.Message)" }
    if(-not $thrown){throw "FAIL $name did not reject"}
}
$empty=Join-Path $testRoot 'empty';New-Item -ItemType Directory -Path $empty | Out-Null
MustReject 'missing18' { & "$workspace/scripts/Prepare-Payload.ps1" @common -ReplacementFiles $empty -Destination (Join-Path $testRoot 'out1') } '18'
$unknown=Join-Path $testRoot 'unknown';New-Item -ItemType Directory -Path $unknown | Out-Null
1..18 | ForEach-Object { [IO.File]::WriteAllText((Join-Path $unknown "unknown$_.dll"),'rejection-only fixture, not a real DLL') }
MustReject 'unknownDll' { & "$workspace/scripts/Prepare-Payload.ps1" @common -ReplacementFiles $unknown -Destination (Join-Path $testRoot 'out2') } '未知材料'
$tree=Join-Path $testRoot 'tree';New-Item -ItemType Directory -Path (Join-Path $tree 'sub') | Out-Null
MustReject 'nestedInput' { & "$workspace/scripts/Prepare-Payload.ps1" @common -ReplacementFiles $tree -Destination (Join-Path $testRoot 'out3') } '平铺'
MustReject 'outputInsideSource' { & "$workspace/scripts/Prepare-Payload.ps1" @common -ReplacementFiles $empty -Destination (Join-Path $empty 'out') } '输出目录'
if(@(Get-ChildItem -LiteralPath $testRoot -Directory | Where-Object Name -like 'out*').Count){throw 'Rejection wrote payload outputs'}
Write-Output 'PASS: four input rejection cases; no payload generated; no installer/unlocker/game executed.'
