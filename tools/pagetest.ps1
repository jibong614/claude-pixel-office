# Claude Office 창을 띄워 화면(WebView2) 안에서 JS 단계를 차례로 실행하고, 단계마다 결과를 출력하고 화면을 캡처한다.
# 실제 마우스·키보드를 쓰지 않아 다른 작업을 방해하지 않는다(버튼 클릭은 JS의 element.click()으로).
# 앱은 --offscreen 으로 띄운다: 창이 화면 밖에 포커스·작업표시줄 없이 뜬다(캡처는 브라우저가 직접 그려 줌).
# 임시 WebView2 데이터 폴더를 쓰므로 평소 쓰는 화면 설정(localStorage)을 건드리지 않는다.
#
# 예) powershell -File tools\pagetest.ps1 -Exe src\ClaudeOffice\bin\Debug\net8.0-windows\ClaudeOffice.exe `
#       -Args1 "--demo --select=settings" -StepsFile C:\temp\steps.js -Out C:\temp\t.png
#   steps.js : 단계마다 JS 식 하나, 단계 사이는 '---' 만 있는 줄. 식의 값(문자열 등)이 출력되고 t_1.png, t_2.png ... 로 캡처된다.
#   화면 스크립트의 최상위 let/const(data, pickMode 등)도 그대로 읽을 수 있다.
#   주의: 단계에서 window.R·P 처럼 화면의 그리기 함수와 같은 이름을 만들면 그림이 사라진다(REP 등 겹치지 않는 이름).
#   -DataDir <폴더> : 임시 폴더 대신 이 폴더를 앱 데이터 폴더로(실행이 끝나도 지우지 않음 — 두 번에 나눠 시험할 때)
#   -Mobile         : 휴대폰 화면 흉내(390×844, 배율 3, 터치)
#   단계가 "@cdp 메서드 {JSON}" 이면 JS 대신 브라우저(CDP)에 직접 묻는다(캡처 없음). 예) @cdp Page.getInstallabilityErrors {}
#   단계가 "@sleep 밀리초" 면 페이지 밖에서 기다린다 — location.href 로 주소를 옮긴 다음 단계로 꼭 넣을 것(페이지 안에서 기다리면 응답이 사라져 멈춤)
#Requires -Version 5.1
param([string]$Exe, [string]$Args1 = "", [string]$StepsFile, [string]$Out, [int]$StepMs = 700, [int]$Port = 9333,
      [string]$DataDir = "", [switch]$Mobile)
$ErrorActionPreference = 'Stop'

# @( ) 로 감싸야 단계가 하나뿐일 때도 배열이 된다(아니면 문자열이 되어 $steps[0] 이 첫 글자가 됨)
$steps = @(((Get-Content -Raw -Encoding UTF8 $StepsFile) -split '(?m)^---\s*$') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$keepProfile = [bool]$DataDir
$profileDir = if ($DataDir) { $DataDir } else { Join-Path $env:TEMP ("ClaudeOffice-pagetest-" + [Guid]::NewGuid().ToString('N')) }

# 이 프로세스에서 띄우는 앱에만 적용된다 — 화면 설정·휴대폰 보기 키·훅 기록 모두 이 폴더로.
# 창은 화면 밖에 포커스 없이 뜨므로(--offscreen) 그리기를 멈추지 않게 가림 판정도 끈다
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$Port --disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows --disable-renderer-backgrounding"
$env:WEBVIEW2_USER_DATA_FOLDER = Join-Path $profileDir 'WebView2'
$env:CLAUDEOFFICE_DATA_DIR = $profileDir

# 작업 중인 화면 앞에 창이 불쑥 뜨지 않게: 화면 밖·포커스 없음·작업표시줄 없음
$p = Start-Process -FilePath $Exe -ArgumentList (($Args1 + ' --offscreen').Trim()) -PassThru
$ws = $null
try {
  # 화면(office.local) 페이지가 디버깅 목록에 뜰 때까지
  $target = $null
  $deadline = (Get-Date).AddSeconds(25)
  while (-not $target -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 400
    try { $target = Invoke-RestMethod "http://127.0.0.1:$Port/json/list" | Where-Object { $_.type -eq 'page' -and $_.url -like '*office.local*' } | Select-Object -First 1 }
    catch { }
  }
  if (-not $target) { throw "화면 페이지를 찾지 못했습니다(포트 $Port). 같은 데이터 폴더를 쓰는 Claude Office가 이미 떠 있으면 디버깅 포트가 열리지 않습니다." }
  Start-Sleep -Milliseconds 1500   # 첫 스냅샷·그리기가 끝나도록

  $ws = New-Object System.Net.WebSockets.ClientWebSocket
  $ws.ConnectAsync([Uri]$target.webSocketDebuggerUrl, [Threading.CancellationToken]::None).Wait()
  $script:nextId = 0

  # 요청 하나를 보내고 같은 id의 응답 원문(JSON 문자열)을 돌려준다. 캡처 응답은 수 MB라 ConvertFrom-Json(5.1은 2MB 제한)을 거치지 않는다.
  function Invoke-Cdp([string]$method, [hashtable]$params) {
    $script:nextId++
    $id = $script:nextId
    $json = @{ id = $id; method = $method; params = $params } | ConvertTo-Json -Depth 10 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $ws.SendAsync([ArraySegment[byte]]::new($bytes), [Net.WebSockets.WebSocketMessageType]::Text, $true, [Threading.CancellationToken]::None).Wait()
    $buf = New-Object byte[] 65536
    while ($true) {
      $ms = New-Object IO.MemoryStream
      do {
        $task = $ws.ReceiveAsync([ArraySegment[byte]]::new($buf), [Threading.CancellationToken]::None)
        # 페이지가 이동하는 도중 등에 응답이 안 오면 멈춰 있지 말고 끝낸다(아래 finally 가 앱·임시 폴더를 정리)
        if (-not $task.Wait(60000)) { throw "브라우저 응답 없음(60초): $method — 페이지 이동 직후라면 그 사이에 '@sleep 밀리초' 단계를 넣을 것" }
        $r = $task.Result
        $ms.Write($buf, 0, $r.Count)
      } while (-not $r.EndOfMessage)
      $text = [Text.Encoding]::UTF8.GetString($ms.ToArray())
      if ($text -match '^\{"id":(\d+)' -and [int]$Matches[1] -eq $id) { return $text }
    }
  }

  if ($Mobile) {
    Invoke-Cdp 'Emulation.setDeviceMetricsOverride' @{ width = 390; height = 844; deviceScaleFactor = 3; mobile = $true } | Out-Null
    Invoke-Cdp 'Emulation.setTouchEmulationEnabled' @{ enabled = $true; maxTouchPoints = 5 } | Out-Null
    Start-Sleep -Milliseconds 600
  }

  for ($i = 1; $i -le $steps.Count; $i++) {
    $step = $steps[$i - 1]
    if ($step -match '^@sleep\s+(\d+)$') {
      # 페이지 밖에서 기다리기 — 주소를 옮긴 직후에는 페이지 안에서 기다리면(Promise) 응답이 사라질 수 있다
      Start-Sleep -Milliseconds ([int]$Matches[1])
      "[$i] (기다림 $($Matches[1])ms)"
      continue
    }
    if ($step -match '^@cdp\s+(\S+)\s*(.*)$') {
      # 브라우저에 직접 묻는 단계: "@cdp Page.getInstallabilityErrors {}" — 응답 원문(앞 800자)을 출력
      $cdpParams = @{}
      if ($Matches[2].Trim()) { ($Matches[2] | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $cdpParams[$_.Name] = $_.Value } }
      $raw = Invoke-Cdp $Matches[1] $cdpParams
      "[$i] $($raw.Substring(0, [math]::Min(800, $raw.Length)))"
      continue
    }
    $res = Invoke-Cdp 'Runtime.evaluate' @{ expression = $step; returnByValue = $true; awaitPromise = $true } | ConvertFrom-Json
    $value = if ($res.result.exceptionDetails) { "!! 오류: " + $res.result.exceptionDetails.exception.description } else { $res.result.result.value }
    "[$i] $value"
    Start-Sleep -Milliseconds $StepMs
    $shot = Invoke-Cdp 'Page.captureScreenshot' @{ format = 'png' }
    $start = $shot.IndexOf('"data":"') + 8
    $png = [Convert]::FromBase64String($shot.Substring($start, $shot.IndexOf('"', $start) - $start))
    $path = $Out -replace '\.png$', "_$i.png"
    [IO.File]::WriteAllBytes($path, $png)
    "    saved $path"
  }
} finally {
  if ($ws) { $ws.Dispose() }
  if (-not $p.HasExited) {
    # ✕ 는 숨기기라서 --exit(같은 데이터 폴더로 실행 중인 앱을 정상 종료 — 휴대폰에 "꺼짐"도 남김)로 끝낸다. 견본은 창 닫기로
    Start-Process -FilePath $Exe -ArgumentList '--exit' -Wait -WindowStyle Hidden
    $p.WaitForExit(6000) | Out-Null
    if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; $p.WaitForExit(1500) | Out-Null }
    if (-not $p.HasExited) { $p.Kill() }
  }
  Start-Sleep -Milliseconds 500
  if (-not $keepProfile) { Remove-Item -Recurse -Force $profileDir -ErrorAction SilentlyContinue }
}
