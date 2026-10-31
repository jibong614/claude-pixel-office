# 무료 서버(모두가 함께 쓰는 Firebase 프로젝트)에 데이터베이스 규칙 mobile/database.rules.json 을 올린다 — 관리자용.
# 사용자는 할 일이 없다(앱에 무료 서버 주소가 들어 있고, 휴대폰 화면은 GitHub Pages 가 올린다).
#
# 처음 한 번은 그 프로젝트에 권한이 있는 구글 계정으로 로그인해야 한다(브라우저가 열림):
#   & "C:\Program Files\nodejs\npx.cmd" --yes firebase-tools login
# 예) powershell -File tools\deploy-rules.ps1
#Requires -Version 5.1
param([string]$ProjectId = 'claude-pixel-office-relay')   # CloudConfig.RelayProjectId 와 같게
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$npx = (Get-Command npx.cmd -ErrorAction SilentlyContinue).Source
if (-not $npx) { $npx = Join-Path $env:ProgramFiles 'nodejs\npx.cmd' }
if (-not (Test-Path $npx)) { throw "Node.js(npx)가 없습니다. https://nodejs.org 에서 설치한 뒤 다시 실행해 주세요." }
# firebase-tools 는 안에서 "node" 를 PATH 로 찾는다 — Node.js 폴더가 PATH 에 없는 창에서도 되게 이 실행에만 넣는다
$env:Path = (Split-Path -Parent $npx) + ';' + $env:Path

Push-Location (Join-Path $root 'mobile')
try {
  & $npx --yes firebase-tools deploy --only database --project $ProjectId
  if ($LASTEXITCODE) { throw "배포 실패(종료 코드 $LASTEXITCODE). 처음이면 먼저 로그인하세요:  & `"$npx`" --yes firebase-tools login" }
} finally {
  Pop-Location
}
"완료 — $ProjectId 의 데이터베이스 규칙을 올렸습니다."
