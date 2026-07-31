[CmdletBinding()]
param(
    [string]$ExpectedVersion
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$authorityPath = Join-Path $repositoryRoot "Directory.Build.props"
$authorityText = Get-Content -LiteralPath $authorityPath -Raw
$authorityMatch = [regex]::Match(
    $authorityText,
    '<CopelandToolchainVersion>([^<]+)</CopelandToolchainVersion>')
if (-not $authorityMatch.Success) {
    throw "Directory.Build.props does not define CopelandToolchainVersion."
}

$version = $authorityMatch.Groups[1].Value
if ($ExpectedVersion -and $version -ne $ExpectedVersion) {
    throw "Version authority is '$version'; expected '$ExpectedVersion'."
}

$checks = @(
    @{ Path = "src/Copeland/Copeland.TS.LanguageServer/LanguageServerHost.cs"; Pattern = "public const string Version = `"$version`";" },
    @{ Path = "src/Copeland/Copeland.TS.MSBuild/build/Copeland.TS.Sdk.props"; Pattern = ">$version</CopelandSdkVersion>" },
    @{ Path = "src/Copeland/Copeland.TS.VSCode/package.json"; Pattern = "`"version`": `"$version`"" },
    @{ Path = "src/Copeland/Copeland.TS.VSCode/package-lock.json"; Pattern = "`"version`": `"$version`"" },
    @{ Path = "src/Copeland/Copeland.TS.Npm/package.json"; Pattern = "`"version`": `"$version`"" },
    @{ Path = "samples/copeland-ts/templates/BootstrapTemplate.tsx"; Pattern = "const packageVersion = `"$version`";" },
    @{ Path = "samples/copeland-ts/CopelandHello/CopelandHello/CopelandHello.csproj"; Pattern = ">$version</CopelandRequiredVersion>" },
    @{ Path = "samples/copeland-ts/CopelandHello/CopelandHello/package.json"; Pattern = "`"version`": `"$version`"" },
    @{ Path = "src/Copeland/Copeland.TS.Templates/templates/copeland-console/Copeland.Console.csproj"; Pattern = ">$version</CopelandRequiredVersion>" },
    @{ Path = "src/Copeland/Copeland.TS.Templates/templates/copeland-library/Copeland.Library.csproj"; Pattern = ">$version</CopelandRequiredVersion>" },
    @{ Path = "src/Copeland/Copeland.TS.Templates/templates/copeland-react/Copeland.React.csproj"; Pattern = ">$version</CopelandRequiredVersion>" },
    @{ Path = "src/Copeland/Copeland.TS.Templates/templates/copeland-workspace/Copeland.Workspace.csproj"; Pattern = ">$version</CopelandRequiredVersion>" }
)

foreach ($check in $checks) {
    $path = Join-Path $repositoryRoot $check.Path
    $text = Get-Content -LiteralPath $path -Raw
    if (-not $text.Contains($check.Pattern, [StringComparison]::Ordinal)) {
        throw "$($check.Path) does not contain coordinated version '$version'."
    }
}

$scanRoots = @(
    "src/Copeland",
    "samples/copeland-ts",
    "docs/Copeland",
    "tools"
)
$stalePattern = '0\.1\.0-preview\.1-tsxml\.1'
$staleFiles = foreach ($relativeRoot in $scanRoots) {
    $root = Join-Path $repositoryRoot $relativeRoot
    Get-ChildItem -LiteralPath $root -Recurse -File |
        Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj|node_modules|out|dist|\.vscode-test|\.copeland)[\\/]' -and
            $_.Extension -in @(".cs", ".csproj", ".props", ".targets", ".json", ".ts", ".tsx", ".md", ".ps1", ".yml", ".yaml")
        } |
        Select-String -Pattern $stalePattern |
        ForEach-Object { "$($_.Path):$($_.LineNumber)" }
}
if ($staleFiles) {
    throw "Stale candidate version found:`n$($staleFiles -join [Environment]::NewLine)"
}

Write-Output "Coordinated Copeland release version: $version"
