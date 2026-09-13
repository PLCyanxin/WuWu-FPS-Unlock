# Format conversion of the original user image only; no generated artwork.
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class IconResourceLifetime { [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon); }'
$taskSource=[Drawing.Bitmap]::FromFile((Join-Path $taskRoot 'src\WuWaFpsUnlock\Assets\Icon.original.jpg'))
$taskBitmap=[Drawing.Bitmap]::new($taskSource,64,64)
$taskHandle=$taskBitmap.GetHicon()
$taskIcon=[Drawing.Icon]::FromHandle($taskHandle)
$taskStream=[IO.File]::Create((Join-Path $taskRoot 'src\WuWaFpsUnlock\Assets\App.ico'))
try{$taskIcon.Save($taskStream)}finally{$taskStream.Dispose();$taskIcon.Dispose();[IconResourceLifetime]::DestroyIcon($taskHandle)|Out-Null;$taskBitmap.Dispose();$taskSource.Dispose()}
Add-Type -AssemblyName PresentationCore
$taskDecoded=[Windows.Media.Imaging.BitmapDecoder]::Create([Uri](Join-Path $taskRoot 'src\WuWaFpsUnlock\Assets\App.ico'),[Windows.Media.Imaging.BitmapCreateOptions]::None,[Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
Write-Output "WPF decoded user icon: $($taskDecoded.Frames[0].PixelWidth)x$($taskDecoded.Frames[0].PixelHeight)"
