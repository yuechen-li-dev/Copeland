param(
    [string]$Blender = 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe',
    [string]$TexturePython = '',
    [int]$TextureEdge = 1024
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$root = Join-Path $repo 'artifacts/local/bistro'
$source = Join-Path $root 'source'
$archive = Join-Path $source 'bistro.zip'
$bundle = Join-Path $source 'Bistro_v5_2'
if (-not (Test-Path -LiteralPath $Blender -PathType Leaf)) {
    throw 'Supply the Blender executable with -Blender.'
}
if (-not $TexturePython) {
    $venv = Join-Path $root 'texture-venv'
    $TexturePython = Join-Path $venv 'Scripts/python.exe'
    if (-not (Test-Path -LiteralPath $TexturePython)) {
        python -m venv $venv
        if ($LASTEXITCODE) { throw 'Could not prepare the texture converter Python environment.' }
    }
    & $TexturePython -m pip install Pillow==11.3.0 numpy==2.2.6
    if ($LASTEXITCODE) { throw 'Could not install the texture decoder dependencies.' }
}

New-Item -ItemType Directory -Path $source -Force | Out-Null
if (-not (Test-Path -LiteralPath $archive)) {
    $partial = "$archive.part"
    curl.exe --fail --location --retry 2 --output $partial 'https://developer.nvidia.com/bistro'
    if ($LASTEXITCODE) { throw 'The ORCA download failed.' }
    Move-Item -LiteralPath $partial -Destination $archive
}
$releaseHash = '0D50E3C724C6C5DA19F8EB99AD3F53E36FEC37FFA2DF9621F9CCF0603F3934E1'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $releaseHash) {
    throw 'The archive differs from the reviewed ORCA Bistro v5.2 release.'
}
$interior = Join-Path $bundle 'BistroInterior_Wine.fbx'
python -m zipfile -e $archive $source
if ($LASTEXITCODE) { throw 'The ORCA archive could not be unpacked.' }
$out = Join-Path $root 'assets'
$converter = Join-Path $PSScriptRoot 'import_bistro.py'
& $Blender --background --factory-startup --python-exit-code 1 --python $converter -- $interior $out --texture-python $TexturePython --texture-edge $TextureEdge --glass-ratio 0.63
if ($LASTEXITCODE) { throw 'The Bistro conversion failed.' }
Copy-Item -LiteralPath (Join-Path $bundle 'LICENSE.txt') -Destination (Join-Path $out 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $bundle 'README.txt') -Destination (Join-Path $out 'ORCA-README.txt')
