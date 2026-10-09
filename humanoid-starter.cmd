@echo off
cd /d "%~dp0"
dotnet run --project Games\Starter\Aurelian.Starter -c Release -- %* --humanoid "%~dp0..\Aetheris\artifacts\local\humanoid-locomotion\antonia.gameplay-body.json" --locomotion "%~dp0artifacts\local\playable-humanoid-locomotion\mixamo.locomotion.json"
