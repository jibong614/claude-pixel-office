# Claude Pixel Office - One-Click Windows Installer
# Usage:
#   irm https://jibong614.github.io/claude-pixel-office/install.ps1 | iex
#   or: powershell -ExecutionPolicy Bypass -File install.ps1

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Magenta
Write-Host "   🧑‍💻 Claude Pixel Office 원클릭 설치기" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Magenta
Write-Host ""

$installDir = Join-Path $env:LOCALAPPDATA "ClaudePixelOffice"
Write-Host "• 설치 경로: $installDir" -ForegroundColor Gray
Write-Host "• 대상: Windows 데스크탑 + 시작 메뉴 및 바탕화면 바로가기" -ForegroundColor Gray
Write-Host ""

# 1. 기존 프로세스 확인
$running = Get-Process -Name "ClaudeOffice" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "실행 중인 Claude Office 프로세스를 종료합니다..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 600
}

# 2. 최신 릴리스 정보 가져오기
Write-Host "최신 버전 정보를 확인하고 있습니다..." -ForegroundColor Cyan
$repo = "jibong614/claude-pixel-office"
$apiUrl = "https://api.github.com/repos/$repo/releases/latest"
$zipUrl = ""
$ver = "최신"

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $headers = @{ "User-Agent" = "ClaudePixelOffice-Installer" }
    $rel = Invoke-RestMethod -Uri $apiUrl -Headers $headers -TimeoutSec 15
    $ver = $rel.tag_name
    $asset = $rel.assets | Where-Object { $_.name -like "ClaudePixelOffice-*-win-x64.zip" } | Select-Object -First 1
    if ($asset) {
        $zipUrl = $asset.browser_download_url
    }
} catch {
    # API 호출 실패 시 고정 fallback 주소 사용
    Write-Host "API 조회 폴백: 최신 릴리스 직접 링크로 전환합니다." -ForegroundColor DarkGray
}

if (-not $zipUrl) {
    $zipUrl = "https://github.com/$repo/releases/download/v1.9.20/ClaudePixelOffice-v1.9.20-win-x64.zip"
}

Write-Host "다운로드 버전: $ver" -ForegroundColor Green
Write-Host "다운로드 주소: $zipUrl" -ForegroundColor Gray

# 3. 임시 ZIP 파일 다운로드
$tempZip = Join-Path $env:TEMP ("ClaudePixelOffice-" + [Guid]::NewGuid().ToString("N") + ".zip")
Write-Host "파일을 다운로드하고 있습니다..." -ForegroundColor Cyan

try {
    $webClient = New-Object System.Net.WebClient
    $webClient.Headers.Add("User-Agent", "ClaudePixelOffice-Installer")
    $webClient.DownloadFile($zipUrl, $tempZip)
} catch {
    Invoke-WebRequest -Uri $zipUrl -OutFile $tempZip -UseBasicParsing
}

if (!(Test-Path $tempZip) -or ((Get-Item $tempZip).Length -lt 1000000)) {
    Write-Error "다운로드 파일이 유효하지 않습니다."
    exit 1
}

# 4. 압축 해제
Write-Host "설치 폴더에 파일을 압축 해제합니다..." -ForegroundColor Cyan
if (!(Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

$extractTemp = Join-Path $env:TEMP ("co-extract-" + [Guid]::NewGuid().ToString("N"))
Expand-Archive -Path $tempZip -DestinationPath $extractTemp -Force

# 내부 폴더명(ClaudePixelOffice-v*-win-x64)이 포함되어 있으면 내용물만 꺼내기
$innerDir = Get-ChildItem -Path $extractTemp -Directory | Where-Object { Test-Path (Join-Path $_.FullName "ClaudeOffice.exe") } | Select-Object -First 1
if ($innerDir) {
    Copy-Item -Path (Join-Path $innerDir.FullName "*") -Destination $installDir -Recurse -Force
} else {
    Copy-Item -Path (Join-Path $extractTemp "*") -Destination $installDir -Recurse -Force
}

# 임시 파일 정리
Remove-Item -Path $tempZip -Force -ErrorAction SilentlyContinue
Remove-Item -Path $extractTemp -Recurse -Force -ErrorAction SilentlyContinue

$exePath = Join-Path $installDir "ClaudeOffice.exe"
if (!(Test-Path $exePath)) {
    Write-Error "설치 파일(ClaudeOffice.exe)을 찾을 수 없습니다."
    exit 1
}

# 5. 바탕화면 및 시작메뉴 바로가기 생성
Write-Host "바탕화면 및 시작 메뉴에 바로가기 아이콘을 생성합니다..." -ForegroundColor Cyan
$wsh = New-Object -ComObject WScript.Shell

# 바탕화면 바로가기
$desktopDir = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
$desktopLnk = Join-Path $desktopDir "Claude Pixel Office.lnk"
$sc = $wsh.CreateShortcut($desktopLnk)
$sc.TargetPath = $exePath
$sc.WorkingDirectory = $installDir
$sc.Description = "Claude Pixel Office - Claude Code 레트로 픽셀 모니터"
$sc.IconLocation = "$exePath,0"
$sc.Save()

# 시작 메뉴 바로가기
$programsDir = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$startLnk = Join-Path $programsDir "Claude Pixel Office.lnk"
$scStart = $wsh.CreateShortcut($startLnk)
$scStart.TargetPath = $exePath
$scStart.WorkingDirectory = $installDir
$scStart.Description = "Claude Pixel Office - Claude Code 레트로 픽셀 모니터"
$scStart.IconLocation = "$exePath,0"
$scStart.Save()

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "   🎉 Claude Pixel Office 설치가 완료되었습니다!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "• 바탕화면: $desktopLnk" -ForegroundColor Yellow
Write-Host "• 시작 메뉴: 검색창에 'Claude Pixel Office'를 입력하세요" -ForegroundColor Yellow
Write-Host ""
Write-Host "앱을 즉시 실행합니다..." -ForegroundColor Cyan

Start-Process -FilePath $exePath -WorkingDirectory $installDir
Write-Host "완료! 창을 닫으셔도 좋습니다." -ForegroundColor Green
