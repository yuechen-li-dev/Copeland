param(
    [string]$Output = 'artifacts/local/rendering-reference/final',
    [string]$LightingFixture = 'artifacts/local/beacon-compiled-lighting'
)

$ErrorActionPreference = 'Stop'
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
        LightingTemporal = $lighting; Flip = $flip } | ConvertTo-Json -Depth 30 |
        Set-Content -LiteralPath $evidencePath
    Write-Output "AURELIAN_REFERENCE_QUALIFIED: $evidencePath"
}
finally {
    $env:VK_LAYER_VALIDATE_SYNC = $previousSyncValidation
    Pop-Location
}
