[CmdletBinding()]
param(
    [string] $BaseRef,
    [string[]] $Paths,
    [string] $AllowlistPath = ".github/artifact-budget-allowlist.txt",
    [int64] $MaximumFileBytes = 262144,
    [int] $MaximumFilesPerArtifactRoot = 16
)

$ErrorActionPreference = "Stop"

function Normalize-RepositoryPath {
    param([string] $Path)

    return $Path.Replace("\", "/").TrimStart("./")
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
        $diffPaths = & git diff --diff-filter=A --name-only "$BaseRef...HEAD"
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to list files added since '$BaseRef'."
        }

        return @($diffPaths | ForEach-Object { Normalize-RepositoryPath $_ })
    }

    $indexPaths = & git diff --cached --diff-filter=A --name-only
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

$allowlistPatterns = Read-AllowlistPatterns -Path $AllowlistPath
$candidatePaths = @(Get-CandidatePaths | Sort-Object -Unique)
$artifactPaths = @($candidatePaths | Where-Object { $_ -like "artifacts/*" })
$violations = [System.Collections.Generic.List[string]]::new()

$artifactGroups = $artifactPaths | Group-Object {
    $parts = $_.Split("/")
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

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $violations.Add("$path is listed as an added artifact but is not a file in the checkout.")
        continue
    }

    $file = Get-Item -LiteralPath $path
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
    "Artifact budget check passed for $($artifactPaths.Count) added artifact file(s). " +
    "Compact manifests and golden fixtures remain eligible; exceptions require the reviewed allowlist."
)
