[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][Alias('GameRootFiles')][string]$ReplacementFiles,
    [Parameter(Mandatory=$true)][string]$MfgAddon,
    [Parameter(Mandatory=$true)][ValidateSet('dxgi','d3d12')][string]$ProxyApi,
    [string]$Destination=(Join-Path (Split-Path $PSScriptRoot -Parent) 'payload'),
    [string]$PackageId='wuwa-mfg-0.9-local',
    [string]$MfgIni='',
    [Parameter(Mandatory=$true)][string]$LocalReShadeSetup,
    [string]$ReShadeRuntimeSha256='',
    [int]$FixedMultiplier=4
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ReplacementFiles).Path.TrimEnd('\','/')
$dest=[IO.Path]::GetFullPath($Destination)
if($dest.TrimEnd('\','/').Equals($root,[StringComparison]::OrdinalIgnoreCase) -or $dest.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw '输出目录不能位于输入文件树内部。'}
if(-not(Test-Path -LiteralPath $MfgAddon -PathType Leaf)){throw '缺少 renodx-mfgunlock.addon64。'}
if([IO.Path]::GetFileName($MfgAddon) -cne 'renodx-mfgunlock.addon64'){throw '请提供标准文件名 renodx-mfgunlock.addon64。'}
if($FixedMultiplier -lt 2 -or $FixedMultiplier -gt 6){throw 'Fixed 倍率必须为 2–6。'}
$allowed=@('nvngx_dlss.dll','nvngx_dlssg.dll','nvngx_dlssd.dll','sl.interposer.dll','sl.common.dll','sl.dlss_g.dll','sl.reflex.dll','sl.pcl.dll','sl.dlss.dll','sl.dlss_d.dll','sl.deepdvc.dll','sl.directsr.dll','sl.nis.dll','NvLowLatencyVk.dll','nvngx_deepdvc.dll','nvngx_dlssnr.dll','sl.dlss_nr.dll','sl.nvperf.dll')
function PlainFiles([string]$dir){
    if(([IO.File]::GetAttributes($dir) -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "不允许目录链接：$dir"}
    foreach($item in Get-ChildItem -LiteralPath $dir -Force){
        if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "不允许重解析点：$($item.FullName)"}
        if($item.PSIsContainer){throw '替换必须是平铺素材目录。'}else{$item}
    }
}
$inputs=@(PlainFiles $root)
if($inputs.Count -ne 18){throw '输入必须恰好包含全部18个指定DLL。'}
foreach($file in $inputs){
    if($allowed -notcontains $file.Name){throw "未知材料：$($file.Name)"}
}
if(([IO.File]::GetAttributes($MfgAddon) -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Addon不允许链接。'}
if(([IO.File]::GetAttributes($LocalReShadeSetup) -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Setup不允许链接。'}
if([IO.Path]::GetFileName($LocalReShadeSetup) -cne 'ReShade_Setup_6.8.0_Addon.exe'){throw '必须使用用户提供的6.8.0 Full Add-on Setup。'}
$entries=@()
foreach($file in $inputs){
    if($allowed -notcontains $file.Name.ToLowerInvariant()){throw "未知 DLL，不会猜测其部署用途：$($file.FullName)"}
    $relative=$file.Name
    $source='files/game/'+$relative
    $out=Join-Path $dest $source
    New-Item (Split-Path $out -Parent) -ItemType Directory -Force|Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $out -Force
    $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if((Get-FileHash -LiteralPath $out -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash){throw "复制校验失败：$out"}
    $entries+=@{source=$source;anchor='GameRoot';target=$relative;kind='Vendor';size=$file.Length;sha256=$hash}
}
if($entries.Count -ne 18){throw '输入必须包含全部18个指定DLL。'}
$out=Join-Path $dest 'files/addon/renodx-mfgunlock.addon64';New-Item (Split-Path $out -Parent) -ItemType Directory -Force|Out-Null
Copy-Item -LiteralPath $MfgAddon -Destination $out -Force
if((Get-FileHash -LiteralPath $MfgAddon -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $out -Algorithm SHA256).Hash){throw 'Addon复制校验失败。'}
$entries+=@{source='files/addon/renodx-mfgunlock.addon64';anchor='AddonDir';target='renodx-mfgunlock.addon64';kind='Addon';size=(Get-Item $out).Length;sha256=(Get-FileHash $out -Algorithm SHA256).Hash.ToLowerInvariant()}
$config=@{}
if($MfgIni){
    $active=$false
    foreach($line in [IO.File]::ReadAllLines((Resolve-Path -LiteralPath $MfgIni).Path)){
        $t=$line.Trim()
        if($t.StartsWith('[')){$active=($t -ieq '[RenoDX.MFGUnlock]');continue}
        if($active -and $t -and -not $t.StartsWith(';') -and -not $t.StartsWith('#')){
            $eq=$t.IndexOf('=');if($eq -gt 0){$config[$t.Substring(0,$eq).Trim()]=$t.Substring($eq+1).Trim()}
        }
    }
}
if([IO.Path]::GetFileName($LocalReShadeSetup) -cne 'ReShade_Setup_6.8.0_Addon.exe'){throw '必须使用用户提供的6.8.0 Full Add-on Setup。'}
$setupHash=(Get-FileHash -LiteralPath $LocalReShadeSetup -Algorithm SHA256).Hash.ToLowerInvariant()
$setupOut=Join-Path $dest 'setup/ReShade_Setup_6.8.0_Addon.exe'
New-Item -ItemType Directory -Path (Split-Path $setupOut -Parent) -Force | Out-Null
Copy-Item -LiteralPath $LocalReShadeSetup -Destination $setupOut -Force
if((Get-FileHash -LiteralPath $setupOut -Algorithm SHA256).Hash.ToLowerInvariant() -ne $setupHash){throw 'Setup复制完整性失败。'}
$manifest=@{
    schemaVersion=1;packageId=$PackageId;addonVersion=(Get-Item -LiteralPath $MfgAddon).VersionInfo.FileVersion;dynamicMinimumDriver=59541;fixedMinimumDriver=$null
    fixedMultiplier=$FixedMultiplier;preferDynamic=$true
    reShade=@{version='6.8.0';proxyApi=$ProxyApi;setupSha256=$setupHash;localSetupPath='setup/ReShade_Setup_6.8.0_Addon.exe';fullRuntimeSha256=$ReShadeRuntimeSha256}
    files=$entries;mfgConfig=$config
}
New-Item $dest -ItemType Directory -Force|Out-Null
[IO.File]::WriteAllText((Join-Path $dest 'manifest.json'),($manifest|ConvertTo-Json -Depth 12),(New-Object Text.UTF8Encoding $false))
Write-Host "Created $(Join-Path $dest 'manifest.json')"
Write-Host 'Vendor target仅为同名搜索键，运行时在用户选定游戏根内查找实际已有文件；不存在则跳过，真实映射须确认。'
Write-Host '随包携带用户本地ReShade安装器并校验SHA-256；已有ReShade优先复用。代理布局游戏内待验证。'
