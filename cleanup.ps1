$ErrorActionPreference = 'Stop'
$delete = $true

$root = 'C:\Users\yuech\source\repos\Copeland'
$targets = @(
    "$root\tools\CtsJitM0"
    "$root\.tmp\m26-baseline"
)

# Validate every target before deleting anything.
foreach ($target in $targets) {
    if (-not (Test-Path -LiteralPath $target)) { continue }

    $resolved = (Resolve-Path -LiteralPath $target).Path
    if (-not $resolved.StartsWith("$root\", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Target escaped repository: $resolved"
    }

    $entries = @(
        Get-Item -LiteralPath $target -Force
        Get-ChildItem -LiteralPath $target -Recurse -Force
    )

    foreach ($entry in $entries) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing linked path: $($entry.FullName)"
        }

        if (-not $entry.PSIsContainer) {
            $relative = [IO.Path]::GetRelativePath($target, $entry.FullName)
            if ($relative -notmatch '(^|[\\/])(bin|obj)[\\/]') {
                throw "Unexpected non-build file: $($entry.FullName)"
            }
        }
    }
}

foreach ($target in $targets) {
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force -WhatIf:(-not $delete)
    }
}