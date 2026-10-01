param([string]$DependencyDirectory,[string]$OutputDirectory='artifacts/tests/native-ci')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
$source=Get-Content release-assets/dynamicmax/source.json -Raw | ConvertFrom-Json
function CheckoutPinned([string]$Url,[string]$Commit,[string]$Path){
    if((Test-Path -LiteralPath $Path) -and @(Get-ChildItem -LiteralPath $Path -Force).Count -gt 0){throw "Dependency destination is not empty: $Path"}
    git init $Path
    if($LASTEXITCODE -ne 0){throw 'Dependency initialization failed'}
    git -C $Path remote add origin $Url
    git -C $Path fetch --depth=1 origin $Commit
    if($LASTEXITCODE -ne 0){throw 'Pinned dependency fetch failed'}
    git -C $Path checkout --detach FETCH_HEAD
    if($LASTEXITCODE -ne 0){throw 'Dependency checkout failed'}
}
if(!$DependencyDirectory){
    $DependencyDirectory=Join-Path $repo ('artifacts/tests/native-deps-'+[guid]::NewGuid().ToString('N'))
    CheckoutPinned 'https://github.com/crosire/reshade.git' $source.dependencyCommits.reshade "$DependencyDirectory/external/reshade"
    CheckoutPinned 'https://github.com/ocornut/imgui.git' $source.dependencyCommits.imgui "$DependencyDirectory/external/reshade/deps/imgui"
    CheckoutPinned 'https://github.com/microsoft/Detours.git' $source.dependencyCommits.detours "$DependencyDirectory/external/Detours"
    $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $vc=Join-Path $vs 'VC/Auxiliary/Build/vcvarsall.bat'
    foreach($path in @($vc,$DependencyDirectory)){if($path -match '["%&|<>^!\r\n]'){throw 'Unsupported build path characters'}}
    $batch="@echo off`r`nsetlocal DisableDelayedExpansion`r`ncall `"$vc`" x64`r`nif errorlevel 1 exit /b 1`r`ncd /d `"$DependencyDirectory/external/Detours/src`"`r`nnmake`r`nexit /b %errorlevel%`r`n"
    $command=Join-Path $DependencyDirectory 'build-detours.cmd';[IO.File]::WriteAllText($command,$batch,[Text.Encoding]::Default)
    & $env:ComSpec /d /c ('"'+$command+'"')
    if($LASTEXITCODE -ne 0){throw 'Detours build failed'}
}
foreach($entry in @(@('external/reshade',$source.dependencyCommits.reshade),@('external/reshade/deps/imgui',$source.dependencyCommits.imgui),@('external/Detours',$source.dependencyCommits.detours))){
    $head=git -C "$DependencyDirectory/$($entry[0])" rev-parse HEAD
    if($LASTEXITCODE -ne 0 -or $head -ne $entry[1]){throw "Dependency pin mismatch: $($entry[0])"}
}
& tools/FpsCore/Build.ps1 -OutputDirectory "$OutputDirectory/fps"
& tools/DynamicController/Build.ps1 -DependencyDirectory $DependencyDirectory -OutputDirectory "$OutputDirectory/dynamicmax"
& tools/DynamicController/Test-MenuDocking.ps1 -DependencyDirectory $DependencyDirectory -OutputDirectory "$OutputDirectory/menu-docking"
& tools/MfgUiIntegration/Test-FrameModes.ps1 -OutputDirectory "$OutputDirectory/frame-modes"
