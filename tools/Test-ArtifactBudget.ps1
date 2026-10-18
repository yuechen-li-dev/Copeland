[CmdletBinding()]
param(
    [string] $BaseRef,
    [string[]] $Paths,
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot ".."),
    [string] $AllowlistPath = ".github/artifact-budget-allowlist.txt",
    [int64] $MaximumFileBytes = 262144,
    [int] $MaximumFilesPerArtifactRoot = 16
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path

function Normalize-RepositoryPath {
    param([string] $Path)

    $absolutePath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
    $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $absolutePath).Replace("\", "/")
    if ($relativePath -eq ".." -or $relativePath.StartsWith("../") -or [IO.Path]::IsPathRooted($relativePath)) {
        throw "Artifact path escapes the repository: $Path"
    }
    return $relativePath
}

function Read-AllowlistPatterns {
    param([string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return @()
    }

    return @(
        Get-Content -LiteralPath $Path |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ -and -not $_.StartsWith("#") }
    )
}

function Test-Allowlisted {
    param(
        [string] $Path,
        [string[]] $Patterns
    )

    foreach ($pattern in $Patterns) {
        if ($Path -like $pattern) {
            return $true
        }
    }

    return $false
}

function Get-CandidatePaths {
    if ($Paths) {
        return @($Paths | ForEach-Object { Normalize-RepositoryPath $_ })
    }

    if ($BaseRef) {
        $diffPaths = & git -C $repositoryRoot -c core.quotepath=false diff --diff-filter=ACMR --name-only "$BaseRef...HEAD"
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to list files added since '$BaseRef'."
        }

        return @($diffPaths | ForEach-Object { Normalize-RepositoryPath $_ })
    }

    $indexPaths = & git -C $repositoryRoot -c core.quotepath=false diff --cached --diff-filter=ACMR --name-only
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to list files added to the index."
    }

    return @($indexPaths | ForEach-Object { Normalize-RepositoryPath $_ })
}

$allowedCompactExtensions = @(
    ".csv",
    ".json",
    ".md",
    ".toml",
    ".tson",
    ".txt"
)

$allowlistPatterns = Read-AllowlistPatterns -Path (Join-Path $repositoryRoot $AllowlistPath)
$candidatePaths = @(Get-CandidatePaths | Sort-Object -Unique)
$artifactPaths = @($candidatePaths | Where-Object {
    $_ -like "artifacts/*" -or $_ -like "docs/Copeland/reviews/evidence/*"
})
$violations = [System.Collections.Generic.List[string]]::new()

foreach ($path in $candidatePaths) {
    if (Test-Allowlisted -Path $path -Patterns $allowlistPatterns) {
        continue
    }
    if ($path -match '^(\.tmp|tmp|\.work)/' -or $path -eq 'copeland-bench-fixes.patch') {
        $violations.Add("$path is repository-local scratch material, not maintained source.")
    }
    elseif ($path -match '(^|/)(bin|obj|node_modules|\.vscode-test|\.tspack)(/|$)' -or $path -like 'artifacts/local/*') {
        $violations.Add("$path is generated local/cache output and must not be tracked.")
    }
    elseif ($path -match '^(src/Copeland/|docs/Copeland/|samples/copeland-ts/)' -and
        ($path -match '/publish/' -or $path -match '\.(nupkg|snupkg|vsix|tgz|zip|dll|exe|pdb|wasm|br|gz)$')) {
        $violations.Add("$path is a Copeland delivery payload. Keep source and compact provenance in Git; publish payloads outside source/docs.")
    }
}

$artifactGroups = $artifactPaths | Group-Object {
    $parts = $_.Split("/")
    if ($_ -like "docs/Copeland/reviews/evidence/*") {
        return ($parts[0..4] -join "/")
    }
    if ($parts.Length -ge 2) {
        return "artifacts/$($parts[1])"
    }

    return $_
}

foreach ($group in $artifactGroups) {
    $unlistedPaths = @(
        $group.Group |
            Where-Object { -not (Test-Allowlisted -Path $_ -Patterns $allowlistPatterns) }
    )

    if ($unlistedPaths.Count -gt $MaximumFilesPerArtifactRoot) {
        $violations.Add(
            "$($group.Name) adds $($unlistedPaths.Count) files; the compact-artifact limit is $MaximumFilesPerArtifactRoot."
        )
    }
}

foreach ($path in $artifactPaths) {
    if (Test-Allowlisted -Path $path -Patterns $allowlistPatterns) {
        continue
    }

    if ($path -match "^artifacts/m[0-9][^/]*/") {
        $violations.Add("$path is a milestone artifact bundle path.")
        continue
    }

    $absolutePath = Join-Path $repositoryRoot $path
    if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
        $violations.Add("$path is listed as an added artifact but is not a file in the checkout.")
        continue
    }

    $file = Get-Item -LiteralPath $absolutePath
    if ($file.Length -gt $MaximumFileBytes) {
        $violations.Add(
            "$path is $($file.Length) bytes; the compact-artifact limit is $MaximumFileBytes bytes."
        )
    }

    $extension = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
    $isGoldenFixture = $path -match "(^|/)(golden|goldens|fixtures)(/|$)"
    if (-not $isGoldenFixture -and $extension -notin $allowedCompactExtensions) {
        $violations.Add(
            "$path is not a compact text artifact. Put generated media/playback outside git or add a reviewed allowlist exception."
        )
    }
}

if ($violations.Count -gt 0) {
    [Console]::Error.WriteLine(
        "Artifact budget check failed:`n - " +
        ($violations -join "`n - ")
    )
    exit 1
}

Write-Host (
    "Artifact budget check passed: $($candidatePaths.Count) changed paths, $($artifactPaths.Count) compact artifact/evidence files. " +
    "Compact manifests and golden fixtures remain eligible; exceptions require the reviewed allowlist."
)
