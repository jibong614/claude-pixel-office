# 휴대폰 앱(설치형 웹앱) 아이콘을 만든다 — 원본 PNG 에서(정사각형이 아니면 비율을 지켜 가운데에)
#   icon-192.png / icon-512.png        : 투명 배경
#   icon-maskable-512.png              : 안드로이드가 원·둥근 사각형으로 잘라 쓰는 것 — 배경색을 채우고 가운데 80% 원 안에 들어가게 줄임
#   apple-touch-icon.png (180)         : 아이폰 홈 화면 — 투명을 못 쓰므로 배경색을 채움
# -Pixel: 픽셀아트 — 점을 정수 배로 그대로 키운다(흐려지지 않게)
# 예) powershell -File tools\make-app-icons.ps1 -Pixel
#Requires -Version 5.1
param([string]$Png = 'src\ClaudeOffice\Assets\office_pixel.png',
      [string]$OutDir = 'src\ClaudeOffice\wwwroot\icons',
      [string]$Background = '#2d2750',
      [switch]$Pixel)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile((Resolve-Path $Png))
New-Item -ItemType Directory -Force $OutDir | Out-Null
$attr = New-Object System.Drawing.Imaging.ImageAttributes
$attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)   # 줄이고 늘릴 때 가장자리에 반투명 테두리가 생기지 않게

# fill: 그림이 차지할 수 있는 칸(크기에 대한 비율). circle: 그 대신 그림의 대각선이 가운데 80% 원 안에 들게
function Save-Icon([int]$size, [double]$fill, [string]$bg, [string]$name, [switch]$circle) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
  if ($bg) { $g.Clear([System.Drawing.ColorTranslator]::FromHtml($bg)) } else { $g.Clear([System.Drawing.Color]::Transparent) }
  $f = if ($circle) { 0.8 * $size / [math]::Sqrt($src.Width * $src.Width + $src.Height * $src.Height) }
       else { [math]::Min($size * $fill / $src.Width, $size * $fill / $src.Height) }
  if ($Pixel -and $f -ge 1) {
    $f = [math]::Floor($f)   # 정수 배: 점 하나가 고르게
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
  } else {
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  }
  $w = [int][math]::Round($src.Width * $f); $h = [int][math]::Round($src.Height * $f)
  $g.DrawImage($src, (New-Object System.Drawing.Rectangle ([int][math]::Floor(($size - $w) / 2)), ([int][math]::Floor(($size - $h) / 2)), $w, $h),
               0, 0, $src.Width, $src.Height, [System.Drawing.GraphicsUnit]::Pixel, $attr)
  $g.Dispose()
  $path = Join-Path $OutDir $name
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  "saved $path ($size px, 그림 ${w}x$h)"
}

Save-Icon 192 0.92 $null 'icon-192.png'
Save-Icon 512 0.92 $null 'icon-512.png'
Save-Icon 512 0 $Background 'icon-maskable-512.png' -circle
Save-Icon 180 0.95 $Background 'apple-touch-icon.png'   # 아이폰이 모서리를 둥글게 잘라도 그림 끝이 남는 만큼
$src.Dispose()
