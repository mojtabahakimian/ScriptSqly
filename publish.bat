@echo off
setlocal
cd /d "%~dp0"

echo [1/2] Cleaning previous publish output...
if exist "ScriptSqly.Runner\bin\Release\net8.0\publish\win-x64" (
    rmdir /s /q "ScriptSqly.Runner\bin\Release\net8.0\publish\win-x64"
)

echo [2/2] Publishing ScriptSqly.Runner as single exe (Framework-dependent, win-x64)...
dotnet publish ScriptSqly.Runner\ScriptSqly.Runner.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained false ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -o "ScriptSqly.Runner\bin\Release\net8.0\publish\win-x64"

if %ERRORLEVEL% equ 0 (
    echo.
    echo ========================================================
    echo SUCCESS: Single EXE published at:
    echo %~dp0ScriptSqly.Runner\bin\Release\net8.0\publish\win-x64\ScriptSqly.Runner.exe
    echo ========================================================
    explorer.exe /select,"%~dp0ScriptSqly.Runner\bin\Release\net8.0\publish\win-x64\ScriptSqly.Runner.exe"
) else (
    echo.
    echo ========================================================
    echo ERROR: Publish failed with exit code %ERRORLEVEL%.
    echo ========================================================
    pause
)

endlocal
