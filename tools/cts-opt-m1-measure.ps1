param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

$artifactRoot = Join-Path $RepositoryRoot "artifacts/cts-opt-m1"
$temporaryRoot = Join-Path $RepositoryRoot ".tmp/cts-opt-m1-measure"
$cliProject = Join-Path $RepositoryRoot "src/Copeland/Copeland.Cli/Copeland.Cli.csproj"

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null

function Write-ScalingSource {
    param(
        [int]$TableCount,
        [int]$ColumnCount,
        [int]$RowCount,
        [string]$Path
    )

    $rows = if ($RowCount -eq 0) { "" } else { (0..($RowCount - 1)) -join ", " }
    $builder = [System.Text.StringBuilder]::new()
    for ($tableIndex = 0; $tableIndex -lt $TableCount; $tableIndex += 1) {
        [void]$builder.AppendLine("record table T$tableIndex {")
        for ($columnIndex = 0; $columnIndex -lt $ColumnCount; $columnIndex += 1) {
            [void]$builder.AppendLine("    c$columnIndex`: int = [$rows];")
        }
        [void]$builder.AppendLine("}")
    }
    [void]$builder.AppendLine("function main(): int { return 0; }")
    [System.IO.File]::WriteAllText($Path, $builder.ToString(), [System.Text.UTF8Encoding]::new($false))
}

function Measure-Case {
    param(
        [string]$Axis,
        [int]$Value,
        [int]$TableCount,
        [int]$ColumnCount,
        [int]$RowCount
    )

    $sourcePath = Join-Path $temporaryRoot "$Axis-$Value.ts"
    $outputPath = Join-Path $temporaryRoot "$Axis-$Value.js"
    Write-ScalingSource -TableCount $TableCount -ColumnCount $ColumnCount -RowCount $RowCount -Path $sourcePath
    & dotnet run --no-build --project $cliProject -- compile $sourcePath --emit javascript --javascript-profile production --out $outputPath | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Compilation failed for scaling case $Axis=$Value."
    }

    $payloadText = if ($RowCount -eq 0) { "" } else { (0..($RowCount - 1)) -join ", " }
    $payloadBytes = [System.Text.Encoding]::UTF8.GetByteCount($payloadText) * $TableCount * $ColumnCount
    $totalBytes = (Get-Item -LiteralPath $outputPath).Length
    [ordered]@{
        axis = $Axis
        value = $Value
        tables = $TableCount
        columnsPerTable = $ColumnCount
        rows = $RowCount
        payloadBytes = $payloadBytes
        scaffoldBytes = $totalBytes - $payloadBytes
        totalBytes = $totalBytes
        scaffoldToPayloadRatio = if ($payloadBytes -eq 0) { $null } else { [Math]::Round(($totalBytes - $payloadBytes) / $payloadBytes, 6) }
    }
}

$cases = [System.Collections.Generic.List[object]]::new()
foreach ($count in @(1, 2, 5, 10)) {
    $cases.Add((Measure-Case -Axis "tables" -Value $count -TableCount $count -ColumnCount 2 -RowCount 2))
}
foreach ($count in @(1, 2, 5, 10, 20)) {
    $cases.Add((Measure-Case -Axis "columns" -Value $count -TableCount 1 -ColumnCount $count -RowCount 2))
}
foreach ($count in @(0, 1, 10, 100, 1000, 10000)) {
    $cases.Add((Measure-Case -Axis "rows" -Value $count -TableCount 1 -ColumnCount 2 -RowCount $count))
}

$result = [ordered]@{
    milestone = "CTS-OPT-M1"
    profile = "Production"
    measurement = "UTF-8 emitted bytes; payload is the exact repeated numeric literal text"
    cases = $cases
}
$json = $result | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText(
    (Join-Path $artifactRoot "scaling-results.json"),
    $json + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

Write-Output $json
