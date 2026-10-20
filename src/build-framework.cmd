@echo off
setlocal
set ROOT=%~dp0
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find the Windows .NET Framework C# compiler.
  exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /platform:x64 ^
 /out:"%ROOT%SlipperyTaskbar.exe" ^
 /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll ^
 /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll ^
 "%ROOT%Program.cs" "%ROOT%SlipperyContext.Core.cs" "%ROOT%SlipperyContext.Polling.cs" ^
 "%ROOT%SlipperyContext.Messages.cs" "%ROOT%SlipperyContext.Diagnostics.cs" "%ROOT%Support.cs" ^
 "%ROOT%TaskbarController.Core.cs" "%ROOT%TaskbarController.WorkArea.cs" ^
 "%ROOT%TaskbarController.Shell.cs" "%ROOT%TaskbarController.GapWatch.cs" ^
 "%ROOT%CrashLog.cs" "%ROOT%NativeMethods.cs"
if errorlevel 1 exit /b %errorlevel%
echo Built %ROOT%SlipperyTaskbar.exe
