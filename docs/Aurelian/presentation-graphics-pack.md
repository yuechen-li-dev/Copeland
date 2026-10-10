# Directional coverage, height fog, translucency and subsurface diffusion

`NativeGameGraphics` loads these reusable Visual TypeScript passes for all shared
3D game starters. Scene geometry, imported static models and compute-skinned solid
geometry retain the existing lighting, AO, TAA, bloom and menu/HUD pipeline.

```csharp
var graphics = Graphics3DSettings.Default with
{
    ShadowDistance = 100,          // camera-forward scene metres
    ShadowCasterPadding = 30,     // admit casters toward the light outside the visible slice
    ShadowWorldBias = .015f,      // minimum quantization margin in metres
    Fog = new HeightFog3D
    {
        Density = .025f,          // extinction per metre at BaseHeight
        HeightFalloff = .3f,
        BaseHeight = 0,
        StartDistance = 2,
        MaximumDistance = 500,
        Color = new(.18f, .24f, .32f), // linear incident fog radiance
    },
};

var pane = new ModelMaterial("pane")
{
    BaseColor = new(.1f, .3f, .65f, .35f),
    AlphaBlend = true,
    DoubleSided = true,
    Metallic = 0,
    Roughness = .15f,
};

var wax = new ModelMaterial("wax")
{
    BaseColor = new(.58f, .25f, .12f, 1),
    Metallic = 0,
    Roughness = .3f,
    SubsurfaceStrength = .8f,
    SubsurfaceColor = new(1, .35f, .15f), // per-channel diffuse mixing weights
    SubsurfaceRadius = .025f,           // diffusion support radius in metres
};

var objectNode = Scene.Box("wax-object", Vector3.One, Vector4.One) with { Material = wax };
```

Fog defaults to zero density and subsurface strength defaults to zero. Both skip
their GPU passes when unused. Standard glTF `alphaMode: BLEND` now imports directly;
its factor, vertex alpha and base-texture alpha determine coverage. `MASK` retains
its existing cutoff behavior. `AlphaMask` and `AlphaBlend` are mutually exclusive.
All new material fields contribute to compiled scene identity. Asset preparation
prepares opaque and translucent resources before replacement is published.

## GPU implementation and supported bounds

Directional shadows use three 1024-square maps, split at 16%, 45% and 100% of the
admitted camera-forward distance. Fits include the complete view slices, preserve
camera height, quantize a conservative sphere and snap light-space translation to
texels. Neighboring slices overlap for the last 10%; the final slice fades out.
The compiler uses the unjittered camera, so TAA jitter does not move shadow fits.
Nine-tap PCF extrapolates the receiver plane to each sampled texel center, preventing
grazing-surface self-shadow bands without a large slope bias. A small world/texel
margin handles quantization. `ShadowRadius` and normalized `ShadowBias` remain the
low-level legacy forward renderer's controls. Point-light shadows, alpha-tested
casters, translucent shadow transmission and unbounded off-screen caster searches
remain unsupported; those materials do not cast an incorrect solid silhouette.

Height fog analytically integrates exponential height extinction over the
eye-to-surface segment, including a stable horizontal-ray limit. It runs on linear
HDR after scattering, before transparency. Translucent fragments integrate their
own distance using the same shader helper. The background uses `MaximumDistance`.
This analytic mode uses an authored incident color. The optional shadowed froxel
mode and glass transport are described in [the transmission pack](transmission-graphics-pack.md).

Translucency uses a separate, retained weighted blended OIT pass. Two RGBA32F
attachments accumulate weighted premultiplied radiance and logarithmic revealage
with the same additive blend equation. This requires no per-frame sorting and no
independent-blend feature. A fragment behind opaque depth is discarded, and no
translucent fragment writes the opaque G-buffer. The forward fragment evaluates
the same GGX/environment, box probe, directional cascades and both spot-shadow
slots as opaque surfaces; local lights retain the bounded 32-light contract.
Composite coverage marks transparent TAA history invalid, and a single-frame
unjittered reconstruction prevents stale surface correspondence from producing
color trails. Opaque pixels keep their ordinary temporal accumulation.

Weighted blending is an approximation for overlapping layers; it does not reproduce
sorted alpha colors exactly. Refractive materials use the transmission pack's
separate nearest-surface pass. Transparent coverage has no
temporal accumulation yet, so animated subpixel translucent edges can shimmer.
Translucent draws share the existing one-million-vertex frame bound.

Subsurface materials add a fifth G-buffer attachment containing their explicit
RGB diffusion weight and world-space support radius. The lighting resolve outputs
the diffuse response separately from the complete HDR result. Two normalized
nine-tap separable GPU filters reject sky, depth discontinuities, incompatible
profiles and surfaces with disagreeing normals. A final pass replaces only the
requested fraction of diffuse radiance; specular and emission remain sharp.
The support is capped at 32 pixels per axis. This is a bounded screen-space
diffusion approximation, not a Burley path-traced solution or measured tissue
profile. Backlit transmission and hidden/off-screen scattering are not modeled.
Scattering requires an opaque, lit, nonmetal material; invalid combinations fail
validation rather than silently dropping an effect.

The surface path requires five color attachments. The device limit and required
float formats are checked explicitly; OIT requires float32 attachment blending.
GPU resources persist between frames, algorithms execute on the supplied Vulkan
plant, and no production draw reads lighting data back to the CPU.

## Native qualification

```powershell
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet build Aurelian.slnx -m:1
dotnet test Aurelian.slnx -m:1
dotnet run --project tools/Aurelian.GraphicsProof -- --presentation-graphics
dotnet run --project tools/Aurelian.GraphicsProof -- --surface-graphics
dotnet run --project tools/Aurelian.GraphicsProof -- --temporal-graphics
```

The presentation proof writes `artifacts/local/presentation-graphics/evidence.json`
and separate captures for near/far coverage, elevated cameras, an isolated grazing
receiver, analytic fog, alpha zero/one, transparent ordering/depth/fog agreement,
history removal, diffuse-only scattering and the combined scene. It starts with
`Accepted:false` and publishes acceptance only after every invariant passes.
Timings cover GPU work only, excluding resource preparation and readback. The
RTX 3070 witness is device-specific, not a cross-device performance guarantee.

The October 9, 2026 witness passed: the far receiver darkened by 36.9 sRGB byte
levels with 100-metre coverage; an isolated grazing receiver had zero byte error
against shadows disabled; homogeneous fog had 0.0022 linear RGB error; transparency
ordering and preserved-batch history removal had zero byte error. Diffusion visibly
changed the wax sphere while preserving the surrounding scene and isolated
specular/emissive output. The full build had no warnings or errors, all 1,045
Aurelian tests across 28 projects passed, and all 1,469 Copeland TS tests passed.
The native surface/temporal regressions retained their AA improvements with zero
measured object, camera and compute-skinning trails. The hidden window smoke
switched and restored graphics through InputMan for four presented frames.

The implementation is original. Research references include Microsoft's
[cascaded shadow maps](https://learn.microsoft.com/en-us/windows/desktop/DxTechArts/cascaded-shadow-maps),
McGuire and Bavoil's [weighted blended OIT](https://jcgt.org/published/0002/02/09/),
and [screen-space subsurface scattering](https://www.advances.realtimerendering.com/s2018/Efficient%20screen%20space%20subsurface%20scattering%20Siggraph%202018.pdf).
These describe the techniques; no third-party shader source was copied. Existing
Aetheris temporal-policy attribution and licensing remain intact.
