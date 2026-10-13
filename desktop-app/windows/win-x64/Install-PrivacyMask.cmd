@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "SOURCE_INSTALLER=%SCRIPT_DIR%..\..\..\Install-PrivacyMask.bat"
set "SOURCE_ROOT=%SCRIPT_DIR%..\..\.."

rem A checkout must rebuild after pull, even when an older published EXE exists.
rem Release archives have no source project/publish script and install as-is.
if exist "%SOURCE_ROOT%\src\PrivacyMask.App\PrivacyMask.App.csproj" if exist "%SOURCE_ROOT%\scripts\publish-win-x64-single-file.ps1" if exist "%SOURCE_INSTALLER%" goto install_from_source
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
