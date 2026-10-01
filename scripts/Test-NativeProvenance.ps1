$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
foreach($item in @(@('components/PROVENANCE.json','components/ww_plugin_base.dll'),@('release-assets/dynamicmax/source.json','release-assets/dynamicmax/wuwa-dynamicmax.addon64'))){
    $metadata=Get-Content (Join-Path $repo $item[0]) -Raw | ConvertFrom-Json
    $binary=Join-Path $repo $item[1]
    $length=if($metadata.PSObject.Properties['length']){$metadata.length}else{$metadata.size}
    if((Get-FileHash -LiteralPath $binary).Hash -ne $metadata.sha256 -or (Get-Item -LiteralPath $binary).Length -ne $length){throw "Binary provenance mismatch: $binary"}
    if(!$metadata.sourceFiles){throw "Native source inventory missing: $($item[0])"}
    foreach($file in $metadata.sourceFiles){
        if($file.path -match '\.\.' -or [IO.Path]::IsPathRooted($file.path)){throw 'Unsafe native source path'}
        $text=[IO.File]::ReadAllText((Join-Path $repo $file.path)).Replace("`r`n","`n")
        $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text)))
        if($hash -ne $file.sha256){throw "Native source changed without rebuilt binary inventory: $($file.path)"}
    }
}
Write-Output 'Native binary identities and recorded source files verified.'
