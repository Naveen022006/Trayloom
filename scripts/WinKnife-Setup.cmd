@echo off
rem WinKnife setup: double-click this file.
rem  - Inside the unzipped WinKnife folder it starts WinKnife.exe --install.
rem  - Next to WinKnife-win-x64.zip it unpacks the zip to %TEMP% first, then starts the installer.
rem The installer is WinKnife itself: per-user, no administrator rights, shortcuts + Apps & features entry.
setlocal
set "LOG=%TEMP%\WinKnife-Setup.log"
echo %DATE% %TIME% setup started from %~dp0> "%LOG%"

if exist "%~dp0WinKnife.exe" (
  start "" "%~dp0WinKnife.exe" --install
  exit /b 0
)

if not exist "%~dp0WinKnife-win-x64.zip" (
  echo Put this file next to WinKnife-win-x64.zip or inside the unzipped WinKnife folder.
  pause
  exit /b 1
)

set "DEST=%TEMP%\WinKnife-Setup"
echo Unpacking WinKnife, one moment...
if exist "%DEST%" rmdir /s /q "%DEST%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%~dp0WinKnife-win-x64.zip' -DestinationPath '%DEST%' -Force" >> "%LOG%" 2>&1
if not exist "%DEST%\WinKnife.exe" (
  echo Could not unpack WinKnife. Details: %LOG%
  pause
  exit /b 1
)
echo Starting the installer...
start "" /wait "%DEST%\WinKnife.exe" --install
rmdir /s /q "%DEST%" 2>nul
exit /b 0
