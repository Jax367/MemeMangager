$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'
$source = [System.Drawing.Image]::FromFile((Join-Path $assetRoot 'app-icon-final.png'))
# Normalize the exported icon canvas, without modifying the approved source artwork.
# The source has a wide transparent border that made the taskbar mark appear undersized.
$trimX = [int][Math]::Floor($source.Width * 0.055)
$trimY = [int][Math]::Floor($source.Height * 0.055)
$sourceArea = New-Object System.Drawing.Rectangle($trimX,$trimY,($source.Width-2*$trimX),($source.Height-2*$trimY))
$frames = @()
try {
 foreach ($size in @(16,24,32,48,64,128,256)) {
  $bitmap = New-Object System.Drawing.Bitmap($size,$size)
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $stream = New-Object System.IO.MemoryStream
  try {
   $graphics.Clear([System.Drawing.Color]::Transparent)
   $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
   $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
   $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
   $scale = [Math]::Min($size / $sourceArea.Width,$size / $sourceArea.Height)
   $width = [int][Math]::Round($sourceArea.Width * $scale)
   $height = [int][Math]::Round($sourceArea.Height * $scale)
   $targetArea = New-Object System.Drawing.Rectangle([int](($size-$width)/2),[int](($size-$height)/2),$width,$height)
   $graphics.DrawImage($source,$targetArea,$sourceArea,[System.Drawing.GraphicsUnit]::Pixel)
   $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
   $bytes = $stream.ToArray()
   $frames += @{Size=$size;Bytes=$bytes}
   if ($size -eq 32 -or $size -eq 64) { [IO.File]::WriteAllBytes((Join-Path $assetRoot "app-icon-$size.png"),$bytes) }
  } finally { $stream.Dispose();$graphics.Dispose();$bitmap.Dispose() }
 }
 $file = [IO.File]::Create((Join-Path $assetRoot 'app-icon.ico'))
 $writer = New-Object System.IO.BinaryWriter($file)
 try {
  $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
  $offset = 6 + 16 * $frames.Count
  foreach ($frame in $frames) {
   $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
   $writer.Write([byte]$dimension);$writer.Write([byte]$dimension)
   $writer.Write([byte]0);$writer.Write([byte]0)
   $writer.Write([uint16]1);$writer.Write([uint16]32)
   $writer.Write([uint32]$frame.Bytes.Length);$writer.Write([uint32]$offset)
   $offset += $frame.Bytes.Length
  }
  foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
 } finally { $writer.Dispose();$file.Dispose() }
} finally { $source.Dispose() }
Write-Host '图标已生成：16 / 24 / 32 / 48 / 64 / 128 / 256 px'
