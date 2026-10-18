@echo off
cd /d "%~dp0"
dotnet run --project Games/TinyFarm/TinyFarm/TinyFarm.csproj -c Release -- %*
