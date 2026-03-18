@echo off
setlocal EnableExtensions
cd /d "%~dp0.."

echo Building ContextMenuProfiler.Hook (x64) - Modularized...
set "BUILD_DIR=.build\hook"
set "OBJ_DIR=%BUILD_DIR%\obj"
if not exist "%BUILD_DIR%" mkdir "%BUILD_DIR%"
if not exist "%OBJ_DIR%" mkdir "%OBJ_DIR%"

if /I "%VSCMD_ARG_TGT_ARCH%"=="x64" (
    echo Environment already configured for x64, skipping vcvars64.bat
    goto :compile
)

:: Try to find VS path using vswhere
set "VS_PATH="
set "VSWHERE_EXE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE_EXE%" (
    for /f "usebackq tokens=*" %%i in (`"%VSWHERE_EXE%" -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VS_PATH=%%i"
)

if not defined VS_PATH (
    for %%E in (Community Professional Enterprise BuildTools) do (
        if exist "C:\Program Files\Microsoft Visual Studio\2022\%%E\VC\Auxiliary\Build\vcvars64.bat" set "VS_PATH=C:\Program Files\Microsoft Visual Studio\2022\%%E"
    )
)

if not defined VS_PATH (
    echo Error: Visual Studio C++ build tools not found.
    echo Hint: Install VS 2022 with Desktop development with C++ workload.
    exit /b 1
)

if not exist "%VS_PATH%\VC\Auxiliary\Build\vcvars64.bat" (
    echo Error: vcvars64.bat not found in "%VS_PATH%".
    exit /b 1
)
echo Using VS Path: %VS_PATH%
call "%VS_PATH%\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo Error: Failed to initialize VC++ x64 environment.
    exit /b 1
)

:compile
where cl >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo Error: cl.exe not found after environment initialization.
    exit /b 1
)

:: Build Hook DLL
cl /LD /MT /Zi /EHsc /utf-8 /Fo"%OBJ_DIR%\\" /Fd"%BUILD_DIR%\\vc140.pdb" ^
    ContextMenuProfiler.Hook\src\dllmain.cpp ^
    ContextMenuProfiler.Hook\src\common.cpp ^
    ContextMenuProfiler.Hook\src\ipc_server.cpp ^
    ContextMenuProfiler.Hook\src\handlers\com_handler.cpp ^
    ContextMenuProfiler.Hook\src\handlers\ecmd_handler.cpp ^
    ContextMenuProfiler.Hook\src\hook.c ^
    ContextMenuProfiler.Hook\src\trampoline.c ^
    ContextMenuProfiler.Hook\src\buffer.c ^
    ContextMenuProfiler.Hook\src\hde\hde32.c ^
    ContextMenuProfiler.Hook\src\hde\hde64.c ^
    /I ContextMenuProfiler.Hook\include ^
    /link /DLL /DEBUG /OUT:"%BUILD_DIR%\\ContextMenuProfiler.Hook.dll" /PDB:"%BUILD_DIR%\\ContextMenuProfiler.Hook.pdb" /IMPLIB:"%BUILD_DIR%\\ContextMenuProfiler.Hook.lib" user32.lib ole32.lib shell32.lib shlwapi.lib advapi32.lib gdiplus.lib comctl32.lib gdi32.lib
if %ERRORLEVEL% NEQ 0 (
    echo Error: Hook DLL build failed.
    exit /b %ERRORLEVEL%
)

echo Building Injector...
cl /MT /Zi /EHsc /utf-8 /Fo"%OBJ_DIR%\\" /Fd"%BUILD_DIR%\\vc140.pdb" ^
    ContextMenuProfiler.Hook\src\injector.cpp ^
    /I ContextMenuProfiler.Hook\include ^
    /link /DEBUG /OUT:"%BUILD_DIR%\\ContextMenuProfiler.Injector.exe" /PDB:"%BUILD_DIR%\\ContextMenuProfiler.Injector.pdb" user32.lib kernel32.lib advapi32.lib
if %ERRORLEVEL% NEQ 0 (
    echo Error: Injector build failed.
    exit /b %ERRORLEVEL%
)

if /I "%CMP_SKIP_ROOT_COPY%"=="1" (
    echo Skipping root binary copy because CMP_SKIP_ROOT_COPY=1
    echo Build outputs are available in "%BUILD_DIR%"
    echo Done.
    exit /b 0
)

copy /Y "%BUILD_DIR%\ContextMenuProfiler.Hook.dll" "ContextMenuProfiler.Hook.dll" >nul
if %ERRORLEVEL% NEQ 0 (
    echo Error: Failed to copy ContextMenuProfiler.Hook.dll
    echo Hint: if this file is locked by Explorer, rerun with CMP_SKIP_ROOT_COPY=1 for validation-only builds.
    exit /b 1
)
copy /Y "%BUILD_DIR%\ContextMenuProfiler.Injector.exe" "ContextMenuProfiler.Injector.exe" >nul
if %ERRORLEVEL% NEQ 0 (
    echo Error: Failed to copy ContextMenuProfiler.Injector.exe
    exit /b 1
)

echo Done.
exit /b 0
