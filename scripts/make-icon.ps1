# Writes assets\app.ico (16-256 px) with the same drawing as Platform\AppIcon.cs:
# a bright amber monitor in front of a dimmed one, under an amber crescent.
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot "..\assets\app.ico"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function Fill-Rounded($g, $brush, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath($brush, $path)
    $path.Dispose()
}

function Draw-Png([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 32.0
    $dim = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 70, 74, 84))
    $frame = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 32, 36, 44))
    $screen = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 196, 64))
    Fill-Rounded $g $dim (13 * $s) (5 * $s) (18 * $s) (13 * $s) (2.5 * $s)
    $arc = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 255, 214, 110), (2 * $s))
    $arc.StartCap = 'Round'; $arc.EndCap = 'Round'
    $g.DrawArc($arc, (3 * $s), (2 * $s), (26 * $s), (22 * $s), 195, 150)
    $arc.Dispose()
    Fill-Rounded $g $frame (1 * $s) (9 * $s) (20 * $s) (15 * $s) (2.5 * $s)
    Fill-Rounded $g $screen (3 * $s) (11 * $s) (16 * $s) (11 * $s) (1.5 * $s)
    $g.FillRectangle($frame, 7 * $s, 24 * $s, 8 * $s, 3 * $s)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

# ICO file: header, one directory entry per size, then the PNG images
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = $sizes | ForEach-Object { ,(Draw-Png $_) }
$file = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($file)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $px = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }   # 0 means 256
    $w.Write([byte]$px); $w.Write([byte]$px); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Resolve-Path (Split-Path $out)).Path + "\app.ico", $file.ToArray())
"Wrote $out ($($file.Length) bytes, sizes $($sizes -join ', '))"
