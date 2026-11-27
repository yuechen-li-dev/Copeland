# Bistro after dark

A guided first-person walk through the authored Amazon Lumberyard Bistro
wine interior. Run `bistro.cmd` from the Copeland checkout. W and S walk the
route, A and D or the left and right arrows turn, and the up and down
arrows tilt. R returns to the entrance. Escape closes the demo.

The night rig aims four shadow-casting lights down from below the original
pendants. The bar globes and sconces add warm accents, and cool
window-side fill reveals the chair backs and table edges. The advanced
profile uses retained visibility, hardware ray shadows, dynamic diffuse
probes, contact occlusion, reconstructed full-resolution GGX reflections,
TAA, ocular glare and low-light eye adaptation. Use
`bistro.cmd --bistro-scaled` for the two-thirds internal resolution profile,
or `bistro.cmd --bistro-raster` for the raster control.

The goblets use screen-space refraction with an IOR of 1.55 over the
ray-lit opaque scene. Wine bottles add a green absorption tint. The
hex tile, varnished furniture and ceramic use distinct coat roughnesses.
The material overrides preserve the opaque textures and soften the normal
maps on the most reflective surfaces. Lower glare keeps the globes readable.

The guided route was checked against the original geometry at four heights
between each stop. The original floor is about 0.40 m in the converted
coordinate system and the camera walks at 1.98 m.

## Prepare and capture

The assets are prepared offline with Blender 5.2 and a Python environment
with Pillow and NumPy. The preparation script downloads ORCA Bistro v5.2,
verifies its archive hash and writes to `artifacts/local/bistro`. The
converter aligns the wine edition to the original interior's fixtures and
walking route. It simplifies the standalone glass meshes offline to fit the
forward draw budget. It keeps authored texture assignments, maps the
packed roughness and metalness channels and corrects the DirectX normal
convention. It exports material occlusion only when the packed red channel
contains data. The empty red channels in the v5.2 interiors previously
suppressed environment illumination across the room. It caps texture
edges at 1024 pixels by default and
groups nearby static objects into self-contained GLBs within the existing
importer limits. The export repair orthogonalizes invalid tangents, removes
an unused secondary UV channel and drops triangles below the ray shading
path's area tolerance.

```powershell
pwsh -File tools/aurelian-bistro/Prepare.ps1
dotnet build Aurelian.slnx -c Release -m:1
.\bistro.cmd
```

To render and capture the scripted route through the native Vulkan
swapchain, run the following from the repository root.

```powershell
$env:VK_LAYER_VALIDATE_SYNC = '1'
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll --bistro
```

## Vulkan qualification

The prepared wine interior contains 1,209,369 triangles and 941,502
expanded forward vertices after the glass LOD. The current profile uses
unculled retained visibility and 20 rays per probe per frame. The proof
captures the three playable stops and an additional comparison angle.

With the same lighting and 1,043,075-triangle original interior, removing
the empty occlusion maps raised median output brightness from 40 to 57
out of 255 in the entrance table crop. The pendant shells and chair backs
recovered detail without adding a brighter key light. The matched frames
are `art-ao-black` and `art-neutral-ao` under
`artifacts/local/bistro/art-direction/`.

`glass` and `glass-alpha-control` hold the camera, geometry and lights
constant while changing the clear glass from a forward blend to refraction.
The new pass ran alongside local ray shadows and GGX reconstruction on
the RTX 3070 with synchronization validation enabled. Setting
`AURELIAN_BISTRO_ALPHA_GLASS=1` enables the comparison material in the
proof executable.

The filled goblets exposed a screen-space miss when the wine surface lay
inside the glass exit estimate. On a miss, the shader uses the visible opaque
receiver behind the glass. The close-receiver Vulkan regression measured
0.00141 linear RGB error against the expected transmitted color. The final
wine captures and timings are in `bistro-night-native` and
`bistro-night-scaled` under `artifacts/local/bistro/art-direction/`.

## Renderer gaps exposed by Bistro

1. **Layered glass and liquids need transport beyond the closest surface.**
   Bistro now resolves screen-space refraction over the ray-lit opaque
   frame. The pass sees the nearest refractive surface and samples opaque
   scene color and depth. The wine, bottle walls and ice can overlap, and
   off-screen paths use the environment. Secondary rays also skip the
   glass itself. Those cases need a layered dielectric transport path.
2. **Finite-size fixture shadows need the gameplay shader bundle.** The
   four-sphere experiment requested importance sampling and the existing
   sampled-light denoiser. Native gameplay composition stopped at
   `AUR-LIGHT-SAMPLING-002` because it does not provide the sampled shader.
   The earlier proof-only sphere run left visible wall and ceiling noise.
   The current exact spots still draw sharp penumbra-free edges.
3. **Dense transparent meshes hit the expanded draw limit.** The full
   wine edition imports 1,263,762 transparent index entries and expands
   them into the forward path's one-million-vertex upload. It reached
   that limit before rendering. A retained indexed transparent path would
   preserve the original geometry without this per-frame expansion.
4. **Cutout ray hits need opacity testing.** Masks are correct in the
   raster silhouettes. Ray shadows, diffuse probes and reflections skip
   those cutout meshes to avoid treating their coverage as fully opaque.
5. **Scaled glass and reflections need motion correspondence.** With
   these passes active, scaled rendering disables raster jitter. The scaled
   Bistro capture shows stair steps around the trim and glass. Matching
   secondary visibility with temporal history would restore jittered sampling.

## Credits

Amazon Lumberyard Bistro, Open Research Content Archive, © 2017 Amazon.
The original scene is distributed under CC BY 4.0. The prepared copy
changes file format, material wiring, texture resolution and lighting.
[ORCA release and attribution](https://developer.nvidia.com/orca/amazon-lumberyard-bistro)
