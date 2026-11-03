@echo off
rem Trayloom setup: double-click this file.
rem  - Inside the unzipped Trayloom folder it starts Trayloom.exe --install.
rem  - Next to Trayloom-win-x64.zip it unpacks the zip to %TEMP% first, then starts the installer.
rem The installer is Trayloom itself: per-user, no administrator rights, shortcuts + Apps & features entry.
setlocal
set "LOG=%TEMP%\Trayloom-Setup.log"
echo %DATE% %TIME% setup started from %~dp0> "%LOG%"

if exist "%~dp0Trayloom.exe" (
  start "" "%~dp0Trayloom.exe" --install
  exit /b 0
)

if not exist "%~dp0Trayloom-win-x64.zip" (
  echo Put this file next to Trayloom-win-x64.zip or inside the unzipped Trayloom folder.
  pause
  exit /b 1
)

set "DEST=%TEMP%\Trayloom-Setup"
echo Unpacking Trayloom, one moment...
if exist "%DEST%" rmdir /s /q "%DEST%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%~dp0Trayloom-win-x64.zip' -DestinationPath '%DEST%' -Force" >> "%LOG%" 2>&1
if not exist "%DEST%\Trayloom.exe" (
  echo Could not unpack Trayloom. Details: %LOG%
  pause
  exit /b 1
)
echo Starting the installer...
start "" /wait "%DEST%\Trayloom.exe" --install
rmdir /s /q "%DEST%" 2>nul
exit /b 0
