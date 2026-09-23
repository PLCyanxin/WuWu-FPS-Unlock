param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$Version
)
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+(?:RC\d*)?$'){throw 'Use a three-part product version, optionally followed by RC or RC<number>.'}
$root=(Resolve-Path -LiteralPath $PackageDirectory).Path
$required=@('更新.exe','update-payload/WuWaFpsUnlock.exe','update-payload/components/fps/ww_plugin_base.dll','update-payload/components/PROVENANCE.json','update-payload/payload/files/addon/renodx-mfgunlock.addon64','update-payload/payload/addon-source.json')
foreach($name in $required){if(!(Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf)){throw "Missing required package file: $name"}}
$files=@(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object Name -ne 'update-manifest.json' | Sort-Object FullName | ForEach-Object {
    if(($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "Reparse point: $($_.FullName)"}
    [ordered]@{path=[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/');size=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
[ordered]@{protocolVersion=1;productId='WuWaFpsUnlock';version=$Version;updater='更新.exe';files=$files} |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'update-manifest.json') -Encoding utf8
