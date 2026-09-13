[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SourceDirectory,
      [string]$Destination=(Join-Path (Split-Path $PSScriptRoot -Parent) 'input/desktop-materials'))
$ErrorActionPreference='Stop'
$source=(Resolve-Path -LiteralPath $SourceDirectory).Path
$dest=[IO.Path]::GetFullPath($Destination)
if($dest.Equals($source,[StringComparison]::OrdinalIgnoreCase) -or $dest.StartsWith($source+'\',[StringComparison]::OrdinalIgnoreCase)){throw '输出不能覆盖桌面原材料。'}
$names=@('NvLowLatencyVk.dll','nvngx_deepdvc.dll','nvngx_dlss.dll','nvngx_dlssd.dll','nvngx_dlssg.dll','nvngx_dlssnr.dll','sl.common.dll','sl.deepdvc.dll','sl.directsr.dll','sl.dlss.dll','sl.dlss_d.dll','sl.dlss_g.dll','sl.dlss_nr.dll','sl.interposer.dll','sl.nis.dll','sl.nvperf.dll','sl.pcl.dll','sl.reflex.dll')
$relative=@($names | ForEach-Object { '替换/'+$_ })+@('renodx-mfgunlock.addon64','ReShade_Setup_6.8.0_Addon.exe')
function AssertPlain([string]$path){
    $current=Get-Item -LiteralPath $path
    while($null -ne $current){
        if(($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "不允许重解析点：$($current.FullName)"}
        $current=$current.Parent
    }
}
AssertPlain $source
$actual=@(Get-ChildItem -LiteralPath (Join-Path $source '替换') -File -Force)
if($actual.Count -ne 18 -or @($actual | Where-Object { $_.Name -notin $names }).Count){throw '替换目录必须恰好包含指定的18个DLL。'}
foreach($rel in $relative){AssertPlain (Join-Path $source $rel)}
$records=foreach($rel in $relative){
    $src=Join-Path $source $rel; $out=Join-Path $dest $rel
    $file=Get-Item -LiteralPath $src
    $hash=(Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash.ToLowerInvariant()
    if(Test-Path -LiteralPath $out){
        AssertPlain $out
        if((Get-FileHash -LiteralPath $out -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash){throw "工程已有不同材料，不覆盖：$out"}
    }else{
        New-Item -ItemType Directory -Path (Split-Path $out -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $src -Destination $out
    }
    $copiedHash=(Get-FileHash -LiteralPath $out -Algorithm SHA256).Hash.ToLowerInvariant()
    if($copiedHash -ne $hash){throw "复制后哈希不一致：$out"}
    $stream=[IO.File]::OpenRead($out);$reader=[IO.BinaryReader]::new($stream)
    try {
        if($reader.ReadUInt16() -ne 0x5a4d){throw "非PE文件：$out"}
        $stream.Position=0x3c;$peOffset=$reader.ReadInt32();$stream.Position=$peOffset
        if($reader.ReadUInt32() -ne 0x4550){throw "非PE签名：$out"}
        $machine=$reader.ReadUInt16()
    }finally{$reader.Dispose();$stream.Dispose()}
    $sig=Get-AuthenticodeSignature -LiteralPath $out
    [ordered]@{name=$file.Name;relativePath=$rel;sourcePath=$src;copiedPath=$out;length=$file.Length;sha256=$hash;copySha256=$copiedHash;copyVerified=$true;fileVersion=$file.VersionInfo.FileVersion;productVersion=$file.VersionInfo.ProductVersion;company=$file.VersionInfo.CompanyName;peMachine=('0x{0:x4}' -f $machine);architecture=$(switch($machine){0x8664{'x64'} 0x14c{'x86'} 0xaa64{'arm64'} default{'unknown'}});signatureStatus=$sig.Status.ToString();signatureMessage=$sig.StatusMessage;signer=$(if($sig.SignerCertificate){$sig.SignerCertificate.Subject}else{$null});certificateThumbprint=$(if($sig.SignerCertificate){$sig.SignerCertificate.Thumbprint}else{$null})}
}
$audit=[ordered]@{auditedAt=[DateTimeOffset]::Now.ToString('o');sourceRoot=$source;destination=$dest;provenance='用户本地文件；本地哈希用于完整性，不证明官方来源。签名状态为本机实际校验结果。';files=@($records)}
[IO.File]::WriteAllText((Join-Path $dest 'source-manifest.json'),($audit|ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
$records | ForEach-Object { Write-Output "$($_.relativePath) $($_.architecture) $($_.fileVersion) $($_.sha256) copyVerified=$($_.copyVerified) signature=$($_.signatureStatus)" }
