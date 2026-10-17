@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-service.ps1"
exit /b %errorlevel%
