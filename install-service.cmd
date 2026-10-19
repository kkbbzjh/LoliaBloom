@echo off
setlocal
set "LOLIABLOOM_INSTALL_LOG=%TEMP%\LoliaBloom-install-%RANDOM%-%RANDOM%.log"
echo Run this file as administrator, or use the installation button in Lolia Bloom.
start "" /wait "%~dp0Lolia.App.exe" --install-service --install-log "%LOLIABLOOM_INSTALL_LOG%"
set "LOLIABLOOM_INSTALL_EXIT=%errorlevel%"
if exist "%LOLIABLOOM_INSTALL_LOG%" type "%LOLIABLOOM_INSTALL_LOG%"
echo.
echo Exit code: %LOLIABLOOM_INSTALL_EXIT%
echo Log: %LOLIABLOOM_INSTALL_LOG%
pause
exit /b %LOLIABLOOM_INSTALL_EXIT%
