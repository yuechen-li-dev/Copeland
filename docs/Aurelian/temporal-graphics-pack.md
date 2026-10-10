# Temporal graphics pack

The shared `NativeGameGraphics` host defaults to utility-selected TAA and HDR
bloom. Game starters receive these passes automatically. The menu/HUD compositor
runs after scene tone mapping, so text and UI never enter temporal history or bloom.

```csharp
Graphics = Graphics3DSettings.Default with
{
    AntiAliasing = AntiAliasing3D.Temporal,
    TemporalHistoryWeight = .875f,
    BloomIntensity = .08f,
    BloomThreshold = 1,
    BloomKnee = .5f,
};

var lamp = Scene.Box("lamp", new(.05f, 2.5f, .05f), Vector4.One) with
{
    Material = new("lamp")
    {
        BaseColor = new(.01f, .01f, .01f, 1),
        Metallic = 0,
        Emissive = new(9, 3.5f, .7f),
    },
};
```

`SceneBox.Material` and `SceneMesh.Material` use the same material draw path as
imported models. Their factors and texture contents participate in scene identity.
Primitive UVs are planar and measured in local scene metres. Primitive normal maps
are rejected at scene compilation; use an explicit `Scene.Model` UV/tangent asset
for those. Emission is finite linear HDR radiance, bounded to 60,000 per channel
for the FP16 lighting target. Unlit surfaces also support emission. GLB import
supports `KHR_materials_emissive_strength` without clamping its radiance to [0,1].
Emission and bloom make a surface visible and produce a halo; lighting other
surfaces still belongs to the engine's lighting/transport system.

## GPU path

1. The scene pass writes linear HDR color and a second RGBA32F attachment with
   previous projected UV, previous depth, and current depth. Both outputs share
   rasterization, depth testing, back-face handling and alpha-mask discard.
2. Eight Halton projection samples jitter rendering without changing the logical
   gameplay camera. Previous positions are a separate vertex stream, retaining
   the existing solid/model vertex storage ABI. GPU skinning retains previous
   positions through a GPU buffer copy; presentation does not download vertices.
3. The resolve uses two retained RGBA32F color/depth images. It qualifies each
   historical bilinear tap separately, clips history to current neighborhood color
   bounds, and selects reject/stable/clamped/coverage behavior through Visual
   TypeScript `when utility`. Projection jitter is removed from motion vectors
   before sampling the history grid.
4. Stationary silhouettes may accumulate fractional coverage. Moving silhouettes
   reject stale coverage and reconstruct from the current frame. Depth-gradient
   tolerance is bounded so a foreground/background discontinuity cannot authorize
   arbitrary old surfaces. This favors clean disocclusion over retaining history
   at moving edges; it is not a universal solution to every temporal artifact.
5. A five-level FP16 bloom pyramid applies soft-threshold filtering, tent
   downsampling and upsampling. The tone-map pass combines bloom in linear HDR
   before exposure, filmic mapping and output encoding. No postprocess readbacks,
   per-frame pipeline compilation or per-frame texture allocations are required.

Model textures generate their full mip chain through Vulkan linear blits at upload.
sRGB channels use sRGB images, so filtering respects their transfer function.
Linear samplers use trilinear mip filtering and up to 8x anisotropy, capped by the
device limit. Device creation explicitly enables supported anisotropy. Point
sampler choices remain point filtered. Unsupported blit capabilities produce a
named device/format diagnostic. Normal vectors are normalized during shading;
alpha-coverage-preserving and normal-variance mip generation remain follow-ups.

`Graphics3DSettings.Basic` disables TAA, bloom, shadows and tone mapping. Set
`AntiAliasing3D.None` for numerical reference captures. Low-level custom renderer
construction supplies `TemporalResolve3D.v.ts` and `Bloom3D.v.ts` alongside its
scene, shadow and output programs; requesting a missing pass fails explicitly.

History resets when settings, model material/occurrence correspondence, raw vertex
count, GPU source ownership, scene revision, or published lighting changes.
`Renderer.ResetTemporalHistory()` handles camera cuts, teleports and raw topology
replacement. Custom raw triangle streams must preserve vertex correspondence or
change `temporalRevision`; equal vertex counts alone cannot prove correspondence.
The shared scene projector supplies revision and occurrence keys automatically.

## Qualification

Run the native image-quality proof:

```powershell
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --temporal-graphics
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --output artifacts/local/graphics-pack-gallery --body ../Aetheris/artifacts/local/humanoid-locomotion/antonia.gameplay-body.json --locomotion artifacts/local/playable-humanoid-locomotion/mixamo.locomotion.json
dotnet run --project Games/Beacon3D/Aurelian.Beacon3D -c Release -- --proof --output artifacts/local/graphics-pack-beacon
```

The October 9, 2026 native RTX 3070 run measured:

- At 192x192, static linear RGB MAE against a 4x supersampled reference decreased
  from 0.00293143 without AA to 0.00108277 with TAA: a 63.1% reduction.
- Moving CPU geometry, camera translation and compute-skinned geometry produced zero excess-history
  trail pixels at the proof's threshold. The control uses the same projection
  jitter and current-frame reconstruction with history weight zero. Ordinary
  current-frame coverage outside the supersampled silhouette is reported
  separately, including the adjacent pixel; it is not counted as proof of ghosting.
- Camera-cut output matched a fresh history-free control byte for byte.
- An extremely minified sRGB checker resolved to the expected linear half-grey
  (sRGB byte 188), with zero maximum byte error in the sampled interior.
- The 1280x800 gallery included the humanoid and authored HDR light strips. TAA
  and bloom took approximately 0.23 and 0.25 ms in a captured warmed frame.
  These are GPU pass timestamps, not total frame time or a cross-device guarantee.
- Beacon's 3,681-frame route collected all three beacons and won. Its byte-exact
  depth/menu/reference oracles explicitly disable temporal processing, then the
  route restores the new default graphics.

The shaders run through the canonical Visual TypeScript binder, VD-MIR, HLSL,
DXC and SPIR-V validation. The HLSL emitter now protects generated resource/return
names from local-variable collisions and emits explicit Vulkan locations; source
declaration order no longer changes the vertex attribute mapping.

The witnesses do not establish universal ghost freedom, animated alpha-mask
coverage quality, dynamic-shadow history behavior, or multi-device performance.
AO, prefiltered environment lighting, transparency and subsurface scattering are
separate subsequent capabilities.

The [surface graphics pack](surface-graphics-pack.md) now provides AO and
prefiltered environment lighting, and corrects perspective silhouette detection.
Its native coverage witness and moving-edge regressions qualify the updated
resolve separately from the historical measurements above.

## Aetheris provenance

`TemporalPolicy.v.ts` adapts Aetheris's
`fixtures/three-telos/temporal-policy.v.ts`. The upstream qualification explicitly
did not accept its static AA quality. This port changes the accumulation weights
and supplies native motion/coverage validation; it does not inherit an upstream
quality claim.

The policy retains its AGPL license. The runtime publishes the original source
hash, notice, AGPL text and temporal shader sources with game output. The temporal
resolve artifact incorporates that policy; the repository's root license remains
unchanged. See `src/Aurelian/Aurelian.Shaders/Assets/Licenses` and the existing
[Aetheris boundary notes](aetheris-field-lighting.md).
