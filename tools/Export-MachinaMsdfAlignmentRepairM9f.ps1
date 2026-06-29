[CmdletBinding()]
param(
    [string]$OutputDir = "artifacts\\m9f",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "tests\\Machina.Fonts.Tooling.Tests\\Machina.Fonts.Tooling.Tests.csproj"
$filter = "FullyQualifiedName~Machina.Fonts.Tooling.Tests.MsdfAlignmentRegressionTests.MsdfAlignmentExport_M9fWorkflowExportsArtifacts"

if ([System.IO.Path]::IsPathRooted($OutputDir))
{
    $resolvedOutputDir = [System.IO.Path]::GetFullPath($OutputDir)
}
else
{
    $resolvedOutputDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}

$beforeDirectory = Join-Path $resolvedOutputDir "_before"

function Remove-DirectoryIfRequested([string]$path)
{
    if ($Clean.IsPresent -and (Test-Path -LiteralPath $path))
    {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

function Run-M9fExport([string]$directory, [bool]$scaleFields)
{
    $env:MACHINA_M9F_OUTPUT_DIR = $directory
    $env:MACHINA_M9F_SCALE_FIELDS = $scaleFields.ToString().ToLowerInvariant()

    try
    {
        & dotnet test $projectPath --configuration $Configuration --filter $filter
        if ($LASTEXITCODE -ne 0)
        {
            throw "M9f export workflow failed for '$directory'."
        }
    }
    finally
    {
        Remove-Item Env:\MACHINA_M9F_OUTPUT_DIR -ErrorAction SilentlyContinue
        Remove-Item Env:\MACHINA_M9F_SCALE_FIELDS -ErrorAction SilentlyContinue
    }
}

function Get-Fixture([object]$report, [int]$sizePx, [string]$text)
{
    $size = $report.Sizes | Where-Object { $_.SizePx -eq $sizePx } | Select-Object -First 1
    if ($null -eq $size)
    {
        throw "Missing size report for ${sizePx}px."
    }

    $fixture = $size.Fixtures | Where-Object { $_.Text -eq $text } | Select-Object -First 1
    if ($null -eq $fixture)
    {
        throw "Missing fixture '$text' at ${sizePx}px."
    }

    return $fixture
}

function Copy-Artifact([string]$sourceRelativePath, [string]$targetFileName)
{
    $sourcePath = Join-Path $resolvedOutputDir $sourceRelativePath
    $targetPath = Join-Path $resolvedOutputDir $targetFileName
    Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
}

function New-BeforeAfterComposite([string]$beforePath, [string]$afterPath, [string]$outputPath)
{
    Add-Type -AssemblyName System.Drawing

    $beforeImage = [System.Drawing.Bitmap]::new($beforePath)
    $afterImage = [System.Drawing.Bitmap]::new($afterPath)

    try
    {
        $gutter = 12
        $width = $beforeImage.Width + $afterImage.Width + $gutter
        $height = [Math]::Max($beforeImage.Height, $afterImage.Height)
        $canvas = [System.Drawing.Bitmap]::new($width, $height)

        try
        {
            $graphics = [System.Drawing.Graphics]::FromImage($canvas)
            try
            {
                $graphics.Clear([System.Drawing.Color]::FromArgb(16, 16, 24))
                $graphics.DrawImage($beforeImage, 0, 0, $beforeImage.Width, $beforeImage.Height)
                $graphics.DrawImage($afterImage, $beforeImage.Width + $gutter, 0, $afterImage.Width, $afterImage.Height)
            }
            finally
            {
                $graphics.Dispose()
            }

            $canvas.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally
        {
            $canvas.Dispose()
        }
    }
    finally
    {
        $beforeImage.Dispose()
        $afterImage.Dispose()
    }
}

function Write-AlignmentReports([object]$beforeReport, [object]$afterReport)
{
    $samples = @(
        @{ Text = "Hello Machina"; FontSize = 64; Artifact = "m9f-direct-vs-msdf-hello-machina.png" },
        @{ Text = "Machina"; FontSize = 64; Artifact = "m9f-direct-vs-msdf-machina.png" },
        @{ Text = "Settings"; FontSize = 64; Artifact = "m9f-direct-vs-msdf-settings.png" },
        @{ Text = "AV To Ta Wa Yo"; FontSize = 64; Artifact = "" },
        @{ Text = "Aa0 1234567890"; FontSize = 64; Artifact = "" },
        @{ Text = "Direct outline static text"; FontSize = 64; Artifact = "" }
    )

    $sampleReports = foreach ($sample in $samples)
    {
        $beforeFixture = Get-Fixture $beforeReport $sample.FontSize $sample.Text
        $afterFixture = Get-Fixture $afterReport $sample.FontSize $sample.Text

        [ordered]@{
            text = $sample.Text
            fontSize = $sample.FontSize
            before = [ordered]@{
                iou = $beforeFixture.DirectVsMsdf.IntersectionOverUnion
                meanEdgeDistance = $beforeFixture.DirectVsMsdf.MeanEdgeDistance
                p95EdgeDistance = $beforeFixture.DirectVsMsdf.P95EdgeDistance
                maxEdgeDistance = $beforeFixture.DirectVsMsdf.MaxEdgeDistance
                boundsDelta = [ordered]@{
                    left = $beforeFixture.DirectVsMsdf.DeltaLeft
                    top = $beforeFixture.DirectVsMsdf.DeltaTop
                    right = $beforeFixture.DirectVsMsdf.DeltaRight
                    bottom = $beforeFixture.DirectVsMsdf.DeltaBottom
                    width = $beforeFixture.DirectVsMsdf.DeltaWidth
                    height = $beforeFixture.DirectVsMsdf.DeltaHeight
                }
            }
            after = [ordered]@{
                iou = $afterFixture.DirectVsMsdf.IntersectionOverUnion
                meanEdgeDistance = $afterFixture.DirectVsMsdf.MeanEdgeDistance
                p95EdgeDistance = $afterFixture.DirectVsMsdf.P95EdgeDistance
                maxEdgeDistance = $afterFixture.DirectVsMsdf.MaxEdgeDistance
                boundsDelta = [ordered]@{
                    left = $afterFixture.DirectVsMsdf.DeltaLeft
                    top = $afterFixture.DirectVsMsdf.DeltaTop
                    right = $afterFixture.DirectVsMsdf.DeltaRight
                    bottom = $afterFixture.DirectVsMsdf.DeltaBottom
                    width = $afterFixture.DirectVsMsdf.DeltaWidth
                    height = $afterFixture.DirectVsMsdf.DeltaHeight
                }
            }
            diagnosis = [ordered]@{
                suspectedStage = "projectionScale/fieldResolution"
                fixApplied = "Scale experimental MSDF field dimensions with em size and sample atlas UVs with a texel-center contract."
                notes = "DirectOutlineStatic remained the geometry oracle. No arbitrary offsets were introduced."
            }
        }
    }

    $report = [ordered]@{
        milestone = "M9f"
        geometryOracle = "DirectOutlineStatic"
        msdfBackend = "MsdfScalableExperimental"
        layoutContract = [ordered]@{
            sharedRunLayout = $true
            browserKerningTarget = $false
            notes = "Direct-outline and MSDF diagnostics share the same DistanceFieldTextLayout pen positions, advances, pair adjustments, whitespace handling, and glyph order."
        }
        samples = $sampleReports
        summary = [ordered]@{
            fixedStages = @(
                "UV and texel-center sampling contract",
                "Experimental MSDF field resolution scaling"
            )
            deferredStages = @(
                "Additional smoothing-only tuning, if still desired after M9f"
            )
            warnings = @(
                "MSDF remains explicit experimental/scalable and is not the production UI default.",
                "Browser kerning is not the M9f geometry oracle."
            )
        }
    }

    $jsonPath = Join-Path $resolvedOutputDir "msdf-alignment-report.json"
    $txtPath = Join-Path $resolvedOutputDir "msdf-alignment-report.txt"
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("Machina MSDF Alignment Repair M9f")
    $lines.Add("geometryOracle: DirectOutlineStatic")
    $lines.Add("msdfBackend: MsdfScalableExperimental")
    $lines.Add("diagnosis: the experimental MSDF path was still using a fixed 32x32 field for larger text, so larger sizes reconstructed the right layout from under-resolved glyph fields. A smaller UV texel-center mismatch also existed in sampling.")
    $lines.Add("fixApplied: scale MSDF field dimensions with em size in the experimental diagnostic/export path and sample atlas UVs on a texel-center contract.")
    $lines.Add("")

    foreach ($sample in $sampleReports)
    {
        $lines.Add("[$($sample.fontSize)px] $($sample.text)")
        $lines.Add("  before: IoU=$('{0:0.0000}' -f $sample.before.iou), meanEdge=$('{0:0.0000}' -f $sample.before.meanEdgeDistance), p95=$('{0:0.0000}' -f $sample.before.p95EdgeDistance), max=$('{0:0.0000}' -f $sample.before.maxEdgeDistance)")
        $lines.Add("  after:  IoU=$('{0:0.0000}' -f $sample.after.iou), meanEdge=$('{0:0.0000}' -f $sample.after.meanEdgeDistance), p95=$('{0:0.0000}' -f $sample.after.p95EdgeDistance), max=$('{0:0.0000}' -f $sample.after.maxEdgeDistance)")
        $lines.Add("  fix: $($sample.diagnosis.fixApplied)")
        $lines.Add("")
    }

    Set-Content -LiteralPath $txtPath -Value $lines
}

Push-Location $repoRoot

try
{
    Remove-DirectoryIfRequested $beforeDirectory
    Remove-DirectoryIfRequested $resolvedOutputDir
    New-Item -ItemType Directory -Force -Path $resolvedOutputDir | Out-Null

    Run-M9fExport -directory $beforeDirectory -scaleFields $false
    Run-M9fExport -directory $resolvedOutputDir -scaleFields $true

    $beforeReport = Get-Content -LiteralPath (Join-Path $beforeDirectory "shape-diff-report.json") | ConvertFrom-Json
    $afterReport = Get-Content -LiteralPath (Join-Path $resolvedOutputDir "shape-diff-report.json") | ConvertFrom-Json

    Copy-Artifact "64\\m9d-direct-vs-msdf-hello-machina.png" "m9f-direct-vs-msdf-hello-machina.png"
    Copy-Artifact "64\\m9d-direct-vs-msdf-machina.png" "m9f-direct-vs-msdf-machina.png"
    Copy-Artifact "64\\m9d-direct-vs-msdf-settings.png" "m9f-direct-vs-msdf-settings.png"
    Copy-Artifact "64\\m9d-msdf-debug-hello-machina.png" "m9f-msdf-debug-hello-machina.png"
    Copy-Artifact "64\\m9d-cad-debug-hello-machina.png" "m9f-cad-debug-hello-machina.png"

    New-BeforeAfterComposite `
        -beforePath (Join-Path $beforeDirectory "64\\m9d-direct-vs-msdf-hello-machina.png") `
        -afterPath (Join-Path $resolvedOutputDir "64\\m9d-direct-vs-msdf-hello-machina.png") `
        -outputPath (Join-Path $resolvedOutputDir "m9f-before-after-direct-vs-msdf-hello-machina.png")

    Write-AlignmentReports -beforeReport $beforeReport -afterReport $afterReport
}
finally
{
    Pop-Location
}

Write-Host "Created Machina M9f MSDF alignment artifacts:"
Get-ChildItem -LiteralPath $resolvedOutputDir -Recurse | Sort-Object FullName | ForEach-Object {
    Write-Host $_.FullName
}
