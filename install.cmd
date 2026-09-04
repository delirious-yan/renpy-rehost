@echo off
REM Installs rehost.exe + RenpyRehost.exe to %LOCALAPPDATA%\RenpyRehost\bin,
REM puts that folder on your user PATH, and adds a Start Menu shortcut for the GUI.
REM After this you can run `rehost` or `renpyrehost` from any terminal, and find
REM "Ren'Py Rehost" in the Start Menu. Needs the .NET 8 SDK to build.
setlocal
cd /d "%~dp0"

set "BIN=%LOCALAPPDATA%\RenpyRehost\bin"
if not exist "%BIN%" mkdir "%BIN%"

echo Building rehost.exe (CLI)...
dotnet publish src\RenpyRehost.Cli\RenpyRehost.Cli.csproj -c Release -r win-x64 ^
  -p:PublishSingleFile=true -p:SelfContained=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none ^
  -o "%BIN%" -nologo -clp:ErrorsOnly
if errorlevel 1 goto :fail

echo Building RenpyRehost.exe (GUI)...
dotnet publish src\RenpyRehost.App\RenpyRehost.App.csproj -c Release -r win-x64 ^
  -p:PublishSingleFile=true -p:SelfContained=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none ^
  -o "%BIN%" -nologo -clp:ErrorsOnly
if errorlevel 1 goto :fail

del /q "%BIN%\*.xml" 2>nul

echo Adding "%BIN%" to your user PATH (if not already there)...
powershell -NoProfile -Command ^
  "$p=[Environment]::GetEnvironmentVariable('PATH','User'); $b=$env:LOCALAPPDATA+'\RenpyRehost\bin'; if (($p -split ';') -notcontains $b) { [Environment]::SetEnvironmentVariable('PATH', ($p.TrimEnd(';')+';'+$b), 'User'); Write-Host '  added.' } else { Write-Host '  already on PATH.' }"

echo Creating Start Menu shortcut...
powershell -NoProfile -Command ^
  "$s=(New-Object -ComObject WScript.Shell); $lnk=$s.CreateShortcut([Environment]::GetFolderPath('Programs')+'\Ren''Py Rehost.lnk'); $lnk.TargetPath=$env:LOCALAPPDATA+'\RenpyRehost\bin\RenpyRehost.exe'; $lnk.Save()"

echo.
echo Done.
echo   rehost         - CLI, from any NEW terminal window
echo   renpyrehost    - opens the GUI
echo   Start Menu     - "Ren'Py Rehost"
echo.
echo (Open a new terminal for the PATH change to take effect.)
exit /b 0

:fail
echo.
echo Install failed - see the errors above.
exit /b 1
