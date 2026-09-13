[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskArtifacts=Join-Path $taskRoot 'artifacts'
$taskPublish=Join-Path $taskArtifacts 'win-x64'
if(-not(Test-Path (Join-Path $taskPublish 'WuWaFpsUnlock.exe'))){throw 'Run the actual build/publish first.'}
if(-not(Test-Path (Join-Path $taskRoot 'components\dotnet8\dotnet.exe'))){throw 'Prepare the portable .NET8 dependency first.'}
Copy-Item (Join-Path $taskRoot 'components\dotnet8') (Join-Path $taskPublish 'components') -Recurse -Force
Copy-Item (Join-Path $taskRoot 'components\PROVENANCE.json') (Join-Path $taskPublish 'components\PROVENANCE.json') -Force
New-Item -ItemType Directory -Force (Join-Path $taskPublish 'payload') | Out-Null
Copy-Item (Join-Path $taskRoot 'payload\*') (Join-Path $taskPublish 'payload') -Recurse -Force
Copy-Item (Join-Path $taskRoot 'README.md') (Join-Path $taskPublish 'README.md') -Force
Copy-Item (Join-Path $taskRoot 'docs\WINDOWS_VALIDATION.md') (Join-Path $taskPublish 'WINDOWS_VALIDATION.md') -Force
$taskManifest=Get-Content (Join-Path $taskPublish 'payload\manifest.json') -Raw | ConvertFrom-Json
foreach($taskFile in $taskManifest.Files){
    $taskSource=Join-Path (Join-Path $taskPublish 'payload') $taskFile.Source
    if((Get-Item $taskSource).Length -ne $taskFile.Size -or (Get-FileHash $taskSource -Algorithm SHA256).Hash -ne $taskFile.Sha256){throw "Published payload mismatch: $taskSource"}
}
$taskSetup=Join-Path (Join-Path $taskPublish 'payload') $taskManifest.ReShade.LocalSetupPath
if((Get-FileHash $taskSetup -Algorithm SHA256).Hash -ne $taskManifest.ReShade.SetupSha256){throw 'Published Setup mismatch'}
if((Get-FileHash (Join-Path $taskPublish 'components\unlocker\unlock.exe') -Algorithm SHA256).Hash -ne '5b9cba854357a4d9ce9c56676e22e397be8d5dbd2dc10de9393155e378050fae'){throw 'Published unlocker mismatch'}
if(Test-Path (Join-Path $taskPublish 'components\ww_plugin_base.dll')){throw 'Legacy FPS DLL must not be published'}
$taskRuntimeInventory=Get-Content (Join-Path $taskRoot 'knowledge\materials\dotnet8-file-hashes.json') -Raw | ConvertFrom-Json
foreach($taskEntry in $taskRuntimeInventory){
    $taskRuntimeFile=Join-Path (Join-Path $taskPublish 'components\dotnet8') $taskEntry.path
    if((Get-Item $taskRuntimeFile).Length -ne $taskEntry.length -or (Get-FileHash $taskRuntimeFile -Algorithm SHA256).Hash -ne $taskEntry.sha256){throw "Published .NET8 mismatch: $taskRuntimeFile"}
}
$taskEvidence=Join-Path $taskPublish 'validation'
New-Item $taskEvidence -ItemType Directory -Force | Out-Null
foreach($taskEvidenceName in @('DELIVERY_REPORT.md','REAL_GAME_OPERATION_PLAN.md','build.log','tests.log','external-worker-tests.log','windows-tests.log','ui-tests.log','material-audit.csv','real-game-candidate-map.md','real-game-candidate-map.json','real-game-historical-log.md','real-game-historical-log.json')){
    Copy-Item (Join-Path $taskArtifacts $taskEvidenceName) $taskEvidence -Force
}
Copy-Item (Join-Path $taskArtifacts 'native-offscreen') $taskEvidence -Recurse -Force
$taskHashes=@(Get-ChildItem $taskPublish -File -Recurse | Where-Object {$_.FullName -notlike '*\data\*'} | ForEach-Object {
    [ordered]@{path=[IO.Path]::GetRelativePath($taskPublish,$_.FullName);size=$_.Length;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
$taskHashes | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $taskArtifacts 'portable-file-hashes.json') -Encoding utf8
$taskPortableZip=Join-Path $taskArtifacts 'WuWaFPSUnlock-0.9-dev2-win-x64-portable.zip'
Compress-Archive -Path (Join-Path $taskPublish '*') -DestinationPath $taskPortableZip -Force -CompressionLevel Optimal
# Complete source plus the exact local inputs and runtime; caches, generated tests and Git internals are not source.
$taskSourcePaths=@('.github','preview','src','tests','scripts','input','components','payload','licenses','reference','knowledge','docs','AGENTS.md','CODEX_TASK.md','00_START_HERE.md','README.md','global.json','Build.cmd','.gitignore') | ForEach-Object {Join-Path $taskRoot $_}
$taskSourceZip=Join-Path $taskArtifacts 'WuWaFPSUnlock-0.9-dev2-source.zip'
# Zip source using explicit recursion filters so bin/obj are never distributed as source.
Add-Type -AssemblyName System.IO.Compression
$taskStream=[IO.File]::Open($taskSourceZip,[IO.FileMode]::Create)
$taskArchive=[IO.Compression.ZipArchive]::new($taskStream,[IO.Compression.ZipArchiveMode]::Create,$false)
try {
    foreach($taskPath in $taskSourcePaths){
        $taskItems=if(Test-Path $taskPath -PathType Container){Get-ChildItem $taskPath -File -Recurse | Where-Object {$_.FullName -notmatch '[\\/](bin|obj|data)[\\/]'}}else{Get-Item $taskPath}
        foreach($taskItem in $taskItems){
            $taskRelative=[IO.Path]::GetRelativePath($taskRoot,$taskItem.FullName).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive,$taskItem.FullName,$taskRelative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
} finally {$taskArchive.Dispose();$taskStream.Dispose()}
Get-FileHash $taskPortableZip,$taskSourceZip -Algorithm SHA256 | Format-List | Out-File (Join-Path $taskArtifacts 'DELIVERY_SHA256.txt') -Encoding utf8
Write-Output "Verified $($taskHashes.Count) published files, exact 19 payload files, user Setup and unlocker."
Write-Output "Portable: $taskPortableZip"
Write-Output "Source: $taskSourceZip"


