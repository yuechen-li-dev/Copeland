# Cloth foundation and SIGGRAPH 2025/2026 research

Reviewed 2026-10-10. This is a bounded research review and working baseline, not
a reproduction of the papers' performance results. Both 2026 papers below have
SIGGRAPH 2026 publications; their earlier preprints are not separate 2025 results.

## What the audit found

`PhysicsClothSpecimen` in GraphicsProof is a useful BEPU particle/link control.
Its 17x17 sheet has sphere contacts at vertices and distance-limit springs. It
does not collide the rendered triangle interiors, implement a shell bending
energy, or provide reusable cloth state. Its existing qualification limits remain
in [the physics lab report](physics3d-qualification-lab.md).

Keep BEPU as the rigid-body authority. Cloth gets its own optional solver and
typed snapshots, rather than making every cloth vertex a rigid-body agent. One
cloth occurrence is an object agent; its numerical discretization is solver data.

## Research shortlist

The final column is our recommendation, not a claim made by the authors.

| Primary source | Contribution relevant here | Experiment / boundary |
| --- | --- | --- |
| [Offset Geometric Contact, SIGGRAPH 2025](https://ankachan.github.io/Projects/OGC/) | Face-normal offset contact for codimensional surfaces; conservative per-vertex displacement bounds avoid intersection without conventional CCD line searches. | Highest-priority contact experiment: compare offset contact with our discrete control on folds and stacked layers. Carry the bounds and conservative initialization, not just the contact energy. The paper requires an initially intersection-free state; it discusses contact-force discontinuities and overly conservative bounds for fast motion with sparse contacts. |
| [Augmented Vertex Block Descent, SIGGRAPH 2025](https://graphics.cs.utah.edu/research/projects/avbd/) | Augmented Lagrangian treatment improves hard-constraint and high-stiffness-ratio convergence in a parallel local solver. | Second solver candidate for stiff fabric and driven attachments. Keep material/contact energies fixed when comparing. Stability does not imply small constraint error at a finite iteration budget; the paper discusses momentum injected by under-converged hard constraints. The demo is not a drop-in garment system. |
| [MGPBD, SIGGRAPH 2025](https://chunleili.github.io/project-page-mgpbd/) | Global XPBD with aggregation multigrid/PCG and reusable prolongators targets low-frequency error at high resolution/stiffness. | Try only after residual/refinement experiments isolate global convergence as the bottleneck. Static topology can own reusable hierarchy data, with explicit invalidation. Its large tetrahedral examples do not establish our cloth performance. |
| [Domain-decomposed Projective Dynamics, SIGGRAPH 2025](https://sig25ddmpd.github.io/) | Partitioned local/global work exploits multicore CPUs rather than assuming GPU acceleration is always preferable. | Maintain a useful CPU backend and compare complete frame costs. A CPU garment solver can leave GPU capacity for rendering; published comparisons are not portable performance guarantees. |
| [Automated Task Scheduling, SIGGRAPH 2025](https://chengzhuuwu.github.io/publication/siggraph-2025-ats/) | Dependency graphs, communication cost and hybrid scheduling of iterative simulation tasks; evaluated on Apple M-series CPU/GPU hardware. | Fits our plant/controller scheduling model. First measure dispatch, submission/wait, readback and residency. Do not extrapolate unified-memory results to a discrete RTX GPU, or introduce an automatic scheduler before measuring a useful crossover. |
| [Better Bending, SIGGRAPH 2026](https://zhenchen-jay.github.io/publication/betterbending/) | Compares ten bending models using analytic refinement, mesh-dependence, sharp-bend and practical benchmarks; introduces improved variants including BAC. | Borrow the test discipline first: clamped strip, rotated/refined triangulations, tight folds, then contact. Our flat-rest quadratic hinge is a control, not BAC. The paper's purely isometric convergence analysis does not settle every stretch/bend coupling regime. |
| [Efficient B-Spline Finite Elements, SIGGRAPH 2026](https://simulation-intelligence.github.io/BS-Cloth/) | Smooth quadratic B-spline discretization, tailored integration/Hessian/linear-solve work and IPC contact improve accuracy and reduce locking. | Strong offline garment/authoring reference. The project comparison reports seconds per step, e.g. 1.43 s for upright hanging and 2.11 s for drape, at 0.01 s timesteps on an i7-12700F. Its speedup is against other FEM methods, not a demonstrated game-frame budget. |

The deliberately older baseline is [XPBD](https://matthias-research.github.io/pages/publications/XPBD.pdf)
with [small substeps](https://matthias-research.github.io/pages/publications/smallsteps.pdf).
Compliance enters as `alpha / h²`; multipliers accumulate across iterations and
reset per substep. Finite solver budgets still leave discretization/convergence
error. The flat-rest quadratic hinge follows the cotangent energy described in
the [PBD survey, section 5.2](https://matthias-research.github.io/pages/publications/EG2015PBD.pdf):
`E = 1/2 |sum(k_i x_i)|²`. We solve the vector residual directly, so the elastic
potential is quadratic rather than the square of an already-quadratic energy.

## Code and ownership boundaries

No external solver code, packages or tools were installed or vendored for this
pass. The implementation uses our existing Vulkan, VTS/DXC, scene and spatial
query seams. Reading a paper is not permission to copy an unlicensed repository.

| Candidate code | Observed boundary on review date |
| --- | --- |
| [OGC/VBD Gaia](https://github.com/AnkaChan/Gaia) | Apache-2.0 repository; importing it would also bring a substantial C++ dependency stack. |
| [AVBD 3D demo](https://github.com/savant117/avbd-demo3d/blob/main/LICENSE) | MIT, copyright Chris Giles; preserve notices if source is adopted later. |
| [Better Bending benchmarks](https://github.com/evouga/better-bending) | MIT repository; LibShell/Polyscope and other dependency licenses need separate inspection before reuse. |
| [BS-Cloth](https://github.com/Simulation-Intelligence/BS-Cloth) | Apache-2.0 repository; its solver dependencies and data have separate provenance. |
| [MGPBD](https://github.com/chunleili/mgpbd), [libAtsSim](https://github.com/ChengzhuUwU/libAtsSim) | No root license was visible in the inspected listings; MGPBD's root `LICENSE` request returned 404. Copying source remains unqualified until permission/license is established. |

The cloth core does not depend on Aetheris, BEPU, graphics, or native interop.
It depends on Spatial3D for shared triangle proximity. Vulkan is a separate
integration. NativeComposition projects typed cloth snapshots into scene meshes.
Existing Aetheris-derived spatial-query provenance stays with Spatial3D.

## What this pass implements

See [the API and qualification report](cloth3d-foundation.md). Authoring compiles
triangle topology, area-weighted masses, material-axis links, quadratic bending
hinges and disjoint write colors. CPU and Vulkan consume the same data and
schedule. Plane/sphere contact includes triangle interiors; snapshots carry
positions, velocities, pin targets, content identity and tick. Presentation is
separate and stepping is explicit.

The material controls are a spring-network surrogate, not measured warp/weft
constitutive parameters. Flat rest patterns are supported; shaped rest curvature
is rejected explicitly. Contact is discrete and one-way. Self-contact, CCD,
frictional garments, seams, arbitrary collider meshes, two-way BEPU coupling and
simulation-to-render mesh refinement remain later experiments.

## Next experiments, in order

1. Keep this control and add an OGC contact/displacement-bound experiment. Use
   initially valid sheets, layered folds, edge/face contacts and fast sparse
   encounters; preserve a failure specimen for every violated assumption.
2. Improve material/bending qualification using analytic clamped-strip and
   rotated-mesh refinement tests. Compare quadratic, dihedral and BAC-style
   energies before selecting a production fabric model.
3. Compare AVBD with XPBD on identical energies/contact and driven pins. Report
   error versus wall time, not iterations alone. Try multigrid only if spatial
   low-frequency residuals remain dominant.
4. Batch multiple sheets on GPU, reduce color-dispatch overhead, and project
   resident state directly into draw buffers. Measure alongside rendering before
   choosing a CPU/GPU crossover or adding asynchronous plant scheduling.

The current Vulkan path is a correctness control and experiment seam. Our small
sheet measurements favor CPU; GPU residency alone has not made this baseline
faster. That is a result to act on, not a reason to hide the CPU backend.
