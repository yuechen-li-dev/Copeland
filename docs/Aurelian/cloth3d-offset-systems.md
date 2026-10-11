# Cloth bending and offset-contact foundation

The research follow-up is implemented through the existing XPBD solver,
Dominatus agent snapshots and Visual TypeScript Vulkan path. BEPU remains the
rigid-body authority. CPU remains the default backend; Vulkan is explicit.
No external solver code or dependencies were imported.

## Authoring and admission

```csharp
using Aetheris.Cloth3D;
using System.Numerics;

var plan = (ClothDefinition3D.Grid(
    "panel", columns: 3, rows: 3,
    size: new Vector2(1, 1), origin: new Vector3(0, 2, 0)) with
{
    Pins = [0, 2],
    OffsetContact = true,
    // BoundaryCorrectedVertex is the default bending model.
}).Compile();

using var solver = new ClothSolver3D(plan);
solver.Step(new ClothStepOptions3D
{
    Substeps = 1,
    Iterations = 2,
    ContactIterations = 1,
}, new ClothContacts3D { PlaneNormal = Vector3.UnitY });
ClothSnapshot3D state = solver.Capture();
ClothMetrics3D metrics = ClothState3D.Measure(plan, state, new());
```

Use the same compiled plan and options with `VulkanClothSolver3D`. Existing
`ClothAgentDefinition3D`, presentation and capture/restore seams remain the
integration points. Contact mode belongs to the authored definition, not the
renderer. `ContactSafetyFactor` defaults to 0.4 and must be strictly between zero
and one half. `OffsetContactCompliance` controls the normal-response XPBD residual.

The contact foundation deliberately compiles exhaustive candidate sets. It is
limited to 128 vertices (`AUR-CLOTH-004`); larger panels need a conservative
broadphase before enabling this mode. Sparse bend rows have at most 32 entries
(`AUR-CLOTH-003`); contact one-rings/incident faces are also bounded by 32
(`AUR-CLOTH-005`). Vulkan still admits at most 4096 dispatches per tick
(`AUR-CLOTH-002`), independently of the vertex limit. Candidate coloring can
produce many dispatches even for small meshes. Limits fail explicitly instead
of truncating candidates or silently falling back.

Initial/restore states need positive clearance and no self-intersection.
Validation includes nonincident edge/facet piercing, not just isolated
vertex/face and edge/edge distances. `AUR-CLOTH-006` rejects invalid states before
stepping or restoring mutates physics. Colliders are fixed during a tick;
teleporting a sphere or plane into the cloth is rejected. This is not collider
trajectory CCD. The minimum admission clearance is 2 micrometres and movement
budgets retain a 1 micrometre numerical floor. Extremely compressed states may
hit the next tick's admission check; no recovery or global floating-point
collision certificate is claimed.

## Boundary-corrected vertex bending

The default uses the QB(PL) boundary-corrected Laplacian from
[Better Bending](https://zhenchen-jay.github.io/publication/betterbending/),
equations 39–40: `E = 1/2 |M^-1/2 (L + N) x|²`. `M` is barycentric geometric
area, independently of density; `N` restores the boundary normal derivative.
Each sparse vector row is projected through the existing XPBD constraint path.
Incidence, coefficients and conflict-free colors are compiled once.

The old `QuadraticHinge` remains an explicit control. Both models still require
locally flat rest geometry; shaped-rest shells, intrinsic remeshing, directors,
sharp-fold accuracy and physical textile calibration are not supplied here.
Changing models may require retuning `BendCompliance`; their residuals differ.

The native proof includes 24 cylinder energy cases over three refinements,
two crosswise densities and two orientations. At 33 rows:

| Crosswise density | Orientation | Vertex energy / analytic reference | Hinge control / reference |
| --- | --- | --- | --- |
| Equal vertex counts | 0 degrees | 0.97 | 2.90 |
| Equal vertex counts | 45 degrees | 1.09 | 12.64 |
| Four times the columns | 0 degrees | 0.992 | 2.98 |
| Four times the columns | 45 degrees | 0.996 | 3.27 |

All tested flat-rest energies are below `1e-4`. This materially improves the
measured mesh/orientation bias; it does not establish convergence for every
triangulation or fix the known sharp-fold weakness. `BendEnergy` remains a
geometric diagnostic, not joules.

## Offset contacts and bounded motion

The implementation is inspired by [OGC](https://ankachan.github.io/Projects/OGC/).
Face interior, edge wedge and full vertex one-ring blocks select active normal
responses. Nonshared edge pairs provide the edge-only contact manifold, with
endpoint normal-cone checks. Bounds use every nonincident vertex/face and every
nonshared edge/edge distance, scattered to all participating vertices.

Each refresh captures an immutable position anchor and a per-vertex displacement
budget. It runs before prediction and every solver iteration. Predictor motion,
pin interpolation, stretching, bending, plane/sphere projection and offset
responses all clip their absolute displacement against that anchor. Colors
include the normal-block read neighborhood as well as written vertices.
CPU and GPU therefore use the same geometric contract without iteration readbacks.

Compiled GPU tapes contain distance leaves, parallel minimum-reduction levels
and contact colors. Dynamic distances, bounds and response execute in Vulkan.
The host performs initial validation, with one snapshot readback at the start
of each offset-enabled step. This is a correctness foundation, not a GPU speedup
or asynchronous-execution claim.

Two deliberate adaptations distinguish this from a full paper reproduction:

- The normalized outer potential is `1/2 (r-d)²`. Its inner logarithmic branch
  uses coefficient `r²/4` at `d=r/2`, derived to match value, slope and curvature.
  The printed equation 19 coefficient does not satisfy those joins with equation
  18; the implementation and regression test use the continuous form.
- Near-parallel edge-pair response uses a hard angular filter, not a smooth IPC
  mollifier. Distances remain in the motion bounds, but response smoothness near
  that threshold is unqualified.

Infeasible pin targets are intentionally clipped. Inspect `MaximumPinError`
rather than assuming a commanded teleport occurred. `OffsetContact` metrics
report minimum feature clearance, active offset overlap and active pair count.
There is no friction, accelerated broadphase, arbitrary mesh collider,
two-way rigid response or complete AVBD cloth backend. The preceding AVBD probe
did not justify replacing XPBD.

## Native proof and validation

```powershell
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
$env:DOTNET_TieredCompilation = '0'
dotnet build tools/Aurelian.GraphicsProof -c Release -m:1
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll --cloth-systems
dotnet test tests/Copeland/Copeland.TS.Tests -m:1
dotnet test tests/Copeland/Copeland.Cli.Tests -m:1
dotnet test Aurelian.slnx -m:1
```

The proof compiles the real `.v.ts` through VD-MIR/HLSL/DXC, validates SPIR-V,
and executes on the RTX 3070 with Vulkan validation enabled. It checks fast
layer ordering, the former high-speed sphere-tunneling case, crossing edge
strips, clipped driven pins, deformed vertex-bend CPU/GPU agreement, and the
default 17x17 drape. Each paired case checks exact same-device GPU rewind.
The edge strips' active offset overlap falls from 3 mm to zero. Small cases
measured CPU/GPU position error below 0.004 mm. The 289-vertex drape measured
0.0234 mm maximum difference over 12 ticks, about 1.93% final stretch, zero
measured surface penetration and zero pin error. Each proof asserts a 1 mm
maximum agreement budget. These tests do not certify arbitrary garment contacts
or long-term cross-backend trajectories.

All 1,182 Aurelian tests across 30 projects passed, including 25 cloth tests.
Copeland compiler tests passed 1,492 checks and CLI tests passed 80 checks.
Logs are in `artifacts/local/cloth-research-checks/systems-*.log`; accepted native
evidence and individual cases are in `artifacts/local/cloth-systems/`.
This follow-up produces numerical/native-compute evidence, not a new rendered
garment showcase or performance benchmark. The original rendered drape remains
historical evidence in the foundation report.

Snapshot content identity includes bending model and contact mode. The source
and state transport is ABI v2; compile the matching shader. Old snapshots and
old SPIR-V are not interchangeable with the new plan. Garment panel/seam
authoring is the next milestone, before stress testing large garments.
