# 휴대폰 앱(설치형 웹앱) 아이콘을 만든다 — 원본 PNG(256px)에서
#   icon-192.png / icon-512.png        : 투명 배경, 그대로
#   icon-maskable-512.png              : 안드로이드가 원·둥근 사각형으로 잘라 쓰는 것 — 배경색을 채우고 가운데 80% 원 안에 들어가게 줄임
#   apple-touch-icon.png (180)         : 아이폰 홈 화면 — 투명을 못 쓰므로 배경색을 채움
# 예) powershell -File tools\make-app-icons.ps1
#Requires -Version 5.1
param([string]$Png = 'src\ClaudeOffice\Assets\office_building_3d.png',
      [string]$OutDir = 'src\ClaudeOffice\wwwroot\icons',
      [string]$Background = '#2d2750')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile((Resolve-Path $Png))
New-Item -ItemType Directory -Force $OutDir | Out-Null
$attr = New-Object System.Drawing.Imaging.ImageAttributes
$attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)   # 줄이고 늘릴 때 가장자리에 반투명 테두리가 생기지 않게

function Save-Icon([int]$size, [double]$scale, [string]$bg, [string]$name) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
  if ($bg) { $g.Clear([System.Drawing.ColorTranslator]::FromHtml($bg)) } else { $g.Clear([System.Drawing.Color]::Transparent) }
  $w = [int][math]::Round($size * $scale)
  $o = [int][math]::Round(($size - $w) / 2)
  $g.DrawImage($src, (New-Object System.Drawing.Rectangle $o, $o, $w, $w), 0, 0, $src.Width, $src.Height, [System.Drawing.GraphicsUnit]::Pixel, $attr)
  $g.Dispose()
  $path = Join-Path $OutDir $name
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  "saved $path ($size px)"
}

Save-Icon 192 1.0 $null 'icon-192.png'
Save-Icon 512 1.0 $null 'icon-512.png'
# 정사각형 그림의 대각선 절반이 가운데 80% 원(반지름 0.4) 안에 들도록 0.56
Save-Icon 512 0.56 $Background 'icon-maskable-512.png'
Save-Icon 180 0.78 $Background 'apple-touch-icon.png'
$src.Dispose()
