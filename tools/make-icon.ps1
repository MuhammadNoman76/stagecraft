# Renders Stagecraft's icons from the vector logo in src\Stagecraft\Assets\Logo.xaml:
#   src\Stagecraft\Assets\Stagecraft.ico       app icon (16-256 px; the 16-24 px frames use the bolder Logo.Small)
#   src\Stagecraft\Assets\StagecraftDeck.ico   icon for .stagecraft files (a page with the mark)
#   docs\icon.png                              256 px preview
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root "src\Stagecraft\Assets"
$xaml = [System.IO.File]::ReadAllText((Join-Path $assets "Logo.xaml"))
$logo = [System.Windows.Markup.XamlReader]::Parse($xaml)

function Render-Png([System.Windows.Media.Drawing]$drawing, [int]$size) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $scale = $size / 256.0
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform $scale, $scale))
    $dc.DrawDrawing($drawing)
    $dc.Pop()
    $dc.Close()
    [System.Windows.Media.RenderOptions]::SetEdgeMode($visual, [System.Windows.Media.EdgeMode]::Unspecified)
    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object System.IO.MemoryStream
    $encoder.Save($ms)
    return , $ms.ToArray()
}

# Sizes up to 48 px as classic 32-bit bitmaps (best compatibility), larger ones as PNG.
function Get-IcoFrame([byte[]]$png, [int]$size) {
    if ($size -gt 48) { return , $png }
    $bmp = New-Object System.Drawing.Bitmap (New-Object System.IO.MemoryStream (, $png))
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([UInt32]40); $w.Write([Int32]$size); $w.Write([Int32]($size * 2))
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]0)
    $w.Write([UInt32]0); $w.Write([Int32]0); $w.Write([Int32]0); $w.Write([UInt32]0); $w.Write([UInt32]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $rowBytes = [int]([Math]::Ceiling($size / 32.0) * 4)
    $w.Write((New-Object byte[] ($rowBytes * $size)))
    $w.Flush()
    $bmp.Dispose()
    return , $ms.ToArray()
}

function Write-Ico([string]$path, [scriptblock]$drawingFor) {
    $sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
    $frames = @()
    foreach ($s in $sizes) {
        $png = Render-Png (& $drawingFor $s) $s
        $frames += , @{ Size = $s; Bytes = (Get-IcoFrame $png $s) }
    }
    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $out
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) {
        $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([UInt16]1); $w.Write([UInt16]32)
        $w.Write([UInt32]$f.Bytes.Length); $w.Write([UInt32]$offset)
        $offset += $f.Bytes.Length
    }
    foreach ($f in $frames) { $w.Write([byte[]]$f.Bytes) }
    $w.Flush()
    [System.IO.File]::WriteAllBytes($path, $out.ToArray())
    Write-Host "Icon written: $path ($($out.Length) bytes)"
}

$mark = $logo["Logo.Mark"].Drawing
$small = $logo["Logo.Small"].Drawing

# App icon
Write-Ico (Join-Path $assets "Stagecraft.ico") { param($s) if ($s -le 24) { $small } else { $mark } }

# Deck file icon: a landscape slide page with the mark in its lower right corner.
function New-DeckDrawing([System.Windows.Media.Drawing]$inner, [bool]$small) {
    $group = New-Object System.Windows.Media.DrawingGroup
    $group.Children.Add((New-Object System.Windows.Media.GeometryDrawing ([System.Windows.Media.Brushes]::Transparent), $null, ([System.Windows.Media.Geometry]::Parse("M 0,0 H 256 V 256 H 0 Z"))))
    $pageGeo = [System.Windows.Media.Geometry]::Parse("M 30,40 H 226 C 237,40 246,49 246,60 V 176 C 246,187 237,196 226,196 H 30 C 19,196 10,187 10,176 V 60 C 10,49 19,40 30,40 Z")
    $stroke = if ($small) { 12 } else { 6 }
    $pen = New-Object System.Windows.Media.Pen ((New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0x8A, 0x8A, 0x8A)))), $stroke
    $pen.LineJoin = [System.Windows.Media.PenLineJoin]::Round
    $group.Children.Add((New-Object System.Windows.Media.GeometryDrawing ([System.Windows.Media.Brushes]::White), $pen, $pageGeo))
    if (-not $small) {
        $line = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0xC9, 0xC9, 0xD2))
        $group.Children.Add((New-Object System.Windows.Media.GeometryDrawing $line, $null, ([System.Windows.Media.Geometry]::Parse("M 36,70 H 150 V 86 H 36 Z"))))
        $group.Children.Add((New-Object System.Windows.Media.GeometryDrawing $line, $null, ([System.Windows.Media.Geometry]::Parse("M 36,100 H 120 V 110 H 36 Z"))))
        $group.Children.Add((New-Object System.Windows.Media.GeometryDrawing $line, $null, ([System.Windows.Media.Geometry]::Parse("M 36,122 H 104 V 132 H 36 Z"))))
    }
    $markGroup = New-Object System.Windows.Media.DrawingGroup
    $k = if ($small) { 0.62 } else { 0.5 }
    $transform = New-Object System.Windows.Media.TransformGroup
    $transform.Children.Add((New-Object System.Windows.Media.ScaleTransform $k, $k))
    $transform.Children.Add((New-Object System.Windows.Media.TranslateTransform (256 - 256 * $k), (256 - 256 * $k)))
    $markGroup.Transform = $transform
    $markGroup.Children.Add($inner)
    $group.Children.Add($markGroup)
    return $group
}
Write-Ico (Join-Path $assets "StagecraftDeck.ico") { param($s) if ($s -le 24) { New-DeckDrawing $small $true } else { New-DeckDrawing $mark $false } }

# README preview
$docs = Join-Path $root "docs"
New-Item -ItemType Directory -Force $docs | Out-Null
[System.IO.File]::WriteAllBytes((Join-Path $docs "icon.png"), (Render-Png $mark 256))
Write-Host "Preview written: docs\icon.png"
