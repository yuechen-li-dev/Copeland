param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "../Common.ps1")
Initialize-Qualification $RepositoryRoot $OutputDirectory

$null = Invoke-Table "workbook" @("validate", $workbook, "--format", "json")
foreach ($name in @("counted-repetition", "filled-column-first", "table-with-fill")) {
    $source = Join-Path $PSScriptRoot "../inputs/$name.ts"
    $result = Invoke-Table $name @("validate", $source, "--format", "json")
    Require ($result.totalRows -eq 3) "$name changed the expected row count"
}
[ordered]@{
    success = $true
    checks = $script:checks
    forms = @("counted-repetition", "filled-column-first", "table-with-fill")
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidenceDirectory "summary.json") -Encoding utf8
Write-Output "TABLESCRIPT_INITIALIZERS_PASSED checks=$script:checks"
