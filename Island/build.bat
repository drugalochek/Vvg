@echo off
setlocal
cd /d "%~dp0"
echo Building Moku.exe for Windows x64...
dotnet publish Island.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o publish
if errorlevel 1 (
    echo.
    echo BUILD FAILED. Check the compiler errors above. If dotnet is not found, install the .NET 8 SDK.
    pause
    exit /b 1
)
echo.
echo BUILD COMPLETE: %~dp0publish\Moku.exe
pause
