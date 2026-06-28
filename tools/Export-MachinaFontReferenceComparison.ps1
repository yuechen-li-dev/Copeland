[CmdletBinding()]
param(
    [string]$OutputDir = "artifacts\\m8o",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$BrowserPath
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$PathValue)

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $PathValue))
}

function Find-BrowserPath {
    param([string]$PreferredPath)

    if (-not [string]::IsNullOrWhiteSpace($PreferredPath)) {
        if (-not (Test-Path -LiteralPath $PreferredPath)) {
            throw "BrowserPath does not exist: $PreferredPath"
        }

        return [System.IO.Path]::GetFullPath($PreferredPath)
    }

    $candidates = @(
        "C:\Program Files\Google\Chrome\Application\chrome.exe",
        "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return $null
}

function Convert-ToFileUri {
    param([string]$PathValue)
    return ([System.Uri]([System.IO.Path]::GetFullPath($PathValue))).AbsoluteUri
}

function Invoke-BrowserScreenshot {
    param(
        [string]$BrowserExe,
        [string]$Url,
        [string]$ScreenshotPath,
        [int]$Width,
        [int]$Height
    )

    $args = @(
        "--headless=new",
        "--disable-gpu",
        "--hide-scrollbars",
        "--force-device-scale-factor=1",
        "--allow-file-access-from-files",
        "--virtual-time-budget=3000",
        "--window-size=$Width,$Height",
        "--screenshot=$ScreenshotPath",
        $Url
    )

    & $BrowserExe @args

    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "Browser screenshot failed for $ScreenshotPath"
    }

    $attempt = 0
    while ($attempt -lt 20 -and -not (Test-Path -LiteralPath $ScreenshotPath)) {
        Start-Sleep -Milliseconds 250
        $attempt++
    }

    if (-not (Test-Path -LiteralPath $ScreenshotPath)) {
        throw "Expected screenshot was not created: $ScreenshotPath"
    }
}

function Invoke-BrowserDumpDom {
    param(
        [string]$BrowserExe,
        [string]$Url
    )

    $args = @(
        "--headless=new",
        "--disable-gpu",
        "--hide-scrollbars",
        "--force-device-scale-factor=1",
        "--allow-file-access-from-files",
        "--virtual-time-budget=3000",
        "--dump-dom",
        $Url
    )

    $stdoutPath = Join-Path ([System.IO.Path]::GetTempPath()) ("machina-font-reference-dom-" + [Guid]::NewGuid().ToString("N") + ".txt")
    $stderrPath = Join-Path ([System.IO.Path]::GetTempPath()) ("machina-font-reference-dom-" + [Guid]::NewGuid().ToString("N") + ".err.txt")

    try {
        $process = Start-Process `
            -FilePath $BrowserExe `
            -ArgumentList $args `
            -NoNewWindow `
            -PassThru `
            -Wait `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath

        if ($process.ExitCode -ne 0) {
            $stderr = if (Test-Path -LiteralPath $stderrPath) {
                Get-Content -LiteralPath $stderrPath -Raw
            }
            else {
                ""
            }

            throw "Browser DOM dump failed for $Url`n$stderr"
        }

        return Get-Content -LiteralPath $stdoutPath -Raw
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $stderrPath -ErrorAction SilentlyContinue
    }
}

function Get-MetricsJsonFromDom {
    param([object]$DomLines)

    if ($null -eq $DomLines) {
        throw "Browser DOM dump did not return any content."
    }

    $dom = if ($DomLines -is [System.Array]) {
        [string]::Join([Environment]::NewLine, [string[]]$DomLines)
    }
    else {
        [string]$DomLines
    }

    $match = [System.Text.RegularExpressions.Regex]::Match(
        $dom,
        '<pre[^>]*id="metrics-output"[^>]*>(?<json>\{.*\})</pre>',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)

    if (-not $match.Success) {
        throw "Metrics JSON payload was not found in browser DOM output."
    }

    return $match.Groups["json"].Value
}

function Build-QueryString {
    param([hashtable]$Values)

    $pairs = foreach ($key in $Values.Keys) {
        $encodedKey = [System.Uri]::EscapeDataString([string]$key)
        $encodedValue = [System.Uri]::EscapeDataString([string]$Values[$key])
        "$encodedKey=$encodedValue"
    }

    return [string]::Join("&", $pairs)
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "tests\\Machina.Fonts.Tests\\Machina.Fonts.Tests.csproj"
$filter = "FullyQualifiedName~Machina.Fonts.Tests.Rendering.FontReferenceOracleWorkflowTests.FontReferenceOracleWorkflow_ScriptWorkflowExportsArtifacts"
$fixtureHtmlPath = Join-Path $repoRoot "tools\\font-reference\\reference-render.html"
$fontPath = Join-Path $repoRoot "tests\\Machina.Fonts.Tests\\Fixtures\\Fonts\\CrimsonText-Regular.ttf"
$definitions = @(
    @{ Id = "machina"; Text = "Machina" },
    @{ Id = "hello-machina"; Text = "Hello Machina" },
    @{ Id = "kerning"; Text = "AV To Ta Wa Yo" },
    @{ Id = "aa0"; Text = "Aa0" },
    @{ Id = "a-space-a"; Text = "A A" }
)

$resolvedOutputDir = Resolve-FullPath $OutputDir
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

        $metricsJson = Get-MetricsJsonFromDom (Invoke-BrowserDumpDom `
            -BrowserExe $browserExe `
            -Url ($fixtureHtmlUri + "?" + $metricsParams))

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
