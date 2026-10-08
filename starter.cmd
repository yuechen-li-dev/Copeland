@echo off
cd /d "%~dp0"
dotnet run --project Games/Starter/Aurelian.Starter/Aurelian.Starter.csproj -c Release -- %*
