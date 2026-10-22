param(
    [switch]$Headless,
    [switch]$NoBuild,
    [string[]]$Games = @("beacon", "mossward", "sunkill", "characterlab", "starter", "tinyfarm", "cloudstep", "hearth"),
    [string]$BodyPath,
    [string]$Output = "artifacts/local/game-shells",
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    if (-not $NoBuild) {
        & dotnet build Aurelian.slnx -c Release -m:1 -nr:false -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) {
            throw "The game-shell build failed."
        }
    }
    if (-not $env:AURELIAN_NAGA) {
        $env:AURELIAN_NAGA = Join-Path $repositoryRoot "artifacts/aurelian-beacon3d/toolchain/bin/naga.exe"
    }
    if (-not $BodyPath) {
        $BodyPath = Join-Path $repositoryRoot "../Aetheris/artifacts/local/humanoid-production/antonia.gameplay-body.json"
    }
    $applications = @{
        beacon = "Games/Beacon3D/Aurelian.Beacon3D/bin/Release/net10.0/Aurelian.Beacon3D.exe"
        mossward = "Games/Strategy/Aurelian.StrategyDemo/bin/Release/net10.0/Aurelian.StrategyDemo.exe"
        sunkill = "Games/Sunkill/Aurelian.Ariadne.VnDemo/bin/Release/net10.0/Aurelian.Ariadne.VnDemo.exe"
        characterlab = "Games/CharacterLab/Aurelian.CharacterLab/bin/Release/net10.0/Aurelian.CharacterLab.exe"
        starter = "Games/Starter/Aurelian.Starter/bin/Release/net10.0/Aurelian.Starter.exe"
        tinyfarm = "Games/TinyFarm/TinyFarm/bin/Release/net10.0/TinyFarm.exe"
        cloudstep = "Games/Cloudstep/Cloudstep/bin/Release/net10.0/Cloudstep.exe"
        hearth = "Games/Hearth/Hearth/bin/Release/net10.0/Hearth.exe"
    }
    foreach ($game in $Games) {
        if (-not $applications.ContainsKey($game)) {
            throw "Unknown game '$game'. Choose: $($applications.Keys -join ', ')."
        }
    }
    $runId = (Get-Date -AsUTC -Format "yyyyMMdd-HHmmss") + "-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
    $runDirectory = [IO.Path]::GetFullPath((Join-Path $Output $runId))
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    $scriptName = if ($Headless) { "common-shell-headless.json" } else { "common-shell.json" }
    $scriptPath = Join-Path $repositoryRoot "Games/Playtests/$scriptName"
    $results = [Collections.Generic.List[object]]::new()
    foreach ($game in $Games) {
        $directory = Join-Path $runDirectory $game
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = Join-Path $repositoryRoot $applications[$game]
        $startInfo.WorkingDirectory = $repositoryRoot
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        foreach ($argument in @("--playtest-script", $scriptPath, "--output", $directory, "--save-root", (Join-Path $directory "saves"))) {
            $startInfo.ArgumentList.Add($argument)
        }
        if ($Headless) {
            $startInfo.ArgumentList.Add("--headless")
        }
        if ($game -eq "characterlab") {
            $startInfo.ArgumentList.Add("--body")
            $startInfo.ArgumentList.Add([IO.Path]::GetFullPath($BodyPath))
        }
        Write-Output "Checking $game ($scriptName)..."
        $process = [Diagnostics.Process]::Start($startInfo)
        try {
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            $finished = $process.WaitForExit($TimeoutSeconds * 1000)
            if (-not $finished) {
                $process.Kill($true)
                $process.WaitForExit()
            }
            $stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $directory "stdout.log")
            $stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $directory "stderr.log")
            $accepted = $finished -and $process.ExitCode -eq 0
            $results.Add([ordered]@{ game = $game; accepted = $accepted; exitCode = $process.ExitCode; timedOut = -not $finished; artifacts = $directory })
            Write-Output "$game accepted=$accepted artifacts=$directory"
        }
        finally {
            $process.Dispose()
        }
    }
    $accepted = @($results | Where-Object { -not $_.accepted }).Count -eq 0
    [ordered]@{ accepted = $accepted; headless = [bool]$Headless; results = $results } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runDirectory "evidence.json")
    if (-not $accepted) {
        throw "Game-shell qualification failed. See $runDirectory/evidence.json and per-game stderr.log files."
    }
    Write-Output "GAME_SHELLS_PASSED games=$($Games.Count) artifacts=$runDirectory"
}
finally {
    Pop-Location
}
