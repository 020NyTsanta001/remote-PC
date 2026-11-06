@echo off
setlocal EnableExtensions
title Desinstallation PCRemote
net session >nul 2>&1
if %errorlevel% neq 0 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
taskkill /f /im PCRemote.exe >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v PCRemote /f >nul 2>&1
netsh advfirewall firewall delete rule name="PCRemote" >nul 2>&1
timeout /t 1 >nul
rmdir /s /q "%LOCALAPPDATA%\PCRemote" >nul 2>&1
echo PCRemote supprime.
pause
