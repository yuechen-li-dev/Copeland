@echo off
cd /d "%~dp0"
if not exist "%~dp0..\Aetheris\artifacts\local\humanoid-locomotion\antonia.gameplay-body.json" goto geometry
if not exist "%~dp0artifacts\local\playable-humanoid-locomotion\mixamo.locomotion.json" goto geometry
dotnet run --project tools\Aurelian.GraphicsProof -c Release -- --view %* --body "%~dp0..\Aetheris\artifacts\local\humanoid-locomotion\antonia.gameplay-body.json" --locomotion "%~dp0artifacts\local\playable-humanoid-locomotion\mixamo.locomotion.json"
exit /b %errorlevel%
:geometry
dotnet run --project tools\Aurelian.GraphicsProof -c Release -- --view %*
