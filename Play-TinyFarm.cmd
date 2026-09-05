@echo off
cd /d "%~dp0"
dotnet run --project "%~dp0src\TinyFarm\TinyFarm.Native\TinyFarm.Native.csproj"
if errorlevel 1 pause
