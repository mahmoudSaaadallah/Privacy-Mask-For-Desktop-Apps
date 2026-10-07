@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "SOURCE_INSTALLER=%SCRIPT_DIR%..\..\..\Install-PrivacyMask.bat"

if exist "%SCRIPT_DIR%single-file\PrivacyMask.App.exe" goto install_published
if exist "%SCRIPT_DIR%app\PrivacyMask.App.exe" goto install_published
if exist "%SOURCE_INSTALLER%" goto install_from_source

:install_published
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Install-PrivacyMask.ps1"
set "EXIT_CODE=%ERRORLEVEL%"
pause
endlocal & exit /b %EXIT_CODE%

:install_from_source
call "%SOURCE_INSTALLER%"
exit /b %ERRORLEVEL%
