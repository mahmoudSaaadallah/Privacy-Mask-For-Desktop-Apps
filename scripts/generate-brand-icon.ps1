# Regenerate checked-in Windows assets from the WPF vector master.
# Run with Windows PowerShell -STA; this is an asset build, not a runtime step.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
  throw 'Run this script using powershell -STA -File scripts\generate-brand-icon.ps1.'
}

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$repoRoot = Split-Path -Parent $PSScriptRoot
$assetsPath = Join-Path $repoRoot 'src\PrivacyMask.App\Assets'
$reader = [System.Xml.XmlReader]::Create((Join-Path $assetsPath 'Brand.xaml'))
try {
  $resources = [Windows.Markup.XamlReader]::Load($reader)
}
finally {
  $reader.Dispose()
}
$drawing = $resources['PrivacyMaskLogo'].Drawing
$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$frames = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
  $visual = New-Object Windows.Media.DrawingVisual
  $context = $visual.RenderOpen()
  try {
    $scale = $size / 256.0
    $context.PushTransform((New-Object Windows.Media.ScaleTransform($scale, $scale)))
    $context.DrawDrawing($drawing)
    $context.Pop()
  }
  finally {
    $context.Close()
  }
  $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap(
    $size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
  $bitmap.Render($visual)
  $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
  $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
  $buffer = New-Object System.IO.MemoryStream
  try {
    $encoder.Save($buffer)
    $bytes = $buffer.ToArray()
    $frames.Add($bytes)
    if ($size -eq 256) {
      [System.IO.File]::WriteAllBytes((Join-Path $assetsPath 'PrivacyMask.png'), $bytes)
    }
  }
  finally {
    $buffer.Dispose()
  }
}

# ICO directory entries point to PNG-compressed frames, preserving alpha.
$iconStream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($iconStream)
try {
  $writer.Write([UInt16]0)
  $writer.Write([UInt16]1)
  $writer.Write([UInt16]$sizes.Count)
  $offset = 6 + (16 * $sizes.Count)
  for ($index = 0; $index -lt $sizes.Count; $index++) {
    $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frames[$index].Length)
    $writer.Write([UInt32]$offset)
    $offset += $frames[$index].Length
  }
  foreach ($frame in $frames) {
    $writer.Write([byte[]]$frame)
  }
  $writer.Flush()
  [System.IO.File]::WriteAllBytes((Join-Path $assetsPath 'PrivacyMask.ico'), $iconStream.ToArray())
}
finally {
  $writer.Dispose()
  $iconStream.Dispose()
}
Write-Host 'Generated PrivacyMask.png and PrivacyMask.ico from Assets/Brand.xaml.'
