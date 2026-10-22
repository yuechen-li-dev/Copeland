@echo off
cd /d "%~dp0"
dotnet run --project tools\Aurelian.GraphicsProof -c Release -- --diffuse-lab %*
