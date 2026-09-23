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
$first=@(Get-ChildItem $root -Directory -Filter 'update-backup-*')[0]
Check ((Get-Content "$($first.FullName)/snapshot.json" -Raw|ConvertFrom-Json).Complete) 'first update creates complete backup'
Copy-Item "$fixture/third/WuWaFpsUnlock.exe" "$payload/WuWaFpsUnlock.exe" -Force
Remove-Item -LiteralPath "$package/回退.cmd"
& "$repo/scripts/New-UpdateManifest.ps1" -PackageDirectory $package -Version '3.0.0'
Worker "$package/更新.exe" @()
$backups=@(Get-ChildItem $root -Directory -Filter 'update-backup-*')
Check ($backups.Count -eq 1 -and $backups[0].FullName -ne $first.FullName) 'second successful CLI update removes prior backup'
$backup=$backups[0].FullName
$expected=(Get-FileHash "$backup/snapshot/WuWaFpsUnlock.exe").Hash
Check ((Get-FileHash "$root/WuWaFpsUnlock.exe").Hash -ne $expected) 'rollback source differs from installed version'
$child=StartLauncher
Worker "$fixture/worker/WuWaUpdaterFixture.exe" @('--wait-for-rollback',"$($child.Id)","$root/WuWaFpsUnlock.exe",$backup)
Check ($child.HasExited) 'CLI rollback waited for real inert launcher natural exit';$child.Dispose()
Check ((Get-FileHash "$root/WuWaFpsUnlock.exe").Hash -eq $expected) 'CLI rollback restored captured launcher bytes'
Check ((Get-FileHash "$root/data/settings.json").Hash -eq $dataHash) 'update and rollback preserved current user data'
Check ((Test-Path "$backup/rollback.completed") -and !(Get-Content "$backup/snapshot.json" -Raw|ConvertFrom-Json).Complete) 'consumption blocks both new and legacy rollback consumers'
Worker "$fixture/worker/WuWaUpdaterFixture.exe" @('--rollback',$backup) 1
Write-Output 'PASS repeated rollback CLI refused'
Write-Output "RESULT: production CLI integration passed; fixture location $fixture; only desktop shortcut adapter replaced."
