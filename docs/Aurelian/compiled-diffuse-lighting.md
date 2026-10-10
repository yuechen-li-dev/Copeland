# Compiled static diffuse lighting

The first production slice promotes the continuous/adaptive experiments into
engine-owned assets, authoring and rendering. Beacon Run compiles its own arena
and loads the result through `NativeGameGraphics.LoadLighting`. The normal
`Solid3D.v.ts` shader evaluates uploaded data; replacing an asset or changing
light coefficients does not generate or compile a scene-specific shader.

This is a bounded static diffuse receiver capability. It does not replace the
directional shadow map, implement reflections, or enable indirect lighting on
every material.

## Author once, load normally

Describe a static scene using the ordinary scene API. Select a horizontal +Y
receiver rectangle in metres, a fixed sun direction, and explicit emission:

```csharp
ScenePlan scene = SceneCompiler.Compile(MyScene());
StaticDiffuseLightingRecipe recipe = SceneDiffuseLighting.Describe(
    scene,
    receiverId: "floor",
    receiver: new HorizontalDiffuseReceiver(-11, -11, 22, 22),
    sunDirection: Graphics3DSettings.Default.SunDirection,
    emission: new Dictionary<string, Vector3> { ["lamp"] = new(12, 7, 3) });

// Explicit authoring command, never called by runtime loading.
string assetPath = StaticDiffuseLightingCompiler.Build(
    recipe, outputDirectory, installedBlenderExecutable);

// Failure supplies LightingDiagnostic and uses fallback lighting.
bool ready = graphics.LoadLighting(assetPath, recipe.ContentKey);

// Independent coefficients require no rebake or upload.
graphics.Settings = graphics.Settings with { SunIntensity = 2 };
graphics.Renderer.StaticEmissionIntensity = .5f;

// Static geometry/material changes withdraw publication immediately.
graphics.InvalidateLighting("StaticWallMoved");
// Load a newly compiled asset using the new recipe.ContentKey when available.
```

The types come from `Aurelian.World.Scenes`, `Aurelian.NativeComposition`,
`Aurelian.Rendering.Contracts.Lighting`, `Aurelian.Rendering.Contracts.Models`,
and `Aurelian.Assets.Lighting`.

`SceneDiffuseLighting` reuses the existing scene geometry projection. It admits
static boxes and meshes with constant opaque albedo per triangle and aligns
authoring face winding with the scene's explicit normals. The receiver must cover
the rectangle with positive albedo. Per-face colors, including Beacon's
checkerboard, participate in the reference and content identity. Models, agents,
textures, transparency, interpolated albedo and skinning are rejected by this
adapter. Direct typed recipes are also supported without the native adapter.
Receiver width/depth are bounded from 1 mm to 1,000 m.

## Compilation and asset contract

The optional Blender adapter writes a native USD scene, acquires two lighting
bases using Cycles OptiX, and fits a shared quadratic receiver mesh. It stores
sun indirect radiance and static-emitter total radiance **per unit receiver
albedo**, in scene-linear Rec.709. Sun intensity/color and one coefficient for all
static emitters change independently. Receiver shading multiplies the response
by albedo before tone mapping. Direct sunlight uses the existing runtime shadow
map and a Lambertian direct term on this receiver.

Acquisition uses 8,192 samples and an eight-bounce cap, without denoising,
adaptive sampling or clamping. Training is 48 by 48; independent validation
and repeat/noise measurements are 64 by 64. This is finite, noisy integration,
not physically exact transport. Reference reuse checks scene, acquisition source,
Blender version and checksums of all three sample files.

Eight training-selected regions seed a conforming mesh. Paired interior-edge
bisection refines it using training residuals alone. Held-out samples may reject
the result; they cannot select topology or fit coefficients. Quality thresholds,
numeric budget and refinement cap are declared before compilation. The accepted
design must have full observed coefficient rank. Split-plane candidates use
normalized contributor bounds; these are fitting hints, not the Cycles geometry.

The compiler writes `lighting.usda` over `scene.usda`, reopens the native composed
stage, and verifies the exact float32/int32 payload roundtrip. It then packs
`lighting.alight`. The runtime file contains a checksum, scene identity,
decoder/basis/color identities, topology and coefficients, measured quality,
rank/storage limits and source/training provenance. Loading verifies these and
validates oriented manifold topology, coverage and finite bounded arrays.
Failures use `AUR-LIGHT-001` diagnostics. Checksums detect corruption; they are
not signatures or proof of trusted authorship.

The numerical compiler and Cycles acquisition now live in
`Aurelian.Assets/Lighting/Authoring`. Research tools import that owner instead of
keeping a second implementation. `DiffuseReceiverMesh` in Rendering.Contracts
owns shared CPU topology/evaluation. Research tools retain their experimental
profiles and comparisons.

Blender, Python/NumPy, Cycles and OpenUSD are authoring dependencies supplied by
the installed Blender distribution. They are invoked explicitly and are not
bundled or required by game loading. This slice uses our numerical implementation
and adds no Aetheris package dependency or license boundary change.

## GPU publication and fallback

`CompiledDiffuseLighting.v.ts` is a stable reusable decoder. Its RGBA32F ABI has
two header pixels, five pixels per element (barycentric rows and shared coefficient
indices), and two pixels per coefficient. Nearest sampling with clamp-to-edge
addressing preserves numeric data. The shared Vulkan uploader supports RGBA32F;
native format support is checked before allocation.

The current upload waits for its actual submission fence. The existing Dominatus
lighting HFSM qualifies and publishes the matching content/generation ticket.
`LightingStatus` and `LightingInspector` expose status and trace. The controller's
existing abstract cost admission is reused for this bounded upload; its unit work
request does not represent runtime ray tracing. This is synchronous replacement
on the host thread, not async streaming or qualified multi-GPU scheduling.

Rejection or explicit invalidation immediately withdraws publication. Old tickets
cannot republish an invalidated generation; a new target is required to resume.
Camera movement and either coefficient reuse the data. Changing sun direction
through `graphics.Settings` invalidates the asset. Direct renderer callers also
get fallback when direction/material no longer matches, but must invalidate their
controller when changing static inputs. The API cannot infer changes to arbitrary
raw geometry supplied by a caller.

The compiled branch applies to horizontal receiver pixels within the declared
rectangle/height with nonmetallic solid material. Other surfaces retain existing
shading. Nonmatching direction, basic lighting, unsupported format, missing/stale/
corrupt assets or failed publication use fallback lighting.

## Qualification and commands

```powershell
dotnet build Aurelian.slnx -c Release -m:1
dotnet run --project Games/Beacon3D/Aurelian.Beacon3D -c Release --no-build -- --build-lighting --output artifacts/local/beacon-compiled-lighting
dotnet run --project Games/Beacon3D/Aurelian.Beacon3D -c Release --no-build -- --lighting-proof --lighting artifacts/local/beacon-compiled-lighting/lighting.alight --output artifacts/local/beacon-lighting-game
# Interactive play uses the same asset loader and Solid3D program:
dotnet run --project Games/Beacon3D/Aurelian.Beacon3D -c Release --no-build -- --lighting artifacts/local/beacon-compiled-lighting/lighting.alight
```

Use `--blender <installed-executable>` to override Beacon's default authoring path.
The finite native proof captures compiled/fallback/coefficient/reload frames,
rejects stale/missing/corrupt assets, checks changed-direction fallback, records
Dominatus trace, and measures warmed GPU lighting-pass timestamps. Its 64 by 64
native decoder probe imports the actual engine decoder and uploads the actual
asset for four coefficient settings. RGBA8 readback has a declared quantization
allowance; it does not establish float32 GPU seam precision.

Beacon's fresh held-out RMSE is 0.001507 (limit 0.02); repeat-reference noise is
0.000164. This covers 3,963 conservatively visible sites. Contributor AABB
footprints touching the receiver are excluded from fit/quality measurement.
Complex concave meshes may have visible areas outside that qualified mask. The
acceptance case is the static box arena and flat mesh floor, not arbitrary meshes.

The mesh has 47 triangles and 106 shared coefficients. The fitting budget reports
3,676 bytes including 140 bytes of retained research routing allowance; packed
topology/coefficients alone use 3,536 bytes. The expanded padded GPU texture is
7,680 bytes. JSON provenance/file storage is larger and reported separately.
These quantities are not interchangeable.

The first native measurement on RTX 3070 at 960 by 600 gave a 0.95 ms median
lighting pass versus 0.19 ms for fallback. It evaluates additional lighting; this
is not a speedup claim over fallback or a runtime path tracer. Dense GPU/CPU
comparisons passed the 0.6/255 encoded allowance for 16,384 samples. Shared-node
CPU seam mismatch was 2.98e-8. Final timing/readback details are recorded in
`lighting-proof.json`.

Faster uploaded element routing, further receiver orientations/materials and
dynamic indirect correction are future extensions. Specular GI/reflections,
textured/skinned receivers, independent emitter groups and a general authoring
visibility mask remain separate work.

## Validation record, 2026-10-09

- `dotnet build Aurelian.slnx -c Release -m:1 -v:q`: passed, zero warnings/errors.
- Full serial `Aurelian.slnx` test run: 1,018 passed, zero failed, 28 projects.
- Numerical compiler regression: 12 passed via
  `python -B -m unittest discover -s tools/Aurelian.GraphicsProof -p 'test_*lighting_experts.py'`.
- Existing local/constrained/continuous/adaptive native research harness:
  all five proof markers passed with the shared engine-owned implementation.
- Fresh Beacon authoring and `--lighting-proof`: passed on RTX 3070; final
  median lighting pass 0.950 ms, p95 1.049 ms; fallback median 0.197 ms,
  p95 0.296 ms. GPU upload uses 7,680 bytes; runtime file 36,246 bytes.
- Full native Beacon `--proof --lighting ...`: passed, 3,681 frames, all three
  beacons collected and exit reached. This also exercises moving game cameras.

The full test command used the existing repository Naga executable for WGSL
qualification:

```powershell
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet test Aurelian.slnx -c Release --no-build -m:1 --logger 'trx;LogFilePrefix=compiled-lighting-final' --results-directory artifacts/local/compiled-lighting-tests-final -- RunConfiguration.MaxCpuCount=1
```

Native evidence: `artifacts/local/beacon-lighting-game/lighting-proof.json` and
`artifacts/local/beacon-lighting-full-game/proof.json`. Authoring evidence:
`artifacts/local/beacon-compiled-lighting/compile-evidence.json`, retained native
USD layers, reference manifests and sample files. Test evidence: 28 TRX files
under `artifacts/local/compiled-lighting-tests-final`. Artifacts remain local and
ignored; they are not redistributed binaries or cross-device qualification.
