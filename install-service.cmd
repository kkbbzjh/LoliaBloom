@echo off
setlocal
net session >nul 2>&1 || (echo Administrator permission required. & exit /b 1)
set "SOURCE=%~dp0"
set "TARGET=%ProgramFiles%\LoliaBloom"
set "DATA=%ProgramData%\LoliaBloom"
if not exist "%SOURCE%Service\Lolia.Service.exe" (echo ERROR: Service\Lolia.Service.exe missing & exit /b 1)
if not exist "%TARGET%\Service" mkdir "%TARGET%\Service"
if not exist "%DATA%" mkdir "%DATA%"
sc query LoliaBloomService >nul 2>&1
if not errorlevel 1 (sc stop LoliaBloomService >nul 2>&1 & timeout /t 2 /nobreak >nul)
xcopy /E /I /Y "%SOURCE%Service\*" "%TARGET%\Service\" >nul
if errorlevel 1 exit /b 1
if exist "%SOURCE%frpc.exe" copy /Y "%SOURCE%frpc.exe" "%DATA%\frpc.exe" >nul
sc query LoliaBloomService >nul 2>&1
if errorlevel 1 (sc create LoliaBloomService binPath= "\"%TARGET%\Service\Lolia.Service.exe\"" start= auto DisplayName= "Lolia Bloom Service") else (sc config LoliaBloomService binPath= "\"%TARGET%\Service\Lolia.Service.exe\"" start= auto)
if errorlevel 1 (echo ERROR: service registration failed & exit /b 1)
sc failure LoliaBloomService reset= 86400 actions= restart/5000/restart/15000/restart/60000
sc start LoliaBloomService
if errorlevel 1 (echo ERROR: service failed to start. & exit /b 1)
echo Service installation finished.
