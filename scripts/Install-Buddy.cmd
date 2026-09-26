@echo off
setlocal
title Buddy - installer
if not exist "%~dp0Buddy.exe" goto missing
set "BUDDY_LOGS=%LOCALAPPDATA%\Buddy\Logs"
if not exist "%BUDDY_LOGS%" mkdir "%BUDDY_LOGS%"
set "COREHOST_TRACE=1"
set "COREHOST_TRACEFILE=%BUDDY_LOGS%\host-installer.log"
echo Installing Buddy for your Windows account.
start "" /wait "%~dp0Buddy.exe" --install
set "BUDDY_EXIT=%ERRORLEVEL%"
if "%BUDDY_EXIT%"=="0" exit /b 0
echo.
echo Buddy could not install. Exit code: %BUDDY_EXIT%
echo Error logs: "%BUDDY_LOGS%"
echo Attach startup.log and host-installer.log when reporting the problem.
pause
exit /b %BUDDY_EXIT%
:missing
echo Buddy.exe was not found in this folder.
echo Download the Windows application ZIP from Buddy Releases and choose Extract All.
echo Run this file beside Buddy.exe in that extracted Windows application folder.
pause
exit /b 1
