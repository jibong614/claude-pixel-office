# PNG 한 장으로 여러 크기가 든 Windows 아이콘(.ico)을 만든다. 정사각형이 아니면 비율을 지켜 가운데에 넣는다.
# 크기마다 PNG로 담는다(Windows Vista 이후 지원) — 작업표시줄·탐색기·제목 표시줄이 알맞은 크기를 골라 쓴다.
# -Pixel: 픽셀아트 — 정수 배로 키울 수 있는 크기는 점을 그대로 키우고(흐려지지 않게), 원본보다 작은 크기만 부드럽게 줄인다.
# 예) powershell -File tools\make-icon.ps1 -Png src\ClaudeOffice\Assets\office_pixel.png -Out src\ClaudeOffice\Assets\office.ico -Pixel
#Requires -Version 5.1
param([string]$Png, [string]$Out, [int[]]$Sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256), [switch]$Pixel)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile((Resolve-Path $Png))
$attr = New-Object System.Drawing.Imaging.ImageAttributes
$attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)   # 줄일 때 가장자리에 반투명 테두리가 생기지 않게

$frames = @()
foreach ($s in $Sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
  $g.Clear([System.Drawing.Color]::Transparent)
  $fit = [math]::Min($s / $src.Width, $s / $src.Height)
  if ($Pixel -and $fit -ge 1) {
    $k = [math]::Floor($fit)   # 정수 배: 점 하나가 k×k 로 고르게
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $dw = $src.Width * $k; $dh = $src.Height * $k
  } else {
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $dw = [int][math]::Round($src.Width * $fit); $dh = [int][math]::Round($src.Height * $fit)
  }
  $g.DrawImage($src, (New-Object System.Drawing.Rectangle ([int][math]::Floor(($s - $dw) / 2)), ([int][math]::Floor(($s - $dh) / 2)), $dw, $dh),
               0, 0, $src.Width, $src.Height, [System.Drawing.GraphicsUnit]::Pixel, $attr)
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  $frames += , @($s, $ms.ToArray())
}
$src.Dispose()

# ICONDIR(6바이트) + ICONDIRENTRY(16바이트 × 개수) + 그림 데이터
$ico = [System.IO.MemoryStream]::new()   # $out 이라 하면 매개변수 $Out(문자열)과 같은 변수가 된다(대소문자 무시)
$w = [System.IO.BinaryWriter]::new($ico)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
  $side = if ($f[0] -ge 256) { 0 } else { $f[0] }   # 256은 0으로 적는 것이 규칙
  $w.Write([byte]$side); $w.Write([byte]$side); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$f[1].Length); $w.Write([uint32]$offset)
  $offset += $f[1].Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes($Out, $ico.ToArray())
"saved $Out ($($frames.Count) sizes: $($Sizes -join ', '), $($ico.Length) bytes)"
