@echo off
echo Buddy OCR needs the Microsoft Visual C++ x64 Runtime.
echo This opens Microsoft's installer so you can review its terms and installation.
choice /C YN /N /M "Download and open it now? [Y/N] "
if errorlevel 2 exit /b 0
powershell.exe -NoProfile -File "%~dp0Install-Prerequisites.ps1"
if errorlevel 1 pause
