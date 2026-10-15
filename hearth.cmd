@echo off
cd /d "%~dp0"
dotnet run --project Games/Hearth/Hearth/Hearth.csproj -c Release -- %*
