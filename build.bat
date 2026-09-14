@echo off
echo Building 2ShipDiscordPresence.exe...
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /win32icon:app.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:2ShipDiscordPresence.exe Program.cs
if %errorlevel% equ 0 (
    echo Build succeeded: 2ShipDiscordPresence.exe generated successfully.
) else (
    echo Build failed: Error during compilation.
)
