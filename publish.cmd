@echo off
REM Builds portable, self-contained copies of the CLI and the GUI into .\dist\
REM Each is a single .exe that runs on Windows 10 2004+ / 11 x64 with no .NET
REM installed. Needs the .NET 8 SDK to build:  winget install Microsoft.DotNet.SDK.8
setlocal
cd /d "%~dp0"

if exist dist rmdir /s /q dist

echo Building rehost.exe (CLI)...
dotnet publish src\RenpyRehost.Cli\RenpyRehost.Cli.csproj -c Release -r win-x64 ^
  -p:PublishSingleFile=true -p:SelfContained=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none ^
  -o dist -nologo -clp:ErrorsOnly
if errorlevel 1 goto :fail

echo Building RenpyRehost.exe (GUI)...
dotnet publish src\RenpyRehost.App\RenpyRehost.App.csproj -c Release -r win-x64 ^
  -p:PublishSingleFile=true -p:SelfContained=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none ^
  -o dist -nologo -clp:ErrorsOnly
if errorlevel 1 goto :fail

del /q "dist\*.xml" 2>nul

echo.
echo Done.  Portable builds in "%~dp0dist":
echo   rehost.exe          - the CLI
echo   RenpyRehost.exe     - the GUI
echo.
echo ffmpeg and the Ren'Py SDKs download on first use into
echo   %%LOCALAPPDATA%%\RenpyRehost\
echo.
start "" explorer "%~dp0dist"
exit /b 0

:fail
echo.
echo Publish failed - see the errors above.
exit /b 1
