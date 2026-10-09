@echo off
cd /d "%~dp0"
dotnet run --project Games\CharacterLab\Aurelian.CharacterLab -c Release -- %* --body "%~dp0..\Aetheris\artifacts\local\humanoid-production\antonia.gameplay-body.json"
