@echo off
rem Trayloom setup: double-click this file.
rem  - Inside the unzipped Trayloom folder it starts Trayloom.exe --install.
rem  - Next to Trayloom-win-x64.zip it unpacks the zip to %TEMP% first, then starts the installer.
rem The installer is Trayloom itself: per-user, no administrator rights, shortcuts + Apps & features entry.
setlocal

rem Refuse to run as administrator: the installer is per-user and needs no administrator rights, and an elevated copy would
rem install into the administrator account's folders instead of yours.
net session >nul 2>&1
if not errorlevel 1 (
  echo Run Trayloom-Setup.cmd normally, not as administrator. Close this window and double-click the file again.
  pause
  exit /b 1
)
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

rem A folder name nobody can guess, so another program cannot plant files in it first. The paths go to PowerShell through
rem environment variables, never inside the command text.
set "DEST=%TEMP%\Trayloom-Setup-%RANDOM%%RANDOM%"
set "TL_ZIP=%~dp0Trayloom-win-x64.zip"
set "TL_DEST=%DEST%"
echo Unpacking Trayloom, one moment...
if exist "%DEST%" (
  echo The folder %DEST% already exists; run this again.
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath $env:TL_ZIP -DestinationPath $env:TL_DEST" >> "%LOG%" 2>&1
if not exist "%DEST%\Trayloom.exe" (
  echo Could not unpack Trayloom. Details: %LOG%
  pause
  exit /b 1
)
echo Starting the installer...
start "" /wait "%DEST%\Trayloom.exe" --install
rem the unpacked copy has exited by now (start /wait); if anything still holds a file the folder is simply left behind
rmdir /s /q "%DEST%" 2>nul
exit /b 0
