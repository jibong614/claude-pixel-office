@echo off
chcp 65001 >nul
title Claude Pixel Office 원클릭 설치기
cls
echo ==========================================================
echo    🧑‍💻 Claude Pixel Office 원클릭 설치기
echo ==========================================================
echo.
echo  PC에 Claude Pixel Office를 설치하고 바탕화면에 바로가기
echo  아이콘을 자동으로 생성합니다.
echo.
echo  • 설치 위치: %LOCALAPPDATA%\ClaudePixelOffice
echo  • 대상: 바탕화면 및 시작 메뉴 바로가기 아이콘 생성
echo.
set /p CONFIRM="지금 설치를 진행하시겠습니까? (Y/n): "
if /i "%CONFIRM%"=="n" goto cancel

echo.
echo 최신 버전을 다운로드하여 설치하는 중입니다. 잠시만 기다려 주세요...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; irm https://jibong614.github.io/claude-pixel-office/install.ps1 | iex"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [오류] 설치 중 문제가 발생했습니다. 관리자 권한 또는 네트워크를 확인해 주세요.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo 설치가 성공적으로 완료되었습니다!
timeout /t 3 >nul
exit /b 0

:cancel
echo 설치를 취소했습니다.
timeout /t 2 >nul
exit /b 0
