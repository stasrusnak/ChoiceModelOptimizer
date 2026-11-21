@echo off
chcp 65001 >nul
rem Trims textures and particles in a folder. Drag the folder onto this file or type the path.
set "DIR=%~1"
if "%DIR%"=="" set /p "DIR=Models folder (e.g. F:\ChoiceModels): "
if "%DIR%"=="" goto :eof
echo.
echo === Dry run (nothing is changed yet) ===
"%~dp0Wc3ModelOptimizer.exe" "%DIR%"
echo.
set "OK="
set /p "OK=Apply these changes? (y = yes): "
if /i not "%OK%"=="y" goto :end
"%~dp0Wc3ModelOptimizer.exe" "%DIR%" --write
:end
echo.
pause
