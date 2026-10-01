param([string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fixture=Join-Path $repo ('.tmp/updater-cli-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
function RunDotnet([string[]]$Arguments){& $Dotnet @Arguments;if($LASTEXITCODE -ne 0){throw 'Fixture build failed'}}
RunDotnet @('publish',"$PSScriptRoot/OfflineUpdater.IntegrationTests.csproj",'-c','Release','-o',"$fixture/worker")
RunDotnet @('publish',"$PSScriptRoot/Launcher/Launcher.csproj",'-c','Release','-p:Version=1.0.0','-p:InformationalVersion=1.0.0','-o',"$fixture/old")
RunDotnet @('publish',"$PSScriptRoot/Launcher/Launcher.csproj",'-c','Release','-p:Version=2.0.0','-p:InformationalVersion=2.0.0','-o',"$fixture/new")
RunDotnet @('publish',"$PSScriptRoot/Launcher/Launcher.csproj",'-c','Release','-p:Version=3.0.0','-p:InformationalVersion=3.0.0','-o',"$fixture/third")
$root=Join-Path $fixture 'installation';$package=Join-Path $root 'update';$payload=Join-Path $package 'update-payload'
New-Item "$root/data","$payload/components/fps","$payload/payload/files/addon" -ItemType Directory -Force | Out-Null
Copy-Item "$fixture/old/WuWaFpsUnlock.exe" $root
Set-Content "$root/data/settings.json" '{"keep":"current user settings"}'
$dataHash=(Get-FileHash "$root/data/settings.json").Hash;$oldHash=(Get-FileHash "$root/WuWaFpsUnlock.exe").Hash
Copy-Item "$fixture/new/WuWaFpsUnlock.exe" $payload
Copy-Item "$fixture/worker/WuWaUpdaterFixture.exe" "$package/更新.exe"
Set-Content "$payload/components/fps/ww_plugin_base.dll" 'inert component'
Set-Content "$payload/components/PROVENANCE.json" '{}'
Set-Content "$payload/payload/files/addon/renodx-mfgunlock.addon64" 'inert addon'
$addon=Get-Item "$payload/payload/files/addon/renodx-mfgunlock.addon64"
@{sha256=(Get-FileHash $addon.FullName).Hash;length=$addon.Length} | ConvertTo-Json | Set-Content "$payload/payload/addon-source.json"
& "$repo/scripts/New-UpdateManifest.ps1" -PackageDirectory $package -Version '2.0.0'
function Worker([string]$Exe,[string[]]$Arguments,[int]$Expected=0){
    $psi=[Diagnostics.ProcessStartInfo]::new($Exe);$psi.UseShellExecute=$false;$psi.CreateNoWindow=$true;$psi.RedirectStandardInput=$true;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
    $psi.Environment['WUWA_FIXTURE_GAME_AT']=[string]$script:ProbeFailAt
    $psi.Environment['WUWA_FIXTURE_KILL_AFTER']=[string]$script:KillAfter
    $psi.Environment['WUWA_FIXTURE_KILL_METADATA']=[string]$script:KillMetadata
    $psi.Environment['WUWA_FIXTURE_FAIL_METADATA']=[string]$script:FailMetadata
    foreach($argument in $Arguments){$psi.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($psi);$out=$process.StandardOutput.ReadToEndAsync();$err=$process.StandardError.ReadToEndAsync()
    if(!$process.WaitForExit(60000)){throw 'Fixture worker timed out'}
    Write-Output $out.Result;Write-Output $err.Result
    if($process.ExitCode -ne $Expected){throw "Unexpected worker exit: $($process.ExitCode), expected $Expected"}
    $process.Dispose()
}
function Check([bool]$ok,[string]$message){if(!$ok){throw $message};Write-Output "PASS $message"}
function StartLauncher(){
    $psi=[Diagnostics.ProcessStartInfo]::new("$root/WuWaFpsUnlock.exe");$psi.UseShellExecute=$false;$psi.CreateNoWindow=$true
    return [Diagnostics.Process]::Start($psi)
}
$child=StartLauncher
Worker "$package/更新.exe" @('--wait-for-exit',"$($child.Id)","$root/WuWaFpsUnlock.exe")
Check ($child.HasExited) 'CLI update waited for real inert launcher natural exit';$child.Dispose()
Check ((Get-FileHash "$root/WuWaFpsUnlock.exe").Hash -ne $oldHash) 'production update CLI replaced launcher'
for($i=0;$i -lt 100 -and !(Test-Path "$root/completion-fixture.txt");$i++){Start-Sleep -Milliseconds 50}
Check ((Get-Content "$root/completion-fixture.txt").Count -eq 1) 'successful update launches completion handoff once'
$first=@(Get-ChildItem $root -Directory -Filter 'update-backup-*')[0]
Check ((Get-Content "$($first.FullName)/snapshot.json" -Raw|ConvertFrom-Json).Complete) 'first update creates complete backup'
Check (!(Test-Path "$($first.FullName)/original")) 'CLI update stores old bytes once in complete snapshot'
Check ((Get-FileHash "$($first.FullName)/snapshot/WuWaFpsUnlock.exe").Hash -eq $oldHash) 'shared snapshot preserves exact pre-update launcher'
Copy-Item "$fixture/third/WuWaFpsUnlock.exe" "$payload/WuWaFpsUnlock.exe" -Force
Remove-Item -LiteralPath "$package/回退.cmd"
& "$repo/scripts/New-UpdateManifest.ps1" -PackageDirectory $package -Version '3.0.0'
Worker "$package/更新.exe" @()
$backups=@(Get-ChildItem $root -Directory -Filter 'update-backup-*')
Check ($backups.Count -eq 1 -and $backups[0].FullName -ne $first.FullName) 'second successful CLI update removes prior backup'
$backup=$backups[0].FullName
$expected=(Get-FileHash "$backup/snapshot/WuWaFpsUnlock.exe").Hash
Check ((Get-FileHash "$root/WuWaFpsUnlock.exe").Hash -ne $expected) 'rollback source differs from installed version'
$rollbackBefore=(Get-FileHash "$root/WuWaFpsUnlock.exe").Hash
$script:ProbeFailAt=1
Worker "$fixture/worker/WuWaUpdaterFixture.exe" @('--rollback',$backup) 1
$script:ProbeFailAt=0
Check ((Get-FileHash "$root/WuWaFpsUnlock.exe").Hash -eq $rollbackBefore -and !(Test-Path "$backup/rollback.completed")) 'direct rollback CLI refuses running game before any write'
$child=StartLauncher
Worker "$fixture/worker/WuWaUpdaterFixture.exe" @('--wait-for-rollback',"$($child.Id)","$root/WuWaFpsUnlock.exe",$backup)
Check ($child.HasExited) 'CLI rollback waited for real inert launcher natural exit';$child.Dispose()
Check ((Get-FileHash "$root/WuWaFpsUnlock.exe").Hash -eq $expected) 'CLI rollback restored captured launcher bytes'
Check ((Get-FileHash "$root/data/settings.json").Hash -eq $dataHash) 'update and rollback preserved current user data'
Check ((Test-Path "$backup/rollback.completed") -and !(Get-Content "$backup/snapshot.json" -Raw|ConvertFrom-Json).Complete) 'consumption blocks both new and legacy rollback consumers'
Worker "$fixture/worker/WuWaUpdaterFixture.exe" @('--rollback',$backup) 1
Write-Output 'PASS repeated rollback CLI refused'
$completionBefore=Get-Content "$root/completion-fixture.txt" -Raw
$beforeRejected=(Get-FileHash "$root/WuWaFpsUnlock.exe").Hash
$script:ProbeFailAt=2
Worker "$package/更新.exe" @() 1
$script:ProbeFailAt=0
Check ((Get-FileHash "$root/WuWaFpsUnlock.exe").Hash -eq $beforeRejected) 'game appearing before first replacement prevents installation'
Check ((Get-Content "$root/completion-fixture.txt" -Raw) -eq $completionBefore) 'game guard refusal never sends success handoff'
foreach($killAt in @(1,3,5)){
    $beforeCrash=(Get-FileHash "$root/WuWaFpsUnlock.exe").Hash
    $componentBefore=(Get-FileHash "$root/components/fps/ww_plugin_base.dll").Hash
    Set-Content "$payload/components/fps/ww_plugin_base.dll" "inert crash fixture revision $killAt"
    if(Test-Path -LiteralPath "$package/回退.cmd"){Remove-Item -LiteralPath "$package/回退.cmd"}
    & "$repo/scripts/New-UpdateManifest.ps1" -PackageDirectory $package -Version '3.0.0'
    $script:KillAfter=$killAt
    Worker "$package/更新.exe" @() 77
    $script:KillAfter=0
    $interrupted=Get-ChildItem $root -Directory -Filter 'update-backup-*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Worker "$fixture/worker/WuWaUpdaterFixture.exe" @('--recover',$interrupted.FullName)
    Check ((Get-FileHash "$root/WuWaFpsUnlock.exe").Hash -eq $beforeCrash -and (Get-FileHash "$root/components/fps/ww_plugin_base.dll").Hash -eq $componentBefore) "OS-level interruption after commit $killAt restores exact prior bytes"
}
$completionCountBefore=@(Get-Content "$root/completion-fixture.txt").Count
$script:KillMetadata=1
Worker "$package/更新.exe" @() 78
$script:KillMetadata=0
Worker "$package/更新.exe" @()
for($i=0;$i -lt 100 -and @(Get-Content "$root/completion-fixture.txt").Count -le $completionCountBefore;$i++){Start-Sleep -Milliseconds 50}
Check (@(Get-Content "$root/completion-fixture.txt").Count -eq $completionCountBefore+1) 'torn legacy completion metadata recovers through atomic state and successful retry'
$recoveredBackup=@(Get-ChildItem $root -Directory -Filter 'update-backup-*')
Check ($recoveredBackup.Count -eq 1 -and (Get-FileHash "$($recoveredBackup[0].FullName)/snapshot/WuWaFpsUnlock.exe").Hash -eq $beforeRejected) 'completion recovery preserves original pre-update backup instead of backing up already-updated version'
$beforeMetadataFailure=(Get-FileHash "$root/components/fps/ww_plugin_base.dll").Hash
Set-Content "$payload/components/fps/ww_plugin_base.dll" 'inert exception-after-commit fixture'
if(Test-Path -LiteralPath "$package/回退.cmd"){Remove-Item -LiteralPath "$package/回退.cmd"}
& "$repo/scripts/New-UpdateManifest.ps1" -PackageDirectory $package -Version '3.0.0'
$script:FailMetadata=1
Worker "$package/更新.exe" @() 1
$script:FailMetadata=0
Check ((Get-FileHash "$root/components/fps/ww_plugin_base.dll").Hash -eq (Get-FileHash "$payload/components/fps/ww_plugin_base.dll").Hash) 'exception after durable completion never restores old files under completed metadata'
Worker "$package/更新.exe" @()
$completedBackup=@(Get-ChildItem $root -Directory -Filter 'update-backup-*')
Check ($completedBackup.Count -eq 1 -and (Get-FileHash "$($completedBackup[0].FullName)/snapshot/components/fps/ww_plugin_base.dll").Hash -eq $beforeMetadataFailure) 'retry repairs completion mirrors and retains actual pre-update component snapshot'
$completionBefore=Get-Content "$root/completion-fixture.txt" -Raw
Add-Content "$payload/components/fps/ww_plugin_base.dll" 'corrupt fixture bytes'
Worker "$package/更新.exe" @() 1
Check ((Get-Content "$root/completion-fixture.txt" -Raw) -eq $completionBefore) 'failed update never launches success handoff'
Write-Output "RESULT: production CLI integration passed; fixture location $fixture; desktop shortcut and game probe adapters replaced."
