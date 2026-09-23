param([Parameter(Mandatory)][string]$Workspace)
$ErrorActionPreference='Stop'
$evidence=Join-Path $Workspace 'artifacts/update-acceptance'
$install=Join-Path $evidence ('installation-'+[guid]::NewGuid().ToString('N'))
$package=Join-Path $install '.updates/acceptance'
$baseline=Join-Path $Workspace 'artifacts/online-v1.1.1/baseline/extracted'
$newPackage=Join-Path $Workspace 'artifacts/layout-final-v1.1.1-20260923-152029/update'
$desktop=[Environment]::GetFolderPath('DesktopDirectory')
$beforeLinks=@{}; Get-ChildItem -LiteralPath $desktop -Filter '*.lnk' -File | ForEach-Object { $beforeLinks[$_.FullName]=(Get-FileHash -LiteralPath $_.FullName).Hash }
$shell=New-Object -ComObject WScript.Shell
$script:oldProcess=$null
$transcript=Join-Path $evidence 'isolated-update-rollback.log'
Start-Transcript -LiteralPath $transcript -Force | Out-Null
function Assert([bool]$condition,[string]$message){if(!$condition){throw $message}; Write-Host "PASS $message"}
function New-Worker([string]$exe,[string[]]$parameters){
    $info=[Diagnostics.ProcessStartInfo]::new($exe);$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.WorkingDirectory=Split-Path -Parent $exe
    $info.RedirectStandardInput=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $info.StandardOutputEncoding=[Text.Encoding]::UTF8;$info.StandardErrorEncoding=[Text.Encoding]::UTF8
    foreach($argument in $parameters){$info.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($info);$process.StandardInput.Close();return $process
}
function Complete-Worker($process,[string]$log){
    $out=$process.StandardOutput.ReadToEndAsync();$err=$process.StandardError.ReadToEndAsync()
    if(!$process.WaitForExit(180000)){throw "Worker timed out; not terminated: PID $($process.Id)"}
    $text=$out.GetAwaiter().GetResult()+$err.GetAwaiter().GetResult()
    $text | Set-Content -LiteralPath (Join-Path $evidence $log) -Encoding utf8
    Write-Host $text
    Assert ($process.ExitCode -eq 0) "$log exit code zero"
}
try {
    New-Item -ItemType Directory -Path $install | Out-Null
    Copy-Item -Path (Join-Path $baseline '*') -Destination $install -Recurse
    New-Item -ItemType Directory -Path (Join-Path $install 'data/deployments') -Force | Out-Null
    '{"fixture":"old ownership","Files":[]}' | Set-Content -LiteralPath (Join-Path $install 'data/deployments/acceptance.json')
    '{"AutoCheckUpdates":false,"GameRoot":"","GameExe":"","FpsEnabled":false}' | Set-Content -LiteralPath (Join-Path $install 'data/settings.json')
    'user original INI bytes' | Set-Content -LiteralPath (Join-Path $install 'custom.ini')
    $original=@{}
    Get-ChildItem -LiteralPath $install -Recurse -File | Where-Object { !$_.FullName.StartsWith((Join-Path $install 'data')+[IO.Path]::DirectorySeparatorChar) } | ForEach-Object {
        $original[[IO.Path]::GetRelativePath($install,$_.FullName)]=(Get-FileHash -LiteralPath $_.FullName).Hash
    }
    $original | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'original-file-hashes.json')
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    Copy-Item -Path (Join-Path $newPackage '*') -Destination $package -Recurse
    Copy-Item -LiteralPath (Join-Path $evidence 'updater-build/WuWaUpdater.exe') -Destination (Join-Path $package '更新.exe') -Force
    & (Join-Path $Workspace 'scripts/New-UpdateManifest.ps1') -PackageDirectory $package -Version '1.1.1'
    Complete-Worker (New-Worker (Join-Path $package '更新.exe') @('--validate-package',$package,'1.1.1')) 'package-validation.log'
    Complete-Worker (New-Worker (Join-Path $package '更新.exe') @('--self-test')) 'updater-self-tests.log'
    $target=Join-Path $install 'WuWaFpsUnlock.exe'
    # A harmless updater child exits itself; no WPF/game is opened. Live native
    # identity/wait coverage is separately exercised by --self-test above.
    $child=New-Worker (Join-Path $package '更新.exe') @('--self-test-child')
    Assert ($child.WaitForExit(10000)) 'Known harmless child exited naturally'
    $worker=New-Worker (Join-Path $package '更新.exe') @('--wait-for-exit',[string]$child.Id,$target)
    Complete-Worker $worker 'online-wait-update.log'
    foreach($file in Get-ChildItem -LiteralPath (Join-Path $package 'update-payload') -Recurse -File){
        $relative=[IO.Path]::GetRelativePath((Join-Path $package 'update-payload'),$file.FullName)
        Assert ((Get-FileHash -LiteralPath $file.FullName).Hash -eq (Get-FileHash -LiteralPath (Join-Path $install $relative)).Hash) "Updated bytes match package: $relative"
    }
    $backups=@(Get-ChildItem -LiteralPath $install -Directory -Filter 'update-backup-*')
    Assert ($backups.Count -eq 1) 'Exactly one independent update snapshot created'
    $backup=$backups[0].FullName
    Assert (Test-Path -LiteralPath (Join-Path $backup 'snapshot/data/deployments/acceptance.json')) 'Old deployment record included in snapshot'
    Assert (Test-Path -LiteralPath (Join-Path $package '回退.cmd')) 'Package rollback launcher generated'
    Complete-Worker (New-Worker (Join-Path $package '更新.exe') @('--validate-package',$package,'1.1.1')) 'package-retry-validation.log'
    'current ownership after new deployment' | Set-Content -LiteralPath (Join-Path $install 'data/deployments/acceptance.json')
    'later user-created material' | Set-Content -LiteralPath (Join-Path $install 'payload/user-new.txt')
    $dataHash=(Get-FileHash -LiteralPath (Join-Path $install 'data/deployments/acceptance.json')).Hash
    # Delete only this verified fixture update directory, never the supplied source package.
    $resolved=[IO.Path]::GetFullPath($package)
    if(!$resolved.StartsWith([IO.Path]::GetFullPath($install)+[IO.Path]::DirectorySeparatorChar)){throw 'Unsafe fixture package deletion'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
    Assert (!(Test-Path -LiteralPath $package)) 'Update folder removed before standalone-backup rollback'
    Complete-Worker (New-Worker (Join-Path $backup 'WuWaUpdater.exe') @('--rollback',$backup)) 'rollback.log'
    foreach($relative in $original.Keys){Assert ((Get-FileHash -LiteralPath (Join-Path $install $relative)).Hash -eq $original[$relative]) "Original user bytes restored: $relative"}
    Assert ((Get-FileHash -LiteralPath (Join-Path $install 'data/deployments/acceptance.json')).Hash -eq $dataHash) 'Current deployment ownership retained during rollback'
    Assert (Test-Path -LiteralPath (Join-Path $install 'payload/user-new.txt')) 'Later user-created material retained'
    Complete-Worker (New-Worker (Join-Path $backup 'WuWaUpdater.exe') @('--rollback',$backup)) 'rollback-repeat.log'
    Assert ((Get-FileHash -LiteralPath (Join-Path $install 'data/deployments/acceptance.json')).Hash -eq $dataHash) 'Repeated rollback remains safe for current deployment record'
    Write-Host "SUCCESS installation=$install backup=$backup"
}
finally {
    if($script:oldProcess -and !$script:oldProcess.HasExited){Write-Warning "Isolated launcher remains running PID $($script:oldProcess.Id); not killed."}
    foreach($file in Get-ChildItem -LiteralPath $desktop -Filter '*.lnk' -File){
        if($beforeLinks.ContainsKey($file.FullName)){continue}
        $link=$shell.CreateShortcut($file.FullName)
        if([string]::IsNullOrWhiteSpace($link.TargetPath)){continue}
        if([IO.Path]::GetFullPath($link.TargetPath) -eq (Join-Path $install 'WuWaFpsUnlock.exe')){
            Write-Host "Removing only test-created desktop link: $($file.FullName) -> $($link.TargetPath)"
            Remove-Item -LiteralPath $file.FullName
        }
    }
    foreach($path in $beforeLinks.Keys){Assert ((Test-Path -LiteralPath $path) -and (Get-FileHash -LiteralPath $path).Hash -eq $beforeLinks[$path]) "Existing desktop shortcut unchanged: $path"}
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
    Stop-Transcript | Out-Null
}
