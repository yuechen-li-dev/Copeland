$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
[xml]$solution = Get-Content -LiteralPath (Join-Path $repository 'Aurelian.Core.slnx') -Raw
$visited = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

function Test-ProjectClosure([string]$project) {
    $project = [System.IO.Path]::GetFullPath($project)
    if (-not $project.StartsWith($repository + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Core solution reaches a sibling repository: $project"
    }
    $relative = [System.IO.Path]::GetRelativePath($repository, $project).Replace('\', '/')
    if ($relative.StartsWith('Games/', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Core solution reaches a game project: $relative"
    }
    if (-not $visited.Add($project)) { return }
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Missing core project: $relative" }
    [xml]$definition = Get-Content -LiteralPath $project -Raw
    foreach ($reference in $definition.SelectNodes('//ProjectReference')) {
        if ($reference.Include.Contains('$(')) { throw "Unresolved core reference: $($reference.Include)" }
        Test-ProjectClosure (Join-Path (Split-Path -Parent $project) $reference.Include)
    }
}

foreach ($project in $solution.SelectNodes('//Project')) {
    Test-ProjectClosure (Join-Path $repository $project.Path)
}
Write-Output "Aurelian core closure passed: $($visited.Count) projects, no sibling repositories or game projects."
