@echo off
cd /d "%~dp0.."

echo Stopping Explorer...
taskkill /F /IM explorer.exe >nul 2>&1
timeout /t 2 /nobreak >nul

echo Cleaning old DLL...
if exist ContextMenuProfiler.Hook.dll (
    del /F /Q ContextMenuProfiler.Hook.dll >nul 2>&1
    if exist ContextMenuProfiler.Hook.dll (
        echo ERROR: ContextMenuProfiler.Hook.dll is still locked by Explorer!
        start explorer.exe
        exit /b 1
    )
)

echo Building Hook DLL...
call scripts\build_hook.bat
if %ERRORLEVEL% NEQ 0 (
    echo Build FAILED.
    start explorer.exe
    exit /b 1
)
echo Build succeeded.

echo Starting Explorer...
start explorer.exe
echo Waiting for Explorer to initialize...
timeout /t 8 /nobreak >nul

echo Injecting DLL...
if not exist ContextMenuProfiler.Injector.exe (
    echo Injection FAILED: ContextMenuProfiler.Injector.exe not found.
    echo Hint: Run scripts\build_hook.bat and verify output files.
    exit /b 1
)
if not exist ContextMenuProfiler.Hook.dll (
    echo Injection FAILED: ContextMenuProfiler.Hook.dll not found.
    echo Hint: Run scripts\build_hook.bat and verify output files.
    exit /b 1
)
ContextMenuProfiler.Injector.exe ContextMenuProfiler.Hook.dll
if %ERRORLEVEL% NEQ 0 (
    echo Injection FAILED.
    echo Hint: Re-run as Administrator and ensure Explorer is running.
    exit /b 1
)

echo Redeploy Successful.
