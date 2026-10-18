param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "../Common.ps1")
Initialize-Qualification $RepositoryRoot $OutputDirectory

$baseline = Join-Path $PSScriptRoot "../inputs/Workbook.columns.ts"
$scenario = Join-Path $evidenceDirectory "Scenario.ts"

Push-Location $repositoryRoot
try {
    $before = Invoke-Table "plan.before" @("query", $baseline, "PurchasePlan", "--order-by", "productId asc", "--format", "json")
    $after = Invoke-Table "plan.after" @("query", $workbook, "PurchasePlan", "--order-by", "productId asc", "--format", "json")
    $canonicalRows = ConvertTo-Json -InputObject @($before.rows) -Depth 8 -Compress
    Require ((ConvertTo-Json -InputObject @($after.rows) -Depth 8 -Compress) -eq $canonicalRows) "Authoring changes altered purchase rows"
    $list = Invoke-Table "tables" @("list", $workbook, "--format", "json")
    Require (@($list.tables | Where-Object kind -eq "derived").Count -eq 1) "The workbook still needs an intermediate derived relation"
    $schema = Invoke-Table "schema" @("schema", $workbook, "PurchasePlan", "--format", "json")
    $cost = ($schema.columns | Where-Object name -eq "orderCents").provenance
    Require (($cost.inputs -join ",") -eq "costCents,purchasing,reorderPoint,stock.onHand,targetStock") "Local lost transitive cost dependencies"
    Require (($cost.relationships -join ",") -eq "Stock.productId -> Products.id") "Local lost relationship provenance"
    foreach ($target in @("mir", "csharp")) {
        & dotnet $cli compile $workbook --emit $target --out (Join-Path $evidenceDirectory "Workbook.$target")
        Require ($LASTEXITCODE -eq 0) "$target emission failed"
    }
    $csharp = Get-Content -LiteralPath (Join-Path $evidenceDirectory "Workbook.csharp") -Raw
    Require ([regex]::Matches($csharp, "int __derive_local_0 =").Count -eq 1) "Policy local was duplicated in generated code"

    Copy-Item -LiteralPath $workbook -Destination $scenario -Force
    $sourceBefore = Get-Content -LiteralPath $scenario -Raw
    $hashBefore = (Get-FileHash -LiteralPath $scenario).Hash
    $preview = Invoke-Table "row-preview" @("set", $scenario, "Suppliers", "--key", "10", "--column", "leadDays", "--value", "7", "--dry-run", "--format", "json")
    Require ((Get-FileHash -LiteralPath $scenario).Hash -eq $hashBefore) "Row dry run changed source"
    $edit = Invoke-Table "row-edit" @("set", $scenario, "Suppliers", "--key", "10", "--column", "leadDays", "--value", "7", "--format", "json")
    Require ((Get-Content -LiteralPath $scenario -Raw) -eq $sourceBefore.Replace('name: "North Roastery", leadDays: 3', 'name: "North Roastery", leadDays: 7')) "Row edit changed unrelated source"
    $changed = Invoke-Table "row-changed-plan" @("query", $scenario, "PurchasePlan", "--order-by", "productId asc", "--format", "json")
    Require (($changed.rows.orderCents | Measure-Object -Sum).Sum -eq 31210) "Supplier lead time changed purchasing costs"
    Require (@($changed.rows | Where-Object { $_.supplierId -eq 10 -and $_.leadDays -eq 7 }).Count -eq 3) "Supplier edit did not reach its three related products"
    $added = Invoke-Table "row-added" @("add-row", $scenario, "Suppliers", "--json", '{"id":40,"name":"Trial Supplier","leadDays":1}', "--format", "json")
    $deleted = Invoke-Table "row-deleted" @("delete-row", $scenario, "Suppliers", "--key", "40", "--format", "json")
    $csv = Join-Path $evidenceDirectory "Suppliers.csv"
    $exported = Invoke-Table "row-export" @("export", $scenario, "Suppliers", "--format", "csv", "--output", $csv, "--result-format", "json")
    $imported = Invoke-Table "row-import" @("import", $scenario, "Suppliers", "--format", "csv", "--input", $csv, "--replace", "--result-format", "json")
    Require ((Get-Content -LiteralPath $scenario -Raw).Contains('} = [')) "CSV import discarded row authoring"
    $roundTrip = Invoke-Table "round-trip-plan" @("query", $scenario, "PurchasePlan", "--order-by", "productId asc", "--format", "json")
    Require ((ConvertTo-Json -InputObject @($roundTrip.rows) -Depth 8 -Compress) -eq (ConvertTo-Json -InputObject @($changed.rows) -Depth 8 -Compress)) "Row CSV round trip changed calculated rows"
    $hashBeforeRejection = (Get-FileHash -LiteralPath $scenario).Hash
    $rejection = Invoke-Table "referenced-row-rejection" @("delete-row", $scenario, "Suppliers", "--key", "10", "--format", "json") $false
    Require ($rejection.diagnostics[0].code -eq "COPE-TABLE-0038") "Referenced supplier deletion omitted its exact diagnostic"
    Require ((Get-FileHash -LiteralPath $scenario).Hash -eq $hashBeforeRejection) "Rejected supplier deletion changed source"
    $summary = [ordered]@{
        success = $true
        checks = $script:checks
        baselineCents = 31210
        identicalPurchaseRows = $true
        derivedTablesBefore = 2
        derivedTablesAfter = 1
        executor = "csharp-relation-plan"
        sourceSha256 = (Get-FileHash -LiteralPath $workbook).Hash
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceDirectory "summary.json") -Encoding utf8
    Write-Output "TABLESCRIPT_ROW_AUTHORING_PASSED checks=$script:checks baseline=31210 derived=2->1"
}
finally {
    Pop-Location
}
