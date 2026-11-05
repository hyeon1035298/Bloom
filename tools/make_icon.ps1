# Bloom's icon (src\Bloom\Assets\icon.ico): a cherry blossom, five broad white petals with notched
# tips around a rose centre, on a dark rose-tinted tile — drawn with WPF at every icon size and
# packed as PNG entries. Same drawing as Ui\Theme.MarkVisual.   pwsh tools\make_icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'src\Bloom\Assets\icon.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $v = New-Object System.Windows.Media.DrawingVisual
    $dc = $v.RenderOpen()
    $bg = New-Object System.Windows.Media.LinearGradientBrush ([System.Windows.Media.Color]::FromRgb(0x2E, 0x25, 0x29)), ([System.Windows.Media.Color]::FromRgb(0x12, 0x0E, 0x10)), 90
    $dc.DrawRoundedRectangle($bg, $null, (New-Object System.Windows.Rect 0, 0, $s, $s), $s * 0.23, $s * 0.23)
    $white = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0xF1, 0xEC, 0xEE))
    $rose = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0xFF, 0x7A, 0x9E))
    $cx = $s / 2
    $cy = $s * 0.5273
    # a petal pointing up: narrow base at the centre, broad sides, a V notch in the tip; points in units of the size
    # (invariant numbers for the path syntax)
    $p = { param([double]$x, [double]$y) (($cx + $x * $s), ($cy + $y * $s) | ForEach-Object { ([double]$_).ToString('0.###', [Globalization.CultureInfo]::InvariantCulture) }) -join ',' }
    $path = "M $(& $p 0 -0.0195) C $(& $p 0.0586 -0.043) $(& $p 0.1328 -0.1608) $(& $p 0.1328 -0.2527) " +
        "C $(& $p 0.1328 -0.3292) $(& $p 0.093 -0.3828) $(& $p 0.0586 -0.379) L $(& $p 0 -0.3313) L $(& $p -0.0586 -0.379) " +
        "C $(& $p -0.093 -0.3828) $(& $p -0.1328 -0.3292) $(& $p -0.1328 -0.2527) " +
        "C $(& $p -0.1328 -0.1608) $(& $p -0.0586 -0.043) $(& $p 0 -0.0195) Z"
    $petal = [System.Windows.Media.Geometry]::Parse($path)
    for ($i = 0; $i -lt 5; $i++) {
        $dc.PushTransform((New-Object System.Windows.Media.RotateTransform ($i * 72), $cx, $cy))
        $dc.DrawGeometry($white, $null, $petal)
        $dc.Pop()
    }
    $r = [Math]::Max(1.5, $s * 0.0586)
    $dc.DrawEllipse($rose, $null, (New-Object System.Windows.Point $cx, $cy), $r, $r)
    $dc.Close()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $s, $s, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($v)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    $pngs += , $ms.ToArray()
}
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $b = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
"icon: $out ($((Get-Item $out).Length) bytes, $($sizes.Count) sizes)"
