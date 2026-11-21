@echo off
chcp 65001 >nul
rem Report: heaviest models by particles and largest textures. Changes nothing.
set "DIR=%~1"
if "%DIR%"=="" set /p "DIR=Models folder (e.g. F:\ChoiceModels): "
if "%DIR%"=="" goto :eof
"%~dp0Wc3ModelOptimizer.exe" "%DIR%" --report
echo.
pause
