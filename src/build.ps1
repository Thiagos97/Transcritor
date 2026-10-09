# Compila Transcritor.exe com o csc.exe do .NET Framework (vem com o Windows).
# Uso: powershell -ExecutionPolicy Bypass -File C:\Users\Usuario\Documents\Transcritor\src\build.ps1

$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$outDir = Split-Path $src -Parent
$icon = Join-Path $src 'app.ico'

if (-not (Test-Path $icon)) {
    Add-Type -AssemblyName System.Drawing
    $pngs = @()
    foreach ($size in 16, 24, 32, 48, 64, 256) {
        $bmp = New-Object System.Drawing.Bitmap $size, $size
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'
        $s = $size / 64.0

        # fundo: quadrado arredondado azul
        $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
        $r = 14 * $s; $d = 2 * $r; $m = 1 * $s; $w = $size - 2 * $m
        $bg.AddArc($m, $m, $d, $d, 180, 90)
        $bg.AddArc($m + $w - $d, $m, $d, $d, 270, 90)
        $bg.AddArc($m + $w - $d, $m + $w - $d, $d, $d, 0, 90)
        $bg.AddArc($m, $m + $w - $d, $d, $d, 90, 90)
        $bg.CloseFigure()
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 37, 99, 235))), $bg)

        # microfone branco
        $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
        $cap = New-Object System.Drawing.Drawing2D.GraphicsPath
        $cx = 24 * $s; $cy = 10 * $s; $cw = 16 * $s; $ch = 28 * $s
        $cap.AddArc($cx, $cy, $cw, $cw, 180, 180)
        $cap.AddArc($cx, $cy + $ch - $cw, $cw, $cw, 0, 180)
        $cap.CloseFigure()
        $g.FillPath($white, $cap)
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.5, 4 * $s))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        $g.DrawArc($pen, 17 * $s, 18 * $s, 30 * $s, 26 * $s, 0, 180)
        $g.DrawLine($pen, 32 * $s, 44 * $s, 32 * $s, 52 * $s)
        $g.DrawLine($pen, 24 * $s, 53 * $s, 40 * $s, 53 * $s)
        $g.Dispose()

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $pngs += , @($size, $ms.ToArray())
    }

    # .ico com as imagens em PNG dentro
    $fs = [System.IO.File]::Create($icon)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$pngs.Count)
    $offset = 6 + 16 * $pngs.Count
    foreach ($p in $pngs) {
        $dim = if ($p[0] -ge 256) { 0 } else { $p[0] }
        $bw.Write([Byte]$dim); $bw.Write([Byte]$dim); $bw.Write([Byte]0); $bw.Write([Byte]0)
        $bw.Write([UInt16]1); $bw.Write([UInt16]32)
        $bw.Write([UInt32]$p[1].Length); $bw.Write([UInt32]$offset)
        $offset += $p[1].Length
    }
    foreach ($p in $pngs) { $bw.Write($p[1]) }
    $bw.Close()
    Write-Host "Icone criado: $icon"
}

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $outDir 'Transcritor.exe'
& $csc /nologo /target:winexe /optimize+ "/out:$exe" "/win32icon:$icon" `
    "/resource:$(Join-Path $src 'worker.py'),worker.py" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    (Join-Path $src 'Transcritor.cs')
if ($LASTEXITCODE -ne 0) { throw "csc falhou ($LASTEXITCODE)" }
Write-Host "Compilado: $exe"
