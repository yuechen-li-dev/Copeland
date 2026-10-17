$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../../..")).Path
$evidence = Join-Path $repositoryRoot "artifacts/file-profiles"
$null = New-Item -ItemType Directory -Path $evidence -Force
$cli = Join-Path $repositoryRoot "src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll"
$legacy = Join-Path $PSScriptRoot "src/legacy/Legacy.ts"
$original = Get-Content -LiteralPath $legacy -Raw
$checks = 0

function Require([bool] $Condition, [string] $Description) {
    if (-not $Condition) { throw $Description }
    $script:checks += 1
}

Push-Location $PSScriptRoot
try {
    Require (Test-Path "node_modules/typescript/bin/tsc") "Run npm ci in the sample first."
    $sync = & dotnet $cli workspace sync --format json
    Require ($LASTEXITCODE -eq 0) "Workspace synchronization failed: $sync"
    $sync | Set-Content (Join-Path $evidence "sync.json")
    $owner = & dotnet $cli workspace owner src/shared/Math.ts --format json | ConvertFrom-Json
    Require ($LASTEXITCODE -eq 0 -and $owner.owner -eq "tscl" -and $owner.matchedRule -eq "CopelandProfile") "Shared source did not resolve from its header."
    & dotnet $cli compile $legacy
    Require ($LASTEXITCODE -eq 0) "Delegated TypeScript compilation failed."
    $compiled = @(Get-ChildItem obj/tsc -Recurse -Filter Legacy.js)
    Require ($compiled.Count -eq 1) "Expected one compiled Legacy.js."
    Require (-not (Get-Content $compiled[0].FullName -Raw).Contains("CopelandProfile")) "TypeScript did not erase the profile alias."
    $legacyOutput = & node $compiled[0].FullName
    Require ($LASTEXITCODE -eq 0 -and $legacyOutput -eq "PROFILE_LEGACY_PASSED answer=42") "Compiled Legacy behavior differs."
    $legacyOutput | Set-Content (Join-Path $evidence "legacy.txt")

    Add-Content -LiteralPath $legacy -Value 'export const invalid: number = "wrong";'
    $direct = & node node_modules/typescript/bin/tsc --project obj/copeland/workspace/tsconfig.generated.json --noEmit --pretty false
    $directCode = $LASTEXITCODE
    $delegated = & dotnet $cli compile $legacy -- --noEmit --pretty false
    $delegatedCode = $LASTEXITCODE
    Require ($directCode -ne 0 -and $directCode -eq $delegatedCode) "Delegation changed TypeScript's failure exit status."
    Require (($direct -join "`n") -ceq ($delegated -join "`n")) "Delegation changed TypeScript's diagnostics."
    Require (($delegated -join "`n").Contains("TS2322")) "The real compiler did not reject the invalid TypeScript assignment."
    $delegated | Set-Content (Join-Path $evidence "tsc-diagnostics.txt")
    [System.IO.File]::WriteAllText($legacy, $original)

    & dotnet build App.csproj -m:1 --nologo
    Require ($LASTEXITCODE -eq 0) "Profiled Copeland MSBuild compilation failed."
    $dotnetOutput = & dotnet run --project App.csproj --no-build
    Require ($LASTEXITCODE -eq 0 -and $dotnetOutput -eq "PROFILE_DOTNET_PASSED answer=42") "Compiled .NET behavior differs."
    $dotnetOutput | Set-Content (Join-Path $evidence "dotnet.txt")
    $jsSource = Join-Path $evidence "Main.ts"
    $jsOutput = Join-Path $evidence "main.mjs"
    @'
export type CopelandProfile = "JavaScript";
record Value { value: int; }
export function answer(): int {
    const data: Value = { value: 42 };
    return data.value;
}
'@ | Set-Content -LiteralPath $jsSource
    & dotnet $cli compile $jsSource --javascript-profile production --out $jsOutput
    Require ($LASTEXITCODE -eq 0) "Profile-selected JavaScript emission failed."
    $request = Join-Path $evidence "project.json"
    $jsDirectory = Join-Path $evidence "javascript"
    @{
        projectRoot = $evidence
        entry = @{ module = "Main.ts"; export = "answer" }
        sources = @(@{ logicalPath = "Main.ts"; path = $jsSource })
        outputDirectory = $jsDirectory
        javascriptRuntime = "node"
        entryOutputPath = "run.js"
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $request
    & dotnet $cli build --project $request --result (Join-Path $evidence "javascript-build.json")
    Require ($LASTEXITCODE -eq 0) "Profiled JavaScript project build failed."
    $javascriptOutput = & node (Join-Path $jsDirectory "run.js")
    Require ($LASTEXITCODE -eq 0 -and $javascriptOutput -eq "42") "Compiled JavaScript behavior differs."
    $javascriptOutput | Set-Content (Join-Path $evidence "javascript.txt")
    $mismatch = & dotnet $cli compile $jsSource --emit csharp 2>&1
    Require ($LASTEXITCODE -ne 0 -and ($mismatch -join "`n").Contains("COPE-PROFILE-0004")) "A mismatched emitter was accepted."
    [ordered]@{ success = $true; checks = $checks; typescript = (& node node_modules/typescript/bin/tsc --version); directExit = $directCode; delegatedExit = $delegatedCode } |
        ConvertTo-Json | Set-Content (Join-Path $evidence "summary.json")
    Write-Output "FILE_PROFILES_PASSED checks=$checks legacy=42 dotnet=42 javascript=42"
}
finally {
    [System.IO.File]::WriteAllText($legacy, $original)
    Pop-Location
}
