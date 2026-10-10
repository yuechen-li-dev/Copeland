# 3D graphics starter: lighting, shadows and HDR

The shared native game host now renders 3D scenes into an `R16G16B16A16_SFLOAT`
linear color attachment. A separate GPU output pass applies exposure, a filmic
curve and output encoding before the existing menu/HUD compositor runs. UI colors
and text do not pass through scene exposure or tone mapping.

This is a bounded first graphics pass. It adds:

- Shared per-pixel GGX metallic/roughness lighting for solid geometry, GPU-skinned
  characters and imported static models. Imported models retain their existing
  base-color, normal, metallic/roughness, occlusion and emissive texture channels.
- Configurable directional sun color, intensity and direction.
- Procedural sky/ground ambient lighting with a roughness-dependent specular
  approximation. This is not textured, prefiltered image-based lighting or dynamic
  global illumination.
- A 1024-square directional shadow map, with nine depth-comparison taps and a
  configurable bias and coverage radius. The light-space placement snaps to texels
  as the camera moves. The renderer uses the actual GPU-skinned vertex buffer for
  animated shadow casters; it does not download or CPU-skin those vertices.
- HDR exposure and a fitted filmic tone curve. This curve is not a complete ACES
  color-management implementation. sRGB targets encode in hardware; UNORM targets
  use the sRGB transfer function in the output shader.
- Actual GPU timestamp measurements for the shadow, lighting and output passes,
  exposed through `Native3DFrameResult.GpuPassTimes`. Devices without queue timestamp
  support return an empty timing list. These exclude CPU upload, submission and capture.

## Authoring

`Graphics3DSettings` is a renderer-neutral immutable record in
`Aurelian.Rendering.Contracts.Models`. Sun direction points from a surface towards
the light. Material factors and light colors are linear; exposure is a multiplier.
Shadow bias is in normalized shadow depth units, not metres. Shadow coverage is
currently centered on the camera's X/Z position at world Y=0.

Games using `GameStarter` can configure the default in ordinary C#:

```csharp
using Aurelian.Games;
using Aurelian.Rendering.Contracts.Models;

var options = new StarterOptions(Title: "My Game")
{
    Graphics = Graphics3DSettings.Default with
    {
        Exposure = 0.85f,
        SunIntensity = 3.5f,
        SolidRoughness = 0.7f,
        Shadows = true,
    },
};

GameStarter.Run("my-game", GamePresets.FirstPersonShooter, args, options);
```

Custom hosts can set `NativeGameGraphics.Settings` between completed frames.
Invalid settings are rejected before rendering. `Graphics3DSettings.Basic`
disables solid PBR, shadows and tone mapping for comparison; imported materials
still use their material shader. Pass `--basic-graphics` to the starter or Beacon
Run to select this preset. Rendering settings do not change simulation state.

Low-level renderer construction requires both `Shadow3D.v.ts` and `ToneMap3D.v.ts`
to enable the modern path. Omitting both retains the basic direct-rendering path;
requesting unavailable shadows, exposure or tone mapping fails explicitly.

## Shader ownership

`Lighting3D.v.ts` contains shared lighting functions. `Solid3D.v.ts` and
`StaticModel3D.v.ts` compile together with that source through the existing
Copeland GPU binder and Aurelian HLSL/DXC/SPIR-V backend. `GameAssets` supplies this
source composition explicitly and caches compiled programs. There are no shader
strings embedded in game code or reflection-based runtime binding decisions.

The bounded graphics profile explicitly qualifies the new material layouts
(240-byte solid scene, 288-byte textured scene, 64-byte shadow camera and 16-byte
output parameters), scalar `Pow(f32, f32)`, and sampler parameters in shared shader
functions. Vulkan validates those compiled resource layouts before constructing
the passes. Modern rendering also checks device format features for the HDR and
shadow attachments.

Shadow visibility is stored in an `R32_SFLOAT` color attachment while a D32 buffer
selects the nearest caster. This reuses the existing color/depth render-pass and
sampled-image contracts. It is a rasterized shadow map, not a ray-query shadow
backend. Ordinary opaque static models cast shadows. Alpha-masked models receive
shadows but are excluded as casters until there is a material-aware cutout pass.

## Review and qualification

Run `graphics-gallery.cmd` from the repository root. Press **V** to compare the
basic and modern presets; **Esc** closes the gallery. Input goes through the shared
InputMan game controls. The launcher includes the locally available humanoid body
and locomotion bank when both exist, otherwise it shows the geometry/material scene.

Reproduce offscreen Vulkan captures and assertions:

```powershell
dotnet run --project tools/Aurelian.GraphicsProof -c Release
# Optional: append --body <gameplay-body.json> --locomotion <locomotion-bank.json>
```

Output is under `artifacts/local/graphics-starter`. The runner writes
`Accepted: false` before qualification and writes acceptance only after checking
repeatability, visible shadow changes, exposure, alternate sun direction and
sRGB/UNORM target agreement. A receiver-only floor must also stay free of visible
self-shadow striping. With a body supplied it separately checks the visible
GPU-skinned body and its cast shadow outside the body's silhouette. It retains
captures, pixel hashes, GPU pass timings and separate CPU submission/readback times.

The first pass is qualified on the local RTX 3070; it does not establish support on
every Vulkan device. Native starter/Beacon regressions exercise the shared host and
menu composition. The host's existing fixed-size window restriction remains.

The retained proof contains 61,232 triangles including the humanoid. The final
receiver-only check reports zero visibly self-shadowed pixels, the humanoid casts
a visible shadow outside its silhouette, repeated captures match exactly, and
sRGB/UNORM output differs by at most one byte. The gallery's hidden native smoke
test switches and restores the presets through InputMan. The existing static
asset proof also passes with the shared material shader.

Validation commands:

```powershell
# Use the repository's existing Naga installation for the browser shader regression.
$env:AURELIAN_NAGA = "$PWD/artifacts/aurelian-beacon3d/toolchain/bin/naga.exe"
dotnet build Aurelian.slnx -c Release -m:1
dotnet test Aurelian.slnx -c Release -m:1
dotnet test tests/Copeland/Copeland.TS.Tests -c Release -m:1
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --launch-smoke
```

[The temporal graphics pack](temporal-graphics-pack.md) now adds default TAA,
HDR bloom/emission and GPU mip generation with anisotropic filtering. Environment
maps, local lights, cascaded shadows, AO and fog remain follow-up capabilities.

A bounded [receiver-space MSDF shadow experiment](shadow-distance-experiment.md)
now compares baked distance fields with raster/PCF and the default depth-shadow path.
It qualifies static planar contour reconstruction, including explicit overlap and
thin-feature failure specimens. It does not change these production defaults.
