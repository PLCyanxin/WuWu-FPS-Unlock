#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SourceDirectory,
      [Parameter(Mandatory=$true)][string]$DestinationDirectory)
$ErrorActionPreference='Stop'
function Canonical([string]$path){
    if(-not [IO.Path]::IsPathFullyQualified($path)){throw "必须提供完整包目录：$path"}
    [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($path))
}
function Within([string]$path,[string]$root){$path.Equals($root,[StringComparison]::OrdinalIgnoreCase) -or $path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)}
function Plain([string]$path){
    for($current=[IO.Path]::GetFullPath($path);$current;$current=[IO.Path]::GetDirectoryName($current)){
        $item=Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if($item -and (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)){throw "拒绝链接：$current"}
    }
}
function Hash([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
$source=Canonical $SourceDirectory;$destination=Canonical $DestinationDirectory
if((Within $source $destination) -or (Within $destination $source)){throw '源包与目标包不能相同或相互嵌套。'}
Plain $source;Plain $destination
if(-not(Test-Path -LiteralPath $source -PathType Container)){throw '源包目录不存在。'}
if(-not(Test-Path -LiteralPath $destination -PathType Container)){throw '目标包目录不存在；请先生成完整新包。'}
$sourceData=Join-Path $source 'data';$destinationData=Join-Path $destination 'data'
Plain $sourceData;Plain $destinationData
$reportDirectory=Join-Path $destination 'migration-reports';Plain $reportDirectory
$reportPath=Join-Path $reportDirectory ('migration-'+[DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N')+'.json')
$report=[ordered]@{startedAt=[DateTimeOffset]::Now.ToString('o');sourceDirectory=$source;destinationDirectory=$destination;scope='仅data子树；源只读，程序不执行';status='Planning';sourceModified=$false;executablesRun=$false;files=@();errors=@();completedAt=$null}
function SaveReport {
    Plain $reportDirectory
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    $report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $reportPath -Encoding utf8
}
$plan=[Collections.Generic.List[object]]::new()
try{
    if(-not(Test-Path -LiteralPath $sourceData -PathType Container)){
        $report.status='NoSourceData';$report.completedAt=[DateTimeOffset]::Now.ToString('o');SaveReport;Write-Output "No source data. Report: $reportPath";return
    }
    $queue=[Collections.Generic.Queue[string]]::new();$queue.Enqueue($sourceData)
    while($queue.Count){
        $directory=$queue.Dequeue();Plain $directory
        foreach($item in Get-ChildItem -LiteralPath $directory -Force){
            if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "源data包含链接，整次迁移停止：$($item.FullName)"}
            if(-not(Within $item.FullName $sourceData)){throw '源data枚举越界。'}
            if($item.PSIsContainer){$queue.Enqueue($item.FullName);continue}
            $relative=$item.FullName.Substring($sourceData.Length+1)
            $target=[IO.Path]::GetFullPath((Join-Path $destinationData $relative))
            if(-not(Within $target $destinationData)){throw '目标data路径越界。'}
            Plain $target
            $sourceHash=Hash $item.FullName;$outputHash=$sourceHash;$outputBytes=$null;$relocated=$false;$oldManifest=$null;$newManifest=$null
            if($relative -ieq 'settings.json'){
                if($item.Length -gt 20MB){throw 'settings.json超过20MB，拒绝解析。'}
                $node=[System.Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText($item.FullName))
                if($node -isnot [System.Text.Json.Nodes.JsonObject]){throw 'settings.json根必须是JSON对象。'}
                $keys=@($node | Where-Object {$_.Key -ieq 'PackageManifest'})
                if($keys.Count -gt 1){throw 'settings.json存在多个大小写冲突的PackageManifest键。'}
                if($keys.Count -eq 1 -and $null -ne $keys[0].Value){
                    $manifestNode=$keys[0].Value
                    if($manifestNode.GetValueKind() -ne [System.Text.Json.JsonValueKind]::String){throw 'PackageManifest应为字符串。'}
                    $oldManifest=$manifestNode.ToString()
                    $expectedOld=Join-Path $source 'payload/manifest.json';$expectedNew=Join-Path $destination 'payload/manifest.json'
                    if([IO.Path]::IsPathFullyQualified($oldManifest) -and (Canonical $oldManifest).Equals((Canonical $expectedOld),[StringComparison]::OrdinalIgnoreCase)){
                        Plain $expectedOld;Plain $expectedNew
                        if(-not(Test-Path -LiteralPath $expectedOld -PathType Leaf)){throw '旧包内manifest不存在，不能确认路径重定位。'}
                        if(-not(Test-Path -LiteralPath $expectedNew -PathType Leaf)){throw '新包内manifest不存在，不能重定位到缺失文件。'}
                        $newManifest=[IO.Path]::GetFullPath($expectedNew)
                        $node[$keys[0].Key]=[System.Text.Json.Nodes.JsonValue]::Create([string]$newManifest)
                        $jsonOptions=[System.Text.Json.JsonSerializerOptions]::new();$jsonOptions.WriteIndented=$true
                        $outputBytes=[Text.UTF8Encoding]::new($false).GetBytes($node.ToJsonString($jsonOptions))
                        $outputHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($outputBytes)).ToLowerInvariant();$relocated=$true
                    }
                }
            }
            $status='Pending';$existingHash=$null
            if(Test-Path -LiteralPath $target){
                if(-not(Test-Path -LiteralPath $target -PathType Leaf)){throw "目标文件位置是目录：$target"}
                $existingHash=Hash $target;$status=if($existingHash -eq $outputHash){'AlreadyIdentical'}else{'ConflictNotOverwritten'}
            }
            $record=[ordered]@{relativePath=$relative;source=$item.FullName;destination=$target;sourceLength=$item.Length;sourceSha256=$sourceHash;expectedDestinationSha256=$outputHash;existingDestinationSha256=$existingHash;destinationSha256=$null;manifestRelocated=$relocated;previousManifest=$oldManifest;newManifest=$newManifest;status=$status}
            $plan.Add([PSCustomObject]@{record=$record;bytes=$outputBytes})
        }
    }
    $report.files=@($plan | ForEach-Object {$_.record})
    if(@($plan | Where-Object {$_.record.status -eq 'ConflictNotOverwritten'}).Count){$report.status='BlockedConflicts';throw '存在不同的目标文件：已保存冲突报告，未复制任何data文件，不覆盖。'}
    foreach($entry in $plan){
        Plain $entry.record.source;Plain $entry.record.destination
        if((Hash $entry.record.source) -ne $entry.record.sourceSha256){throw "源文件在预检后改变：$($entry.record.source)"}
    }
    $report.status='Copying';SaveReport
    foreach($entry in $plan){
        $record=$entry.record;Plain $record.source;Plain $record.destination
        if((Hash $record.source) -ne $record.sourceSha256){throw "源文件在复制前改变：$($record.source)"}
        if($record.status -eq 'AlreadyIdentical'){
            if((Hash $record.destination) -ne $record.expectedDestinationSha256){throw '预检后目标改变，停止。'}
        }else{
            New-Item -ItemType Directory -Path (Split-Path $record.destination -Parent) -Force | Out-Null
            Plain $record.destination
            $stream=[IO.File]::Open($record.destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
            try{
                if($null -ne $entry.bytes){$stream.Write($entry.bytes,0,$entry.bytes.Length)}
                else{$inputStream=[IO.File]::OpenRead($record.source);try{$inputStream.CopyTo($stream)}finally{$inputStream.Dispose()}}
            }finally{$stream.Dispose()}
            $record.status='Copied'
        }
        $record.destinationSha256=Hash $record.destination
        if($record.destinationSha256 -ne $record.expectedDestinationSha256){$record.status='VerificationFailed';throw '复制后SHA-256不一致。'}
        if((Hash $record.source) -ne $record.sourceSha256){throw '源文件在复制期间改变，不能声称稳定迁移。'}
        SaveReport
    }
    $report.status='Completed';$report.completedAt=[DateTimeOffset]::Now.ToString('o');SaveReport
    Write-Output "Migration completed ($($plan.Count) files), source unchanged. Report: $reportPath"
}catch{
    if($report.status -ne 'BlockedConflicts'){$report.status=if($report.status -eq 'Copying'){'PartialFailure'}else{'ValidationFailed'}}
    $report.errors+=@($_.Exception.Message);$report.completedAt=[DateTimeOffset]::Now.ToString('o');SaveReport;throw
}
