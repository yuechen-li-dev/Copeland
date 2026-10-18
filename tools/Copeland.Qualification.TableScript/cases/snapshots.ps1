param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "../Common.ps1")
Initialize-Qualification $RepositoryRoot $OutputDirectory

$probeSource = Join-Path $evidenceDirectory "Probe.ts"
$oracleSource = Join-Path $evidenceDirectory "EditedWorkbook.ts"

function Compare-Summary([string] $Name, [string] $Source, $Actual, [int] $Cents, [int] $Units, [int] $Lines) {
    $plan = Invoke-Table "$Name.plan" @("query", $Source, "PurchasePlan", "--format", "json")
    $suppliers = Invoke-Table "$Name.suppliers" @("query", $Source, "PurchasePlan", "--where", "orderUnits > 0", "--group-by", "supplierId, supplierName", "--aggregate", "sum(orderCents) as totalCents, sum(orderUnits) as units, count() as lines", "--format", "json")
    Require ($Actual.rows.Count -eq $plan.rows.Count) "$Name row count changed"
    for ($index = 0; $index -lt $plan.rows.Count; $index += 1) {
        foreach ($field in @("productId", "productName", "supplierId", "supplierName", "leadDays", "onHand", "orderUnits", "orderCents")) {
            Require ($Actual.rows[$index].$field -ceq $plan.rows[$index].$field) "$Name row $index field $field differs"
        }
    }
    Require ($Actual.suppliers.Count -eq $suppliers.rows.Count) "$Name supplier count changed"
    for ($index = 0; $index -lt $suppliers.rows.Count; $index += 1) {
        foreach ($field in @("supplierId", "supplierName", "totalCents", "units", "lines")) {
            Require ($Actual.suppliers[$index].$field -ceq $suppliers.rows[$index].$field) "$Name supplier $index field $field differs"
        }
    }
    Require ($Actual.totals.totalCents -eq $Cents -and $Actual.totals.units -eq $Units -and $Actual.totals.lines -eq $Lines) "$Name typed total differs"
}

Push-Location $repositoryRoot
try {
    $originalHash = (Get-FileHash -LiteralPath $workbook).Hash

    $source = Get-Content -LiteralPath $workbook -Raw
    $summaryProbe = Get-Content -LiteralPath (Join-Path $PSScriptRoot "../inputs/Summaries.ts") -Raw
    $scenarioProbe = Get-Content -LiteralPath (Join-Path $PSScriptRoot "../inputs/Snapshots.ts") -Raw
    ($source + [Environment]::NewLine + $summaryProbe + [Environment]::NewLine + $scenarioProbe) |
        Set-Content -LiteralPath $probeSource -Encoding utf8
    $probeHash = (Get-FileHash -LiteralPath $probeSource).Hash
    New-ProbeProject (Join-Path $PSScriptRoot "../inputs/SnapshotRunner.cs")
    & dotnet $cli compile $probeSource --emit csharp --out (Join-Path $probeDirectory "Workbook.g.cs")
    Require ($LASTEXITCODE -eq 0) "Probe C# emission failed"
    & dotnet build (Join-Path $probeDirectory "Probe.csproj") -m:1 --nologo
    Require ($LASTEXITCODE -eq 0) "Probe build failed"
    $output = & dotnet (Join-Path $probeDirectory "bin/Debug/net10.0/Probe.dll")
    Require ($LASTEXITCODE -eq 0) "Probe execution failed"
    $json = $output -join [Environment]::NewLine
    $json | Set-Content -LiteralPath (Join-Path $evidenceDirectory "runtime.json") -Encoding utf8
    $actual = $json | ConvertFrom-Json
    Compare-Summary "before" $workbook $actual.before 31210 67 3

    # This copy is an independent CLI oracle; the runtime probe never reads it.
    Copy-Item -LiteralPath $workbook -Destination $oracleSource -Force
    $null = Invoke-Table "oracle.stock-edit" @("set", $oracleSource, "Stock", "--key", "100", "--column", "onHand", "--value", "12", "--format", "json")
    Compare-Summary "preview" $oracleSource $actual.preview 12710 47 2
    Require ((ConvertTo-Json $actual.before -Depth 12 -Compress) -ceq (ConvertTo-Json $actual.after -Depth 12 -Compress)) "Preview mutated the global workbook"
    Require ((ConvertTo-Json $actual.preview -Depth 12 -Compress) -ceq (ConvertTo-Json $actual.repeated -Depth 12 -Compress)) "Repeated preview differs"
    Require ((Get-FileHash -LiteralPath $workbook).Hash -eq $originalHash) "Preview changed the authored workbook"
    Require ((Get-FileHash -LiteralPath $probeSource).Hash -eq $probeHash) "Runtime preview changed its input source"
    & dotnet $cli compile $workbook --emit mir --out (Join-Path $evidenceDirectory "Workbook.mir")
    Require ($LASTEXITCODE -eq 0) "MIR emission failed"
    $mir = Get-Content -LiteralPath (Join-Path $evidenceDirectory "Workbook.mir") -Raw
    Require ($mir.Contains("table-recalculate")) "MIR lacks snapshot-relative execution facts"
    $generated = Get-Content -LiteralPath (Join-Path $probeDirectory "Workbook.g.cs") -Raw
    Require ([regex]::Matches($generated, "int __derive_local_0 =").Count -eq 1) "Preview duplicated the compiled purchasing policy"
    [ordered]@{
        success = $true
        checks = $script:checks
        originalCents = 31210
        previewCents = 12710
        previewUnits = 47
        previewLines = 2
        allRowsAndSupplierFieldsAgree = $true
        globalWorkbookUnchanged = $true
        repeatedPreviewIdentical = $true
        runtimeSourceUnchanged = $true
        sourceSha256 = $originalHash
        canonicalExecutor = "csharp-relation-plan"
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceDirectory "summary.json") -Encoding utf8
    Write-Output "TABLESCRIPT_SCENARIOS_PASSED checks=$script:checks original=31210 preview=12710 globals=unchanged"
}
finally {
    Pop-Location
}
