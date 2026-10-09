@echo off
setlocal
if not exist "%~dp0Buddy.exe" exit /b 1
start "" "%~dp0Buddy.exe" --settings
