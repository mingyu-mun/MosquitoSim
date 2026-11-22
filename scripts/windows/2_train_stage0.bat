@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0train_stage0.ps1" %*
echo.
pause
