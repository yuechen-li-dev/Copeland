[CmdletBinding()]
param(
    [string]$OutputDir = "artifacts\\m9i"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

if ([System.IO.Path]::IsPathRooted($OutputDir))
{
    $resolvedOutputDir = [System.IO.Path]::GetFullPath($OutputDir)
}
else
{
    $resolvedOutputDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}

New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null

$canonicalCommands = @(
    "dotnet test Copeland.slnx",
    "dotnet build Copeland.slnx --no-restore",
    ".\\tools\\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\\font-current -Preset cad-debug -TextBackend DirectOutlineStatic -GridStep 8 -ShowUnitLabels -ShowBounds -Clean",
    ".\\tools\\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\\font-current -IncludeDirectOutlineRenderBridgeProof",
    ".\\tools\\Export-MachinaPresenter.ps1 -OutputPath artifacts\\font-current\\presenter-direct-outline.png -IncludeDirectOutlineRenderBridgeProof"
)

$artifactsGenerated = @(
    "artifacts\\m9i\\component-gallery-direct-outline-render-bridge-proof.png",
    "artifacts\\m9i\\font-phase-closeout-manifest.json",
    "artifacts\\m9i\\font-phase-closeout-manifest.txt",
    "artifacts\\m9i\\presenter-direct-outline-render-bridge-proof.png"
)

$deferred = @(
    "word wrapping",
    "production renderer integration",
    "caller-positioned baseline anchor",
    "MSDF coverage/reconstruction polish"
)

$remainingMsdfWork = @(
    "coverage-reconstruction",
    "smoothing"
)

$manifest = [ordered]@{
    milestone = "M9i"
    kind = "machina-font-phase-closeout"
    directOutlineStatic = [ordered]@{
        status = "static-reference-path"
        renderBridge = $true
        textBoxLayout = $true
        componentGalleryProof = $true
        presenterProof = $true
    }
    msdf = [ordered]@{
        status = "explicit-experimental-scalable"
        alignmentRepair = "M9f"
        remainingWork = $remainingMsdfWork
    }
    tooling = [ordered]@{
        status = "proof-and-diagnostics-hygiene"
        canonicalArtifactRoot = "artifacts\\m9i"
        canonicalCommands = $canonicalCommands
    }
    presenterProof = [ordered]@{
        status = "opt-in-sample-and-export"
        exportAvailable = $true
        defaultBehaviorChanged = $false
    }
    productionUi = [ordered]@{
        defaultRendererChanged = $false
    }
    artifactsGenerated = $artifactsGenerated
    deferred = $deferred
}

$jsonPath = Join-Path $resolvedOutputDir "font-phase-closeout-manifest.json"
$textPath = Join-Path $resolvedOutputDir "font-phase-closeout-manifest.txt"

$jsonText = $manifest | ConvertTo-Json -Depth 8
$textLines = @(
    "milestone=M9i",
    "kind=machina-font-phase-closeout",
    "directOutlineStatic.status=static-reference-path",
    "directOutlineStatic.renderBridge=true",
    "directOutlineStatic.textBoxLayout=true",
    "directOutlineStatic.componentGalleryProof=true",
    "directOutlineStatic.presenterProof=true",
    "msdf.status=explicit-experimental-scalable",
    "msdf.alignmentRepair=M9f",
    "msdf.remainingWork=coverage-reconstruction,smoothing",
    "tooling.status=proof-and-diagnostics-hygiene",
    "tooling.canonicalArtifactRoot=artifacts\\m9i",
    "presenterProof.status=opt-in-sample-and-export",
    "presenterProof.exportAvailable=true",
    "presenterProof.defaultBehaviorChanged=false",
    "productionUi.defaultRendererChanged=false",
    "canonicalCommands:",
    "  dotnet test Copeland.slnx",
    "  dotnet build Copeland.slnx --no-restore",
    "  .\\tools\\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\\font-current -Preset cad-debug -TextBackend DirectOutlineStatic -GridStep 8 -ShowUnitLabels -ShowBounds -Clean",
    "  .\\tools\\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\\font-current -IncludeDirectOutlineRenderBridgeProof",
    "  .\\tools\\Export-MachinaPresenter.ps1 -OutputPath artifacts\\font-current\\presenter-direct-outline.png -IncludeDirectOutlineRenderBridgeProof",
    "artifactsGenerated:",
    "  artifacts\\m9i\\component-gallery-direct-outline-render-bridge-proof.png",
    "  artifacts\\m9i\\font-phase-closeout-manifest.json",
    "  artifacts\\m9i\\font-phase-closeout-manifest.txt",
    "  artifacts\\m9i\\presenter-direct-outline-render-bridge-proof.png",
    "deferred:",
    "  word wrapping",
    "  production renderer integration",
    "  caller-positioned baseline anchor",
    "  MSDF coverage/reconstruction polish"
)

Set-Content -LiteralPath $jsonPath -Value $jsonText -Encoding utf8
Set-Content -LiteralPath $textPath -Value ($textLines -join [Environment]::NewLine) -Encoding utf8

Write-Host "Created font phase closeout manifest files:"
Write-Host $jsonPath
Write-Host $textPath
