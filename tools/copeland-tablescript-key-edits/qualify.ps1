param(
    [string] $OutputDirectory = "artifacts/tablescript-key-edits"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$evidenceDirectory = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$null = New-Item -ItemType Directory -Path $evidenceDirectory -Force
$cli = Join-Path $repositoryRoot "src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll"
$workbook = Join-Path $repositoryRoot "samples/copeland-ts/tablescript-friction-m12/Workbook/Workbook.ts"
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
    Require (($exitCode -eq 0) -eq $ExpectedSuccess) "$Name returned unexpected exit code: $exitCode"
    if ($ExpectedSuccess -and $result.command -eq "table.query") {
        Require ($result.executor -eq "csharp-relation-plan") "$Name bypassed canonical query execution"
    }
    return $result
}

function Reject-Edit([string] $Name, [string] $Source, [string[]] $Arguments, [string] $Code) {
    $before = (Get-FileHash -LiteralPath $Source).Hash
    $result = Invoke-Table $Name $Arguments $false
    Require ($result.diagnostics[0].code -eq $Code) "$Name returned the wrong diagnostic"
    Require ((Get-FileHash -LiteralPath $Source).Hash -eq $before) "$Name changed rejected source"
}

Push-Location $repositoryRoot
try {
    & dotnet build src/Copeland/Copeland.Cli/Copeland.Cli.csproj -m:1 --nologo
    Require ($LASTEXITCODE -eq 0) "CLI build failed"
    $original = Get-Content -LiteralPath $workbook -Raw
    $ids = "key reference productId: int -> Products.id = [103, 100, 105, 102, 104, 101];"
    $onHand = "onHand: Units = [5, 4, 0, 2, 8, 10];"
    Require ($original.Contains($ids) -and $original.Contains($onHand)) "The bounded stock reordering specimen needs updating"
    $reordered = $original.Replace($ids, "key reference productId: int -> Products.id = [101, 104, 102, 105, 100, 103];")
    $reordered = $reordered.Replace($onHand, "onHand: Units = [10, 8, 2, 0, 4, 5];")
    $canonical = Invoke-Table "canonical-plan" @("query", $workbook, "PurchasePlan", "--order-by", "productId asc", "--format", "json")
    Require (($canonical.rows.orderCents | Measure-Object -Sum).Sum -eq 31210) "Canonical baseline total changed"
    $canonicalRows = ConvertTo-Json -InputObject @($canonical.rows) -Depth 8 -Compress
    $scenarios = @(
        @{ name = "authored-order"; source = $original; row = 1 },
        @{ name = "reversed-order"; source = $reordered; row = 4 }
    )
    $results = foreach ($scenario in $scenarios) {
        $name = $scenario.name
        $source = Join-Path $evidenceDirectory "$name.ts"
        [System.IO.File]::WriteAllText($source, $scenario.source)
        $baseline = Invoke-Table "$name-baseline" @("query", $source, "PurchasePlan", "--order-by", "productId asc", "--format", "json")
        Require ((ConvertTo-Json -InputObject @($baseline.rows) -Depth 8 -Compress) -eq $canonicalRows) "$name changed baseline keyed calculations"
        $before = (Get-FileHash -LiteralPath $source).Hash
        $preview = Invoke-Table "$name-preview" @("set", $source, "Stock", "--key", "100", "--column", "onHand", "--value", "12", "--dry-run", "--format", "json")
        Require ((Get-FileHash -LiteralPath $source).Hash -eq $before) "$name wrote a dry-run edit"
        Require ($preview.selection.row -eq $scenario.row) "$name resolved the wrong row"
        Require ($preview.selection.keyColumn -eq "productId" -and $preview.selection.value -eq 100) "$name lost declared key identity"
        $edit = Invoke-Table "$name-edit" @("set", $source, "Stock", "--key", "100", "--column", "onHand", "--value", "12", "--format", "json")
        Require ($edit.changed.row -eq $scenario.row -and $edit.changed.oldValue -eq 4 -and $edit.changed.newValue -eq 12) "$name changed the wrong cell"
        $changed = Invoke-Table "$name-changed" @("query", $source, "PurchasePlan", "--order-by", "productId asc", "--format", "json")
        $cents = ($changed.rows.orderCents | Measure-Object -Sum).Sum
        $units = ($changed.rows.orderUnits | Measure-Object -Sum).Sum
        $orders = @($changed.rows | Where-Object orderUnits -gt 0).Count
        Require ($cents -eq 12710 -and $units -eq 47 -and $orders -eq 2) "$name failed to recalculate the same scenario"
        foreach ($row in $changed.rows) {
            $originalRow = $baseline.rows | Where-Object productId -eq $row.productId
            if ($row.productId -eq 100) {
                Require ($row.onHand -eq 12 -and $row.orderUnits -eq 0) "$name did not replenish espresso"
            }
            else {
                Require ((ConvertTo-Json $row -Depth 8 -Compress) -eq (ConvertTo-Json $originalRow -Depth 8 -Compress)) "$name changed neighboring product $($row.productId)"
            }
        }
        Reject-Edit "$name-missing" $source @("set", $source, "Stock", "--key", "999", "--column", "onHand", "--value", "0", "--format", "json") "COPE-TABLE-TOOL-0023"
        Reject-Edit "$name-conflict" $source @("delete-row", $source, "Stock", "--key", "100", "--row", "$($scenario.row)", "--format", "json") "COPE-TABLE-TOOL-0021"
        Reject-Edit "$name-duplicate" $source @("set", $source, "Products", "--key", "101", "--column", "id", "--value", "100", "--format", "json") "COPE-TABLE-0037"
        Reject-Edit "$name-reference" $source @("delete-row", $source, "Products", "--key", "100", "--format", "json") "COPE-TABLE-0038"
        Reject-Edit "$name-fixed-snapshot" $source @("delete-row", $source, "Stock", "--key", "100", "--format", "json") "COPE-TABLE-0008"
        $survivors = Invoke-Table "$name-survivors" @("rows", $source, "Stock", "--format", "json")
        $espresso = $survivors.rows | Where-Object { $_.values.productId -eq 100 }
        Require ($survivors.rows.Count -eq 6 -and $espresso.values.onHand -eq 12) "$name changed stock after a rejected deletion"
        [ordered]@{ name = $name; selectedKey = 100; resolvedRow = $scenario.row; editedCents = $cents; editedUnits = $units; editedOrders = $orders }
    }
    $summary = [ordered]@{
        success = $true
        checks = $script:checks
        baselineCents = ($canonical.rows.orderCents | Measure-Object -Sum).Sum
        scenarios = @($results)
        sourceSha256 = (Get-FileHash -LiteralPath $workbook).Hash
        executor = "csharp-relation-plan"
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceDirectory "summary.json") -Encoding utf8
    Write-Output "TABLESCRIPT_KEY_EDITS_PASSED checks=$script:checks key=100 rows=1,4 edited=12710"
}
finally {
    Pop-Location
}
