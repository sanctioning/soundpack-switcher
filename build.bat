@echo off
rem Builds "Soundpack Switcher.exe" with the C# compiler that ships with Windows (.NET Framework 4.x).
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:app.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /out:"Soundpack Switcher.exe" SoundpackSwitcher.cs
if errorlevel 1 (echo Build failed.& pause & exit /b 1)
echo Built Soundpack Switcher.exe
pause
