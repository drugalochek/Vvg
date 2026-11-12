@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set "APP_VERSION=2.5.8"
set "RUNTIME=win-x64"

echo [1/3] Publishing Moku application...
dotnet publish Island.csproj -c Release -r %RUNTIME% --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:InvariantGlobalization=false -p:DebugType=None -p:DebugSymbols=false -o publish
if errorlevel 1 goto :failed
if not exist "publish\Moku.exe" (echo ERROR: publish\Moku.exe was not created.& goto :failed)
for %%A in ("publish\Moku.exe") do if %%~zA LSS 1048576 (echo ERROR: publish\Moku.exe looks incomplete.& goto :failed)

echo [2/3] Building standalone animated WPF installer...
dotnet publish ..\Installer\Installer.csproj -c Release -r %RUNTIME% --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:Version=%APP_VERSION% -o installer-publish
if errorlevel 1 goto :failed
if not exist "installer-publish\Moku-Setup.exe" (echo ERROR: installer executable was not created.& goto :failed)

echo [3/3] Copying release artifact...
if not exist "release" mkdir release
copy /y "installer-publish\Moku-Setup.exe" "release\Moku-Setup-%APP_VERSION%-x64.exe" >nul
if errorlevel 1 goto :failed
echo.
echo BUILD COMPLETE
echo Portable app: "%CD%\publish\Moku.exe"
echo Installer:    "%CD%\release\Moku-Setup-%APP_VERSION%-x64.exe"
echo The installer is self-contained; Inno Setup is no longer required.
echo Moku will launch automatically after installation.
pause
exit /b 0
:failed
echo.
echo BUILD FAILED. Review the errors above.
pause
exit /b 1
