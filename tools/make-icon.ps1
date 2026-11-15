# Genera src\WindowsBar\Resources\WindowsBar.ico (varias resoluciones, entradas PNG).
# Diseño: cuadrado redondeado azul con una "barra de tareas" blanca y tres iconos.
# Uso: powershell -ExecutionPolicy Bypass -File .\tools\make-icon.ps1

Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$out  = Join-Path $root 'src\WindowsBar\Resources\WindowsBar.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function DrawIcon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Fondo: degradado azul
    $m = [Math]::Max(1, $s * 0.04)
    $bg = RoundedRect $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.22)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $s, $s), ([System.Drawing.Color]::FromArgb(255, 0x5F, 0xD2, 0xFF)), ([System.Drawing.Color]::FromArgb(255, 0x1A, 0x6F, 0xD8))
    $g.FillPath($brush, $bg)

    # Barra de tareas blanca abajo
    $bh = $s * 0.24
    $bar = RoundedRect ($s * 0.14) ($s * 0.62) ($s * 0.72) $bh ($s * 0.06)
    $g.FillPath([System.Drawing.Brushes]::White, $bar)

    # Tres "apps" en la barra (la del centro, activa, en azul)
    $q = $s * 0.11
    $cy = $s * 0.62 + ($bh - $q) / 2
    $blue = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x1A, 0x8F, 0xE8))
    $grey = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x9A, 0xA6, 0xB2))
    foreach ($i in -1, 0, 1) {
        $cx = $s / 2 + $i * $s * 0.17 - $q / 2
        $b = if ($i -eq 0) { $blue } else { $grey }
        $g.FillPath($b, (RoundedRect $cx $cy $q $q ($q * 0.25)))
    }

    $g.Dispose()
    return $bmp
}

# Entrada DIB (BMP 32 bpp + máscara AND), la más compatible para tamaños pequeños.
function DibEntry([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([uint32]40); $bw.Write([int32]$s); $bw.Write([int32]($s * 2))
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
    $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
    $bw.Write([uint32]($s * $s * 4 + $maskStride * $s))
    $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $bw.Write((New-Object byte[] ($maskStride * $s)))  # máscara vacía: la transparencia va en el alfa
    $bw.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = DrawIcon $s
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        , $ms.ToArray()
    } else {
        DibEntry $bmp
    }
}

# Contenedor ICO: cabecera + directorio + imágenes
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
Write-Host "Icono generado: $out"
