param(
    [string] $OutputDirectory = "artifacts/tablescript-typed-summaries"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$evidenceDirectory = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$null = New-Item -ItemType Directory -Path $evidenceDirectory -Force
$probeDirectory = Join-Path $evidenceDirectory "probe"
$null = New-Item -ItemType Directory -Path $probeDirectory -Force
$cli = Join-Path $repositoryRoot "src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll"
$workbook = Join-Path $repositoryRoot "samples/copeland-ts/tablescript-friction-m12/Workbook/Workbook.ts"
$scenario = Join-Path $evidenceDirectory "Scenario.ts"
$script:checks = 0

function Require([bool] $Condition, [string] $Description) {
    if (-not $Condition) {
        throw $Description
    }
    $script:checks += 1
}

function Invoke-Table([string] $Name, [string[]] $Arguments) {
    $output = & dotnet $cli table @Arguments
    $exitCode = $LASTEXITCODE
    $json = $output -join [Environment]::NewLine
    $json | Set-Content -LiteralPath (Join-Path $evidenceDirectory "$Name.json") -Encoding utf8
    $result = $json | ConvertFrom-Json
    Require ($exitCode -eq 0 -and $result.success) "$Name failed: $json"
    if ($result.command -eq "table.query") {
        Require ($result.executor -eq "csharp-relation-plan") "$Name bypassed canonical query execution"
    }
    return $result
}

function Invoke-Typed([string] $Name) {
    & dotnet $cli compile $scenario --emit csharp --out (Join-Path $probeDirectory "Workbook.g.cs") | Out-Host
    Require ($LASTEXITCODE -eq 0) "$Name C# emission failed"
    & dotnet build (Join-Path $probeDirectory "Probe.csproj") -m:1 --nologo | Out-Host
    Require ($LASTEXITCODE -eq 0) "$Name probe build failed"
    $output = & dotnet (Join-Path $probeDirectory "bin/Debug/net10.0/Probe.dll")
    Require ($LASTEXITCODE -eq 0) "$Name probe execution failed"
    $json = $output -join [Environment]::NewLine
    $json | Set-Content -LiteralPath (Join-Path $evidenceDirectory "$Name.typed.json") -Encoding utf8
    return $json | ConvertFrom-Json
}

function Compare-Summaries([string] $Name, [int] $Cents, [int] $Units, [int] $Lines) {
    $typed = Invoke-Typed $Name
    $totals = Invoke-Table "$Name.totals" @("query", $scenario, "PurchasePlan", "--where", "orderUnits > 0", "--aggregate", "sum(orderCents) as totalCents, sum(orderUnits) as units, count() as lines", "--format", "json")
    $suppliers = Invoke-Table "$Name.suppliers" @("query", $scenario, "PurchasePlan", "--where", "orderUnits > 0", "--group-by", "supplierId, supplierName", "--aggregate", "sum(orderCents) as totalCents, sum(orderUnits) as units, count() as lines", "--format", "json")
    Require ($typed.totals.totalCents -eq $Cents -and $typed.totals.units -eq $Units -and $typed.totals.lines -eq $Lines) "$Name typed totals differ from the motivating workbook"
    Require ($totals.rows[0].totalCents -eq $typed.totals.totalCents) "$Name total cents disagree"
    Require ($totals.rows[0].units -eq $typed.totals.units) "$Name total units disagree"
    Require ($totals.rows[0].lines -eq $typed.totals.lines) "$Name total line count disagrees"
    Require ($suppliers.rows.Count -eq $typed.suppliers.Count) "$Name supplier count disagrees"
    for ($index = 0; $index -lt $typed.suppliers.Count; $index += 1) {
        foreach ($field in @("supplierId", "supplierName", "totalCents", "units", "lines")) {
            Require ($suppliers.rows[$index].$field -ceq $typed.suppliers[$index].$field) "$Name supplier $index field $field disagrees or group order changed"
        }
    }
}

Push-Location $repositoryRoot
try {
    $originalHash = (Get-FileHash -LiteralPath $workbook).Hash
    & dotnet build src/Copeland/Copeland.Cli/Copeland.Cli.csproj -m:1 --nologo
    Require ($LASTEXITCODE -eq 0) "CLI build failed"
    & dotnet build samples/copeland-ts/tablescript-friction-m12/TableScriptFrictionM12.slnx -m:1 --nologo
    Require ($LASTEXITCODE -eq 0) "MSBuild workbook build failed"
    $consumer = & dotnet run --project samples/copeland-ts/tablescript-friction-m12/Consumer --no-build
    Require ($LASTEXITCODE -eq 0) "MSBuild consumer assertions failed"
    $consumer | Set-Content -LiteralPath (Join-Path $evidenceDirectory "consumer.txt") -Encoding utf8
    $probeSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot "Probe.ts") -Raw
    ((Get-Content -LiteralPath $workbook -Raw) + [Environment]::NewLine + $probeSource) |
        Set-Content -LiteralPath $scenario -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "ProbeRunner.cs") -Destination (Join-Path $probeDirectory "Program.cs") -Force
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $probeDirectory "Probe.csproj") -Encoding utf8

    Compare-Summaries "before" 31210 67 3
    $null = Invoke-Table "stock-edit" @("set", $scenario, "Stock", "--key", "100", "--column", "onHand", "--value", "12", "--format", "json")
    Compare-Summaries "after" 12710 47 2
    & dotnet $cli compile $scenario --emit mir --out (Join-Path $evidenceDirectory "Scenario.mir")
    Require ($LASTEXITCODE -eq 0) "Summary MIR emission failed"
    $mir = Get-Content -LiteralPath (Join-Path $evidenceDirectory "Scenario.mir") -Raw
    Require ($mir.Contains("table-summary")) "Typed summary facts are absent from MIR"
    Require ($mir.Contains("inputs(costCents,purchasing,reorderPoint,stock.onHand,targetStock) relationships(Stock.productId -> Products.id)")) "Summary lost cost dependencies or stock relationship provenance"
    Require ((Get-FileHash -LiteralPath $workbook).Hash -eq $originalHash) "Qualification changed the authored workbook"
    [ordered]@{
        success = $true
        checks = $script:checks
        before = @{ totalCents = 31210; units = 67; lines = 3 }
        after = @{ totalCents = 12710; units = 47; lines = 2 }
        canonicalExecutor = "csharp-relation-plan"
        allSupplierFieldsAndOrderAgree = $true
        sourceSha256 = $originalHash
        javascript = "unsupported: COPE-JS-SUMMARY-0001"
        asyncDirect = "unsupported: COPE-SUMMARY-0011; use a synchronous helper"
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceDirectory "summary.json") -Encoding utf8
    Write-Output "TABLESCRIPT_TYPED_SUMMARIES_PASSED checks=$script:checks before=31210/67/3 after=12710/47/2"
}
finally {
    Pop-Location
}
