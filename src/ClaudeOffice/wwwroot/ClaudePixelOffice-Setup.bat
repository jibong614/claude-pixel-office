@echo off
title Claude Pixel Office Setup
cls
echo ==========================================================
echo    Claude Pixel Office - Installer
echo ==========================================================
echo.
echo Launching installer via PowerShell, please wait...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; irm https://jibong614.github.io/claude-pixel-office/install.ps1 | iex"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Installation encountered an issue.
    pause
    exit /b %ERRORLEVEL%
)

echo.
pause
exit /b 0
