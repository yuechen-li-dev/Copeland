param(
    [string]$Output = 'artifacts/local/rendering-hybrid/final',
    [string]$Baseline = 'artifacts/local/rendering-policy/final'
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $repository
try {
    $outputPath = [IO.Path]::GetFullPath($Output)
    $baselinePath = [IO.Path]::GetFullPath($Baseline)
    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
    $evidencePath = Join-Path $outputPath 'qualification.json'
    @{ Accepted = $false } | ConvertTo-Json | Set-Content -LiteralPath $evidencePath

    function Invoke-QualificationCommand {
        param([string]$Name, [string[]]$Arguments)
        $logPath = Join-Path $outputPath ($Name + '.log')
        & dotnet @Arguments *> $logPath
        if ($LASTEXITCODE -ne 0) {
            Get-Content -LiteralPath $logPath -Tail 25
            throw "$Name failed with exit code $LASTEXITCODE. See $logPath"
        }
        if (Select-String -LiteralPath $logPath -Pattern 'Validation Error:|SYNC-HAZARD|VUID-' -Quiet) {
            throw "$Name emitted Vulkan validation diagnostics. See $logPath"
        }
        Write-Output "$Name passed"
    }

    Invoke-QualificationCommand -Name 'build' -Arguments @(
        'build', 'Aurelian.slnx', '-c', 'Release', '-m:1', '-nr:false', '-p:UseSharedCompilation=false'
    )
    $naga = Join-Path $repository 'artifacts/local/webgpu-tools/bin/naga.exe'
    if (Test-Path -LiteralPath $naga) {
        $env:AURELIAN_NAGA = $naga
    }
    Invoke-QualificationCommand -Name 'tests' -Arguments @(
        'test', 'Aurelian.slnx', '-c', 'Release', '--no-build', '-m:1', '-nr:false',
        '--logger', 'trx', '--results-directory', (Join-Path $outputPath 'test-results')
    )

    Invoke-QualificationCommand -Name 'compiler-tests' -Arguments @(
        'test', 'tests/Copeland/Copeland.TS.Tests/Copeland.TS.Tests.csproj', '-c', 'Release',
        '-m:1', '-nr:false', '-p:UseSharedCompilation=false', '--logger', 'trx',
        '--results-directory', (Join-Path $outputPath 'compiler-test-results')
    )

    $env:VK_LAYER_VALIDATE_SYNC = '1'
    $proof = Join-Path $repository 'tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll'
    $r0PreviousTiering = $env:DOTNET_TieredCompilation
    $env:DOTNET_TieredCompilation = '0'
    Invoke-QualificationCommand -Name 'rendering-hybrid' -Arguments @(
        $proof, '--rendering-hybrid', '--output', (Join-Path $outputPath 'rendering-hybrid')
    )
    Invoke-QualificationCommand -Name 'rendering-policy' -Arguments @(
        $proof, '--rendering-policy', '--output', (Join-Path $outputPath 'rendering-policy')
    )
    Invoke-QualificationCommand -Name 'publish-delivery' -Arguments @(
        'publish', 'tools/Aurelian.AssetDeliveryProof/Aurelian.AssetDeliveryProof.csproj', '-c', 'Release',
        '--no-build', '--no-restore', '-o', (Join-Path $outputPath 'delivery-runtime')
    )
    Invoke-QualificationCommand -Name 'managed-delivery' -Arguments @(
        (Join-Path $outputPath 'delivery-runtime/Aurelian.AssetDeliveryProof.dll'),
        (Join-Path $outputPath 'rendering-policy/delivery/models.toml'), 'forest.tree',
        (Join-Path $outputPath 'managed-delivery.json')
    )
    Invoke-QualificationCommand -Name 'publish-starter' -Arguments @(
        'publish', 'Games/Starter/Aurelian.Starter/Aurelian.Starter.csproj', '-c', 'Release',
        '--no-build', '--no-restore', '-o', (Join-Path $outputPath 'starter-runtime')
    )
    $runtimePath = Join-Path $outputPath 'starter-runtime'
    $compilerFiles = @(Get-ChildItem -LiteralPath $runtimePath -Recurse -File |
        Where-Object Name -Match 'meshopt|Aurelian.AssetCompiler')
    $compilerDependencies = @(Get-ChildItem -LiteralPath $runtimePath -Filter '*.deps.json' | Where-Object {
        Select-String -LiteralPath $_.FullName -Pattern 'meshoptimizer|Aurelian.AssetCompiler' -Quiet
    })
    if ($compilerFiles.Count -ne 0 -or $compilerDependencies.Count -ne 0) {
        throw 'Starter game publish includes the offline native LOD compiler.'
    }
    @{ Accepted = $true; CompilerFiles = @($compilerFiles | ForEach-Object FullName);
        CompilerDependencies = @($compilerDependencies | ForEach-Object FullName) } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $outputPath 'starter-runtime-boundary.json')
    Invoke-QualificationCommand -Name 'rendering-r2-lod' -Arguments @(
        $proof, '--rendering-lod', '--output', (Join-Path $outputPath 'rendering-r2-lod')
    )
    Invoke-QualificationCommand -Name 'hzb-regression' -Arguments @(
        $proof, '--rendering-hzb', '--output', (Join-Path $outputPath 'hzb-regression')
    )
    Invoke-QualificationCommand -Name 'visibility-regression' -Arguments @(
        $proof, '--rendering-visibility', '--output', (Join-Path $outputPath 'visibility-regression')
    )
    Invoke-QualificationCommand -Name 'bindless-regression' -Arguments @(
        $proof, '--rendering-bindless', '--output', (Join-Path $outputPath 'bindless-regression')
    )
    Invoke-QualificationCommand -Name 'rendering-r2-regression' -Arguments @(
        $proof, '--rendering-r2', '--output', (Join-Path $outputPath 'rendering-r2-regression')
    )
    $env:DOTNET_TieredCompilation = $r0PreviousTiering

    $modes = @('starter', 'temporal-graphics', 'surface-graphics', 'presentation-graphics',
        'transmission-graphics', 'water-graphics', 'particles-graphics', 'rain-night', 'diffuse-proof')
    foreach ($mode in $modes) {
        $arguments = @($proof)
        if ($mode -ne 'starter') {
            $arguments += '--' + $mode
        }
        $arguments += @('--output', (Join-Path $outputPath $mode))
        Invoke-QualificationCommand -Name $mode -Arguments $arguments
    }

    $comparisons = @()
    foreach ($mode in $modes) {
        $before = Join-Path $baselinePath $mode
        if (!(Test-Path -LiteralPath $before)) {
            throw "Missing pre-change capture directory: $before"
        }
        $images = @(Get-ChildItem -LiteralPath $before -Recurse -File -Filter '*.png')
        foreach ($image in $images) {
            $relative = [IO.Path]::GetRelativePath($before, $image.FullName)
            $after = Join-Path (Join-Path $outputPath $mode) $relative
            if (!(Test-Path -LiteralPath $after)) {
                throw "Missing corresponding capture: $after"
            }
            $beforeHash = (Get-FileHash -LiteralPath $image.FullName -Algorithm SHA256).Hash
            $afterHash = (Get-FileHash -LiteralPath $after -Algorithm SHA256).Hash
            $comparisons += [pscustomobject]@{
                Mode = $mode
                Image = $relative
                Before = $beforeHash
                After = $afterHash
                Identical = $beforeHash -eq $afterHash
            }
        }
    }
    $comparisons | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputPath 'capture-comparison.json')
    if ($comparisons.Count -eq 0 -or @($comparisons | Where-Object { !$_.Identical }).Count -ne 0) {
        throw 'Existing capture comparison failed; see capture-comparison.json.'
    }
    $sourcePaths = @(& git diff --name-only) + @(& git ls-files --others --exclude-standard)
    $manifest = @($sourcePaths | Sort-Object -Unique | ForEach-Object {
        if (Test-Path -LiteralPath $_ -PathType Leaf) {
            [pscustomobject]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
        }
    })
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputPath 'source-manifest.json')
    @{
        Accepted = $true
        Scope = 'Opaque canonical analytic primitives and triangles sharing typed visibility, depth and material resolve'
        FullR2Complete = $false
        Remaining = @('Compute material classification/resolve scheduling',
            'General field/CSG visibility', 'Analytic multi-hit MASK and shadow integration')
        GitRevision = (& git rev-parse HEAD)
        ExistingCaptures = $comparisons.Count
        AllExistingCapturesIdentical = $true
        ProofModes = $modes
        NativeHybridEvidence = 'rendering-hybrid/evidence.json'
        NativePolicyEvidence = 'rendering-policy/evidence.json'
        ManagedDeliveryEvidence = 'managed-delivery.json'
        StarterBoundaryEvidence = 'starter-runtime-boundary.json'
        NativeLodEvidence = 'rendering-r2-lod/evidence.json'
        SourceManifest = 'source-manifest.json'
        GraphicsProofSha256 = (Get-FileHash -LiteralPath $proof -Algorithm SHA256).Hash
        GraphicsRuntimeSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $proof) 'Aurelian.Graphics.dll') -Algorithm SHA256).Hash
        Validation = 'VK_LAYER_KHRONOS_validation with VK_LAYER_VALIDATE_SYNC=1; no error diagnostics'
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $evidencePath
    Write-Output "AURELIAN_RENDERING_HYBRID_QUALIFIED $evidencePath"
}
finally {
    Pop-Location
}
