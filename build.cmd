@echo off
rem Builds Nexio.exe with the C# compiler that ships with Windows (.NET Framework 4.8). Nothing to install.
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set WPF=%FW%\WPF
cd /d "%~dp0"
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /out:Nexio.exe ^
  /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\WindowsBase.dll" ^
  /r:"%FW%\System.Xaml.dll" /r:"%FW%\System.Web.Extensions.dll" /r:"%FW%\System.Core.dll" ^
  /r:"%FW%\System.dll" /r:"%FW%\System.Xml.dll" /r:"%FW%\System.IO.Compression.dll" /r:"%FW%\System.IO.Compression.FileSystem.dll" ^
  src\*.cs
if errorlevel 1 (echo ECHEC de la compilation & exit /b 1)
echo OK : Nexio.exe
