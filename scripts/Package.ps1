[CmdletBinding()]param()
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskArtifacts=Join-Path $taskRoot 'artifacts\v0.1'
$taskPublish=Join-Path $taskArtifacts 'win-x64'
$taskExe=Join-Path $taskPublish 'WuWaFpsUnlock.exe'
if((Get-Item $taskExe).VersionInfo.FileVersion -ne '0.1.0.0'){throw 'Wrong own file version'}
if(Test-Path (Join-Path $taskPublish 'components\unlocker')){throw 'External unlocker must not be published'}
if(Test-Path (Join-Path $taskPublish 'components\dotnet8')){throw 'External runtime must not be published'}
if((Get-FileHash (Join-Path $taskPublish 'components\fps\ww_plugin_base.dll') -Algorithm SHA256).Hash -ne '844d7552692f53e8a1bfe45edf360a094597c5bf2b26dc058ff59b21d2250c3a'){throw 'Builtin core hash mismatch'}
$taskManifest=Get-Content (Join-Path $taskPublish 'payload\manifest.json') -Raw|ConvertFrom-Json
foreach($taskFile in $taskManifest.Files){if((Get-FileHash (Join-Path (Join-Path $taskPublish 'payload') $taskFile.Source) -Algorithm SHA256).Hash -ne $taskFile.Sha256){throw 'Payload hash mismatch'}}
Copy-Item (Join-Path $taskRoot 'README.md') $taskPublish -Force
Copy-Item (Join-Path $taskRoot 'docs\WINDOWS_VALIDATION.md') $taskPublish -Force
Copy-Item (Join-Path $taskRoot 'components\PROVENANCE.json') (Join-Path $taskPublish 'components\PROVENANCE.json') -Force
Copy-Item (Join-Path $taskRoot 'licenses\THIRD_PARTY_NOTICES.md') (Join-Path $taskPublish 'licenses\THIRD_PARTY_NOTICES.md') -Force
$taskHashes=@(Get-ChildItem $taskPublish -File -Recurse|ForEach-Object {[pscustomobject]@{path=[IO.Path]::GetRelativePath($taskPublish,$_.FullName);size=$_.Length;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}})
$taskHashes|ConvertTo-Json -Depth 4|Set-Content (Join-Path $taskArtifacts 'portable-file-hashes.json') -Encoding utf8
$taskZip=Join-Path $taskArtifacts 'WuWaFPSUnlock-0.1-win-x64-portable.zip'
Compress-Archive -Path (Join-Path $taskPublish '*') -DestinationPath $taskZip -CompressionLevel Optimal -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskSourceZip=Join-Path $taskArtifacts 'WuWaFPSUnlock-0.1-source.zip'
$taskStream=[IO.File]::Open($taskSourceZip,[IO.FileMode]::Create)
$taskArchive=[IO.Compression.ZipArchive]::new($taskStream,[IO.Compression.ZipArchiveMode]::Create,$false)
try{
    foreach($taskName in @('src','tests','scripts','components','payload','input','licenses','reference','knowledge','docs','.github','AGENTS.md','README.md','CODEX_TASK.md','00_START_HERE.md','global.json','Directory.Build.props','Build.cmd','.gitignore')){
        $taskPath=Join-Path $taskRoot $taskName
        $taskItems=if(Test-Path $taskPath -PathType Container){Get-ChildItem $taskPath -File -Recurse|Where-Object {$_.FullName -notmatch '[\\/](bin|obj|data|dotnet8)[\\/]'}}else{Get-Item $taskPath}
        foreach($taskItem in $taskItems){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive,$taskItem.FullName,[IO.Path]::GetRelativePath($taskRoot,$taskItem.FullName).Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal)|Out-Null}
    }
}finally{$taskArchive.Dispose();$taskStream.Dispose()}
Get-FileHash $taskZip,$taskSourceZip -Algorithm SHA256|Format-List|Out-File (Join-Path $taskArtifacts 'DELIVERY_SHA256.txt') -Encoding utf8
Write-Output "Verified own v0.1, builtin core, 19 payloads; no unlock.exe dependency. Portable files=$($taskHashes.Count)"
Write-Output $taskZip
Write-Output $taskSourceZip
