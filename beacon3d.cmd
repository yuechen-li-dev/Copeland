@echo off
cd /d "%~dp0"
dotnet run --project Games\Beacon3D\Aurelian.Beacon3D -c Release -- %*
