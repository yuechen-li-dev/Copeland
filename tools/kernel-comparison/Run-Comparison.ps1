param(
    [string]$Output = 'artifacts/local/kernel-comparison',
    [ValidateRange(1, 10)][int]$Runs = 3,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location $repository
$outputPath = [IO.Path]::GetFullPath($Output)
$python = Join-Path $outputPath 'venv/Scripts/python.exe'
for ($run = 1; $run -le $Runs; $run++) {
    $existingRun = Join-Path $outputPath "run$run"
    if (Test-Path -LiteralPath $existingRun) {
        throw "Refusing to overwrite an existing run: $existingRun. Choose a fresh output directory."
    }
}

function Invoke-Checked {
    param([string]$Program, [string[]]$Arguments)
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Program failed with exit code $LASTEXITCODE"
    }
}

if ($Install) {
    if (!(Test-Path -LiteralPath $python)) {
        Invoke-Checked 'python' @('-m', 'venv', (Join-Path $outputPath 'venv'))
    }
    Invoke-Checked $python @('-m', 'pip', 'install', '--upgrade', 'pip')
    Invoke-Checked $python @('-m', 'pip', 'install', 'torch==2.11.0', '--index-url', 'https://download.pytorch.org/whl/cu128')
    Invoke-Checked $python @('-m', 'pip', 'install', '-r', (Join-Path $PSScriptRoot 'requirements-windows.txt'))
}
if (!(Test-Path -LiteralPath $python)) {
    throw 'Repository-local Python environment missing. Run with -Install first.'
}
$fixtures = Join-Path $outputPath 'fixtures'
if (!(Test-Path -LiteralPath (Join-Path $fixtures 'manifest.json'))) {
    Invoke-Checked $python @((Join-Path $PSScriptRoot 'fixtures.py'), $fixtures)
}
$env:TORCHINDUCTOR_CACHE_DIR = Join-Path $outputPath 'inductor-cache'
$env:TRITON_CACHE_DIR = Join-Path $outputPath 'triton-cache'
Invoke-Checked 'dotnet' @('build', 'tools/Aurelian.KernelComparison', '-c', 'Release', '-m:1', '-nr:false', '-p:UseSharedCompilation=false')
$binary = Join-Path $repository 'tools/Aurelian.KernelComparison/bin/Release/net10.0/Aurelian.KernelComparison.dll'
$sourceHashes = [ordered]@{}
$changedFiles = @(& git diff --name-only) + @(& git ls-files --others --exclude-standard)
foreach ($file in ($changedFiles | Sort-Object -Unique)) {
    if ([IO.Path]::GetExtension($file) -in @('.cs', '.ts', '.py', '.ps1', '.csproj', '.slnx', '.props', '.hlsl')) {
        $sourceHashes[$file] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
@{
    runs = $Runs
    sourceSha256 = $sourceHashes
    gitHead = (& git rev-parse HEAD)
    dotnet = (& dotnet --version)
    clockControl = 'Unmanaged; no clock changes requested by this runner'
    telemetry = 'nvidia-smi at 100 ms; timestamps use the Windows local time zone'
    timeZone = [TimeZoneInfo]::Local.Id
} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $outputPath 'protocol.json')
for ($run = 1; $run -le $Runs; $run++) {
    $runPath = Join-Path $outputPath "run$run"
    if (Test-Path -LiteralPath $runPath) {
        throw "Refusing to overwrite an existing run: $runPath. Choose a fresh output directory."
    }
    New-Item -ItemType Directory -Path $runPath | Out-Null
    & nvidia-smi --query-gpu=name,uuid,driver_version,pstate,clocks.sm,clocks.mem,power.draw,temperature.gpu,memory.used --format=csv |
        Set-Content (Join-Path $runPath 'gpu-before.csv')
    $vulkanArguments = @($binary, $fixtures, (Join-Path $runPath 'vulkan'))
    $cudaArguments = @((Join-Path $PSScriptRoot 'baselines.py'), $fixtures, (Join-Path $runPath 'cuda'))
    $telemetry = Start-Process -FilePath (Get-Command nvidia-smi).Source -ArgumentList @(
        '--query-gpu=timestamp,name,clocks.sm,clocks.mem,utilization.gpu,pstate,power.draw,temperature.gpu',
        '--format=csv', '--loop-ms=100'
    ) -RedirectStandardOutput (Join-Path $runPath 'gpu-samples.csv') `
        -RedirectStandardError (Join-Path $runPath 'gpu-sampling-errors.log') -PassThru -WindowStyle Hidden
    try {
        # Alternate backend order across full independent processes.
        if ($run % 2 -eq 1) {
            Invoke-Checked 'dotnet' $vulkanArguments
            Invoke-Checked $python $cudaArguments
        }
        else {
            Invoke-Checked $python $cudaArguments
            Invoke-Checked 'dotnet' $vulkanArguments
        }
    }
    finally {
        if (!$telemetry.HasExited) {
            Stop-Process -Id $telemetry.Id
            $telemetry.WaitForExit(1000) | Out-Null
        }
    }
    & nvidia-smi --query-gpu=name,uuid,driver_version,pstate,clocks.sm,clocks.mem,power.draw,temperature.gpu,memory.used --format=csv |
        Set-Content (Join-Path $runPath 'gpu-after.csv')
}
Invoke-Checked $python @((Join-Path $PSScriptRoot 'summarize.py'), $outputPath)
