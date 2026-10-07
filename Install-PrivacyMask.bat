@echo off
setlocal
title PrivacyMask Installer

set "REPO_ROOT=%~dp0"
set "PUBLISH_SCRIPT=%REPO_ROOT%scripts\publish-win-x64-single-file.ps1"
set "INSTALL_SCRIPT=%REPO_ROOT%desktop-app\windows\win-x64\Install-PrivacyMask.ps1"
set "PUBLISHED_APP=%REPO_ROOT%desktop-app\windows\win-x64\single-file\PrivacyMask.App.exe"
set "EXIT_CODE=0"

cd /d "%REPO_ROOT%"

where dotnet.exe >nul 2>nul
if errorlevel 1 goto dotnet_missing

if not exist "%PUBLISH_SCRIPT%" goto source_missing
if not exist "%INSTALL_SCRIPT%" goto source_missing

echo.
echo [1/2] Building the PrivacyMask standalone Windows app...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PUBLISH_SCRIPT%" -SkipTests
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto publish_failed

if not exist "%PUBLISHED_APP%" goto published_app_missing

echo.
echo [2/2] Installing PrivacyMask for the current Windows user...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%INSTALL_SCRIPT%"
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto install_failed

echo.
echo PrivacyMask was built and installed successfully.
echo You can open it from the Desktop or Start Menu shortcut.
goto finish

:dotnet_missing
set "EXIT_CODE=1"
echo.
echo ERROR: The .NET SDK was not found.
echo Install the .NET 10 SDK, then run this file again:
echo https://dotnet.microsoft.com/download/dotnet/10.0
goto finish

:source_missing
set "EXIT_CODE=1"
echo.
echo ERROR: Required PrivacyMask source files were not found.
echo Run this installer from the root of the cloned repository.
goto finish

:publish_failed
echo.
echo ERROR: PrivacyMask could not be built. Review the build output above.
goto finish

:published_app_missing
set "EXIT_CODE=1"
echo.
echo ERROR: The build completed without creating PrivacyMask.App.exe.
goto finish

:install_failed
echo.
echo ERROR: PrivacyMask was built but could not be installed.
goto finish

:finish
echo.
pause
endlocal & exit /b %EXIT_CODE%
