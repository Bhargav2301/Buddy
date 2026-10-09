@echo off
setlocal
title Buddy - restore previous version
if not exist "%~dp0Buddy.exe" (
  echo Run this file beside Buddy.exe in the extracted Windows package.
  pause
  exit /b 1
)
echo Quit Buddy from its tray icon before continuing.
echo This restores previous application files and keeps your current local data.
echo Older versions may not understand settings introduced by this update.
choice /C YN /M "Restore the previous Buddy version"
if errorlevel 2 exit /b 0
start "" /wait "%~dp0Buddy.exe" --rollback
exit /b %ERRORLEVEL%
