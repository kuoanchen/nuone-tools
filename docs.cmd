@echo off
setlocal
set SCRIPT_DIR=%~dp0
set TARGET=%~1
if "%TARGET%"=="" set TARGET=all
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%scripts\generate-docs.ps1" -Target %TARGET%
endlocal
