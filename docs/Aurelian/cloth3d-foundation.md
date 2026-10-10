# Reusable cloth foundation

The starter is CPU small-step XPBD. Vulkan is an explicit optional backend with
the same data/schedule, intended for correctness comparison and GPU experiments.
BEPU continues to own rigid bodies. See [the research review](cloth3d-research-2025-2026.md)
for the 2025/2026 shortlist and the next contact/material/solver experiments.

## Author a cloth object

```csharp
var cloth = (ClothDefinition3D.Grid(
    "cape", columns: 17, rows: 17,
    size: new Vector2(3, 3), origin: new Vector3(-1.5f, 2.6f, -1.5f)) with
{
    Pins = [0, 16],
    Material = new ClothMaterial3D
    {
        ArealDensity = .2f,
        Thickness = .004f,
        WarpCompliance = .000001f,
        WeftCompliance = .000001f,
        DiagonalCompliance = .000004f,
        BendCompliance = 1000f,
    },
}).Compile();

var definition = new ClothAgentDefinition3D(cloth);
using var scene = SceneCompiler.Compile(Scene.World("room",
[
    Scene.Agent("cape", definition),
])).Mount();
var agent = scene.Agent<ClothSnapshot3D>("cape");

// Resolve world-space targets from a skeleton or PhysicsAssembly port first.
// Each cloth occurrence has independent state. Presentation never steps it.
definition.Advance(agent, new ClothStepOptions3D(), new ClothContacts3D
{
    PlaneNormal = Vector3.UnitY,
    SphereCenter = new Vector3(0, 1, 0),
    SphereRadius = .7f,
});
SceneFrame frame = scene.Project();
```

Core namespaces are `Aurelian.Cloth3D` and `Aurelian.NativeComposition`.
`ClothDefinition3D` also accepts indexed triangles with explicit material
coordinates. The compiler rejects repeated pins, nonfinite data, degenerate
rest/material triangles, duplicate faces, nonmanifold edges, unused vertices
and unsupported non-flat rest hinges. Placement is rigid; author metre dimensions
in the pattern instead of scaling the scene instance.

The material is an anisotropic link network. UV-aligned edges get warp/weft
controls; other edges get diagonal compliance. This is not a calibrated continuum
strain model. Mass is rest triangle area times areal density, distributed to the
three vertices. Thickness supplies a half-thickness contact offset.

Bending uses a four-vertex cotangent quadratic hinge with a flat rest shape.
The parameter controls its vector residual potential, independently of the link
compliances. `BendEnergy` in diagnostics is the **unscaled geometric residual
energy**, not joules or a certificate of correct shell behavior. Tight folds,
triangulation dependence and mesh refinement still require research qualification.

## Explicit solver ownership

For sustained simulation, retain `ClothSolver3D` rather than allocating one per
frame. `SetPin(vertex, worldTarget)` requires an authored pin. `Step(options,
contacts)` advances one explicit tick. Moving pins interpolate from their current
position to the commanded target over that tick's substeps. Callers must supply
the cadence; there is no renderer-driven stepping or hidden variable-delta loop.

The agent convenience `Advance` restores/steps/publishes a typed snapshot and
therefore has allocation overhead. For a retained CPU or GPU solver, restore the
spawned agent state once, step it in the application/controller cadence, then
publish `agent.State = solver.Capture()` when inspection/presentation needs it.
Unmount/dispose the retained solver through ordinary application ownership.

`Capture` and `Restore` carry positions, velocities, pin targets, tick and a
content identity covering topology/material/pins. Foreign, truncated or nonfinite
snapshots fail before mutation. Substep multipliers are reset rather than warm
started, so snapshots need no hidden solver cache. CPU replay is deterministic;
the native proof checks exact GPU replay on the same device/backend. No
cross-device bitwise or long-term CPU/GPU trajectory identity is promised.

`ClothPresentation3D.Mesh` generates smooth normals and an indexed, double-sided
scene mesh. Its diagnostic plaid uses material coordinates. This first bridge
rebuilds presentation meshes; it does not yet consume a resident GPU draw buffer
or preserve deforming vertex correspondence for live temporal reprojection.

## Vulkan option

Reference `Aurelian.Cloth3D.Vulkan`; compile the supplied `Assets/Cloth3D.v.ts`
with `ClothCompute3D.Compile`, then construct `VulkanClothSolver3D(plant, cloth,
spirv)`. It implements the same `IClothSolver3D` API.

Rest stencils and solver storage remain allocated across steps. Conflict-free
colors allow parallel writes without float atomics. Compute barriers order colors,
and aligned dynamic descriptor slices provide immutable per-dispatch headers.
The Vulkan integration has a narrow friend boundary to Graphics' existing native
buffer handles; the core never imports Silk or Vulkan.

`Step` currently records a graphics-queue command buffer and waits for completion.
`Capture` is explicit mapped-state readback; the proof measures it separately.
This is not yet asynchronous compute, GPU-only presentation, device-local-only
storage, automatic backend selection, or a multi-GPU schedule. The bounded runner
rejects more than 4096 dispatches per tick as `AUR-CLOTH-002` before mutation;
reduce the authored solve budget for this baseline.

Visual TS compute currently uses an explicit `Vec3` value record and scalar
storage ABI. The graphics-only `float3` spelling and storage-buffer helper ABI
are not used here. All GPU code lives in the real `.v.ts` asset.

## Contact contract

The initial reusable baseline accepts one unit-normal plane and one sphere per
step, optionally neither. Callers may derive these poses from BEPU or assembly
ports after the rigid solver steps. The coupling is one-way: cloth does not push
those rigid bodies back.

Plane halfspace tests cover vertices; all points of a linear triangle then satisfy
that plane. Sphere contact also projects the closest point on each triangle with
barycentric mass weighting. This fixes triangle-interior contact misses from the
old particle fixture. Iterative contact remains discrete and finite-budget:
conflicting pins/obstacles, extreme motion and later corrections may leave error.
`Measure` evaluates actual closest-triangle sphere penetration, not centroids.

There is no self-contact, swept collision, friction model, arbitrary collider mesh,
seam/garment importer, tearing, hair, two-way rigid coupling, or shaped-rest shell
model yet. A test deliberately demonstrates high-speed tunneling through a sphere
with clear endpoints; endpoint clearance is not a collision-free trajectory claim.

## Run the real proof

From the repository root in PowerShell:

```powershell
dotnet build tools/Aurelian.GraphicsProof -c Release -m:1
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
$env:DOTNET_TieredCompilation = '0'
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll --cloth-foundation
```

`artifacts/local/cloth-foundation/evidence.json` starts with `Accepted:false` and
is accepted only after all assertions pass. Per-grid JSON, a triangle-interior
miss witness, SPIR-V, native initial/draped PNGs and explicit final state are saved.
The proof runs 180 ticks each at 9x9, 17x17 and 33x33 with identical material,
timestep, substeps and iteration settings. Pins start moving after tick 60.
CPU/GPU comparison at tick 12 precedes long-term folding divergence. Both the
early and final maximum position differences are retained in each grid report.

Acceptance budgets are peak stretch <15%, peak measured surface penetration <6 mm,
pin error <0.01 mm, early CPU/GPU position error <1 mm, finite state, exact same-device
rewind and visibly different native pixels. The 33x33 result is a convergence
stress control, not a production garment quality certificate.

The initial stiff, two-iteration profile failed the 33x33 stretch gate at 20.26%;
its measurements remain in `rejected-stiff-baseline`. Bending compliance and
iteration count both changed during tuning, so that result is **not** an isolated
bending-method comparison. `transfer-header-baseline` preserves the slower GPU
header-update implementation's measurements with the tuned physical parameters.

## Final qualification, 2026-10-10

Full `Aurelian.slnx` build: zero warnings/errors in the final incremental build.
All 1,133 tests across 30 projects passed, including 13 cloth checks. The Release
GraphicsProof build also completed with zero warnings/errors. The real native
proof passed on an NVIDIA GeForce RTX 3070 with the Khronos validation layer
enabled. Native PNGs were inspected; the tuned cloth visibly folds around the
sphere and its moving supports follow the pin state.

Identical controls for all three resolutions: 60 Hz, 8 substeps, 4 constraint
iterations, 2 contact iterations, default material. Values below are from the
final native run, excluding the first 20 timing samples.

| Vertices | Peak stretch GPU / CPU | Peak GPU surface penetration | CPU step p95 | Vulkan record/submit/wait p95 | Snapshot readback p95 | CPU/GPU position difference: tick 12 / tick 180 |
| --- | --- | --- | --- | --- | --- | --- |
| 81 | 1.137% / 1.137% | 0.00012 mm | 0.59 ms | 7.60 ms | 0.035 ms | 0.0166 mm / 48.3 mm |
| 289 | 2.923% / 2.923% | 0.00018 mm | 1.88 ms | 10.64 ms | 0.108 ms | 0.0174 mm / 105.7 mm |
| 1,089 | 13.927% / 13.926% | 0.00018 mm | 6.04 ms | 15.30 ms | 0.373 ms | 0.0179 mm / 497.3 mm |

Moving pins reached their targets with zero measured final substep error. The
triangle-interior witness starts with all vertices outside the sphere while the
surface penetrates by 402 mm; the GPU surface projection removes that measured
penetration. Exact same-device GPU rewind passed. Early CPU/GPU position agreement
does **not** extend to matching long-term folds. Floating arithmetic and discrete
contact sensitivity are plausible contributors, but this pass does not prove the
cause or qualify cross-backend trajectory replay. Both trajectories satisfy the
reported stretch/contact gates; those gates are not a complete material oracle.

The large-sheet result is close to the stretch budget and needs stronger
convergence/material work before garment use. GPU execution remains slower on
these sizes, even after header batching and reduced readback. Its many color
dispatches, synchronization and host-visible storage are optimization candidates,
not individually proven causes of the measured cost. No automatic crossover,
GPU-only timing, render-overlap performance or speedup claim follows from this
run. CPU remains the default; the Vulkan path remains an explicit experiment.

Artifacts: `artifacts/local/cloth-foundation/evidence.json`, `grid-*.json`,
`triangle-interior.json`, `final-state.json`, `initial.png`, `draped.png` and
`cloth.spv`. Build/test logs are `artifacts/local/cloth-full-build.log`,
`cloth-full-tests.log`, `cloth-release-build.log` and `cloth-native.log`.
