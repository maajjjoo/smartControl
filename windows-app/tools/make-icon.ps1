<#
.SYNOPSIS
    Genera src/SmartControl.Desktop/Assets/SmartControl.ico: el icono de la app (un reloj envía una
    señal a la pantalla del computador), la misma figura que watch-app/.../drawable/ic_launcher.xml.

.DESCRIPTION
    Dibuja la figura con System.Drawing a 16, 24, 32, 48, 64 y 256 px y la guarda como .ico. Solo hace falta volver a ejecutarlo si cambia el diseño.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$out = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\SmartControl.Desktop\Assets\SmartControl.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

$wine = [Drawing.Color]::FromArgb(0x8E, 0x1C, 0x3A)
$white = [Drawing.Color]::White

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object Drawing.Bitmap $size, $size
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)
    $g.ScaleTransform($size / 120.0, $size / 120.0)
    $fill = New-Object Drawing.SolidBrush $white
    $g.FillEllipse((New-Object Drawing.SolidBrush $wine), 0, 0, 120, 120)
    # La figura, un 15 % más grande alrededor del centro (en el reloj no hace falta: su lanzador ya amplía el icono).
    $g.TranslateTransform(60, 61)
    $g.ScaleTransform(1.15, 1.15)
    $g.TranslateTransform(-60, -61)
    # Reloj: correas y caja.
    $g.FillPath($fill, (New-RoundRect 22 38 10 7 2))
    $g.FillPath($fill, (New-RoundRect 22 75 10 7 2))
    $g.DrawPath((New-Object Drawing.Pen $white, 5), (New-RoundRect 17 44 20 32 7))
    # Señal.
    $pen = New-Object Drawing.Pen $white, 4
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawArc($pen, 32, 48, 24, 24, -48, 96)
    $faint = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(166, 255, 255, 255)), 4
    $faint.StartCap = 'Round'; $faint.EndCap = 'Round'
    $g.DrawArc($faint, 32, 41, 38, 38, -48, 96)
    # Pantalla con pie y "reproducir".
    $g.FillPath($fill, (New-RoundRect 60 40 44 30 4))
    $g.FillRectangle($fill, 78, 69, 8, 10)
    $g.FillPath($fill, (New-RoundRect 70 79 24 5 2.5))
    $g.FillPolygon((New-Object Drawing.SolidBrush $wine), [Drawing.PointF[]]@(
        (New-Object Drawing.PointF 76, 48), (New-Object Drawing.PointF 76, 62), (New-Object Drawing.PointF 88, 55)))
    $g.Dispose()
    $bmp
}

# Imagen de una entrada del .ico: PNG para 256 px y mapa de bits clásico (BGRA de abajo arriba más
# máscara AND vacía) para el resto, que es lo que leen todos los componentes de Windows.
function ConvertTo-IconEntry([Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $ms = New-Object IO.MemoryStream
    if ($size -ge 256) {
        $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
        return , $ms.ToArray()
    }
    $rect = New-Object Drawing.Rectangle 0, 0, $size, $size
    $data = $bmp.LockBits($rect, 'ReadOnly', [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($data.Stride * $size)
    [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)
    $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)
    $w = New-Object IO.BinaryWriter $ms
    $w.Write([uint32]40); $w.Write([int32]$size); $w.Write([int32]($size * 2))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
    $w.Write([uint32]($size * $size * 4 + $maskRow * $size))
    $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $size - 1; $y -ge 0; $y--) { $w.Write($pixels, $y * $data.Stride, $size * 4) }
    $w.Write((New-Object byte[] ($maskRow * $size)))
    $w.Flush()
    , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($s in $sizes) { $bmp = New-IconBitmap $s; $images.Add([byte[]](ConvertTo-IconEntry $bmp)); $bmp.Dispose() }
$file = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $file
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[IO.File]::WriteAllBytes($out, $file.ToArray())
$out
