param(
    [string]$Output = 'artifacts/local/rendering-reference/final',
    [string]$LightingFixture = 'artifacts/local/beacon-compiled-lighting',
    [switch]$IncludeR3,
    [switch]$IncludeSampling,
    [switch]$IncludeRestir,
    [switch]$IncludeTlas,
    [switch]$IncludeRasterMotion,
    [switch]$IncludeArea,
    [switch]$IncludeGi
)

$ErrorActionPreference = 'Stop'
if ($IncludeGi) { $IncludeArea = $true }
if ($IncludeArea) { $IncludeRasterMotion = $true }
if ($IncludeRasterMotion) { $IncludeTlas = $true }
if ($IncludeTlas) { $IncludeRestir = $true }
if ($IncludeRestir) { $IncludeSampling = $true }
if ($IncludeSampling) { $IncludeR3 = $true }
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$previousSyncValidation = $env:VK_LAYER_VALIDATE_SYNC
Push-Location $repository
try {
    $outputPath = [IO.Path]::GetFullPath($Output)
    $fixturePath = [IO.Path]::GetFullPath($LightingFixture)
    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
    $evidencePath = Join-Path $outputPath 'qualification.json'
    @{ Accepted = $false } | ConvertTo-Json | Set-Content -LiteralPath $evidencePath
    $sourcePaths = @(
        'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/ReferenceIntegrator3D.cs',
        'src/Aurelian/Aurelian.Rendering.Contracts/Models/EnvironmentRadiance3D.cs',
        'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/ReferenceLighting3D.cs',
        'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/VulkanReferenceOutput3D.cs',
        'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/VulkanEnvironmentCompiler.cs',
        'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/ReferenceDiffuseScene3D.cs',
        'src/Aurelian/Aurelian.Graphics/Vulkan/RayQueries/VulkanRayQueryScene.cs',
        'src/Aurelian/Aurelian.Graphics/Vulkan/RayQueries/RayQueryAnalytic3D.cs',
        'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/NativeInstanceSet3D.cs',
        'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/VulkanRetainedModels3D.cs',
        'src/Aurelian/Aurelian.Shaders/Assets/Queries/ReferenceIntegrator3D.v.ts',
        'src/Aurelian/Aurelian.Shaders/Assets/Queries/ReferenceFurnace3D.v.ts',
        'src/Aurelian/Aurelian.Shaders/Assets/Lighting/Lighting3D.v.ts',
        'src/Aurelian/Aurelian.Shaders/Assets/Lighting/EnvironmentCoordinates3D.v.ts',
        'src/Aurelian/Aurelian.Shaders/Assets/Lighting/EnvironmentCompile3D.v.ts',
        'src/Aurelian/Aurelian.Shaders/Assets/Queries/ReferenceEnvironment3D.v.ts',
        'src/Aurelian/Aurelian.Shaders/Assets/PostProcessing/ToneMap3D.v.ts',
        'src/Aurelian/Aurelian.Shaders/Assets/Surfaces/AnalyticIntersections3D.v.ts',
        'src/Aurelian/Aurelian.Shaders/Assets/Surfaces/AnalyticGeometry3D.v.ts',
        'src/Aurelian/Aurelian.Shaders/Assets/Surfaces/AnalyticRaster3D.v.ts',
        'src/Aurelian/Aurelian.Shaders/Assets/Surfaces/HybridResolve3D.v.ts',
        'src/Aurelian/Aurelian.Shaders/Assets/Queries/AffineAnalyticQuery3D.v.ts',
        'src/Aurelian/Aurelian.Shaders/Assets/Queries/AffineRayQuery3D.v.ts',
        'src/Aurelian/Aurelian.Shaders/Compute/RayQueryHlslEmitter.cs',
        'src/Aurelian/Aurelian.Shaders/Compute/VdMirComputeHlslEmitter.cs',
        'src/Aurelian/Aurelian.Shaders/Compute/VdMirComputeHlslEmitter.Resources.cs',
        'src/Aurelian/Aurelian.Shaders/Compute/VdMirComputeHlslEmitter.RayQueries.cs',
        'src/Aurelian/Aurelian.Shaders/Compute/VdMirComputeBackend.cs',
        'src/Aurelian/Aurelian.Shaders.Builtins/Library/BuiltinShaderLibrary.cs',
        'src/Copeland/Copeland.TS/Gpu/GpuComputeBinder.cs',
        'src/Copeland/Copeland.TS/Gpu/GpuComputeBinder.RayQueries.cs',
        'src/Copeland/Copeland.TS/Gpu/GpuConstants.cs',
        'tests/Aurelian/Aurelian.Shaders.Tests/RayQueryShaderTests.cs',
        'tests/Aurelian/Aurelian.Graphics.Tests/ReferenceDiffuseScene3DTests.cs',
        'tests/Aurelian/Aurelian.Graphics.Tests/RayQueryAnalytic3DTests.cs',
        'tests/Aurelian/Aurelian.Graphics.Tests/ReferenceLighting3DTests.cs',
        'tests/Aurelian/Aurelian.Rendering.Contracts.Tests/EnvironmentRadiance3DTests.cs',
        'tools/Aurelian.GraphicsProof/RenderingReferenceProof.cs',
        'tools/Aurelian.GraphicsProof/RenderingReferenceProof.Receivers.cs',
        'tools/Aurelian.GraphicsProof/RenderingReferenceLightingProof.cs',
        'tools/Aurelian.GraphicsProof/RenderingReferenceLightingProof.Environment.cs',
        'tools/Aurelian.GraphicsProof/RenderingReferenceLightingProof.Temporal.cs',
        'tools/Aurelian.GraphicsProof/RenderingHybridProof.Oracle.cs',
        'tools/Aurelian.GraphicsProof/RenderingHybridProof.AffineQueries.cs',
        'tools/Aurelian.GraphicsProof/RenderingHybridProof.AffineRaster.cs',
        'tools/Aurelian.GraphicsProof/RenderingHybridProof.AffineCapture.cs',
        'tools/Aurelian.GraphicsProof/Program.cs',
        'tools/aurelian-rendering-reference/Qualify.ps1',
        'tools/aurelian-rendering-reference/requirements-metrics.txt',
        'tools/aurelian-rendering-reference/evaluate_metrics.py'
    )
    if ($IncludeR3) {
        $sourcePaths += @(
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/NativeMesh3D.cs',
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/VulkanSolid3DRenderer.cs',
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/VulkanSolid3DRenderer.FramePlan.cs',
            'tests/Aurelian/Aurelian.Shaders.Tests/GraphicsStarterShaderTests.cs',
            'tests/Aurelian/Aurelian.Rendering.Contracts.Tests/SurfaceLightingContractsTests.cs',
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/VulkanBindlessMaterials3D.cs',
            'tests/Aurelian/Aurelian.Graphics.Tests/NativeMesh3DTests.cs',
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/LocalLightPacking3D.cs',
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/VulkanLocalRayShadows3D.cs',
            'src/Aurelian/Aurelian.Graphics/Vulkan/RayQueries/VulkanSceneAcceleration3D.cs',
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/VulkanSurfaceLighting3D.cs',
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/Native3DFrameGraph.cs',
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/SurfaceLightingPrograms.cs',
            'src/Aurelian/Aurelian.Rendering.Contracts/Models/LocalLight3D.cs',
            'src/Aurelian/Aurelian.Rendering.Contracts/Models/Graphics3DSettings.cs',
            'src/Aurelian/Aurelian.Shaders/Assets/Queries/LocalShadows3D.v.ts',
            'src/Aurelian/Aurelian.Shaders/Assets/Surfaces/SurfaceResolve3D.v.ts',
            'src/Aurelian/Aurelian.Shaders/Assets/Surfaces/SurfaceLighting.v.ts',
            'src/Aurelian/Aurelian.Shaders/Assets/Lighting/LightTiles3D.v.ts',
            'src/Aurelian/Aurelian.Shaders.Builtins/Library/BuiltinShaderLibrary.cs',
            'tools/Aurelian.GraphicsProof/RenderingR3Proof.cs',
            'tools/Aurelian.GraphicsProof/RenderingBindlessProof.cs',
            'tools/aurelian-rendering-r3/Qualify.ps1'
        )
        $sourcePaths = @($sourcePaths | Select-Object -Unique)
    }
    if ($IncludeSampling) {
        $sourcePaths += @(
            'src/Aurelian/Aurelian.Rendering.Contracts/Models/LocalLightSampling3D.cs',
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/Vulkan3DQualificationReadback.cs',
            'src/Aurelian/Aurelian.Shaders/Assets/Queries/LocalLightSamplingMath3D.v.ts',
            'src/Aurelian/Aurelian.Shaders/Assets/Queries/SampledLocalLights3D.v.ts',
            'tools/Aurelian.GraphicsProof/RenderingR3Proof.Sampling.cs',
            'tools/aurelian-light-sampling/Qualify.ps1'
        )
    }
    if ($IncludeRestir) {
        $sourcePaths += @(
            'src/Aurelian/Aurelian.Shaders/Assets/Queries/LocalLightProposal3D.v.ts',
            'src/Aurelian/Aurelian.Shaders/Assets/Queries/RestirSpatial3D.v.ts',
            'tools/Aurelian.GraphicsProof/RenderingR3Proof.Restir.cs',
            'tools/aurelian-restir/Qualify.ps1'
        )
    }
    if ($IncludeTlas) {
        $sourcePaths += @(
            'src/Aurelian/Aurelian.Graphics/Vulkan/RayQueries/VulkanSceneAcceleration3D.Updates.cs',
            'tests/Aurelian/Aurelian.Graphics.Tests/SceneAccelerationUpdateTests.cs',
            'tools/Aurelian.GraphicsProof/RenderingR3Proof.Tlas.cs',
            'tools/aurelian-tlas/Qualify.ps1'
        )
    }
    if ($IncludeRasterMotion) {
        $sourcePaths += @(
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/Native3DContracts.cs',
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/VulkanRetainedModels3D.cs',
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/VulkanClusterVisibility3D.cs',
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/VulkanVisibilityBuffer3D.cs',
            'tools/Aurelian.GraphicsProof/RenderingR3Proof.RasterMotion.cs'
        )
    }
    if ($IncludeArea) {
        $sourcePaths += @(
            'src/Aurelian/Aurelian.Shaders/Assets/Lighting/LocalEmitter3D.v.ts',
            'src/Aurelian/Aurelian.Shaders/Assets/Lighting/LocalTailTemporal3D.v.ts',
            'src/Aurelian/Aurelian.Shaders/Assets/Lighting/LocalTailSpatial3D.v.ts',
            'src/Aurelian/Aurelian.Shaders/Assets/Lighting/LocalTailCompose3D.v.ts',
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/VulkanLocalTail3D.cs',
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/VulkanPostProcess3D.cs',
            'tools/Aurelian.GraphicsProof/RenderingR3Proof.Area.cs',
            'tools/aurelian-area-lights/Qualify.ps1'
        )
    }
    if ($IncludeGi) {
        $sourcePaths += @(
            'src/Aurelian/Aurelian.Rendering.Contracts/Lighting/DynamicDiffuseLighting3D.cs',
            'src/Aurelian/Aurelian.Rendering.Contracts/Lighting/DiffuseProbeGrid.cs',
            'src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/VulkanDynamicDiffuse3D.cs',
            'src/Aurelian/Aurelian.Graphics/Vulkan/RayQueries/VulkanSceneAcceleration3D.Shading.cs',
            'src/Aurelian/Aurelian.Shaders/Assets/Queries/DynamicDiffuse3D.v.ts',
            'tests/Aurelian/Aurelian.Rendering.Contracts.Tests/DynamicDiffuseLightingTests.cs',
            'tools/Aurelian.GraphicsProof/RenderingR3Proof.Gi.cs',
            'tools/aurelian-rendering-gi/Qualify.ps1'
        )
    }
    $sourcePaths = @($sourcePaths | Select-Object -Unique)
    $initialHashes = @($sourcePaths | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })

    function Invoke-QualificationCommand {
        param([string]$Name, [string[]]$Arguments, [string]$Executable = 'dotnet')
        $logPath = Join-Path $outputPath ($Name + '.log')
        & $Executable @Arguments *> $logPath
        if ($LASTEXITCODE -ne 0) {
            Get-Content -LiteralPath $logPath -Tail 25
            throw "$Name failed with exit code $LASTEXITCODE. See $logPath"
        }
        if (Select-String -LiteralPath $logPath -Pattern 'Validation Error:|SYNC-HAZARD|VUID-' -Quiet) {
            throw "$Name emitted Vulkan validation diagnostics. See $logPath"
        }
        Write-Output "$Name passed"
    }

    $metricsRoot = Join-Path $repository 'artifacts/local/rendering-metrics-tools/venv'
    $metricsPython = Join-Path $metricsRoot 'Scripts/python.exe'
    if (-not (Test-Path -LiteralPath $metricsPython)) {
        Invoke-QualificationCommand 'metrics-venv' @('-m', 'venv', $metricsRoot) 'python'
    }
    Invoke-QualificationCommand 'metrics-dependencies' @('-m', 'pip', 'install', '--disable-pip-version-check',
        '-r', (Join-Path $repository 'tools/aurelian-rendering-reference/requirements-metrics.txt')) $metricsPython

    Invoke-QualificationCommand 'build' @('build', 'Aurelian.slnx', '-c', 'Release', '-m:1', '-nr:false', '-p:UseSharedCompilation=false')
    $naga = Join-Path $repository 'artifacts/local/webgpu-tools/bin/naga.exe'
    if (Test-Path -LiteralPath $naga) {
        $env:AURELIAN_NAGA = $naga
    }
    Invoke-QualificationCommand 'tests' @('test', 'Aurelian.slnx', '-c', 'Release', '--no-build', '-m:1', '-nr:false',
        '--logger', 'trx', '--results-directory', (Join-Path $outputPath 'test-results'))
    Invoke-QualificationCommand 'compiler-tests' @('test', 'tests/Copeland/Copeland.TS.Tests/Copeland.TS.Tests.csproj', '-c', 'Release',
        '-m:1', '-nr:false', '-p:UseSharedCompilation=false', '--logger', 'trx',
        '--results-directory', (Join-Path $outputPath 'compiler-test-results'))
    $env:VK_LAYER_VALIDATE_SYNC = '1'
    $proof = Join-Path $repository 'tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll'
    Invoke-QualificationCommand 'reference' @($proof, '--rendering-reference', '--output', (Join-Path $outputPath 'capture'),
        '--reference-lighting', $fixturePath)
    Invoke-QualificationCommand 'affine' @($proof, '--rendering-affine', '--output', (Join-Path $outputPath 'affine'))
    Invoke-QualificationCommand 'lighting-temporal' @($proof, '--rendering-reference-lighting', '--output',
        (Join-Path $outputPath 'lighting-temporal'), '--reference-lighting', $fixturePath)
    Invoke-QualificationCommand 'flip' @((Join-Path $repository 'tools/aurelian-rendering-reference/evaluate_metrics.py'),
        (Join-Path $outputPath 'lighting-temporal/metrics-manifest.json')) $metricsPython
    Invoke-QualificationCommand 'hybrid-regression' @($proof, '--rendering-hybrid', '--output', (Join-Path $outputPath 'hybrid-regression'))
    Invoke-QualificationCommand 'diffuse-regression' @($proof, '--diffuse-proof', '--output', (Join-Path $outputPath 'diffuse-regression'))

    $r3 = $null
    $r3Flip = $null
    if ($IncludeR3) {
        Invoke-QualificationCommand 'r3' @($proof, '--rendering-r3', '--output', (Join-Path $outputPath 'r3'))
        Invoke-QualificationCommand 'r3-flip' @((Join-Path $repository 'tools/aurelian-rendering-reference/evaluate_metrics.py'),
            (Join-Path $outputPath 'r3/metrics-manifest.json')) $metricsPython
        $r3 = Get-Content -LiteralPath (Join-Path $outputPath 'r3/evidence.json') -Raw | ConvertFrom-Json
        $r3Flip = Get-Content -LiteralPath (Join-Path $outputPath 'r3/flip-evidence.json') -Raw | ConvertFrom-Json
        if (-not $r3.Accepted -or -not $r3Flip.Accepted) { throw 'R3 native and FLIP qualification did not accept.' }
    }

    $sampling = $null
    $samplingFlip = $null
    if ($IncludeSampling) {
        Invoke-QualificationCommand 'light-sampling' @($proof, '--rendering-light-sampling', '--output', (Join-Path $outputPath 'sampling'))
        Invoke-QualificationCommand 'sampling-flip' @((Join-Path $repository 'tools/aurelian-rendering-reference/evaluate_metrics.py'),
            (Join-Path $outputPath 'sampling/metrics-manifest.json')) $metricsPython
        $sampling = Get-Content -LiteralPath (Join-Path $outputPath 'sampling/evidence.json') -Raw | ConvertFrom-Json
        $samplingFlip = Get-Content -LiteralPath (Join-Path $outputPath 'sampling/flip-evidence.json') -Raw | ConvertFrom-Json
        if (-not $sampling.Accepted -or -not $samplingFlip.Accepted) { throw 'Local-light sampling and FLIP qualification did not accept.' }
    }

    $restir = $null
    $restirFlip = $null
    if ($IncludeRestir) {
        Invoke-QualificationCommand 'restir' @($proof, '--rendering-restir', '--output', (Join-Path $outputPath 'restir'))
        Invoke-QualificationCommand 'restir-flip' @((Join-Path $repository 'tools/aurelian-rendering-reference/evaluate_metrics.py'),
            (Join-Path $outputPath 'restir/metrics-manifest.json')) $metricsPython
        $restir = Get-Content -LiteralPath (Join-Path $outputPath 'restir/evidence.json') -Raw | ConvertFrom-Json
        $restirFlip = Get-Content -LiteralPath (Join-Path $outputPath 'restir/flip-evidence.json') -Raw | ConvertFrom-Json
        if (-not $restir.Accepted -or -not $restirFlip.Accepted) { throw 'ReSTIR native and FLIP qualification did not accept.' }
    }

    $tlas = $null
    if ($IncludeTlas) {
        Invoke-QualificationCommand 'tlas' @($proof, '--rendering-tlas', '--output', (Join-Path $outputPath 'tlas'))
        $tlas = Get-Content -LiteralPath (Join-Path $outputPath 'tlas/evidence.json') -Raw | ConvertFrom-Json
        if (-not $tlas.Accepted) { throw 'TLAS update qualification did not accept.' }
    }

    $rasterMotion = $null
    if ($IncludeRasterMotion) {
        Invoke-QualificationCommand 'raster-motion' @($proof, '--rendering-raster-motion', '--output', (Join-Path $outputPath 'raster-motion'))
        $rasterMotion = Get-Content -LiteralPath (Join-Path $outputPath 'raster-motion/evidence.json') -Raw | ConvertFrom-Json
        if (-not $rasterMotion.Accepted) { throw 'Raster motion qualification did not accept.' }
    }
    $area = $null
    $areaFlip = $null
    if ($IncludeArea) {
        Invoke-QualificationCommand 'area' @($proof, '--rendering-area', '--output', (Join-Path $outputPath 'area'))
        Invoke-QualificationCommand 'area-flip' @((Join-Path $repository 'tools/aurelian-rendering-reference/evaluate_metrics.py'),
            (Join-Path $outputPath 'area/metrics-manifest.json')) $metricsPython
        $area = Get-Content -LiteralPath (Join-Path $outputPath 'area/evidence.json') -Raw | ConvertFrom-Json
        $areaFlip = Get-Content -LiteralPath (Join-Path $outputPath 'area/flip-evidence.json') -Raw | ConvertFrom-Json
        if (-not $area.Accepted -or -not $areaFlip.Accepted) { throw 'Area native and FLIP qualification did not accept.' }
    }

    $gi = $null
    $giFlip = $null
    if ($IncludeGi) {
        Invoke-QualificationCommand 'gi' @($proof, '--rendering-gi', '--output', (Join-Path $outputPath 'gi'))
        Invoke-QualificationCommand 'gi-flip' @((Join-Path $repository 'tools/aurelian-rendering-reference/evaluate_metrics.py'),
            (Join-Path $outputPath 'gi/metrics-manifest.json')) $metricsPython
        $gi = Get-Content -LiteralPath (Join-Path $outputPath 'gi/evidence.json') -Raw | ConvertFrom-Json
        $giFlip = Get-Content -LiteralPath (Join-Path $outputPath 'gi/flip-evidence.json') -Raw | ConvertFrom-Json
        if (-not $gi.Accepted -or -not $giFlip.Accepted) { throw 'Dynamic diffuse native and FLIP qualification did not accept.' }
    }
    $hashes = @($sourcePaths | ForEach-Object {
        @{ Path = $_; Sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
    })
    for ($index = 0; $index -lt $hashes.Count; $index++) {
        if ($hashes[$index].Sha256 -ne $initialHashes[$index]) {
            throw "Source changed during qualification: $($sourcePaths[$index])"
        }
    }
    $reference = Get-Content -LiteralPath (Join-Path $outputPath 'capture/evidence.json') -Raw | ConvertFrom-Json
    if (-not $reference.Accepted) { throw 'Reference capture did not accept its workload.' }
    $affine = Get-Content -LiteralPath (Join-Path $outputPath 'affine/evidence.json') -Raw | ConvertFrom-Json
    if (-not $affine.Accepted) { throw 'Affine intersection capture did not accept its workload.' }
    $lighting = Get-Content -LiteralPath (Join-Path $outputPath 'lighting-temporal/evidence.json') -Raw | ConvertFrom-Json
    $flip = Get-Content -LiteralPath (Join-Path $outputPath 'lighting-temporal/flip-evidence.json') -Raw | ConvertFrom-Json
    if (-not $lighting.Accepted -or -not $flip.Accepted) { throw 'Lighting, temporal or FLIP qualification did not accept.' }
    @{ Accepted = $true; FullR3aComplete = $false; Sources = $hashes;
        ProofSha256 = (Get-FileHash -LiteralPath $proof -Algorithm SHA256).Hash;
        GraphicsSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $proof) 'Aurelian.Graphics.dll') -Algorithm SHA256).Hash;
        LightingFixture = $fixturePath; Reference = $reference; Affine = $affine;
        LightingTemporal = $lighting; Flip = $flip; R3 = $r3; R3Flip = $r3Flip; Sampling = $sampling; SamplingFlip = $samplingFlip; Restir = $restir; RestirFlip = $restirFlip; Tlas = $tlas; RasterMotion = $rasterMotion; Area = $area; AreaFlip = $areaFlip; Gi = $gi; GiFlip = $giFlip; FullR4Complete = $false; FullR3Complete = $false } | ConvertTo-Json -Depth 30 |
        Set-Content -LiteralPath $evidencePath
    Write-Output "AURELIAN_REFERENCE_QUALIFIED: $evidencePath"
}
finally {
    $env:VK_LAYER_VALIDATE_SYNC = $previousSyncValidation
    Pop-Location
}
