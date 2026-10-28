@echo off
REM Rebuild FFmpeg Forge with the C# compiler shipped with Windows (.NET Framework 4)
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:src\app.ico /out:..\FFmpegForge.exe /resource:shaders\FSR.glsl,FSR.glsl /resource:shaders\NIS.glsl,NIS.glsl /resource:shaders\SGSR.glsl,SGSR.glsl /resource:shaders\Anime4K.glsl,Anime4K.glsl /resource:shaders\OptiScaler.glsl,OptiScaler.glsl /resource:shaders\FSR2.glsl,FSR2.glsl /resource:src\hrir_ircam.bin,hrir_ircam.bin /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
if errorlevel 1 (echo. & echo Build FAILED.) else (echo. & echo OK: ..\FFmpegForge.exe rebuilt.)
REM ForgeRTX.exe (RTX driver engine) is built from rtxhelper\ForgeRTX.c with MinGW-w64:
REM   x86_64-w64-mingw32-gcc -O2 -o ..\ForgeRTX.exe rtxhelper\ForgeRTX.c -ld3d11 -ldxgi -ldxguid -luuid -static
pause
