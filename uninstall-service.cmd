@echo off
net session >nul 2>&1 || (echo Administrator permission required. & exit /b 1)
sc stop LoliaBloomService >nul 2>&1
sc delete LoliaBloomService
echo Service unregistered. User data was kept under ProgramData\LoliaBloom.
pause
