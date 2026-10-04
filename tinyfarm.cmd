@echo off
dotnet run --project "%~dp0src\TinyFarm\TinyFarm.Cli\TinyFarm.Cli.csproj" -c Release --no-launch-profile -- %*
