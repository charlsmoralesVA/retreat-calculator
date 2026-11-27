@echo off
rem Double-click or run from a terminal. Close ATAS first. Optional: install.cmd "D:\Apps\ATAS Platform"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
pause
