param(
    [string] $OutputDirectory = "artifacts/copeland-typescript-instinct-m26/raw"
)

$ErrorActionPreference = "Stop"
$repository = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$sample = Join-Path $repository "samples/copeland-ts/typescript-instinct-m26-benchmark"
$output = Join-Path $repository $OutputDirectory
New-Item -ItemType Directory -Path $output -Force | Out-Null

foreach ($name in @("fib", "nbody", "trees", "sieve", "arrays", "strings", "objects", "closures", "native-strings", "maps")) {
    $clrResult = & dotnet (Join-Path $sample "bin/Release/net10.0/CopeBench.dll") full $name
    if ($LASTEXITCODE -ne 0) {
        throw "CLR benchmark failed: $name"
    }
    (($clrResult -join "`n") + "`n") | Set-Content -LiteralPath (Join-Path $output "copeland-$name.json") -Encoding utf8NoBOM -NoNewline

    $nodeResult = & node (Join-Path $sample "js/twin.mjs") full $name
    if ($LASTEXITCODE -ne 0) {
        throw "Node benchmark failed: $name"
    }
    (($nodeResult -join "`n") + "`n") | Set-Content -LiteralPath (Join-Path $output "node-$name.json") -Encoding utf8NoBOM -NoNewline
    Write-Output "Measured $name"
}
