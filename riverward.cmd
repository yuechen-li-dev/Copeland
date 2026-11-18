@echo off
cd /d "%~dp0"
dotnet run --project Games/Riverward/Riverward -c Release -- %*
