$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$guard = Join-Path $repositoryRoot "tools/Test-ArtifactBudget.ps1"
$fixtureRoot = Join-Path $PSScriptRoot "Fixtures/ArtifactBudget"
$pwsh = (Get-Command pwsh -ErrorAction Stop).Source

$cases = @(
    @{ name = "compact evidence"; path = "artifacts/example/compact.json"; pass = $true; message = "Artifact budget check passed" },
    @{ name = "explicit golden"; path = "artifacts/example/goldens/expected.g.js"; pass = $true; message = "Artifact budget check passed" },
    @{ name = "generated source"; path = "artifacts/example/generated.cs"; pass = $false; message = "not a compact text artifact" },
    @{ name = "scratch dot preserved"; path = "./.tmp/probe.zip"; pass = $false; message = "scratch material" },
    @{ name = "cached output"; path = "src/Copeland/Compiler/bin/compiler.dll"; pass = $false; message = "local/cache output" },
    @{ name = "local artifacts"; path = "artifacts/local/probe/report.json"; pass = $false; message = "local/cache output" },
    @{ name = "docs package"; path = "docs/Copeland/compiler.tgz"; pass = $false; message = "delivery payload" },
    @{ name = "sample publish"; path = "samples/copeland-ts/example/publish/runtime.wasm"; pass = $false; message = "delivery payload" },
    @{ name = "path traversal"; path = "../outside.json"; pass = $false; message = "escapes the repository" },
    @{ name = "missing artifact"; path = "artifacts/example/missing.json"; pass = $false; message = "not a file in the checkout" },
    @{ name = "byte limit"; path = "artifacts/example/compact.json"; pass = $false; message = "compact-artifact limit is 1 bytes"; extra = @("-MaximumFileBytes", "1") },
    @{ name = "root file limit"; path = "artifacts/example/compact.json"; pass = $false; message = "compact-artifact limit is 0"; extra = @("-MaximumFilesPerArtifactRoot", "0") },
    @{ name = "ordinary source"; path = "src/Copeland/Compiler/Parser.cs"; pass = $true; message = "Artifact budget check passed" },
    @{ name = "binary test fixture"; path = "tests/Copeland/fixtures/assembly.dll"; pass = $true; message = "Artifact budget check passed" }
)

foreach ($case in $cases) {
    $arguments = @("-NoProfile", "-File", $guard, "-RepositoryRoot", $fixtureRoot, "-Paths", $case.path)
    if ($case.extra) {
        $arguments += $case.extra
    }
    $output = & $pwsh @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $text = $output -join [Environment]::NewLine
    if (($exitCode -eq 0) -ne $case.pass -or -not $text.Contains($case.message)) {
        throw "Artifact policy case '$($case.name)' failed: $text"
    }
    Write-Output "Passed: $($case.name)"
}
Write-Output "ARTIFACT_POLICY_PASSED cases=$($cases.Count)"
