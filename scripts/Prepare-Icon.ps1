# Convert the user's original colored avatar to multi-resolution 32-bit ICO. No artwork generation.
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Drawing
$taskSource=[Drawing.Bitmap]::FromFile((Join-Path $taskRoot 'src\WuWaFpsUnlock\Assets\Icon.transparent.png'))
$taskFrames=[Collections.Generic.List[object]]::new()
try {
 foreach($taskSize in @(16,24,32,48,64,128,256)){
  $taskBitmap=[Drawing.Bitmap]::new($taskSize,$taskSize,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $taskGraphics=[Drawing.Graphics]::FromImage($taskBitmap)
  $taskMemory=[IO.MemoryStream]::new()
  try {
   $taskGraphics.Clear([Drawing.Color]::Transparent)
   $taskGraphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
   $taskGraphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
   $taskGraphics.DrawImage($taskSource,0,0,$taskSize,$taskSize)
   $taskBitmap.Save($taskMemory,[Drawing.Imaging.ImageFormat]::Png)
   $taskFrames.Add(@{Size=$taskSize;Bytes=$taskMemory.ToArray()})
  } finally {$taskGraphics.Dispose();$taskBitmap.Dispose();$taskMemory.Dispose()}
 }
 $taskStream=[IO.File]::Create((Join-Path $taskRoot 'src\WuWaFpsUnlock\Assets\App.ico'))
 $taskWriter=[IO.BinaryWriter]::new($taskStream)
 try {
  $taskWriter.Write([uint16]0);$taskWriter.Write([uint16]1);$taskWriter.Write([uint16]$taskFrames.Count)
  $taskOffset=6+16*$taskFrames.Count
  foreach($taskFrame in $taskFrames){
   $taskDimension=if($taskFrame.Size -eq 256){0}else{$taskFrame.Size}
   $taskWriter.Write([byte]$taskDimension);$taskWriter.Write([byte]$taskDimension)
   $taskWriter.Write([byte]0);$taskWriter.Write([byte]0)
   $taskWriter.Write([uint16]1);$taskWriter.Write([uint16]32)
   $taskWriter.Write([uint32]$taskFrame.Bytes.Length);$taskWriter.Write([uint32]$taskOffset)
   $taskOffset+=$taskFrame.Bytes.Length
  }
  foreach($taskFrame in $taskFrames){$taskWriter.Write([byte[]]$taskFrame.Bytes)}
 } finally {$taskWriter.Dispose();$taskStream.Dispose()}
} finally {$taskSource.Dispose()}
Add-Type -AssemblyName PresentationCore
$taskDecoded=[Windows.Media.Imaging.BitmapDecoder]::Create([Uri](Join-Path $taskRoot 'src\WuWaFpsUnlock\Assets\App.ico'),[Windows.Media.Imaging.BitmapCreateOptions]::None,[Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
foreach($taskFrame in $taskDecoded.Frames){Write-Output "ICO frame $($taskFrame.PixelWidth)x$($taskFrame.PixelHeight) $($taskFrame.Format)"}
$taskCheck=[Drawing.Icon]::new((Join-Path $taskRoot 'src\WuWaFpsUnlock\Assets\App.ico'),64,64)
$taskCheckBitmap=$taskCheck.ToBitmap();$taskColorCount=0
for($taskY=0;$taskY -lt 64;$taskY++){for($taskX=0;$taskX -lt 64;$taskX++){$taskPixel=$taskCheckBitmap.GetPixel($taskX,$taskY);if([Math]::Abs([int]$taskPixel.R-[int]$taskPixel.G) -gt 15 -or [Math]::Abs([int]$taskPixel.G-[int]$taskPixel.B) -gt 15){$taskColorCount++}}}
$taskCheckBitmap.Save((Join-Path $taskRoot 'artifacts\v0.1\icon-color-preview.png'),[Drawing.Imaging.ImageFormat]::Png)
$taskCheckBitmap.Dispose();$taskCheck.Dispose()
if($taskColorCount -lt 1000){throw 'Icon lost original color'}
Write-Output "PASS colored pixels=$taskColorCount/4096; original avatar format conversion only"

