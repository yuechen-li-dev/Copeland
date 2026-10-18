# Shared cloth ownership

The cloth definition, compiler, CPU solver, contact operators and GPU data layout
now live in the sibling Aetheris repository's `Aetheris.Cloth3D` project. This
project is the [Aetheris bridge](../../../docs/Aurelian/aetheris-boundary.md) for
cloth: it references `Aetheris.Cloth3D` and owns the scene agent
(`ClothAgentDefinition3D`, namespace `Aurelian.Cloth3D`). Aurelian owns the Vulkan
backend (`Aurelian.Cloth3D.Vulkan`).

Firmament's `schema Garment` produces `aetheris.garment.v1` artifacts. Load them
with `ClothGarmentArtifact3D.Load`, then `CreateSolver` to use a retained drape.
`ClothAgentDefinition3D.FromGarment` creates a scene agent and maps the artifact's
Z-up frame into Aurelian's Y-up world. This runtime path does not require the Firmament compiler. Static humanoid body
contact is currently qualified on CPU only; Vulkan rejects unsupported body
surface input explicitly. Weighted seam constraints are supported by both
solvers and exercised in the native `--cloth-systems` proof.

Authoring examples and limitations: Aetheris `docs/public/firmament/garments.md`.

An optional presentation surface compiles bounded Loop subdivision stencils once:

```csharp
var garment = ClothGarmentArtifact3D.Load("dress.garment.json");
var agent = ClothAgentDefinition3D.FromGarment(garment).WithPresentation(2);
```

The agent retains the simulation vertex count and snapshot. Scene projection
evaluates the finer mesh without stepping physics. Every material boundary,
including sewn panel boundaries, remains piecewise linear and unchanged.
Subdivision can reduce body clearance; qualify the visible surface separately.
This is an opt-in CPU presentation path, not GPU draping or contact correction.

`ClothDefinition3D.MaterialRegions` supplies disjoint named vertex regions with
stretch/bend compliance scales. Only fully contained structural stencils change;
mass, rest geometry, seam constraints and crossing stencils remain unchanged.
Regions are hashed into the definition, survive garment transport, and feed the
existing CPU/GPU constraint layout. Firmament still authors one shared `Fabric`;
regional tuning is currently a typed C# capability, not a new Firmament keyword.

Quad material surfaces opt in through `ClothDefinition3D.Surface`. Their Q1
warp/weft/shear operators and shared-edge rest-angle bending are independent of
the rendering diagonal. `FlatFabric` uses the flat chart as rest;
`FittedSurface` retains a stress-free curved rest surface. Compliance is inverse
surface stiffness in this mode, not the legacy edge-spring authoring control.
Do not transfer the old edge compliance defaults as calibrated fabric values.

The existing Vulkan solver supports these compiled stencils through the same
colored dispatch tape. Native `--cloth-systems` coverage includes deformed flat
and curved-rest quad patches, CPU agreement and exact same-device rewind.
Body contacts still use the CPU path. Quad offset self-contact is explicitly
rejected until qualified; flattening and animated garment fit are not implied.

Aetheris `GarmentSurfaceAuthoring.Skirt` produces a body-contour fitted skirt and
the same runtime artifact consumed above. See
`docs/public/firmament/garment-surface-experiment.md` in Aetheris for the actual
Anatolia specimen, reproduction commands and measured limits.
