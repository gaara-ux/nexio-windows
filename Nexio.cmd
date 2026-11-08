@echo off
rem Lanceur de Nexio pour Windows.
rem Verifie que .NET Framework 4.7.2 ou plus recent est installe (Windows 10/11 l'ont deja),
rem l'installe depuis Microsoft s'il manque, puis ouvre l'appli.
setlocal
set "HERE=%~dp0"
set REL=0
for /f "tokens=3" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2^>nul ^| find "Release"') do set /a REL=%%a
if %REL% GEQ 461808 goto run

echo .NET Framework 4.8 est necessaire et n'est pas installe.
echo Telechargement depuis Microsoft (environ 115 Mo)...
set "SETUP=%TEMP%\ndp48-x86-x64-allos-enu.exe"
powershell -NoProfile -Command "$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -UseBasicParsing 'https://go.microsoft.com/fwlink/?linkid=2088631' -OutFile '%SETUP%'"
if not exist "%SETUP%" (
  echo Le telechargement a echoue. Installe .NET Framework 4.8 depuis https://dotnet.microsoft.com/download/dotnet-framework/net48
  pause
  exit /b 1
)
echo Installation : accepte la demande de Windows...
"%SETUP%" /passive /norestart
del "%SETUP%" >nul 2>&1
set REL=0
for /f "tokens=3" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2^>nul ^| find "Release"') do set /a REL=%%a
if %REL% LSS 461808 (
  echo L'installation n'est pas terminee. Redemarre l'ordinateur puis relance ce fichier.
  pause
  exit /b 1
)

:run
start "" "%HERE%Nexio.exe" %*
