@echo off
cd /d "%~dp0"
dotnet run --project Games/Cloudstep/Cloudstep/Cloudstep.csproj -c Release -- %*
