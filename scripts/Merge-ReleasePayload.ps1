param([Parameter(Mandatory)][string]$CheckoutPayload,[Parameter(Mandatory)][string]$DestinationPayload)
$ErrorActionPreference='Stop'
$current=(Resolve-Path -LiteralPath $CheckoutPayload).Path
$target=(Resolve-Path -LiteralPath $DestinationPayload).Path
$manifest=Get-Content -LiteralPath (Join-Path $current 'manifest.json') -Raw | ConvertFrom-Json
function Under([string]$Root,[string]$Relative){
    if([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':')){throw 'Invalid material source path'}
    $path=[IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if(!$path.StartsWith($Root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Material path escapes package'}
    $probe=$path
    while($probe -and $probe.StartsWith($Root,[StringComparison]::OrdinalIgnoreCase)){
        if((Test-Path -LiteralPath $probe) -and ((Get-Item -LiteralPath $probe -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked build material refused'}
        $probe=Split-Path $probe -Parent
    }
    return $path
}
$materials=@($manifest.files | Where-Object source -ne 'files/addon/renodx-mfgunlock.addon64')+
    @([pscustomobject]@{source=$manifest.reShade.localSetupPath;sha256=$manifest.reShade.setupSha256;size=$null})
foreach($file in $materials){
    $source=Under $current $file.source;$destination=Under $target $file.source
    if(Test-Path -LiteralPath $source -PathType Leaf){
        if((Get-FileHash -LiteralPath $source).Hash -ne $file.sha256){throw "Checkout material differs from source inventory: $($file.source)"}
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
    if(!(Test-Path -LiteralPath $destination -PathType Leaf) -or (Get-FileHash -LiteralPath $destination).Hash -ne $file.sha256 -or
       ($file.size -and (Get-Item -LiteralPath $destination).Length -ne $file.size)){throw "Pinned baseline does not supply checkout material: $($file.source)"}
}
# Only runtime bytes may be inherited. Deployment policy comes from this checkout.
foreach($name in @('manifest.json','source-manifest.json','payload-map.json')){
    Copy-Item -LiteralPath (Join-Path $current $name) -Destination (Join-Path $target $name) -Force
}
Write-Output 'Release deployment policy copied from checkout; inherited runtime identities verified.'
