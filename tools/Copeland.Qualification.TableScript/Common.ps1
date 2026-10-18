function Initialize-Qualification {
    param([string]$RepositoryRoot, [string]$OutputDirectory)

    $script:repositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
    $script:evidenceDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    $localRoot = Join-Path $script:repositoryRoot "artifacts/local/"
    if (-not $script:evidenceDirectory.StartsWith($localRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Qualification output must be inside artifacts/local."
    }
    New-Item -ItemType Directory -Path $script:evidenceDirectory -Force | Out-Null
    $script:cli = Join-Path $script:repositoryRoot "src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll"
    $script:workbook = Join-Path $script:repositoryRoot "samples/copeland-ts/tablescript-friction-m12/Workbook/Workbook.ts"
    $script:probeDirectory = Join-Path $script:evidenceDirectory "probe"
    $script:checks = 0
}

function Require {
    param([bool]$Condition, [string]$Description)

    if (-not $Condition) {
        throw $Description
    }
    $script:checks += 1
}

function Invoke-Table {
    param([string]$Name, [string[]]$Arguments, [bool]$ExpectedSuccess = $true)

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = (Get-Command dotnet -ErrorAction Stop).Source
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @($script:cli, "table") + $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "$Name did not finish within 30 seconds."
        }
        $exitCode = $process.ExitCode
        $json = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
    }
    finally {
        $process.Dispose()
    }
    if ([string]::IsNullOrWhiteSpace($json)) {
        throw "$Name returned no JSON: $errorText"
    }
    $json | Set-Content -LiteralPath (Join-Path $script:evidenceDirectory "$Name.json") -Encoding utf8
    $result = $json | ConvertFrom-Json
    Require ($result.success -eq $ExpectedSuccess) "$Name returned unexpected success: $json"
    Require (($exitCode -eq 0) -eq $ExpectedSuccess) "$Name returned unexpected exit code: $exitCode"
    if ($ExpectedSuccess -and $result.command -eq "table.query") {
        Require ($result.executor -eq "csharp-relation-plan") "$Name bypassed canonical query execution"
    }
    if (-not $ExpectedSuccess) {
        Require ($result.diagnostics.Count -gt 0) "$Name failed without repair diagnostics"
    }
    return $result
}

function Reject-Edit {
    param([string]$Name, [string]$Source, [string[]]$Arguments, [string]$Code)

    $before = (Get-FileHash -LiteralPath $Source).Hash
    $result = Invoke-Table $Name $Arguments $false
    Require ($result.diagnostics[0].code -eq $Code) "$Name returned the wrong diagnostic"
    Require ((Get-FileHash -LiteralPath $Source).Hash -eq $before) "$Name changed rejected source"
}

function New-ProbeProject {
    param([string]$RunnerPath)

    New-Item -ItemType Directory -Path $script:probeDirectory -Force | Out-Null
    Copy-Item -LiteralPath $RunnerPath -Destination (Join-Path $script:probeDirectory "Program.cs") -Force
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $script:probeDirectory "Probe.csproj") -Encoding utf8
}
