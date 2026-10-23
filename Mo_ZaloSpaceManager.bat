@echo off
title Zalo Space Manager
cd /d "%~dp0"
if exist "%~dp0ZaloSpace\Launch.ps1" (
    start "" powershell.exe -Sta -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0ZaloSpace\Launch.ps1" %*
) else if exist "%~dp0Launch.ps1" (
    start "" powershell.exe -Sta -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Launch.ps1" %*
)
exit
