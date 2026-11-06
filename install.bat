@echo off
setlocal EnableExtensions
title Installation PCRemote

net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Demande des droits administrateur - necessaire pour autoriser le pare-feu...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

set "SRC=%~dp0"
set "DEST=%LOCALAPPDATA%\PCRemote"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe introuvable : .NET Framework 4 manquant.
  pause
  exit /b 1
)

taskkill /f /im PCRemote.exe >nul 2>&1
if not exist "%DEST%" mkdir "%DEST%"

echo Compilation...
"%CSC%" /nologo /target:winexe /optimize+ /out:"%DEST%\PCRemote.exe" /reference:System.Drawing.dll "%SRC%PCRemote.cs"
if errorlevel 1 (
  echo.
  echo ERREUR de compilation. Envoie le message ci-dessus pour correction.
  pause
  exit /b 1
)
copy /y "%SRC%PCRemote.cs" "%DEST%\" >nul

set "ASK=1"
if exist "%DEST%\config.dat" (
  choice /c ON /n /m "Un mot de passe existe deja. Le changer ? (O/N) "
  if errorlevel 2 set "ASK=0"
)
if "%ASK%"=="0" goto afterpw

:askpw
set "PW="
set /p "PW=Choisis un mot de passe de pairing (4 caracteres minimum) : "
if "%PW:~3,1%"=="" (
  echo Trop court.
  goto askpw
)
set "PCR_PW=%PW%"
start "" /wait "%DEST%\PCRemote.exe" --setpassword
set "PCR_PW="
set "PW="

:afterpw
if not exist "%DEST%\config.dat" (
  echo Le mot de passe n'a pas ete enregistre.
  pause
  exit /b 1
)

netsh advfirewall firewall delete rule name="PCRemote" >nul 2>&1
netsh advfirewall firewall add rule name="PCRemote" dir=in action=allow program="%DEST%\PCRemote.exe" enable=yes profile=any protocol=TCP localport=48555 >nul

reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v PCRemote /t REG_SZ /d "\"%DEST%\PCRemote.exe\"" /f >nul

explorer.exe "%DEST%\PCRemote.exe"

echo.
echo Installation terminee. L'agent tourne en arriere-plan et demarrera avec Windows.
echo Journal en cas de probleme : %DEST%\log.txt
pause
