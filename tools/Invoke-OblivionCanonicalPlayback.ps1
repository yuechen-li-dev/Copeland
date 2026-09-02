[CmdletBinding()]
param(
    [string]$OutputDirectory = "artifacts\\canonical-playback"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repositoryRoot "src\\Oblivion\\Oblivion.Standalone\\Oblivion.Standalone.csproj"
$resolvedOutputDirectory = if ([System.IO.Path]::IsPathRooted($OutputDirectory))
{
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
else
{
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

$scenarios = @(
    @{
        Id = "markdown-card-current"
        Arguments = @(
            "--vault", (Join-Path $repositoryRoot "src\\Oblivion\\Oblivion.Standalone\\M20fFunctionCards.oblivion"),
            "--select-card", "execution-context",
            "--expanded",
            "--appearance", "dark"
        )
    },
    @{
        Id = "diagram-card-current"
        Arguments = @(
            "--vault", (Join-Path $repositoryRoot "src\\Oblivion\\Oblivion.Standalone\\M20dLayeredArchitecture.oblivion"),
            "--select-card", "diagram-realization-architecture",
            "--expanded",
            "--appearance", "dark",
            "--diagram-backend", "mermaid"
        )
    },
    @{
        Id = "table-card-current"
        Arguments = @(
            "--vault", (Join-Path $repositoryRoot "src\\Oblivion\\Oblivion.Standalone\\M20eTsonTables.oblivion"),
            "--select-card", "validation-evidence",
            "--expanded",
            "--appearance", "dark"
        )
    },
    @{
        Id = "function-card-current"
        Arguments = @(
            "--vault", (Join-Path $repositoryRoot "src\\Oblivion\\Oblivion.Standalone\\M20fFunctionCards.oblivion"),
            "--select-card", "passing-function",
            "--run-function-card", "passing-function",
            "--expanded",
            "--appearance", "dark"
        )
    },
    @{
        Id = "viewport-appearance-current"
        Arguments = @(
            "--vault", (Join-Path $repositoryRoot "src\\Oblivion\\Oblivion.Standalone\\M20bViewportDiagram.oblivion"),
            "--select-card", "diagram-card-realization",
            "--layout", "horizontal",
            "--expanded",
            "--appearance", "light",
            "--diagram-backend", "native",
            "--diagram-zoom", "1.2",
            "--diagram-pan", "24,12"
        )
    }
)

Push-Location $repositoryRoot
try
{
    & dotnet build $projectPath -m:1 | Out-Host
    if ($LASTEXITCODE -ne 0)
    {
        throw "Oblivion Standalone build failed."
    }

    $results = foreach ($scenario in $scenarios)
    {
        $capturePath = Join-Path $resolvedOutputDirectory ($scenario.Id + ".png")
        $arguments = @(
            "run",
            "--project", $projectPath,
            "--no-build",
            "--",
            "--capture", $capturePath
        ) + $scenario.Arguments

        $errorMessage = $null
        try
        {
            & dotnet @arguments | Out-Host
            if ($LASTEXITCODE -ne 0)
            {
                throw "Standalone capture exited with code $LASTEXITCODE."
            }

            $viewportPath = [System.IO.Path]::ChangeExtension($capturePath, ".viewport.json")
            if (-not (Test-Path -LiteralPath $capturePath) -or (Get-Item -LiteralPath $capturePath).Length -eq 0)
            {
                throw "Capture was not created: $capturePath"
            }
            if (-not (Test-Path -LiteralPath $viewportPath))
            {
                throw "Viewport proof was not created: $viewportPath"
            }
            Get-Content -Raw -LiteralPath $viewportPath | ConvertFrom-Json | Out-Null
        }
        catch
        {
            $errorMessage = $_.Exception.Message
        }

        [ordered]@{
            id = $scenario.Id
            passed = $null -eq $errorMessage
            capturePath = $capturePath
            viewportProofPath = [System.IO.Path]::ChangeExtension($capturePath, ".viewport.json")
            errorMessage = $errorMessage
        }
    }
}
finally
{
    Pop-Location
}

$passedCount = @($results | Where-Object { $_.passed }).Count
$failedCount = $results.Count - $passedCount
$report = [ordered]@{
    suiteId = "oblivion-current-contract"
    suiteName = "Current Oblivion standalone behavioral regression suite"
    scenarioCount = $results.Count
    passedCount = $passedCount
    failedCount = $failedCount
    skippedCount = 0
    validationStatus = if ($failedCount -eq 0) { "passed" } else { "failed" }
    outputDirectory = $resolvedOutputDirectory
    scenarios = $results
}
$reportPath = Join-Path $resolvedOutputDirectory "playback-suite-report.json"
[System.IO.File]::WriteAllText(
    $reportPath,
    (($report | ConvertTo-Json -Depth 8) + [Environment]::NewLine))

Write-Host "Canonical Oblivion playback: $passedCount/$($results.Count)"
Write-Host "Report: $reportPath"
if ($failedCount -ne 0)
{
    throw "Canonical Oblivion playback failed $failedCount scenario(s)."
}
