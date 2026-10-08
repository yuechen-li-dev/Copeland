@echo off
cd /d "%~dp0"
dotnet run --project samples\Integrations\Aurelian.Beacon3D -c Release -- %*
