[CmdletBinding()]
param(
    [string]$OutputDir = "artifacts\\local\\font-reference\\diff",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$BrowserPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
. (Join-Path $PSScriptRoot "font-reference\FontReference.Common.ps1")
$projectPath = Join-Path $repoRoot "tests\\Machina.UI\\Machina.Fonts.Diagnostics.Tests\\Machina.Fonts.Diagnostics.Tests.csproj"
$filter = "FullyQualifiedName~Machina.Fonts.Tests.Rendering.FontReferenceDiffWorkflowTests.ReferenceDiffWorkflow_ScriptWorkflowExportsArtifacts"
$fixtureHtmlPath = Join-Path $repoRoot "tools\\font-reference\\reference-render.html"
$fontPath = Get-CanonicalReferenceFontPath -RepositoryRoot $repoRoot
$definitions = @(
    @{ Id = "machina"; Text = "Machina" },
    @{ Id = "hello-machina"; Text = "Hello Machina" },
    @{ Id = "kerning"; Text = "AV To Ta Wa Yo" },
    @{ Id = "aa0"; Text = "Aa0" },
    @{ Id = "a-space-a"; Text = "A A" }
)

$resolvedOutputDir = Resolve-FullPath -RepositoryRoot $repoRoot -PathValue $OutputDir
New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null
$browserExe = Find-BrowserPath $BrowserPath
$browserMetricsPath = Join-Path $resolvedOutputDir "browser-text-metrics.json"
$manualInstructionsPath = Join-Path $resolvedOutputDir "manual-reference-instructions.txt"
$fixtureHtmlUri = Convert-ToFileUri $fixtureHtmlPath
$fontUri = Convert-ToFileUri $fontPath

$browserCaptureFixtures = @()

foreach ($definition in $definitions) {
    if ($browserExe) {
        $captureParams = Build-QueryString @{
            mode = "capture"
            width = "320"
            height = "64"
            x = "8"
            baseline = "40"
            showBaselineGuide = "true"
            baselineGuideColor = "#ff0000"
            fontSize = "32"
            fontFamily = "CrimsonText-Regular"
            fontUrl = $fontUri
            foreground = "#f0f0f0"
            background = "#101018"
            text = $definition.Text
        }

        $captureJson = Get-JsonFromDom (Invoke-BrowserDumpDom `
            -BrowserExe $browserExe `
            -Url ($fixtureHtmlUri + "?" + $captureParams) `
            -ExpectedOutputId "capture-output") `
            -ElementId "capture-output"

        $fixtureCapture = $captureJson | ConvertFrom-Json -AsHashtable
        $fixtureCapture["id"] = $definition.Id
        $browserCaptureFixtures += $fixtureCapture
    }
    else {
        $browserCaptureFixtures += @{
            id = $definition.Id
            text = $definition.Text
            fontFamily = "CrimsonText-Regular"
            fontSize = 32
            canvasWidth = 320
            canvasHeight = 64
            x = 8
            baselineY = 40
            baselineGuideEnabled = $true
            baselineGuideY = 40
            baselineGuideColor = "#ff0000"
            textBaseline = "alphabetic"
            textAlign = "left"
            background = "#101018"
            foreground = "#f0f0f0"
            metrics = @{
                width = $null
                actualBoundingBoxLeft = $null
                actualBoundingBoxRight = $null
                actualBoundingBoxAscent = $null
                actualBoundingBoxDescent = $null
                fontBoundingBoxAscent = $null
                fontBoundingBoxDescent = $null
                emHeightAscent = $null
                emHeightDescent = $null
                alphabeticBaseline = $null
                hangingBaseline = $null
                ideographicBaseline = $null
            }
            capture = $null
            unavailableReason = "No compatible headless browser was found for automated browser capture."
        }
    }
}

$browserCaptureDocument = @{
    browserPath = $browserExe
    fixtureHtmlPath = $fixtureHtmlPath
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    fixtures = $browserCaptureFixtures
}

$browserCaptureDocument |
    ConvertTo-Json -Depth 10 |
    Set-Content -LiteralPath $browserMetricsPath -Encoding UTF8

Push-Location $repoRoot

try {
    $env:MACHINA_FONT_REFERENCE_OUTPUT_DIR = $resolvedOutputDir
    $env:MACHINA_FONT_REFERENCE_BROWSER_METRICS_PATH = $browserMetricsPath

    $arguments = @(
        "test",
        $projectPath,
        "--configuration",
        $Configuration,
        "--filter",
        $filter
    )

    & dotnet @arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Machina reference-diff export failed."
    }
}
finally {
    Remove-Item Env:\MACHINA_FONT_REFERENCE_OUTPUT_DIR -ErrorAction SilentlyContinue
    Remove-Item Env:\MACHINA_FONT_REFERENCE_BROWSER_METRICS_PATH -ErrorAction SilentlyContinue
    Pop-Location
}

if (-not $browserExe) {
@"
Automated browser capture was not available.

Open the fixture below in Edge or Chrome, then rerun this script to generate browser-vs-Machina overlay artifacts.

Fixture HTML:
$fixtureHtmlPath

Fixture font:
$fontPath

Texts:
- Machina
- Hello Machina
- AV To Ta Wa Yo
- Aa0
- A A

Canvas:
- size: 320x64
- font size: 32px
- x: 8
- baselineY: 40
- baseline guide: enabled
- baseline guide color: #ff0000
"@ | Set-Content -LiteralPath $manualInstructionsPath

    Write-Host "Automated browser capture is not available."
    Write-Host "Machina proof artifacts and reports were still generated in $resolvedOutputDir"
    Write-Host "Manual instructions: $manualInstructionsPath"
    return
}

Write-Host "Created Machina reference-diff artifacts:"
Get-ChildItem -LiteralPath $resolvedOutputDir | Sort-Object Name | ForEach-Object {
    Write-Host $_.FullName
}
