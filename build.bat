@echo off
echo Compilation de 2ShipDiscordPresence.exe en cours...
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /out:2ShipDiscordPresence.exe Program.cs
if %errorlevel% equ 0 (
    echo Compilation reussie : 2ShipDiscordPresence.exe a ete genere avec succes.
) else (
    echo Erreur lors de la compilation.
)
