@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build.ps1" -InstallSdk
if errorlevel 1 (echo Build failed. See the message above.) else (echo Output: artifacts\win-x64\WuWaFpsUnlock.exe)
pause
