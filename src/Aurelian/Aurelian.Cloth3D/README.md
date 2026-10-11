# Shared cloth ownership

The cloth definition, compiler, CPU solver, contact operators and GPU data layout
now live in the sibling Aetheris repository's `Aetheris.Cloth3D` project. This
project remains as a dependency compatibility wrapper. Import
`Aetheris.Cloth3D`; Aurelian owns the Vulkan backend and game agent integration.

Firmament's `schema Garment` produces `aetheris.garment.v1` artifacts. Load them
with `ClothGarmentArtifact3D.Load`, then `CreateSolver` to use a retained drape.
`ClothAgentDefinition3D.FromGarment` creates a scene agent and maps the artifact's
Z-up frame into Aurelian's Y-up world. This runtime path does not require the Firmament compiler. Static humanoid body
contact is currently qualified on CPU only; Vulkan rejects unsupported body
surface input explicitly. Weighted seam constraints are supported by both
solvers and exercised in the native `--cloth-systems` proof.

Authoring examples and limitations: Aetheris `docs/public/firmament/garments.md`.
