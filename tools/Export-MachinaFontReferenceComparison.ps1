[CmdletBinding()]
param(
    [string]$OutputDir = "artifacts\\local\\font-reference\\comparison",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$BrowserPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
. (Join-Path $PSScriptRoot "font-reference\FontReference.Common.ps1")
$projectPath = Join-Path $repoRoot "tests\\Machina.UI\\Machina.Fonts.Diagnostics.Tests\\Machina.Fonts.Diagnostics.Tests.csproj"
$filter = "FullyQualifiedName~Machina.Fonts.Tests.Rendering.FontReferenceOracleWorkflowTests.FontReferenceOracleWorkflow_ScriptWorkflowExportsArtifacts"
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

$browserMetricsFixtures = @()

foreach ($definition in $definitions) {
    if ($browserExe) {
        $metricsParams = Build-QueryString @{
            mode = "metrics"
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

        $metricsJson = Get-JsonFromDom `
            -DomLines (Invoke-BrowserDumpDom `
                -BrowserExe $browserExe `
                -Url ($fixtureHtmlUri + "?" + $metricsParams) `
                -ExpectedOutputId "metrics-output") `
            -ElementId "metrics-output"

        $fixtureMetrics = $metricsJson | ConvertFrom-Json -AsHashtable
        $fixtureMetrics["id"] = $definition.Id
        $browserMetricsFixtures += $fixtureMetrics
    }
    else {
        $browserMetricsFixtures += @{
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
            unavailableReason = "No compatible headless browser was found for automated TextMetrics capture."
        }
    }
}

$browserMetricsDocument = @{
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    browserPath = $browserExe
    fixtureHtmlPath = $fixtureHtmlPath
    fixtures = $browserMetricsFixtures
}

$browserMetricsDocument |
    ConvertTo-Json -Depth 8 |
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
        throw "Machina reference-oracle export failed."
    }
}
finally {
    Remove-Item Env:\MACHINA_FONT_REFERENCE_OUTPUT_DIR -ErrorAction SilentlyContinue
    Remove-Item Env:\MACHINA_FONT_REFERENCE_BROWSER_METRICS_PATH -ErrorAction SilentlyContinue
    Pop-Location
}

if (-not $browserExe) {
    @"
Automated browser reference export was not available.

Open the fixture below in Edge or Chrome, capture each reference image, then compare it with the Machina PNG next to it.

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
    Write-Host "Machina artifacts and placement reports were still generated in $resolvedOutputDir"
    Write-Host "Manual instructions: $manualInstructionsPath"
    return
}

foreach ($definition in $definitions) {
    $referencePath = Join-Path $resolvedOutputDir ("reference-" + $definition.Id + ".png")
    $referenceParams = Build-QueryString @{
        mode = "reference"
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

    Invoke-BrowserScreenshot `
        -BrowserExe $browserExe `
        -Url ($fixtureHtmlUri + "?" + $referenceParams) `
        -ScreenshotPath $referencePath `
        -Width 320 `
        -Height 64

    $machinaPngPath = Join-Path $resolvedOutputDir ("machina-msdf-" + $definition.Id + ".png")
    $comparePath = Join-Path $resolvedOutputDir ("compare-" + $definition.Id + ".png")
    $compareParams = Build-QueryString @{
        mode = "compare"
        title = ("CrimsonText-Regular 32px - " + $definition.Text)
        referenceLabel = "Browser canvas reference"
        machinaLabel = "Machina MSDF proof"
        referenceUrl = (Convert-ToFileUri $referencePath)
        machinaUrl = (Convert-ToFileUri $machinaPngPath)
    }

    Invoke-BrowserScreenshot `
        -BrowserExe $browserExe `
        -Url ($fixtureHtmlUri + "?" + $compareParams) `
        -ScreenshotPath $comparePath `
        -Width 720 `
        -Height 140
}

Write-Host "Created Machina reference-oracle artifacts:"
Get-ChildItem -LiteralPath $resolvedOutputDir | Sort-Object Name | ForEach-Object {
    Write-Host $_.FullName
}
