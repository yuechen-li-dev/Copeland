[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$ArtifactRoot = (Join-Path $PSScriptRoot "..\artifacts\cts-distribution-m0")
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$packageRoot = Join-Path $artifactRoot "packages"
$firstPackageRoot = Join-Path $artifactRoot "packages-first"
$secondPackageRoot = Join-Path $artifactRoot "packages-second"
$proofRoot = Join-Path $artifactRoot "isolated-proof"
$tspackRepositoryRoot = Join-Path (Split-Path -Parent $repositoryRoot) "tspack"

New-Item -ItemType Directory -Force -Path $packageRoot, $firstPackageRoot, $secondPackageRoot | Out-Null

function Invoke-DotNet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function ConvertTo-DeterministicZipArchive {
    param([Parameter(Mandatory)][string]$ArchivePath)

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $source = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $entries = foreach ($entry in $source.Entries) {
            $stream = $entry.Open()
            try {
                $memory = [IO.MemoryStream]::new()
                $stream.CopyTo($memory)
                [PSCustomObject]@{ Name = $entry.FullName; Bytes = $memory.ToArray() }
            }
            finally {
                $stream.Dispose()
            }
        }
    }
    finally {
        $source.Dispose()
    }

    $temporaryPath = "$ArchivePath.deterministic"
    Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
    $target = [IO.Compression.ZipFile]::Open($temporaryPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $entries | Sort-Object Name) {
            $targetEntry = $target.CreateEntry($entry.Name, [IO.Compression.CompressionLevel]::Optimal)
            $targetEntry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $stream = $targetEntry.Open()
            try {
                $stream.Write($entry.Bytes, 0, $entry.Bytes.Length)
            }
            finally {
                $stream.Dispose()
            }
        }
    }
    finally {
        $target.Dispose()
    }

    Move-Item -LiteralPath $temporaryPath -Destination $ArchivePath -Force
}

function Get-PackageHashes {
    param([string]$Root)

    return Get-ChildItem -LiteralPath $Root -Filter *.nupkg |
        Sort-Object Name |
        ForEach-Object {
            [PSCustomObject]@{
                Name = $_.Name
                Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
}

function ConvertTo-DeterministicNuGetPackage {
    param([string]$PackagePath)

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $source = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entries = foreach ($entry in $source.Entries) {
            $stream = $entry.Open()
            try {
                $memory = [IO.MemoryStream]::new()
                $stream.CopyTo($memory)
                [PSCustomObject]@{ Name = $entry.FullName; Bytes = $memory.ToArray() }
            }
            finally {
                $stream.Dispose()
            }
        }
    }
    finally {
        $source.Dispose()
    }

    $coreProperties = $entries | Where-Object { $_.Name -match '^package/services/metadata/core-properties/.+\.psmdcp$' } | Select-Object -First 1
    if ($coreProperties) {
        $oldCorePath = $coreProperties.Name
        $coreProperties.Name = 'package/services/metadata/core-properties/core.psmdcp'
        foreach ($entry in $entries | Where-Object { $_.Name -in @('_rels/.rels', '[Content_Types].xml') }) {
            $content = [Text.Encoding]::UTF8.GetString($entry.Bytes)
            $entry.Bytes = [Text.Encoding]::UTF8.GetBytes($content.Replace($oldCorePath, $coreProperties.Name))
        }
    }

    $nuspec = $entries | Where-Object { $_.Name -like '*.nuspec' } | Select-Object -First 1
    $relationships = $entries | Where-Object { $_.Name -eq '_rels/.rels' } | Select-Object -First 1
    if ($nuspec -and $relationships) {
        $relationships.Bytes = [Text.Encoding]::UTF8.GetBytes(@"
<?xml version="1.0" encoding="utf-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/$($nuspec.Name)" Id="R1" />
  <Relationship Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="/package/services/metadata/core-properties/core.psmdcp" Id="R2" />
</Relationships>
"@)
    }

    $temporaryPath = "$PackagePath.deterministic"
    Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
    $target = [IO.Compression.ZipFile]::Open($temporaryPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $entries | Sort-Object Name) {
            $targetEntry = $target.CreateEntry($entry.Name, [IO.Compression.CompressionLevel]::Optimal)
            $targetEntry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $stream = $targetEntry.Open()
            try {
                $stream.Write($entry.Bytes, 0, $entry.Bytes.Length)
            }
            finally {
                $stream.Dispose()
            }
        }
    }
    finally {
        $target.Dispose()
    }

    Move-Item -LiteralPath $temporaryPath -Destination $PackagePath -Force
}

function Pack-Copeland {
    param([string]$OutputDirectory)

    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    Invoke-DotNet pack (Join-Path $repositoryRoot "src\Copeland\Copeland.Cli\Copeland.Cli.csproj") --configuration $Configuration --output $OutputDirectory
    Invoke-DotNet pack (Join-Path $repositoryRoot "src\Copeland\Copeland.TS.MSBuild\Copeland.TS.MSBuild.csproj") --configuration $Configuration --output $OutputDirectory
    Invoke-DotNet pack (Join-Path $repositoryRoot "src\Copeland\Copeland.TS.Templates\Copeland.TS.Templates.csproj") --configuration $Configuration --output $OutputDirectory
    Get-ChildItem -LiteralPath $OutputDirectory -Filter *.nupkg | ForEach-Object { ConvertTo-DeterministicNuGetPackage $_.FullName }
}

Pack-Copeland $firstPackageRoot
Pack-Copeland $secondPackageRoot

$firstHashes = Get-PackageHashes $firstPackageRoot
$secondHashes = Get-PackageHashes $secondPackageRoot
if (Compare-Object $firstHashes $secondHashes -Property Name, Sha256) {
    throw "Copeland package hashes differ between identical local builds."
}

Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
Copy-Item -Path (Join-Path $firstPackageRoot "*.nupkg") -Destination $packageRoot

$vsCodeDirectory = Join-Path $repositoryRoot "src\Copeland\Copeland.TS.VSCode"
Push-Location $vsCodeDirectory
try {
    & npm ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE." }
    & npm run package
    if ($LASTEXITCODE -ne 0) { throw "npm run package failed with exit code $LASTEXITCODE." }
    $vsixPath = Join-Path $vsCodeDirectory "dist\copeland-ts-0.1.0.vsix"
    ConvertTo-DeterministicZipArchive $vsixPath
    $firstVsixPath = Join-Path $artifactRoot "copeland-ts-first.vsix"
    Copy-Item -LiteralPath $vsixPath -Destination $firstVsixPath -Force

    & npm run package
    if ($LASTEXITCODE -ne 0) { throw "second npm run package failed with exit code $LASTEXITCODE." }
    ConvertTo-DeterministicZipArchive $vsixPath
    $secondVsixPath = Join-Path $artifactRoot "copeland-ts-second.vsix"
    Copy-Item -LiteralPath $vsixPath -Destination $secondVsixPath -Force
    if ((Get-FileHash -LiteralPath $firstVsixPath -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $secondVsixPath -Algorithm SHA256).Hash) {
        throw "Copeland VSIX hashes differ between identical local builds."
    }

    Copy-Item -LiteralPath $firstVsixPath -Destination (Join-Path $packageRoot "copeland-ts-0.1.0.vsix") -Force
}
finally {
    Pop-Location
}

$tspackPackageRoot = Join-Path $artifactRoot "tspack-browser-proof"
& (Join-Path $tspackRepositoryRoot "tools\Build-CopelandBrowserProofPackage.ps1") -OutputDirectory $tspackPackageRoot -Version "0.1.7"
if ($LASTEXITCODE -ne 0) { throw "TSPack browser-proof package build failed with exit code $LASTEXITCODE." }
$tspackArchive = Join-Path $tspackPackageRoot "TSPack.Tool.0.1.7-win-x64.zip"
$secondTspackPackageRoot = Join-Path $artifactRoot "tspack-browser-proof-second"
& (Join-Path $tspackRepositoryRoot "tools\Build-CopelandBrowserProofPackage.ps1") -OutputDirectory $secondTspackPackageRoot -Version "0.1.7"
if ($LASTEXITCODE -ne 0) { throw "Second TSPack browser-proof package build failed with exit code $LASTEXITCODE." }
$secondTspackArchive = Join-Path $secondTspackPackageRoot "TSPack.Tool.0.1.7-win-x64.zip"
if ((Get-FileHash -LiteralPath $tspackArchive -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $secondTspackArchive -Algorithm SHA256).Hash) {
    throw "TSPack browser-proof package hashes differ between identical local builds."
}
Copy-Item -LiteralPath $tspackArchive -Destination $packageRoot -Force

Remove-Item -LiteralPath $proofRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $proofRoot | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $proofRoot "dotnet-home"
$env:NUGET_PACKAGES = Join-Path $proofRoot "nuget-packages"
$toolPath = Join-Path $proofRoot "tools"
$projectRoot = Join-Path $proofRoot "HelloCopeland"
$nugetConfig = Join-Path $proofRoot "NuGet.config"

@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="copeland-local" value="$packageRoot" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content -LiteralPath $nugetConfig -Encoding utf8

$installStopwatch = [Diagnostics.Stopwatch]::StartNew()
Invoke-DotNet tool install --tool-path $toolPath Copeland.TS.Tool --version 0.1.0 --add-source $packageRoot
Invoke-DotNet new install Copeland.TS.Templates@0.1.0 --nuget-source $packageRoot
$installStopwatch.Stop()

$templateStopwatch = [Diagnostics.Stopwatch]::StartNew()
Invoke-DotNet new copeland-console --name HelloCopeland --output $projectRoot
$templateStopwatch.Stop()

Copy-Item -LiteralPath $nugetConfig -Destination (Join-Path $projectRoot "NuGet.config")
Push-Location $projectRoot
try {
    $doctorStopwatch = [Diagnostics.Stopwatch]::StartNew()
    & (Join-Path $toolPath "tscl") doctor --format json | Set-Content -LiteralPath (Join-Path $proofRoot "doctor.json") -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw "tscl doctor failed with exit code $LASTEXITCODE." }
    $doctorStopwatch.Stop()

    $buildStopwatch = [Diagnostics.Stopwatch]::StartNew()
    Invoke-DotNet restore --configfile NuGet.config
    Invoke-DotNet build --no-restore
    $buildStopwatch.Stop()

    $runStopwatch = [Diagnostics.Stopwatch]::StartNew()
    & dotnet run --no-build | Set-Content -LiteralPath (Join-Path $proofRoot "run-output.txt") -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw "dotnet run failed with exit code $LASTEXITCODE." }
    $runStopwatch.Stop()
}
finally {
    Pop-Location
}

$reactStopwatch = [Diagnostics.Stopwatch]::StartNew()
$reactProjectRoot = Join-Path $proofRoot "HelloCopelandReact"
Invoke-DotNet new copeland-react --name HelloCopelandReact --output $reactProjectRoot
Copy-Item -LiteralPath $nugetConfig -Destination (Join-Path $reactProjectRoot "NuGet.config")
Push-Location $reactProjectRoot
try {
    Invoke-DotNet restore --configfile NuGet.config
    Invoke-DotNet build --no-restore
    & (Join-Path $toolPath "tscl") workspace sync
    if ($LASTEXITCODE -ne 0) { throw "tscl workspace sync failed for generated React project." }
}
finally {
    Pop-Location
}

$installedTspackRoot = Join-Path $proofRoot "tspack"
Expand-Archive -LiteralPath $tspackArchive -DestinationPath $installedTspackRoot -Force
$tspackRuntimeRoot = Join-Path $installedTspackRoot "tspack-windows-amd64"
$env:PLAYWRIGHT_BROWSERS_PATH = Join-Path $tspackRuntimeRoot "playwright-browsers"
$playwrightOutput = & node (Join-Path $tspackRuntimeRoot "tools\Prove-CopelandReactPlaywright.mjs") --url "http://127.0.0.1:5137" --tspack (Join-Path $tspackRuntimeRoot "tspack.exe") --root $reactProjectRoot
if ($LASTEXITCODE -ne 0) { throw "TSPack-owned Playwright proof failed with exit code $LASTEXITCODE." }
$playwrightOutput | Set-Content -LiteralPath (Join-Path $proofRoot "react-playwright.json") -Encoding utf8
$listeningConnection = netstat -ano | Select-String "127\.0\.0\.1:5137.*LISTENING"
if ($listeningConnection) { throw "TSPack did not clean up the generated React host: $listeningConnection" }
$reactStopwatch.Stop()

$vsixStopwatch = [Diagnostics.Stopwatch]::StartNew()
$workspaceProjectRoot = Join-Path $proofRoot "InstalledVsixWorkspace"
Invoke-DotNet new copeland-workspace --name InstalledVsixWorkspace --output $workspaceProjectRoot
Copy-Item -LiteralPath $nugetConfig -Destination (Join-Path $workspaceProjectRoot "NuGet.config")
Push-Location $workspaceProjectRoot
try {
    Invoke-DotNet restore --configfile NuGet.config
    Invoke-DotNet build --no-restore
    & (Join-Path $toolPath "tscl") workspace sync
    if ($LASTEXITCODE -ne 0) { throw "tscl workspace sync failed for installed VSIX workspace." }
}
finally {
    Pop-Location
}

$codeCommand = (Get-Command code.cmd -ErrorAction Stop).Source
$vsixExtensions = Join-Path $proofRoot "vscode-extensions"
$vsixUserData = Join-Path $proofRoot "vscode-user-data"
$vsixPath = Join-Path $packageRoot "copeland-ts-0.1.0.vsix"
& $codeCommand --install-extension $vsixPath --extensions-dir $vsixExtensions
if ($LASTEXITCODE -ne 0) { throw "VSIX installation failed with exit code $LASTEXITCODE." }
$env:COPELAND_VSCODE_INSTALLED_EXTENSION_PATH = Join-Path $vsixExtensions "copeland.copeland-ts-0.1.0"
$env:COPELAND_VSCODE_TEST_WORKSPACE = $workspaceProjectRoot
$env:COPELAND_VSCODE_TEST_TSCL_PATH = Join-Path $toolPath "tscl.exe"
$env:COPELAND_VSCODE_TEST_USER_DATA = $vsixUserData
$env:COPELAND_VSCODE_TEST_EXTENSIONS = $vsixExtensions
Push-Location $vsCodeDirectory
try {
    & npm run test:installed-integration | Set-Content -LiteralPath (Join-Path $proofRoot "installed-vsix-extension-host.txt") -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw "Installed VSIX extension-host proof failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
$vsixStopwatch.Stop()

$metrics = [ordered]@{
    schemaVersion = 1
    installMilliseconds = $installStopwatch.ElapsedMilliseconds
    templateMilliseconds = $templateStopwatch.ElapsedMilliseconds
    doctorMilliseconds = $doctorStopwatch.ElapsedMilliseconds
    restoreAndBuildMilliseconds = $buildStopwatch.ElapsedMilliseconds
    runMilliseconds = $runStopwatch.ElapsedMilliseconds
    reactTemplateAndBrowserMilliseconds = $reactStopwatch.ElapsedMilliseconds
    installedVsixExtensionHostMilliseconds = $vsixStopwatch.ElapsedMilliseconds
    timeToFirstWorkingAppMilliseconds = $installStopwatch.ElapsedMilliseconds + $templateStopwatch.ElapsedMilliseconds + $buildStopwatch.ElapsedMilliseconds + $runStopwatch.ElapsedMilliseconds
    manualInterventions = @()
    packageHashes = $firstHashes
    tspackArchiveSha256 = (Get-FileHash -LiteralPath $tspackArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    vsixSha256 = (Get-FileHash -LiteralPath $vsixPath -Algorithm SHA256).Hash.ToLowerInvariant()
}
$metrics | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $proofRoot "metrics.json") -Encoding utf8

Invoke-DotNet new uninstall Copeland.TS.Templates
Invoke-DotNet tool uninstall --tool-path $toolPath Copeland.TS.Tool
& $codeCommand --uninstall-extension copeland.copeland-ts --extensions-dir $vsixExtensions
if ($LASTEXITCODE -ne 0) { throw "VSIX uninstall failed with exit code $LASTEXITCODE." }

Write-Output "Local feed: $packageRoot"
Write-Output "Isolated proof: $proofRoot"
