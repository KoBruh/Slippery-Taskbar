@echo off
setlocal
set ROOT=%~dp0
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find the Windows .NET Framework C# compiler.
  exit /b 1
)

set ICON_TOOL=%ROOT%IconConverter.exe
set ICON=%ROOT%slippery-taskbar.generated.ico
"%CSC%" /nologo /target:exe /optimize+ /out:"%ICON_TOOL%" ^
 /reference:System.dll /reference:System.Drawing.dll ^
 "%ROOT%IconConverter.cs"
if errorlevel 1 exit /b %errorlevel%
"%ICON_TOOL%" "%ROOT%..\assets\slippery-taskbar.jpg" "%ICON%"
if errorlevel 1 exit /b %errorlevel%

"%CSC%" /nologo /target:winexe /optimize+ /platform:x64 ^
 /out:"%ROOT%SlipperyTaskbar.exe" ^
 /win32icon:"%ICON%" ^
 /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll ^
 /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll ^
 "%ROOT%Program.cs" "%ROOT%SlipperyContext.Core.cs" "%ROOT%SlipperyContext.Polling.cs" ^
 "%ROOT%SlipperyContext.Messages.cs" "%ROOT%SlipperyContext.Diagnostics.cs" "%ROOT%Support.cs" ^
 "%ROOT%TaskbarController.Core.cs" "%ROOT%TaskbarController.WorkArea.cs" ^
 "%ROOT%TaskbarController.Shell.cs" "%ROOT%CrashLog.cs" "%ROOT%NativeMethods.cs"
if errorlevel 1 exit /b %errorlevel%
echo Built %ROOT%SlipperyTaskbar.exe
