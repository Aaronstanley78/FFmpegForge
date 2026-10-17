@echo off
REM Rebuild FFmpeg Forge with the C# compiler shipped with Windows (.NET Framework 4)
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:src\app.ico /out:..\FFmpegForge.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
if errorlevel 1 (echo. & echo Build FAILED.) else (echo. & echo OK: ..\FFmpegForge.exe rebuilt.)
pause
