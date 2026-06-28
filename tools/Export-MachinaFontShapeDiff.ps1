[CmdletBinding()]
param(
    [string]$OutputDir = "artifacts\\m8s",
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

    $stdoutPath = Join-Path ([System.IO.Path]::GetTempPath()) ("machina-font-shape-diff-dom-" + [Guid]::NewGuid().ToString("N") + ".txt")
    $stderrPath = Join-Path ([System.IO.Path]::GetTempPath()) ("machina-font-shape-diff-dom-" + [Guid]::NewGuid().ToString("N") + ".err.txt")

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

function Get-JsonFromDom {
    param(
        [object]$DomLines,
        [string]$ElementId
    )

    if ($null -eq $DomLines) {
        throw "Browser DOM dump did not return any content."
    }

    $dom = if ($DomLines -is [System.Array]) {
        [string]::Join([Environment]::NewLine, [string[]]$DomLines)
    }
    else {
        [string]$DomLines
    }

    $pattern = '<pre[^>]*id="' + [System.Text.RegularExpressions.Regex]::Escape($ElementId) + '"[^>]*>(?<json>\{.*\})</pre>'
    $match = [System.Text.RegularExpressions.Regex]::Match(
        $dom,
        $pattern,
        [System.Text.RegularExpressions.RegexOptions]::Singleline)

    if (-not $match.Success) {
        throw "JSON payload '$ElementId' was not found in browser DOM output."
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
$filter = "FullyQualifiedName~Machina.Fonts.Tests.Rendering.ThreeWayShapeDiffWorkflowTests.ThreeWayShapeDiffWorkflow_ScriptWorkflowExportsArtifacts"
$fixtureHtmlPath = Join-Path $repoRoot "tools\\font-reference\\reference-render.html"
$fontPath = Join-Path $repoRoot "tests\\Machina.Fonts.Tests\\Fixtures\\Fonts\\CrimsonText-Regular.ttf"
$definitions = @(
    @{ Id = "machina"; Text = "Machina" },
    @{ Id = "hello-machina"; Text = "Hello Machina" },
    @{ Id = "kerning"; Text = "AV To Ta Wa Yo" },
    @{ Id = "aa0"; Text = "Aa0" },
    @{ Id = "a-space-a"; Text = "A A" }
)
$canvases = @(
    @{ SizePx = 32; Width = 320; Height = 64; X = 8; BaselineY = 40 },
    @{ SizePx = 48; Width = 480; Height = 96; X = 12; BaselineY = 60 },
    @{ SizePx = 64; Width = 640; Height = 128; X = 16; BaselineY = 80 }
)

$resolvedOutputDir = Resolve-FullPath $OutputDir
New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null
$browserExe = Find-BrowserPath $BrowserPath
$browserCapturePath = Join-Path $resolvedOutputDir "browser-shape-diff-captures.json"
$manualInstructionsPath = Join-Path $resolvedOutputDir "manual-shape-diff-instructions.txt"
$fixtureHtmlUri = Convert-ToFileUri $fixtureHtmlPath
$fontUri = Convert-ToFileUri $fontPath

$browserCaptureFixtures = @()

foreach ($canvas in $canvases) {
    foreach ($definition in $definitions) {
        if ($browserExe) {
            $captureParams = Build-QueryString @{
                mode = "capture"
                width = [string]$canvas.Width
                height = [string]$canvas.Height
                x = [string]$canvas.X
                baseline = [string]$canvas.BaselineY
                showBaselineGuide = "true"
                baselineGuideColor = "#ff0000"
                fontSize = [string]$canvas.SizePx
                fontFamily = "CrimsonText-Regular"
                fontUrl = $fontUri
                foreground = "#f0f0f0"
                background = "#101018"
                text = $definition.Text
            }

            $captureJson = Get-JsonFromDom (Invoke-BrowserDumpDom `
                -BrowserExe $browserExe `
                -Url ($fixtureHtmlUri + "?" + $captureParams)) `
                -ElementId "capture-output"

            $fixtureCapture = $captureJson | ConvertFrom-Json -AsHashtable
            $fixtureCapture["id"] = $definition.Id
            $fixtureCapture["sizePx"] = $canvas.SizePx
            $browserCaptureFixtures += $fixtureCapture
        }
        else {
            $browserCaptureFixtures += @{
                id = $definition.Id
                sizePx = $canvas.SizePx
                text = $definition.Text
                fontFamily = "CrimsonText-Regular"
                fontSize = $canvas.SizePx
                canvasWidth = $canvas.Width
                canvasHeight = $canvas.Height
                x = $canvas.X
                baselineY = $canvas.BaselineY
                baselineGuideEnabled = $true
                baselineGuideY = $canvas.BaselineY
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
                capture = $null
                unavailableReason = "No compatible headless browser was found for automated browser capture."
            }
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
    Set-Content -LiteralPath $browserCapturePath -Encoding UTF8

Push-Location $repoRoot

try {
    $env:MACHINA_FONT_SHAPE_DIFF_OUTPUT_DIR = $resolvedOutputDir
    $env:MACHINA_FONT_SHAPE_DIFF_BROWSER_CAPTURE_PATH = $browserCapturePath

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
        throw "Machina three-way shape-diff export failed."
    }
}
finally {
    Remove-Item Env:\MACHINA_FONT_SHAPE_DIFF_OUTPUT_DIR -ErrorAction SilentlyContinue
    Remove-Item Env:\MACHINA_FONT_SHAPE_DIFF_BROWSER_CAPTURE_PATH -ErrorAction SilentlyContinue
    Pop-Location
}

if (-not $browserExe) {
@"
Automated browser capture was not available.

The direct-outline and MSDF artifacts were still generated. Browser-specific pairwise comparisons remain unavailable until a capture document is supplied.

Fixture HTML:
$fixtureHtmlPath

Fixture font:
$fontPath

Sizes:
- 32px => canvas 320x64, x=8, baselineY=40
- 48px => canvas 480x96, x=12, baselineY=60
- 64px => canvas 640x128, x=16, baselineY=80

Texts:
- Machina
- Hello Machina
- AV To Ta Wa Yo
- Aa0
- A A

Baseline guide:
- enabled
- color: #ff0000
"@ | Set-Content -LiteralPath $manualInstructionsPath

    Write-Host "Automated browser capture is not available."
    Write-Host "Partial M8s artifacts were still generated in $resolvedOutputDir"
    Write-Host "Manual instructions: $manualInstructionsPath"
    return
}

Write-Host "Created Machina M8s shape-diff artifacts:"
Get-ChildItem -LiteralPath $resolvedOutputDir -Recurse | Sort-Object FullName | ForEach-Object {
    Write-Host $_.FullName
}
