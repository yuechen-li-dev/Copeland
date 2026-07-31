[CmdletBinding()]
param(
    [string]$Version = "0.1.0-preview.1"
)

$ErrorActionPreference = "Stop"

$verificationRoot = Join-Path ([IO.Path]::GetTempPath()) ("copeland-public-{0}" -f [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $verificationRoot | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $verificationRoot "dotnet-home"
$env:NUGET_PACKAGES = Join-Path $verificationRoot "nuget-cache"
$env:npm_config_cache = Join-Path $verificationRoot "npm-cache"

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command $($Arguments -join ' ') failed with exit code $LASTEXITCODE. The release may not be public yet."
    }
}

$toolPath = Join-Path $verificationRoot "tools"
Invoke-Checked dotnet tool install --tool-path $toolPath Copeland.TS.Tool --version $Version
$toolExecutable = Join-Path $toolPath "tscl.exe"
$toolVersion = (& $toolExecutable --version).Trim()
if ($toolVersion -ne $Version) {
    throw "NuGet tool reported '$toolVersion'; expected '$Version'."
}

$npmConsumer = Join-Path $verificationRoot "npm-consumer"
New-Item -ItemType Directory -Force -Path $npmConsumer | Out-Null
Push-Location $npmConsumer
try {
    Invoke-Checked npm init --yes
    Invoke-Checked npm install "@copeland/tscl@$Version"
    $npmVersion = (& npx --no-install tscl --version).Trim()
    if ($npmVersion -ne $Version) {
        throw "npm launcher reported '$npmVersion'; expected '$Version'."
    }
}
finally {
    Pop-Location
}

$templatePath = Join-Path $verificationRoot "BootstrapTemplate.tsx"
$templateUrl = "https://github.com/yuechen-li-dev/Copeland/releases/download/v$Version/BootstrapTemplate.tsx"
try {
    Invoke-WebRequest -Uri $templateUrl -OutFile $templatePath
}
catch {
    throw "BootstrapTemplate.tsx is not available from the public GitHub release '$templateUrl'. The release may not be public yet. $($_.Exception.Message)"
}

$projectRoot = Join-Path $verificationRoot "HelloCopeland"
Invoke-Checked $toolExecutable template materialize $templatePath --entry BootstrapTemplate --name HelloCopeland --output $projectRoot
Push-Location $projectRoot
try {
    Invoke-Checked npm install
    Invoke-Checked dotnet restore
    Invoke-Checked dotnet build --configuration Release --no-restore
    Invoke-Checked dotnet test --configuration Release --no-build
    Invoke-Checked dotnet run --configuration Release --no-build
}
finally {
    Pop-Location
}

Write-Output "Public registry verification passed for Copeland TS $Version."
Write-Output "Verification directory: $verificationRoot"
