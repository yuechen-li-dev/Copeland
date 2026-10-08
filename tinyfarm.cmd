@echo off
dotnet run --project "%~dp0Games\TinyFarm\TinyFarm.Cli\TinyFarm.Cli.csproj" -c Release --no-launch-profile -- %*
