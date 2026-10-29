[CmdletBinding()]
param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot '..'),
    [string]$Version = '0.1.0',
    [string]$OutputDirectory = 'artifacts/local/managed-wgsl-release/candidate'
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$SourceRoot = (Resolve-Path $SourceRoot).Path
$output = [IO.Path]::GetFullPath((Join-Path $repository $OutputDirectory))
if (Test-Path $output) { throw 'Use a fresh release output directory.' }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'The managed compiler release requires a stable version.' }
$revision = (& git -C $SourceRoot rev-parse HEAD).Trim()
if (& git -C $SourceRoot status --porcelain --untracked-files=no) { throw 'Compiler source must be clean.' }
New-Item -ItemType Directory -Force "$output/nuget", "$output/tests" | Out-Null
$projects = [Collections.Generic.List[string]]::new()
function Visit([string]$path) {
    $path = [IO.Path]::GetFullPath($path)
    if ($projects.Contains($path)) { return }
    [xml]$project = Get-Content -LiteralPath $path -Raw
    foreach ($reference in $project.Project.ItemGroup.ProjectReference) {
        if ($reference.Include) { Visit (Join-Path (Split-Path $path -Parent) $reference.Include) }
    }
    $projects.Add($path)
}
Visit (Join-Path $SourceRoot 'src/Copeland/Copeland.TS.Backend.Wgsl/Copeland.TS.Backend.Wgsl.csproj')
Push-Location $SourceRoot
try {
    dotnet test tests/Copeland/Copeland.TS.Tests -c Release -m:1 -nr:false -p:UseSharedCompilation=false --logger 'trx;LogFileName=compiler.trx' --results-directory "$output/tests"
    if ($LASTEXITCODE -ne 0) { throw 'Compiler regression suite failed.' }
    foreach ($project in $projects) {
        dotnet pack $project -c Release -m:1 -nr:false -p:UseSharedCompilation=false -o "$output/nuget" "-p:Version=$Version" "-p:PackageVersion=$Version" -p:DebugType=None -p:DebugSymbols=false -p:PackageLicenseExpression=GPL-3.0-only '-p:Authors=Yuechen Li' -p:RepositoryUrl=https://github.com/yuechen-li-dev/Copeland
        if ($LASTEXITCODE -ne 0) { throw "Pack failed: $project" }
    }
} finally { Pop-Location }
$order = $projects | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) }
$order | Set-Content "$output/PACKAGE_ORDER.txt" -Encoding utf8
Add-Type -AssemblyName System.IO.Compression
foreach ($id in $order) {
    $archive = [IO.Compression.ZipFile]::OpenRead("$output/nuget/$id.$Version.nupkg")
    try {
        $entry = $archive.Entries | Where-Object FullName -Like '*.nuspec'
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml]$spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        foreach ($dependency in $spec.SelectNodes("//*[local-name()='dependency']")) {
            if ($dependency.id -like 'Copeland.*' -and ($dependency.id -notin $order -or $dependency.version -ne $Version)) {
                throw "Unclosed compiler dependency: $($dependency.id) $($dependency.version)"
            }
        }
        if ($archive.Entries | Where-Object FullName -Match '(^runtimes/|\.(so|dylib|exe)$)') { throw "Unexpected native/runtime payload in $id" }
    } finally { $archive.Dispose() }
}
& (Join-Path $PSScriptRoot 'Test-ManagedWgslPackage.ps1') -Feed "$output/nuget" -Version $Version -OutputDirectory "$output/consumer"
if (!$?) { throw 'Consumer qualification failed.' }
$hashes = [ordered]@{}
Get-ChildItem "$output/nuget/*.nupkg" | ForEach-Object { $hashes[$_.Name] = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
@{ schema='copeland-managed-wgsl-release/1'; version=$Version; sourceRevision=$revision; packages=$hashes; compilerTestsPassed=$true; cleanConsumerPassed=$true } |
    ConvertTo-Json -Depth 5 | Set-Content "$output/qualification.json" -Encoding utf8
