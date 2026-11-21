@echo off
rem Builds Wc3ModelOptimizer.exe (console) and Wc3ModelOptimizerGui.exe (window) one folder up with the C# compiler of .NET Framework 4 (ships with Windows)
rem AssemblyInfo.cs + app.manifest add version info: without them antivirus heuristics (Avast MalwareX-gen) flag the exe
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
"%CSC%" /nologo /optimize /codepage:65001 /nowarn:649 /win32manifest:"%~dp0app.manifest" /out:"%~dp0..\Wc3ModelOptimizer.exe" "%~dp0PackFix.cs" "%~dp0Jpeg.cs" "%~dp0AssemblyInfo.cs"
"%CSC%" /nologo /optimize /codepage:65001 /target:winexe /define:GUI /win32manifest:"%~dp0app.manifest" /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%~dp0..\Wc3ModelOptimizerGui.exe" "%~dp0PackFix.cs" "%~dp0Jpeg.cs" "%~dp0Gui.cs" "%~dp0AssemblyInfo.cs"
