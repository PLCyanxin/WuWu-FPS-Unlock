[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameRoot,
      [Parameter(Mandatory=$true)][string]$ShippingExe,
      [Parameter(Mandatory=$true)][string]$ManifestPath,
      [string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'))
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $GameRoot).Path.TrimEnd('\')
$out=[IO.Path]::GetFullPath($OutputDirectory)
if($out.Equals($root,[StringComparison]::OrdinalIgnoreCase) -or $out.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw '审计输出不能写入游戏目录'}
function Inside([string]$path){$path.Equals($root,[StringComparison]::OrdinalIgnoreCase) -or $path.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)}
function PlainAncestors([string]$path){
    $item=Get-Item -LiteralPath $path
    while($null -ne $item){if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "拒绝跟随链接：$($item.FullName)"};$item=if($item.PSIsContainer){$item.Parent}else{$item.Directory}}
}
PlainAncestors $root
$shipping=(Resolve-Path -LiteralPath $ShippingExe).Path
if(-not(Inside $shipping)){throw 'Shipping不在候选根内'}
PlainAncestors $shipping
$manifest=(Resolve-Path -LiteralPath $ManifestPath).Path
$manifestRoot=Split-Path $manifest -Parent
$vendors=@((Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json).files | Where-Object kind -eq Vendor)
$names=@($vendors.target)
$proxyNames=@('dxgi.dll','d3d11.dll','d3d12.dll','d3d9.dll','opengl32.dll','ReShade64.dll','ReShade32.dll')
$iniNames=@('ReShade.ini','ReShadePreset.ini')
function InspectBinary([string]$path){
    PlainAncestors $path
    $file=Get-Item -LiteralPath $path
    $machine=$null;$arch='not-PE'
    $stream=[IO.File]::OpenRead($path);$reader=[IO.BinaryReader]::new($stream)
    try{if($reader.ReadUInt16() -eq 0x5a4d){$stream.Position=0x3c;$offset=$reader.ReadInt32();$stream.Position=$offset;if($reader.ReadUInt32() -eq 0x4550){$machine=$reader.ReadUInt16();$arch=switch($machine){0x8664{'x64'} 0x14c{'x86'} 0xaa64{'arm64'} default{'unknown'}}}}}finally{$reader.Dispose();$stream.Dispose()}
    [ordered]@{path=$file.FullName;relativePath=$(if(Inside $file.FullName){$file.FullName.Substring($root.Length+1)}else{$null});length=$file.Length;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant();fileVersion=$file.VersionInfo.FileVersion;productVersion=$file.VersionInfo.ProductVersion;company=$file.VersionInfo.CompanyName;description=$file.VersionInfo.FileDescription;architecture=$arch;peMachine=$(if($null -ne $machine){'0x{0:x4}' -f $machine}else{$null})}
}
$queue=[Collections.Generic.Queue[string]]::new();$queue.Enqueue($root)
$targets=@{};foreach($name in $names){$targets[$name]=[Collections.Generic.List[object]]::new()}
$proxies=[Collections.Generic.List[object]]::new();$inis=[Collections.Generic.List[object]]::new();$addons=[Collections.Generic.List[object]]::new()
$skipped=[Collections.Generic.List[string]]::new();$errors=[Collections.Generic.List[string]]::new();$dirs=0;$files=0
while($queue.Count){
    $dir=$queue.Dequeue();$dirs++
    try{$children=@(Get-ChildItem -LiteralPath $dir -Force -ErrorAction Stop)}catch{$errors.Add("$dir : $($_.Exception.Message)");continue}
    foreach($item in $children){
        if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){$skipped.Add($item.FullName);continue}
        if(-not(Inside $item.FullName)){throw '枚举越界'}
        if($item.PSIsContainer){$queue.Enqueue($item.FullName);continue};$files++
        try{
            if($targets.ContainsKey($item.Name)){$targets[$item.Name].Add((InspectBinary $item.FullName))}
            if($item.Name -in $proxyNames){$proxies.Add((InspectBinary $item.FullName))}
            if($item.Name -eq 'renodx-mfgunlock.addon64'){$addons.Add((InspectBinary $item.FullName))}
            if($item.Name -in $iniNames){
                if($item.Length -gt 1048576){$errors.Add("配置大于1MB，未读：$($item.FullName)");continue}
                $section='';$keys=[Collections.Generic.List[object]]::new()
                foreach($line in [IO.File]::ReadAllLines($item.FullName)){
                    $t=$line.Trim();if($t -match '^\[(.+)\]$'){$section=$Matches[1];continue}
                    if($t -match '^([^;#][^=]*)=(.*)$'){$key=$Matches[1].Trim();$value=$Matches[2].Trim();if($key -match 'Path|Early|Addon|Preset|EffectSearch|TextureSearch'){$keys.Add([ordered]@{section=$section;key=$key;value=$value})}}
                }
                $inis.Add([ordered]@{path=$item.FullName;length=$item.Length;sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant();pathKeys=@($keys);redirectedPathsFollowed=$false})
            }
        }catch{$errors.Add("$($item.FullName) : $($_.Exception.Message)")}
    }
}
$mapping=@(foreach($vendor in $vendors){$source=InspectBinary (Join-Path $manifestRoot $vendor.source);if($source.sha256 -ine $vendor.sha256){throw '源payload hash不一致'};[ordered]@{name=$vendor.target;source=$source;targets=@($targets[$vendor.target]);status=$(if($targets[$vendor.target].Count){'候选已有同名目标；待用户确认全部路径'}else{'无同名目标；跳过，不新增'});confirmed=$false}})
$report=[ordered]@{title='真实游戏目录候选映射—待用户确认';readAt=[DateTimeOffset]::Now.ToString('o');readOnly=$true;gameWritten=$false;executablesRun=$false;gameRoot=$root;rootSource='用户原始ww_fps_config.ini PathValue的父目录；尚未手选确认';shipping=(InspectBinary $shipping);directoryCount=$dirs;fileCount=$files;skippedLinks=@($skipped);readErrors=@($errors);mapping=$mapping;reShade=[ordered]@{proxyCandidates=@($proxies);iniFiles=@($inis);mfgAddons=@($addons);compatibility='仅候选文件与版本读取；未验证加载或兼容性'}}
New-Item -ItemType Directory -Path $out -Force | Out-Null
$report | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $out 'real-game-candidate-map.json') -Encoding utf8
$md=[Collections.Generic.List[string]]::new()
$md.Add('# 真实游戏目录候选映射—待用户确认');$md.Add('');$md.Add("读取时间：$($report.readAt)。仅只读；未写游戏、未运行EXE。根路径来自旧INI，尚未由用户手选确认。");$md.Add('');$md.Add("根：$root");$md.Add("Shipping：$shipping；架构=$($report.shipping.architecture)；版本=$($report.shipping.fileVersion)；大小=$($report.shipping.length)；SHA256=$($report.shipping.sha256)");$md.Add('')
foreach($entry in $mapping){$md.Add("## $($entry.name)");$md.Add('');$md.Add("源：$($entry.source.path)");$md.Add("源版本=$($entry.source.fileVersion)；大小=$($entry.source.length)；架构=$($entry.source.architecture)；SHA256=$($entry.source.sha256)");$md.Add('');if($entry.targets.Count){foreach($target in $entry.targets){$md.Add("- 候选：$($target.path)；版本=$($target.fileVersion)；大小=$($target.length)；架构=$($target.architecture)；SHA256=$($target.sha256)")}}else{$md.Add('无同名目标：跳过，不新增。')};$md.Add('')}
$md.Add('现有相同哈希不代表本工具拥有这些文件；没有部署回执时不能据此执行清除。');$md.Add('');$md.Add('## ReShade候选（不是兼容性结论）');$md.Add('');foreach($proxy in $proxies){$md.Add("- $($proxy.path)；版本=$($proxy.fileVersion)；描述=$($proxy.description)；SHA256=$($proxy.sha256)")};if(-not $proxies.Count){$md.Add('没有发现所列代理文件名。')};foreach($ini in $inis){$md.Add("- INI：$($ini.path)，SHA256=$($ini.sha256)");foreach($key in $ini.pathKeys){$md.Add("  [$($key.section)] $($key.key)=$($key.value)")}};foreach($addon in $addons){$md.Add("- 已有addon：$($addon.path)；版本=$($addon.fileVersion)；SHA256=$($addon.sha256)")};$md.Add('');$md.Add("枚举目录=$dirs，文件=$files，跳过链接=$($skipped.Count)，读取错误=$($errors.Count)。未跟随INI重定向路径。");$md | Set-Content -LiteralPath (Join-Path $out 'real-game-candidate-map.md') -Encoding utf8
Write-Output "Read-only candidate report created: $out; vendor names=$($mapping.Count); matched paths=$((@($mapping.targets | ForEach-Object { $_ })).Count); proxies=$($proxies.Count); inis=$($inis.Count); errors=$($errors.Count); linksSkipped=$($skipped.Count)"
