# Cloth research probes: OGC, AVBD and Better Bending

Reviewed and executed 2026-10-10. This pass adds reproducible verification probes
and fixes a flat-rest classification bug. It does not port a complete OGC contact
model, AVBD cloth solver, or physically calibrated shell material.

## Decisions supported by this pass

1. Keep small-step XPBD as the working cloth baseline. The exact-solution probe
   does not establish an advantage for replacing it with AVBD.
2. Make analytic energy/refinement and sharp-fold tests acceptance gates for the
   next bending model. The current four-vertex hinge shows substantial directional
   bias even when the solver is not involved.
3. Prototype boundary-corrected vertex quadratic bending for flat-rest patterns.
   Evaluate QB(PL) on intrinsic-Delaunay rest meshes and QB(int) for general
   patterns. BAC remains a higher-fidelity candidate with extra director DOFs,
   mass/integration requirements and a larger implementation boundary.
4. Pursue OGC as a complete contact experiment: face/edge/vertex offset blocks,
   conservative neighbor search, per-vertex bounds, safe initialization and bound
   refreshes. Merely adding a normal penalty or clipping a single predictor does
   not implement its penetration-free contract.

The papers support these candidate directions; the priority order is our
interpretation of the local evidence. No published performance number is adopted
as an Aurelian benchmark result.

## Better Bending: the current stencil fails energy consistency

Primary source: [Better Bending](https://zhenchen-jay.github.io/publication/betterbending/),
especially sections 5-6, analytic verification in section 8, sharp-bend testing
in section 9 and recommendations in section 12.

The probe uses actual `ClothDefinition3D.Compile` stencils and
`ClothState3D.Measure().BendEnergy`. It prescribes a unit-radius cylindrical
embedding of a flat patch of area pi. The comparison density is normalized to
`1/2 * curvature^2`, so the analytic energy is pi/2. Values are geometric
diagnostics, not joules or the paper's specific Young's modulus/thickness.
Prescribed positions isolate the stencil from timestep/iteration error.

| Grid family / orientation relative to cylinder curvature | Coarse ratio | Middle ratio | Fine ratio |
| --- | ---: | ---: | ---: |
| 9/17/33 rows, equal row/column counts, aligned | 2.5583 | 2.7945 | 2.9016 |
| Same grid rotated 45 degrees | 12.2159 | 12.5102 | 12.6432 |
| Four times as many column cells, aligned | 2.9016 | 2.9519 | 2.9761 |
| Four times as many column cells, rotated 45 degrees | 3.2596 | 3.2651 | 3.2673 |

The physical rest rectangle is pi by one: equal grid counts produce rectangular
cells with aspect ratio pi. The four-column variant changes that cell aspect to
pi/4. Rotation changes the patch's orientation relative to a constant-curvature
embedding, not just the coordinate frame of the complete deformed scene. The
analytic isotropic energy remains the same because area and curvature remain
constant. This is a local adaptation of the paper's verification methodology,
not a reproduction of its meshes or tables.

The aligned sequence approaches a factor of three; changing orientation/cell
aspect changes the bias. A universal stiffness rescale cannot repair all these
cases. This agrees with the paper's warning about two-triangle hinge models.
The next candidate must include boundary terms: a bare cotangent Laplacian is
not the complete QB(PL) model and can exert spurious boundary contraction.
Intrinsic triangulation and its operator should be prepared during authoring;
dynamic cloth positions remain runtime data.

The one-hinge fold probe rotates one opposite vertex about its shared edge. The
finite-difference geometric restoring moment is about 6.000 at 90 degrees but
0.104 at 179 degrees: approximately 1.73% of its 90-degree value. Its energy
approaches a finite maximum as the hinge folds back. This is an underresolved
sharp-fold weakness of the control, not a measurement of a real textile's torque.

The finest rotated 129x33 pattern initially stopped at `AUR-CLOTH-001` despite
being planar. The compiler had classified flatness using a cancellation-prone
weighted curvature sum whose magnitude scales with inverse edge length. It now
checks the opposing rest-face normals directly. A fine rotated flat-pattern test
and a fully folded-back overlap rejection test guard the repair. Compiled weights,
solver updates and snapshot layout were not changed. All twelve refinement
samples now compile and produce finite energy.

## AVBD: viable mechanism; no reason yet to replace XPBD

Primary source: [AVBD](https://graphics.cs.utah.edu/research/projects/avbd/),
equations 8-12 for hard constraints; sections 3.4-3.7 for finite stiffness,
Hessian stabilization, bounded error correction and warm starts.

The probe has two scalar masses with inertia `m/h^2 = 3600`, predicted displacement
0.01 m, a ground spring of stiffness 10000 and target hard coupling `x1 = x2`.
The exact minimizer is `x1 = x2 = 0.004186046511627907 m`. It compares:

- VBD coordinate descent with a finite 1e8 penalty approximating the hard coupling.
- AVBD primal/dual updates with initial penalty 3600 and beta 10.
- XPBD dual constraint updates with hard coupling and matching ground compliance.

The VBD finite penalty is explicitly a surrogate for the hard target. AVBD and
XPBD share the exact hard-constrained objective in this linear example. This
is not an identical-energy comparison of full cloth implementations, a finite
stiffness-ramping experiment, or a claim about real-time garment throughput.

| Method | Position error at 5 iterations | Position error at 20 iterations | Position error at 64 iterations |
| --- | ---: | ---: | ---: |
| VBD finite penalty | 5.809 mm | 5.794 mm | 5.750 mm |
| AVBD hard coupling | 0.1817 mm | 5.544e-9 m | Rounded to zero in double arithmetic |
| XPBD hard coupling | 0.03905 mm | 1.183e-11 m | 1.735e-18 m |

The stiff VBD control has almost zero coupling error but large stationarity
residual and position error. A contact/strain residual alone can therefore hide
an under-converged global solution. Report stationarity or reference-solution
error as well as constraint violation.

The real `.v.ts` shader executes all twelve method/budget cases on an RTX 3070.
DXC and SPIR-V validation pass; repeated Vulkan results are identical. Maximum
GPU position difference from the double reference is 1.117e-8 m and maximum
dual difference is 3.372e-6. The fixed 64-iteration schedule is expanded by
`static for`; numerical iterations do not introduce a state-machine stack.
No full-cloth speedup, warm-start replay, nonlinear 3x3 block solve, vertex-colored
garment solve or frictional contact is qualified.

An AVBD follow-up must retain the same cloth energy/contact model as the XPBD
control, compare error versus complete frame cost and stress mass/stiffness ratios,
driven attachments and persistent multiplier state. Vertex coloring could reduce
dispatch colors, but the scalar result does not measure that benefit.

## OGC: carry the bounds and preconditions with the force model

Primary source: [Offset Geometric Contact](https://ankachan.github.io/Projects/OGC/),
offset manifold construction, section 3.7/equations 21-27 and the solver algorithm.

The paper computes each vertex's motion budget from vertex/facet clearance,
incident-edge/edge clearance and incident-facet/vertex clearance, with a safety
factor strictly below one half. Bounds refer to an intersection-free anchor and
must cover all participating motion, including predictor initialization and solver
corrections. They can be refreshed during optimization. Broadphase omissions,
missing edge/edge candidates or unbounded moving colliders invalidate that argument.

The local analytic probe uses two congruent, separated triangles. In this special
configuration all three clearance minima equal the gap, and uniform normal motion
has an exact swept-ordering oracle. Nine gap/safety-factor cases check the bound:
gaps 0.1 mm, 10 mm and 100 mm; factors 0.1, 0.4 and 0.49. Proposed motions cross the
sheets, while clipping both sheets to their budgets retains a positive gap. This
is an analytic mechanism check only, not a generic distance-search implementation
or a reproduction of OGC's offset force, friction, self-contact or GPU timings.

Our current plane/sphere endpoint contact cannot express cloth self-contact and
already has a deliberate tunneling witness. A useful full OGC experiment needs
typed face/edge topology, moving-neighbor BVHs or another conservative broadphase,
normal-block classification, complete narrowphase minima and bounded iteration
updates. Authoring can precompute incidence, topology exclusions and schedules;
deformed distances, bounds and forces are runtime GPU work. Begin with initially
valid layers, edge/edge approaches and sparse fast motion, plus rejection specimens
for invalid initialization. Keep the existing discrete backend as the control.

## Reproduction and ownership

```powershell
dotnet build tools/Aurelian.GraphicsProof -c Release -m:1
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll --cloth-research
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet test Aurelian.slnx -m:1
```

Results and generated VD-MIR/HLSL/SPIR-V are in
`artifacts/local/cloth-research-probes/`; `evidence.json` explicitly distinguishes
accepted probes from unqualified full contact/solver/material capabilities.
Build/test logs are in `artifacts/local/cloth-research-checks/`.

Validation for this pass: the Release proof executable built and completed the
CPU and native Vulkan probes; all 1,170 tests in 30 Aurelian test projects passed,
including 16 cloth tests. The two new cloth tests cover fine rotated flat-rest
admission and rejection of a folded-back overlapping hinge. These results do not
qualify a complete OGC contact system or an AVBD garment backend.

The cached full papers in `artifacts/local/cloth-research/sources/` were read,
including visual inspection of the relevant formula pages. SHA-256:

- OGC: `6ABFD9E1CDA6D5413DE348D8455AB4779473D0714F527F54F472E40DE5598F98`
- AVBD: `7957D116B9130CFB0AA5A48AB7CD0D74A64AD79F75C99ACD291BDECE2BE3F2D6`
- Better Bending: `7B7CCBCFD29A69423AC7CBC062EAA9765EA0031A3301CA5E357904E3A7385CF1`

No tools/packages were installed and no external solver source was copied. The
reference repositories' root licenses were rechecked: [Gaia Apache-2.0](https://github.com/AnkaChan/Gaia/blob/main/LICENSE),
[AVBD demo MIT](https://github.com/savant117/avbd-demo3d/blob/main/LICENSE),
[Better Bending MIT](https://github.com/evouga/better-bending/blob/master/LICENSE).
Vendoring their dependency stacks would require its own review. BEPU remains the
rigid-body authority and the cloth core keeps its existing ownership boundaries.

## Implementation follow-up

The next pass implements QB(PL) vertex bending and bounded offset contacts on
CPU and Vulkan. This report's probes explicitly retain `QuadraticHinge` for
historical comparisons. See [the systems report](cloth3d-offset-systems.md) for
current admission limits, adaptations, native results and unqualified scope.
