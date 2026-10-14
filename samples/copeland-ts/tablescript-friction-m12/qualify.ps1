param(
    [string] $OutputDirectory = "artifacts/tablescript-friction-m12"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../../..")).Path
$evidenceDirectory = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$null = New-Item -ItemType Directory -Path $evidenceDirectory -Force
$cli = Join-Path $repoRoot "src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll"
$source = Join-Path $PSScriptRoot "Workbook/Workbook.ts"
$scenario = Join-Path $evidenceDirectory "Scenario.ts"
$script:checks = 0

function Require([bool] $Condition, [string] $Description) {
    if (-not $Condition) {
        throw $Description
    }
    $script:checks += 1
}

function Invoke-Table([string] $Name, [string[]] $Arguments, [bool] $ExpectedSuccess = $true) {
    $output = & dotnet $cli table @Arguments
    $exitCode = $LASTEXITCODE
    $json = $output -join [Environment]::NewLine
    $json | Set-Content -LiteralPath (Join-Path $evidenceDirectory "$Name.json") -Encoding utf8
    $result = $json | ConvertFrom-Json
    Require ($result.success -eq $ExpectedSuccess) "$Name returned unexpected success: $json"
    Require (($exitCode -eq 0) -eq $ExpectedSuccess) "$Name returned unexpected exit code $exitCode"
    if ($ExpectedSuccess -and $result.command -eq "table.query") {
        Require ($result.executor -eq "csharp-relation-plan") "$Name bypassed canonical execution"
    }
    return $result
}

function Reject-Edit([string] $Name, [string[]] $Arguments) {
    $before = (Get-FileHash -LiteralPath $scenario).Hash
    $result = Invoke-Table $Name $Arguments $false
    $after = (Get-FileHash -LiteralPath $scenario).Hash
    Require ($before -eq $after) "$Name changed rejected source"
    Require ($result.diagnostics.Count -gt 0) "$Name omitted repair diagnostics"
}

Push-Location $repoRoot
try {
    & dotnet build src/Copeland/Copeland.Cli/Copeland.Cli.csproj -m:1 --nologo
    Require ($LASTEXITCODE -eq 0) "CLI build failed"
    & dotnet build samples/copeland-ts/tablescript-friction-m12/TableScriptFrictionM12.slnx -m:1 --nologo
    Require ($LASTEXITCODE -eq 0) "workbook build failed"
    $consumer = & dotnet run --project samples/copeland-ts/tablescript-friction-m12/Consumer --no-build
    Require ($LASTEXITCODE -eq 0) "compiled workbook assertions failed"
    $consumer | Set-Content -LiteralPath (Join-Path $evidenceDirectory "consumer.txt") -Encoding utf8

    $plan = Invoke-Table "plan" @("query", $source, "PurchasePlan", "--order-by", "productId asc", "--format", "json")
    Require ($plan.rowCount -eq 6) "plan lost product rows"
    Require (($plan.rows.orderCents | Measure-Object -Sum).Sum -eq 31210) "incorrect base cents"
    Require (($plan.rows.orderUnits | Measure-Object -Sum).Sum -eq 67) "incorrect base units"
    $orders = Invoke-Table "orders" @("query", $source, "PurchasePlan", "--where", "orderUnits > 0", "--order-by", "orderCents desc", "--format", "json")
    Require ($orders.rowCount -eq 3) "incorrect actionable line count"
    Require (($orders.rows.productId -join ",") -eq "100,103,102") "sort did not preserve keyed results"
    $suppliers = Invoke-Table "suppliers" @("query", $source, "PurchasePlan", "--where", "orderUnits > 0", "--group-by", "supplierId, supplierName", "--aggregate", "sum(orderCents) as totalCents, sum(orderUnits) as units, count() as lines", "--order-by", "supplierId asc", "--format", "json")
    Require (($suppliers.rows.totalCents -join ",") -eq "18500,4960,7750") "incorrect supplier totals"
    $schema = Invoke-Table "schema" @("schema", $source, "PurchasePlan", "--format", "json")
    $orderProvenance = ($schema.columns | Where-Object name -eq "orderUnits").provenance
    Require (($orderProvenance.inputs -join ",") -eq "purchasing,reorderPoint,stock.onHand,targetStock") "match lost policy formula dependencies"
    Require (($orderProvenance.relationships -join ",") -eq "Stock.productId -> Products.id") "match lost stock relationship"
    $products = Invoke-Table "products" @("query", $source, "Products", "--format", "json")
    Require (($products.rows.purchasing -join ",") -eq "Active,Active,Active,Active,Active,Paused") "query lost purchasing enum cases"
    $stockInputs = Invoke-Table "stock" @("query", $source, "Stock", "--format", "json")
    $supplierInputs = Invoke-Table "source-suppliers" @("query", $source, "Suppliers", "--format", "json")

    Copy-Item -LiteralPath $source -Destination $scenario -Force
    $beforePreview = (Get-FileHash -LiteralPath $scenario).Hash
    $preview = Invoke-Table "preview" @("set", $scenario, "Stock", "--key", "100", "--column", "onHand", "--value", "12", "--dry-run", "--format", "json")
    Require ((Get-FileHash -LiteralPath $scenario).Hash -eq $beforePreview) "dry run changed source"
    $edit = Invoke-Table "edit" @("set", $scenario, "Stock", "--key", "100", "--column", "onHand", "--value", "12", "--format", "json")
    $changed = Invoke-Table "changed-plan" @("query", $scenario, "PurchasePlan", "--order-by", "productId asc", "--format", "json")
    Require (($changed.rows.orderCents | Measure-Object -Sum).Sum -eq 12710) "stock edit did not recalculate costs"
    Require ($changed.rows[0].orderUnits -eq 0) "replenished espresso still ordered"

    Reject-Edit "invalid-number" @("set", $scenario, "Stock", "--key", "100", "--column", "onHand", "--value", "four", "--format", "json")
    Reject-Edit "duplicate-key" @("set", $scenario, "Products", "--key", "101", "--column", "id", "--value", "100", "--format", "json")
    Reject-Edit "missing-supplier" @("set", $scenario, "Products", "--key", "100", "--column", "supplierId", "--value", "999", "--format", "json")
    Reject-Edit "derived-write" @("set", $scenario, "PurchasePlan", "--key", "100", "--column", "orderCents", "--value", "0", "--format", "json")
    $valid = Invoke-Table "validate" @("validate", $scenario, "--format", "json")
    $arrayBoundary = Join-Path $PSScriptRoot "inputs/array-query-boundary.ts"
    $legacyBoundary = Join-Path $PSScriptRoot "inputs/legacy-query-boundary.ts"
    $arrayFailure = Invoke-Table "array-query-boundary" @("query", $arrayBoundary, "Flags", "--format", "json") $false
    Require ($arrayFailure.diagnostics[0].code -eq "COPE-TABLE-QUERY-0016") "array query boundary changed"
    $legacyFailure = Invoke-Table "legacy-query-boundary" @("query", $legacyBoundary, "Values", "--executor", "legacy", "--format", "json") $false
    Require ($legacyFailure.diagnostics[0].code -eq "COPE-TABLE-QUERY-0016") "legacy query boundary changed"
    $summary = [ordered]@{
        success = $true
        checks = $script:checks
        baselineCents = 31210
        editedCents = 12710
        stockSelector = @{ keyColumn = "productId"; value = 100 }
        sourceSha256 = (Get-FileHash -LiteralPath $source).Hash
        executor = "csharp-relation-plan"
        nativeExcel = "not exercised"
    }
    $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDirectory "summary.json") -Encoding utf8
    Write-Output "TABLESCRIPT_M12_CLI_PASSED checks=$script:checks baseline=31210 edited=12710"
}
finally {
    Pop-Location
}
