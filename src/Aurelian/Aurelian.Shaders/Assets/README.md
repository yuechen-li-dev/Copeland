# Built-in shader sources

These real `.v.ts` files are grouped by rendering purpose. They are distributed
by **Aurelian.Shaders.Builtins**, not by the compiler assembly. The explicit
catalog is `../../Aurelian.Shaders.Builtins/Library/BuiltinShaderLibrary.cs`.

| Folder | Sources | Purpose |
| --- | ---: | --- |
| Ui2D | 3 | Analytic shapes, text, profile rendering |
| Effects2D | 3 | Fluid, fog, shockwave presentation |
| Effects3D | 2 | GPU particle generation and weighted billboard rendering |
| Surfaces | 8 | Geometry, material resolve, subsurface diffusion |
| Lighting | 5 | Shared BRDF, environment compilation, light tiles, AO |
| Shadows | 2 | Directional shadows and distance-field helpers |
| IndirectLighting | 9 | Compiled diffuse lighting, probes and refit queries |
| Atmosphere | 6 | Height fog and volumetric light transport |
| Transparency | 4 | Transparency and screen-space refraction |
| PostProcessing | 5 | Bloom, temporal policy/resolve, tone mapping |
| Deformation | 2 | Cloth and humanoid compute |
| Queries | 1 | Vulkan spatial ray queries |

Folder paths are authoring locations. Compiler module IDs deliberately remain
flat: `SurfaceResolve3D.v.ts` imports `./Lighting3D`, even though their repository
files live in different folders. Use `BuiltinShaderLibrary.LoadSources(name)`
for that virtual module namespace. Ordinary file readers must provide the same
mapping; compiling a physical folder path is not equivalent.

Register each new source in the catalog with its family and Graphics, Compute
or Module kind. `BuiltinShaderLibraryTests` checks the packaged inventory,
import closures, copied bytes and actual DXC/SPIR-V compilation of every entry.
Proof-specific experimental shaders remain under their tools' own `Assets`.

The adapted temporal policy retains its AGPL notice in `Licenses`. The bundled
library is an explicit AGPL dependency; the compiler can be used independently.
See [the library and VFX roadmap](../../../../docs/Aurelian/shader-library-and-vfx-roadmap.md).
