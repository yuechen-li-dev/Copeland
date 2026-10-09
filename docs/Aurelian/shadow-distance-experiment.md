# Receiver-space MSDF shadow experiment

**Research success, with a narrow scope:** a baked multi-channel distance field
reconstructs static shadow outlines substantially better than a coarse raster
visibility mask. It is not a replacement for depth visibility in arbitrary 3D
scenes. Production shadow settings and shaders retain their existing behavior.

## What was reused

- `Machina.VectorAssets.VectorIconMsdfCompiler` and its existing MSDF-Sharp backend
  generate the field from exact projected vector contours. A matching single-channel
  SDF uses the same backend, projection, bounds and four-texel encoded distance range.
- `ShadowDistanceField.v.ts`, owned by Aurelian.Shaders and embedded with the game
  assets, uses the median RGB channel and the same derivative-width coverage rule
  as `ProfileMsdf.v.ts`. This is a pixel-stage library, not a standalone shader.
- The existing `VulkanSolid3DRenderer`, material uploads, static-model bindings,
  PBR lighting, HDR output and GPU timestamps execute the experiment. Field data
  occupies the linear UNORM occlusion texture slot in an explicitly experimental
  material shader. Only direct-light visibility uses the field; ambient lighting
  does not receive it as AO. No parallel renderer or patched generated HLSL exists.

Field generation is an offline CPU bake using the existing generator. Every
per-pixel sample, median, derivative, coverage and lighting operation runs on the
GPU. Dynamic GPU field generation was not implemented or measured.

## Controlled specimen and reference

Seven disjoint convex silhouettes contain diagonal edges, sharp corners, and four
bars with widths 3.6, 1.8, 0.9 and 0.45 source units. One unit is 0.1 world metres.
The contours describe opaque planar casters. The real depth-shadow comparison
raises them four metres and offsets them toward the directional sun, so their
projection onto Y=0 matches the contours while the casters remain outside the
top-down camera view. This is controlled source geometry, not a general-purpose
mesh silhouette extractor.

The geometric reference renders the exact projected triangles using the same
material and lighting at 2048 square and box-downsamples to 512 square. It shades
direct light as fully occluded inside those triangles and leaves ambient light
alone. It is a 4x-per-axis supersampled reference, not an exact analytic integral.

The equal-resolution raster control samples silhouette membership at each 64x64
texel centre and averages nine nearest-filtered taps. The 64x64 SDF/MSDF fields
come directly from the vector source, use linear sampling, and reconstruct about
one screen pixel of antialiasing. The production comparison uses the existing
1024x1024, nine-tap depth-shadow path with its 24-metre coverage radius and bias.
Its light-space texel density differs from a receiver-local field; it is a practical
baseline, not an equal-footprint or equal-memory comparison.

## Results on the local RTX 3070

Retained native captures and machine-readable evidence are under
`artifacts/aurelian-shadow-distance`. The accepted main run gives:

| Method | Mean normalized shadow-contrast error | Silhouette mismatch pixels |
| --- | ---: | ---: |
| 64 raster / PCF | 0.055681 | 5,247 |
| 64 SDF | 0.007718 | 1,663 |
| 64 MSDF | 0.006763 | 1,497 |
| 128 MSDF | 0.004425 | 1,089 |
| Default 1024 depth / PCF | 0.043464 | 3,827 |

The 64 MSDF reduces the contrast error about **88%** against the equal-resolution
raster control. Most of the improvement comes from storing distance instead of
binary membership. The RGB encoding contributes additional corner fidelity over
SDF; it does not solve thin-feature sampling. See the native images rather than
interpreting an image-wide average as universal quality.

The metric averages absolute displayed RGB error against the supersampled reference,
normalized by the corresponding unshadowed-versus-fully-shadowed receiver contrast.
Only receiver pixels with sufficient contrast participate. It is a display-space
shadow-contrast metric, not an estimate of physically linear visibility. Silhouette
mismatches threshold normalized darkness at 0.5. Thin-bar retention samples forty
points along each bar's centreline; it does not establish continuity everywhere.

The 64 field retains the two widest bars fully on their sampled centre lines.
The 0.9-unit bar breaks into fragments (about 31% mean centre coverage); the
0.45-unit bar mostly disappears (about 7%). At 128, the 0.9-unit bar reaches full
centre coverage, while the finest bar still fragments (about 41%). A 32 field loses
more detail. This resolution sweep is a deliberate boundary witness.

Repeated MSDF rendering produces identical pixel hashes. Two camera offsets below
one screen pixel retain the improvement, and a perspective view is checked against
its own reference and raster control. These are three sampled positions, not a
qualification of continuous motion, temporal stability or every view angle.

GPU timings are medians of eight captured frames after four warmup frames. Retained
pass timings are diagnostic: clocks, capture traffic and different projections make
these tiny scenes unsuitable for claiming a speed advantage. Bake times include
both SDF and MSDF generation, and include cold managed/JIT costs. The runtime upload
uses RGBA8: a 64 field is 16 KiB; 128 is 64 KiB. The proof renderer still allocates
and clears its usual depth-shadow resources even for field modes, so these are
field-byte counts, not measured total VRAM savings.

## Failure specimen and applicability

`invalid-overlap-reference.png` and `invalid-overlap-msdf64.png` retain an overlapping
contour input with visible false holes. Source occluder silhouettes need a proper
union before generation. The existing vector generator's contour normalization is
not that union operation. The acceptance fixture is explicitly disjoint; overlap
handling is not quietly qualified by the main result.

Likely applications are cached static receiver shadows, authored shadow decals,
and architectural detail on a known receiver surface. They require retaining an
accurate vector outline or obtaining one from a higher-quality bake. A distance
transform of an already coarse shadow mask cannot recover discarded geometry.

A light-space depth map answers whether a particular 3D receiver lies behind an
occluder. A two-dimensional contour field alone cannot answer that for layered
occluders or changing receiver depths. Moving/skinned casters, changing sun
direction, nonplanar receivers, silhouette extraction and union, alpha cutouts,
physical penumbra widths and arbitrary 3D visibility remain outside this milestone.
General dynamic shadows should retain a depth/ray-query visibility solution and
use distance reconstruction only where its data is valid.

## Prior art

[Chris Green's Valve paper](https://cdn.fastly.steamstatic.com/apps/valve/2007/SIGGRAPH2007_AlphaTestedMagnification.pdf)
describes low-resolution distance-field magnification from higher-quality source
shapes. [Viktor Chlumsky's msdfgen](https://github.com/Chlumsky/msdfgen) specifically
preserves sharp corners using multiple channels. This experiment applies those
reconstruction ideas to a receiver-space shadow footprint.

[Epic's stationary-light documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/stationary-light-mobility-in-unreal-engine)
already describes baked distance-field shadow maps that stay crisp at low
resolutions. Its [dynamic mesh-distance-field shadows](https://dev.epicgames.com/documentation/en-us/unreal-engine/distance-field-soft-shadows-in-unreal-engine)
instead trace through volumetric object SDFs. Those are different visibility data
and algorithms; this experiment implements neither Unreal system and claims no
novelty for distance-field shadows.

## Reproduce

```powershell
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --shadow-experiment
# Optional: --output <directory>

$env:AURELIAN_NAGA = "$PWD/artifacts/aurelian-beacon3d/toolchain/bin/naga.exe"
dotnet build Aurelian.slnx -c Release -m:1
dotnet test Aurelian.slnx -c Release -m:1 --no-build
```

The runner writes `Accepted: false` first. It accepts only after validating all
four shader variants through Visual TypeScript, DXC and SPIR-V; checking repeated
native pixels; measuring at least 30% better contrast error than the raster control
at the main view, both camera offsets and the oblique view; improving main-view
silhouette mismatch count; and preserving the widest thin bar. It leaves captures,
generated HLSL and diagnostic evidence behind when a gate fails.

Validation completed for this milestone: Release build of `Aurelian.slnx` (zero
errors, fourteen existing third-party font warnings), all **970 Aurelian tests**
(zero failures or skips), the four focused shader variant tests, the native shadow
experiment, and the existing native graphics starter proof on the RTX 3070.
