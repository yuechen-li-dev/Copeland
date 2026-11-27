@echo off
setlocal
cd /d "%~dp0"
if not exist "artifacts\local\bistro\assets\assets.toml" (
    echo Prepare the Bistro assets first. See docs\Aurelian\bistro.md.
    exit /b 1
)
set "proof=tools\Aurelian.GraphicsProof\bin\Release\net10.0\Aurelian.GraphicsProof.dll"
if exist "%proof%" (
    dotnet "%proof%" --bistro --play %*
) else (
    dotnet run --project tools\Aurelian.GraphicsProof -c Release -- --bistro --play %*
)
