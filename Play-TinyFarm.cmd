@echo off
cd /d "%~dp0"
dotnet run --project "%~dp0src\TinyFarm\TinyFarm.Native\TinyFarm.Native.csproj" -c Release -- %*
if errorlevel 1 pause
