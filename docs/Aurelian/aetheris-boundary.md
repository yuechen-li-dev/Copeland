# Aetheris boundary

Aetheris (the CAD/geometry kernel in the sibling `../Aetheris` repository) is a dependency of a few Aurelian features,
never of the engine core. Aetheris code enters Aurelian only through **bridge projects**:

| Bridge | Aetheris project | What it brings |
| --- | --- | --- |
| `src/Aurelian/Aurelian.Cloth3D` | `Aetheris.Cloth3D` | Cloth definitions, CPU solver, garments, and `ClothAgentDefinition3D` (the scene agent) |
| `src/Integrations/Aurelian.Humanoid` | `Aetheris.Humanoid` | Humanoid rigs, animation and locomotion |
| `src/Integrations/Aurelian.Lighting.Aetheris` | `Aetheris.Fields` | Analytic field lighting |
| `tools/Aurelian.AetherisBake` | `Aetheris.Kernel.Firmament` | Offline scene baking |

Projects built on a bridge are fine: `Aurelian.Cloth3D.Vulkan` (GPU cloth), `Aurelian.Games.Humanoid` (the FPS/third
person starter template with humanoid characters, `GameStarter` and `StarterGame`), the Starter, Beacon and
CharacterLab games, and the graphics proofs.

Everything else is Aetheris-free, including `Aurelian.Spatial3D`, `Aurelian.World`, `Aurelian.Graphics`,
`Aurelian.NativeComposition`, the game shell `Aurelian.Games`, and 2D games such as TinyFarm.
`AetherisBoundaryTests` (in `Aurelian.Games.Tests`) enforces both rules by walking the project graph.

To use Aetheris in a new feature, add a bridge project that references the Aetheris project and keep the engine-facing
types it exposes small. Do not add an Aetheris reference to an existing core project. Small, general algorithms are
better written in Aurelian; `TriangleSurface3D.ClosestPoint` replaced Spatial3D's only use of `Aetheris.Kernel.Core`.

Building needs the sibling checkout at `../Aetheris` (for example `C:\Users\<you>\source\repos\Aetheris` next to
`Copeland`). Projects that do not reach a bridge build without it.

Licensing: Aetheris is published under AGPL-3.0. Its author relicenses that code to GPL-3.0 for use in Copeland
projects, so a Copeland build that includes a bridge is GPL-3.0. The field-lighting adapter keeps its historical
provenance markers; see its `NOTICE.md`.
