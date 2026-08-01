[CmdletBinding()]
param(
    [string]$ReleaseRoot,
    [string]$Version = "0.1.0-preview.1"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ReleaseRoot = if ($ReleaseRoot) {
    $ReleaseRoot
}
else {
    Join-Path $repositoryRoot "artifacts\releases\$Version"
}
$releaseRoot = [IO.Path]::GetFullPath($ReleaseRoot)
$validationRoot = Join-Path $releaseRoot "validation"
$nugetRoot = Join-Path $releaseRoot "nuget"
$npmRoot = Join-Path $releaseRoot "npm"
$vsCodeRoot = Join-Path $releaseRoot "vscode"
$templatePath = Join-Path $releaseRoot "templates\BootstrapTemplate.tsx"
$toolPackage = Join-Path $nugetRoot "Copeland.TS.Tool.$Version.nupkg"
$sdkPackage = Join-Path $nugetRoot "Copeland.TS.Sdk.$Version.nupkg"
$templatesPackage = Join-Path $nugetRoot "Copeland.TS.Templates.$Version.nupkg"
$npmPackage = Join-Path $npmRoot "copeland-tscl-$Version.tgz"
$vsixPath = Join-Path $vsCodeRoot "copeland-ts-$Version.vsix"

foreach ($required in @($toolPackage, $sdkPackage, $templatesPackage, $npmPackage, $vsixPath, $templatePath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required release artifact is missing: $required"
    }
}

if (Test-Path -LiteralPath $validationRoot) {
    $resolvedValidation = [IO.Path]::GetFullPath($validationRoot)
    if (-not $resolvedValidation.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset validation path outside the release directory: $resolvedValidation"
    }
    Remove-Item -LiteralPath $resolvedValidation -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $validationRoot | Out-Null

$dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source
$npmCommand = (Get-Command npm -ErrorAction Stop).Source
$npxCommand = (Get-Command npx -ErrorAction Stop).Source
$isolatedPathDirectories = @(
    (Split-Path -Parent $dotnetCommand),
    (Split-Path -Parent $npmCommand),
    (Join-Path $env:SystemRoot "System32")
) | Select-Object -Unique
$env:PATH = $isolatedPathDirectories -join [IO.Path]::PathSeparator
$env:DOTNET_CLI_HOME = Join-Path $validationRoot "dotnet-home"
$env:NUGET_PACKAGES = Join-Path $validationRoot "nuget-cache"
$env:npm_config_cache = Join-Path $validationRoot "npm-cache"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$nugetConfig = Join-Path $validationRoot "NuGet.config"
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="copeland-release" value="$nugetRoot" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content -LiteralPath $nugetConfig -Encoding utf8

$toolPath = Join-Path $validationRoot "tools"
Invoke-Checked $dotnetCommand tool install --tool-path $toolPath Copeland.TS.Tool --version $Version --configfile $nugetConfig
$toolExecutable = Join-Path $toolPath "tscl.exe"
$toolVersion = (& $toolExecutable --version).Trim()
if ($LASTEXITCODE -ne 0 -or $toolVersion -ne $Version) {
    throw "Packed NuGet tool reported '$toolVersion'; expected '$Version'."
}
$installInfo = (& $toolExecutable install-info --format json | ConvertFrom-Json)
foreach ($identity in @($installInfo.toolVersion, $installInfo.compilerVersion, $installInfo.languageServerVersion)) {
    if ($identity -ne $Version) {
        throw "Packed tool identity '$identity' does not match '$Version'."
    }
}

$manifestRoot = Join-Path $validationRoot "local-manifest"
New-Item -ItemType Directory -Force -Path $manifestRoot | Out-Null
Push-Location $manifestRoot
try {
    Invoke-Checked $dotnetCommand new tool-manifest
    Invoke-Checked $dotnetCommand tool install Copeland.TS.Tool --version $Version --configfile $nugetConfig
    $manifestVersion = (& $dotnetCommand tscl --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $manifestVersion -ne $Version) {
        throw "Local tool manifest reported '$manifestVersion'; expected '$Version'."
    }
}
finally {
    Pop-Location
}

$templateEngineHome = Join-Path $validationRoot "template-engine-home"
$env:DOTNET_NEW_HOME = $templateEngineHome
Invoke-Checked $dotnetCommand new install $templatesPackage
Invoke-Checked $dotnetCommand new uninstall Copeland.TS.Templates

$npmConsumer = Join-Path $validationRoot "npm-consumer"
New-Item -ItemType Directory -Force -Path $npmConsumer | Out-Null
Push-Location $npmConsumer
try {
    Invoke-Checked $npmCommand init --yes
    Invoke-Checked $npmCommand install --save-dev $npmPackage
    $npmVersion = (& $npxCommand --no-install tscl --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $npmVersion -ne $Version) {
        throw "Packed npm launcher reported '$npmVersion'; expected '$Version'."
    }
}
finally {
    Pop-Location
}

$projectRoot = Join-Path $validationRoot "HelloCopeland"
Invoke-Checked $toolExecutable template materialize $templatePath --entry BootstrapTemplate --name HelloCopeland --output $projectRoot
foreach ($expected in @("HelloCopeland.csproj", "HelloCopeland.slnx", "package.json", "tsconfig.tsx", "src\GreetingDocument.tsx")) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot $expected))) {
        throw "Bootstrap materialization did not create $expected at the output root."
    }
}
Copy-Item -LiteralPath $nugetConfig -Destination (Join-Path $projectRoot "NuGet.config") -Force

Push-Location $projectRoot
try {
    Invoke-Checked $npmCommand install
    Invoke-Checked $dotnetCommand restore --configfile (Join-Path $projectRoot "NuGet.config")
    Invoke-Checked $dotnetCommand build --configuration Release --no-restore
    Invoke-Checked $dotnetCommand test --configuration Release --no-build
    $runOutput = (& $dotnetCommand run --configuration Release --no-build 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Generated project failed to run:`n$runOutput"
    }
}
finally {
    Pop-Location
}

$extensionsDirectory = Join-Path $validationRoot "vscode-extensions"
$userDataDirectory = Join-Path $validationRoot "vscode-user-data"
New-Item -ItemType Directory -Force -Path $extensionsDirectory, $userDataDirectory | Out-Null
$vsixExtractionDirectory = Join-Path $validationRoot "vsix-extraction"
Expand-Archive -LiteralPath $vsixPath -DestinationPath $vsixExtractionDirectory
$packagedExtensionDirectory = Join-Path $vsixExtractionDirectory "extension"
if (-not (Test-Path -LiteralPath (Join-Path $packagedExtensionDirectory "package.json") -PathType Leaf)) {
    throw "The VSIX does not contain an extension package manifest."
}
$installedExtensionDirectory = Join-Path $extensionsDirectory "copeland.copeland-ts-$Version"
Copy-Item -LiteralPath $packagedExtensionDirectory -Destination $installedExtensionDirectory -Recurse
$installedExtensionManifest = Get-Content -LiteralPath (Join-Path $installedExtensionDirectory "package.json") -Raw | ConvertFrom-Json
if ($installedExtensionManifest.publisher -ne "copeland" -or $installedExtensionManifest.name -ne "copeland-ts") {
    throw "The isolated VSIX extension manifest is not copeland.copeland-ts."
}

$vsCodeProject = Join-Path $repositoryRoot "src\Copeland\Copeland.TS.VSCode"
$env:COPELAND_VSCODE_TEST_WORKSPACE = $projectRoot
$env:COPELAND_VSCODE_TEST_TSCL_PATH = $toolExecutable
$env:COPELAND_VSCODE_TEST_USER_DATA = $userDataDirectory
$env:COPELAND_VSCODE_TEST_EXTENSIONS = $extensionsDirectory
Push-Location $vsCodeProject
try {
    Invoke-Checked $npmCommand run compile
    Invoke-Checked (Get-Command node -ErrorAction Stop).Source .\out\runPackageSmoke.js
}
finally {
    Pop-Location
}

$evidence = [ordered]@{
    version = $Version
    validatedAtUtc = [DateTime]::UtcNow.ToString("O")
    nugetToolVersion = $toolVersion
    compilerVersion = $installInfo.compilerVersion
    languageServerVersion = $installInfo.languageServerVersion
    localManifestToolVersion = $manifestVersion
    templatePackage = "installed and uninstalled successfully"
    npmToolVersion = $npmVersion
    generatedProject = [ordered]@{
        path = "isolated/HelloCopeland"
        npmInstall = "passed"
        restore = "passed"
        build = "passed"
        test = "passed"
        run = "passed"
        output = $runOutput
    }
    vscode = [ordered]@{
        installedExtension = "copeland.copeland-ts@$Version"
        isolatedProfileInstall = "VSIX extracted into isolated extensions directory"
        installedArtifactActivation = "passed"
        tsxOwnership = "passed"
    }
}
$evidenceJson = $evidence | ConvertTo-Json -Depth 5
Remove-Item -LiteralPath $validationRoot -Recurse -Force
New-Item -ItemType Directory -Force -Path $validationRoot | Out-Null
$evidenceJson | Set-Content -LiteralPath (Join-Path $validationRoot "package-smoke.json") -Encoding utf8
Write-Output "Package-only smoke test passed for Copeland TS $Version."
