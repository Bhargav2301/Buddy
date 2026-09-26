@echo off
setlocal
title Buddy - startup
if not exist "%~dp0Buddy.exe" goto missing
set "BUDDY_LOGS=%LOCALAPPDATA%\Buddy\Logs"
if not exist "%BUDDY_LOGS%" mkdir "%BUDDY_LOGS%"
set "COREHOST_TRACE=1"
set "COREHOST_TRACEFILE=%BUDDY_LOGS%\host-startup.log"
echo Opening Buddy. Keep all extracted files together.
start "" /wait "%~dp0Buddy.exe"
set "BUDDY_EXIT=%ERRORLEVEL%"
if "%BUDDY_EXIT%"=="0" exit /b 0
echo.
echo Buddy could not start. Exit code: %BUDDY_EXIT%
echo Error logs: "%BUDDY_LOGS%"
echo Attach startup.log and host-startup.log when reporting the problem.
pause
exit /b %BUDDY_EXIT%
:missing
echo Buddy.exe was not found in this folder.
echo Download Buddy-Windows-v0.1.0.zip and choose Extract All.
echo Run this file beside Buddy.exe in that extracted Windows application folder.
pause
exit /b 1
