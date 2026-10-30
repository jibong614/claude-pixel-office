# 휴대폰 보기용 페이지(사무실 화면)와 데이터베이스 규칙을 Firebase 에 올린다(Hosting + Realtime Database 규칙).
# 화면 파일은 앱과 같은 wwwroot — 주소 # 뒤에 키가 있으면 휴대폰 보기로 동작한다.
#
# 처음 한 번은 구글 로그인이 필요하다(브라우저가 열림):
#   & "C:\Program Files\nodejs\npx.cmd" --yes firebase-tools login
# 예) powershell -File tools\deploy-mobile.ps1                  프로젝트 ID는 Claude Office 설정(cloud.json)에서 읽는다
#     powershell -File tools\deploy-mobile.ps1 -ProjectId my-office-1234
#Requires -Version 5.1
param([string]$ProjectId)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $ProjectId) {
  $cfgPath = Join-Path $env:LOCALAPPDATA 'ClaudeOffice\cloud.json'
  if (Test-Path $cfgPath) { $ProjectId = (Get-Content -Raw -Encoding UTF8 $cfgPath | ConvertFrom-Json).ProjectId }
}
if (-not $ProjectId) { throw "프로젝트 ID를 모릅니다. Claude Office ⚙️ → 휴대폰에서 보기 에 Firebase 설정을 먼저 붙여 넣거나 -ProjectId 로 알려 주세요." }

$npx = (Get-Command npx.cmd -ErrorAction SilentlyContinue).Source
if (-not $npx) { $npx = Join-Path $env:ProgramFiles 'nodejs\npx.cmd' }
if (-not (Test-Path $npx)) { throw "Node.js(npx)가 없습니다. https://nodejs.org 에서 설치한 뒤 다시 실행해 주세요." }
# firebase-tools 는 안에서 "node" 를 PATH 로 찾는다 — Node.js 폴더가 PATH 에 없는 창에서도 되게 이 실행에만 넣는다
$env:Path = (Split-Path -Parent $npx) + ';' + $env:Path

$mobile = Join-Path $root 'mobile'
$public = Join-Path $mobile 'public'
if (Test-Path $public) { Remove-Item -Recurse -Force $public }
Copy-Item -Recurse (Join-Path $root 'src\ClaudeOffice\wwwroot') $public

Push-Location $mobile
try {
  & $npx --yes firebase-tools deploy --only "hosting,database" --project $ProjectId
  if ($LASTEXITCODE) { throw "배포 실패(종료 코드 $LASTEXITCODE). 처음이면 먼저 로그인하세요:  & `"$npx`" --yes firebase-tools login" }
} finally {
  Pop-Location
}
"완료 — 휴대폰 화면 주소: https://$ProjectId.web.app  (Claude Office ⚙️ → 휴대폰에서 보기 → QR 보기)"
