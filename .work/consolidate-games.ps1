$ErrorActionPreference = 'Stop'
$repository = (Get-Location).Path
$moves = [ordered]@{
    'src/TinyFarm' = 'Games/TinyFarm'
    'tests/TinyFarm/TinyFarm.Core.Tests' = 'Games/TinyFarm/TinyFarm.Core.Tests'
    'src/Integrations/TinyFarm.Oblivion' = 'Games/TinyFarm/TinyFarm.Oblivion'
    'samples/Integrations/Aurelian.Beacon3D' = 'Games/Beacon3D/Aurelian.Beacon3D'
    'tests/Integrations/Aurelian.Beacon3D.Tests' = 'Games/Beacon3D/Aurelian.Beacon3D.Tests'
    'samples/Integrations/Aurelian.StrategyDemo' = 'Games/Strategy/Aurelian.StrategyDemo'
    'samples/Integrations/Aurelian.Ariadne.VnDemo' = 'Games/Sunkill/Aurelian.Ariadne.VnDemo'
    'tests/Integrations/Sunkill.Tests' = 'Games/Sunkill/Sunkill.Tests'
    'examples/tinyfarm' = 'Games/TinyFarm/Examples'
}

function Remap-Absolute([string] $path) {
    foreach ($entry in $moves.GetEnumerator()) {
        $oldRoot = [IO.Path]::GetFullPath((Join-Path $repository $entry.Key))
        if ($path -eq $oldRoot -or $path.StartsWith($oldRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            return [IO.Path]::GetFullPath((Join-Path $repository $entry.Value)) + $path.Substring($oldRoot.Length)
        }
    }
    return $path
}

# Resolve project paths from their original location before changing either endpoint.
$projects = @(rg --files -g '*.csproj' -g '*.props' -g '*.targets')
foreach ($project in $projects) {
    $oldFile = [IO.Path]::GetFullPath((Join-Path $repository $project))
    $newDirectory = [IO.Path]::GetDirectoryName((Remap-Absolute $oldFile))
    $oldDirectory = [IO.Path]::GetDirectoryName($oldFile)
    $content = [IO.File]::ReadAllText($oldFile)
    $changed = [regex]::Replace($content, '(?<=\b(?:Include|Update|Remove)=")[^"]+', {
        param($match)
        $items = foreach ($item in $match.Value.Split(';')) {
            if ($item.Contains('$(') -or $item -notmatch '[\\/]' -or [IO.Path]::IsPathRooted($item)) {
                $item
                continue
            }
            $target = [IO.Path]::GetFullPath((Join-Path $oldDirectory $item))
            $mapped = Remap-Absolute $target
            [IO.Path]::GetRelativePath($newDirectory, $mapped).Replace('\', '/')
        }
        return $items -join ';'
    })
    if ($changed -ne $content) {
        [IO.File]::WriteAllText($oldFile, $changed)
    }
}

$texts = @(git ls-files '*.cs' '*.csproj' '*.slnx' '*.sln' '*.md' '*.ps1' '*.cmd' '*.json' '*.props' '*.targets' '.gitignore' '.gitattributes')
foreach ($file in $texts) {
    if ($file.StartsWith('artifacts/') -or $file.Contains('/history/')) {
        continue
    }
    $path = Join-Path $repository $file
    $original = [IO.File]::ReadAllText($path)
    $changed = $original
    foreach ($entry in $moves.GetEnumerator()) {
        $changed = $changed.Replace($entry.Key, $entry.Value)
        $changed = $changed.Replace($entry.Key.Replace('/', '\'), $entry.Value.Replace('/', '\'))
        $changed = $changed.Replace($entry.Key.Replace('/', '\\'), $entry.Value.Replace('/', '\\'))
    }
    $changed = $changed.Replace('"src", "TinyFarm"', '"Games", "TinyFarm"')
    $changed = $changed.Replace('"tests", "TinyFarm"', '"Games", "TinyFarm"')
    foreach ($name in @('Aurelian.Beacon3D', 'Aurelian.StrategyDemo', 'Aurelian.Ariadne.VnDemo')) {
        $game = switch ($name) {
            'Aurelian.Beacon3D' { 'Beacon3D' }
            'Aurelian.StrategyDemo' { 'Strategy' }
            'Aurelian.Ariadne.VnDemo' { 'Sunkill' }
        }
        $changed = $changed.Replace(('"samples", "Integrations", "' + $name + '"'), ('"Games", "' + $game + '", "' + $name + '"'))
    }
    if ($changed -ne $original) {
        [IO.File]::WriteAllText($path, $changed)
    }
}

foreach ($entry in $moves.GetEnumerator()) {
    $source = [IO.Path]::GetFullPath((Join-Path $repository $entry.Key))
    $destination = [IO.Path]::GetFullPath((Join-Path $repository $entry.Value))
    if (-not $source.StartsWith($repository + '\') -or -not $destination.StartsWith($repository + '\')) {
        throw 'Move escaped the repository.'
    }
    if (Test-Path -LiteralPath $destination) {
        throw "Destination already exists: $destination"
    }
    New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
    Move-Item -LiteralPath $source -Destination $destination
}
