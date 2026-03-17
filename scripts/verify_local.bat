@echo off
setlocal EnableExtensions
cd /d "%~dp0.."

set "CONFIG=Release"
if /I not "%~1"=="" set "CONFIG=%~1"

echo [1/3] Building Hook core (validation mode, skip root copy)...
set "CMP_SKIP_ROOT_COPY=1"
call scripts\build_hook.bat
if %ERRORLEVEL% NEQ 0 (
    echo Verification failed at step 1: Hook build
    exit /b %ERRORLEVEL%
)

echo [2/3] Building solution (%CONFIG%)...
dotnet build ContextMenuProfiler.sln -c %CONFIG%
if %ERRORLEVEL% NEQ 0 (
    echo Verification failed at step 2: dotnet build
    exit /b %ERRORLEVEL%
)

echo [3/3] Running quality checks (%CONFIG%)...
dotnet run --project ContextMenuProfiler.QualityChecks\ContextMenuProfiler.QualityChecks.csproj -c %CONFIG% --no-build
if %ERRORLEVEL% NEQ 0 (
    echo Verification failed at step 3: quality checks
    exit /b %ERRORLEVEL%
)

echo Verification passed.
exit /b 0
