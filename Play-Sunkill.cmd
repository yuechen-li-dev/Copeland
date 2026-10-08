@echo off
setlocal
pushd "%~dp0"

set "SUNKILL_PROJECT=Games\Sunkill\Aurelian.Ariadne.VnDemo\Aurelian.Ariadne.VnDemo.csproj"
set "SUNKILL_EXE=Games\Sunkill\Aurelian.Ariadne.VnDemo\bin\Debug\net10.0\Aurelian.Ariadne.VnDemo.exe"
set "MSBUILDDISABLENODEREUSE=1"

dotnet build "%SUNKILL_PROJECT%" --nologo --verbosity quiet -m:1 -nodeReuse:false -p:UseSharedCompilation=false
if errorlevel 1 goto build_failed

"%SUNKILL_EXE%" %*
set "SUNKILL_EXIT=%ERRORLEVEL%"
popd
exit /b %SUNKILL_EXIT%

:build_failed
set "SUNKILL_EXIT=%ERRORLEVEL%"
popd
exit /b %SUNKILL_EXIT%
