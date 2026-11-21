@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"

if not exist "%~dp0cleanup-for-test.ps1" (
    echo [ERROR] cleanup-for-test.ps1 not found next to this file.
    echo Put cleanup-for-test.cmd in the same folder as the PowerShell script.
    echo.
    pause
    exit /b 1
)

echo Running cleanup-for-test.ps1 ...
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0cleanup-for-test.ps1" %*
set "EXITCODE=%ERRORLEVEL%"

echo.
echo --------------------------------------------
if "%EXITCODE%"=="0" (
    echo Done. Exit code: 0
) else (
    echo Finished with errors. Exit code: %EXITCODE%
    echo If access was denied, close the launcher or right-click this file
    echo and choose "Run as administrator".
)
echo --------------------------------------------
echo.
pause
exit /b %EXITCODE%
