# Claude Office 창을 화면 밖에 띄워(--offscreen) 창 내용을 캡처하고 닫는다(화면 확인용).
# 작업 중인 화면 앞에 창이 뜨거나 포커스를 뺏지 않는다: 창은 화면 밖(-20000,-20000)·작업표시줄 없음,
# 캡처는 화면을 긁는 대신 PrintWindow(PW_RENDERFULLCONTENT)로 창 내용을 직접 받는다(제목 표시줄·아이콘 포함).
# 임시 데이터 폴더를 써서 평소 설정·키를 건드리지 않는다(그래서 --demo 가 아니면 설정은 기본값으로 보인다).
# 예) powershell -File tools\capture.ps1 -Exe src\ClaudeOffice\bin\Debug\net8.0-windows\ClaudeOffice.exe -Args1 "--demo" -Out C:\temp\shot.png -Shots 3 -GapSec 4
#Requires -Version 5.1
param([string]$Exe, [string]$Args1 = "", [string]$Out, [int]$WaitSec = 6, [int]$Shots = 1, [int]$GapSec = 4)
$ErrorActionPreference = 'Stop'
Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
public static class Cap {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

  // 작업표시줄에 안 나오는 창은 Process.MainWindowHandle 로 못 찾아서 직접 훑는다
  public static IntPtr FindWindow(uint pid) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h) && GetWindowTextLength(h) > 0) { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }

  public static void Shot(IntPtr h, string path) {
    RECT r; GetWindowRect(h, out r);
    int w = r.R - r.L, hh = r.B - r.T;
    using (var bmp = new Bitmap(w, hh)) {
      using (var g = Graphics.FromImage(bmp)) {
        IntPtr hdc = g.GetHdc();
        PrintWindow(h, hdc, 2);   // PW_RENDERFULLCONTENT — WebView2(DirectComposition) 내용까지
        g.ReleaseHdc(hdc);
      }
      bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
  }
}
"@
[Cap]::SetProcessDPIAware() | Out-Null

# 이 프로세스에서 띄우는 앱에만: 임시 데이터 폴더, 화면 밖이어도 그리기를 멈추지 않게
$profileDir = Join-Path $env:TEMP ("ClaudeOffice-capture-" + [Guid]::NewGuid().ToString('N'))
$env:CLAUDEOFFICE_DATA_DIR = $profileDir
$env:WEBVIEW2_USER_DATA_FOLDER = Join-Path $profileDir 'WebView2'
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = '--disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows --disable-renderer-backgrounding'

$argList = (($Args1 + ' --offscreen').Trim())
$p = Start-Process -FilePath $Exe -ArgumentList $argList -PassThru
try {
  $h = [IntPtr]::Zero
  $deadline = (Get-Date).AddSeconds(20)
  while ($h -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300; $h = [Cap]::FindWindow([uint32]$p.Id) }
  if ($h -eq [IntPtr]::Zero) { throw "창을 찾지 못했습니다." }
  Start-Sleep -Seconds $WaitSec
  for ($i = 1; $i -le $Shots; $i++) {
    $path = if ($Shots -eq 1) { $Out } else { $Out -replace '\.png$', "_$i.png" }
    [Cap]::Shot($h, $path)
    "saved $path"
    if ($i -lt $Shots) { Start-Sleep -Seconds $GapSec }
  }
} finally {
  if (-not $p.HasExited) {
    # ✕(WM_CLOSE)는 숨기기라서 먼저 --exit 로 정상 종료, 견본은 WM_CLOSE 로 닫힌다
    Start-Process -FilePath $Exe -ArgumentList '--exit' -Wait -WindowStyle Hidden
    $p.WaitForExit(6000) | Out-Null
    if (-not $p.HasExited -and $h -ne [IntPtr]::Zero) { [Cap]::PostMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; $p.WaitForExit(1500) | Out-Null }   # WM_CLOSE
    if (-not $p.HasExited) { $p.Kill() }
  }
  Start-Sleep -Milliseconds 500
  Remove-Item -Recurse -Force $profileDir -ErrorAction SilentlyContinue
}
