@echo off
rem Builds Mail Transfer Utility 95 using the in-box .NET Framework compiler.
rem Usage:  build.cmd [Debug|Release]
setlocal
set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Release
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Configuration %CONFIG%
endlocal
