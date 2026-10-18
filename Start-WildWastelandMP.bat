@echo off
setlocal
set "WWMP_MODE=%~1"
if not defined WWMP_MODE set "WWMP_MODE=both"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Launch-WildWastelandMP.ps1" -Mode "%WWMP_MODE%"
if errorlevel 1 pause
endlocal
