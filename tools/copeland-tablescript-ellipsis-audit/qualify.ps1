param(
    [string] $OutputDirectory = "artifacts/tablescript-ellipsis-implementation"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$evidenceDirectory = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$null = New-Item -ItemType Directory -Path $evidenceDirectory -Force
$cliPath = Join-Path $repositoryRoot "src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll"
$workbookPath = Join-Path $repositoryRoot "samples/copeland-ts/tablescript-friction-m12/Workbook/Workbook.ts"

function Invoke-Probe([string] $Name, [string] $Source, [bool] $ExpectedSuccess, [int] $ExpectedRows = -1) {
    $sourcePath = Join-Path $evidenceDirectory "$Name.ts"
    $reportPath = Join-Path $evidenceDirectory "$Name.json"
    [System.IO.File]::WriteAllText($sourcePath, $Source)
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = (Get-Command dotnet -ErrorAction Stop).Source
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @($cliPath, "table", "validate", $sourcePath, "--format", "json")) {
        $startInfo.ArgumentList.Add($argument)
    }
    $probeProcess = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $stdout = $probeProcess.StandardOutput.ReadToEndAsync()
        $stderr = $probeProcess.StandardError.ReadToEndAsync()
        if (-not $probeProcess.WaitForExit(15000)) {
            $probeProcess.Kill($true)
            $probeProcess.WaitForExit()
            throw "$Name did not terminate within 15 seconds"
        }
        $exitCode = $probeProcess.ExitCode
        $json = $stdout.GetAwaiter().GetResult()
        $errorOutput = $stderr.GetAwaiter().GetResult()
        if ([string]::IsNullOrWhiteSpace($json)) {
            throw "$Name returned no JSON: $errorOutput"
        }
    }
    finally {
        $probeProcess.Dispose()
    }
    [System.IO.File]::WriteAllText($reportPath, $json)
    $result = $json | ConvertFrom-Json
    if ($result.success -ne $ExpectedSuccess -or (($exitCode -eq 0) -ne $ExpectedSuccess)) {
        throw "Unexpected result for $Name`: $json"
    }
    if (-not $ExpectedSuccess -and $result.diagnostics.Count -eq 0) {
        throw "$Name failed without diagnostics"
    }
    if ($ExpectedRows -ge 0 -and $result.totalRows -ne $ExpectedRows) {
        throw "$Name expected $ExpectedRows rows, got $($result.totalRows)"
    }
    return [ordered]@{
        name = $Name
        success = $result.success
        exitCode = $exitCode
        totalRows = $result.totalRows
        diagnostics = @($result.diagnostics | ForEach-Object { $_.code })
    }
}

Push-Location $repositoryRoot
try {
    $buildOutput = & dotnet build src/Copeland/Copeland.Cli/Copeland.Cli.csproj -m:1 --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Output $buildOutput
        throw "CLI build failed"
    }
    $buildOutput | Set-Content -LiteralPath (Join-Path $evidenceDirectory "dotnet-cli-build.txt") -Encoding utf8

    $probes = @(
        (Invoke-Probe "baseline" (Get-Content -LiteralPath $workbookPath -Raw) $true),
        (Invoke-Probe "counted-repetition" @"
record table Marks {
    key id: int = [10, 20, 30];
    valid: boolean = [true ... 3];
}
"@ $true 3),
        (Invoke-Probe "filled-column-first" @"
record table Marks {
    valid: boolean = [true ...];
    key id: int = [10, 20, 30];
}
"@ $true 3),
        (Invoke-Probe "table-with-fill" @"
record table Marks {
    key id: int = [10, 20, 30];
    valid: boolean = [true, true, true];
}
function cleared(): Marks {
    return Marks with { valid: [false ...] };
}
"@ $true 3)
    )

    $sourceReferences = @(
        @{ repository = "Concept"; path = "docs/language/INITIALIZERS.md" },
        @{ repository = "Concept"; path = "internal/concept/table_repetition_test.go" },
        @{ repository = "oct"; path = "Language/reference/language/07-arrays.md" },
        @{ repository = "oct"; path = "Language/reference/language/11-records.md" },
        @{ repository = "oct"; path = "internal/build/lower_expr.go" },
        @{ repository = "Copeland"; path = "src/Copeland/Copeland.TS/Syntax/Parser.cs" },
        @{ repository = "Copeland"; path = "src/Copeland/Copeland.TS/Semantics/Binder.cs" },
        @{ repository = "Copeland"; path = "src/Copeland/Copeland.Cli/TableToolCommand.cs" }
    )
    $auditedSources = foreach ($reference in $sourceReferences) {
        $sourceRoot = Join-Path (Split-Path $repositoryRoot -Parent) $reference.repository
        $sourcePath = Join-Path $sourceRoot $reference.path
        [ordered]@{
            repository = $reference.repository
            path = $reference.path
            commit = (& git -C $sourceRoot rev-parse HEAD)
            sha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
        }
    }
    $summary = [ordered]@{
        purpose = "Qualify implemented postfix repetition and table remainder fills through the real CLI"
        success = $true
        buildExitCode = 0
        maximumProbeSeconds = 15
        probes = $probes
        auditedSources = @($auditedSources)
        externalExecution = "Concept and Oct source inspected; their suites were not run"
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceDirectory "summary.json") -Encoding utf8
    Write-Output "TABLESCRIPT_ELLIPSIS_PASSED baseline=valid ellipsis=3_valid_forms"
}
finally {
    Pop-Location
}
