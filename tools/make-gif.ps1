# 화면 캡처(PNG) 여러 장을 반복 재생하는 GIF 하나로 묶는다 — README 동작 GIF 용(외부 프로그램 없이 Windows 의 WPF GIF 인코더로).
# WPF 인코더는 프레임 간격·반복(NETSCAPE2.0) 정보를 쓰지 않아서, 만든 뒤 GIF 블록을 다시 엮으며 넣는다.
#
# 예) 견본 화면을 0.3초마다 40장 캡처한 뒤
#   powershell -File tools\pagetest.ps1 -Exe <exe> -Args1 "--demo" -StepsFile frames.js -Out C:\temp\gif\f.png -StepMs 300
#   powershell -File tools\make-gif.ps1 -Frames "C:\temp\gif\f_*.png" -Out docs\screenshots\demo.gif -Width 800 -DelayMs 300
#Requires -Version 5.1
param([Parameter(Mandatory)][string]$Frames, [Parameter(Mandatory)][string]$Out, [int]$Width = 800, [int]$DelayMs = 300)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
Add-Type -AssemblyName System.Core
$I = 'System.Windows.Media.Imaging'

# f_1.png, f_2.png ... 를 번호 순서대로
$files = Get-ChildItem $Frames | Sort-Object { [int]([regex]::Match($_.BaseName, '\d+$').Value) }
if (-not $files) { throw "프레임이 없습니다: $Frames" }

$enc = New-Object "$I.GifBitmapEncoder"
foreach ($f in $files) {
  $dec = [System.Windows.Media.Imaging.BitmapDecoder]::Create([Uri]$f.FullName, 'PreservePixelFormat', 'OnLoad')
  $src = $dec.Frames[0]
  $scale = [Math]::Min(1.0, $Width / $src.PixelWidth)
  $small = New-Object "$I.TransformedBitmap" ($src, (New-Object System.Windows.Media.ScaleTransform ($scale, $scale)))
  $pal = New-Object "$I.BitmapPalette" ($small, 256)
  $idx = New-Object "$I.FormatConvertedBitmap" ($small, [System.Windows.Media.PixelFormats]::Indexed8, $pal, 0)
  $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($idx))
}
$ms = New-Object IO.MemoryStream
$enc.Save($ms)
$b = $ms.ToArray()

# ── GIF 블록 다시 엮기: 머리 + 반복 정보 + (프레임마다 간격 정보 + 그림) + 끝 ──
$o = New-Object 'System.Collections.Generic.List[byte]'
$p = 13
if ($b[10] -band 0x80) { $p += 3 * [Math]::Pow(2, ($b[10] -band 7) + 1) }
$o.AddRange([byte[]]$b[0..($p - 1)])
$o.AddRange([byte[]](@(0x21, 0xFF, 0x0B) + [Text.Encoding]::ASCII.GetBytes('NETSCAPE2.0') + @(3, 1, 0, 0, 0)))   # 무한 반복
$delay = [int][Math]::Round($DelayMs / 10)
function Skip-SubBlocks([int]$at) { while ($b[$at] -ne 0) { $at += $b[$at] + 1 }; return $at + 1 }
while ($p -lt $b.Length) {
  switch ($b[$p]) {
    0x21 { $p = Skip-SubBlocks ($p + 2) }   # 원래 있던 확장 블록은 버린다(간격 정보는 새로 넣음)
    0x2C {
      $o.AddRange([byte[]](0x21, 0xF9, 4, 0, ($delay -band 0xFF), ($delay -shr 8), 0, 0))
      $start = $p
      $p += 10
      if ($b[$start + 9] -band 0x80) { $p += 3 * [Math]::Pow(2, ($b[$start + 9] -band 7) + 1) }
      $p = Skip-SubBlocks ($p + 1)           # LZW 최소 코드 크기 1바이트 뒤 데이터
      $o.AddRange([byte[]]$b[$start..($p - 1)])
    }
    0x3B { $p = $b.Length }
    default { throw "GIF 구조를 읽지 못했습니다(위치 $p)" }
  }
}
$o.Add(0x3B)
$full = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path (Get-Location) $Out }
[IO.File]::WriteAllBytes($full, $o.ToArray())
"$($files.Count)장 → $full ($([Math]::Round($o.Count / 1MB, 2)) MB)"
