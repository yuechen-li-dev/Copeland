# BEPU qualification and deformable research

This round keeps BEPU 2.4.0 as the optional CPU rigid-body backend and adds owned
CCD/contact settings, ball sockets, distance limits, hinges, angular motors and
pair exclusions. It qualifies useful configurations through the actual adapter,
then uses the same owned seam for one cloth research fixture. Engine defaults
remain unchanged. The fixture lives in `tools/Aurelian.GraphicsProof`, not in the
production character or clothing APIs.

## Reproduction and evidence

```powershell
dotnet build Aurelian.slnx -m:1
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet test Aurelian.slnx --no-build -m:1 -- RunConfiguration.MaxCpuCount=1
dotnet build tools/Aurelian.GraphicsProof -c Release -m:1
$previousTieredCompilation = $env:DOTNET_TieredCompilation
$env:DOTNET_TieredCompilation = "0"
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --physics-lab
$env:DOTNET_TieredCompilation = $previousTieredCompilation
```

The default destination is `artifacts/local/physics-lab`. `--case ccd`, `stacks`,
`rotation`, `platform`, `joints`, `lifecycle`, `sleep`, `replay` or `cloth` isolates
a fixture. Add `--headless` to run numerical qualification without rendering.
`--output <directory>` keeps runs separate. Run performance measurements without
other builds/tests consuming the CPU. Release configuration and a fixed JIT policy
matter; Debug and Release timing comparisons do not isolate backend performance.

`results.json` is updated after each profile and preserves measured failures.
`evidence.json` starts with `ExperimentCompleted: false`. Completion requires all
`RequiredProfile` controls to pass, with at least one positive result per case;
`AllProfilesMeetCriteria` remains false when a deliberately weaker profile fails.
No timings act as hardware-independent pass/fail thresholds. Timed `Step` includes
contact collection; metric sampling, projection, rendering and serialization are
outside that interval. The first 60 steps are excluded. Sleeping stays enabled;
stack/rotation timings must not be mistaken for sustained awake-body benchmarks.
The existing `--physics` proof separately measures 256/1,024/4,096 awake bodies.

## Findings

The local observations below use .NET 10.0.12, x64, 16 logical processors and
`DOTNET_TieredCompilation=0`. Detailed numbers and final timings live in the
generated JSON, so the experiment can be repeated instead of relying on prose.

Closeout: 69 profiles across nine cases; all 33 required controls pass. Twenty-five
diagnostic profiles fail their stated budgets and remain in the evidence. The full
Aurelian solution builds and passes 1,101 tests (30 in the physics backend suite),
with zero failures or skips. The existing native scene-agent proof also passes
with Vulkan validation on the RTX 3070. Its 4,096-awake-body benchmark measures
2.872 ms median on one worker and 1.487 ms median / 1.626 ms p95 on four workers,
including contact collection. Evidence lives under `artifacts/local/physics-lab`,
with the original rigid-body proof retained separately in `rigid-proof`.

| Probe | Baseline pressure | Qualified response |
| --- | --- | --- |
| 5 cm sphere, 1 cm wall, 10/100/500 m/s, 30/60/120 Hz | Passive 10 cm margin and soft swept contacts fail several penetration budgets | Four substeps, 1,000 Hz contacts and 1 microsecond sweep tolerances pass all nine wall profiles; a near miss preserves velocity |
| Direct BEPU CCD control | Same thin-wall configurations without the owned adapter | First-step x/vx agreement isolates profile/backend behavior from adapter translation |
| Sixteen 1 m boxes, 100:1 top load | Eight iterations and 30 Hz contacts collapse the heavy tower | Sixteen substeps, 32 iterations, 120 Hz contacts, 10 m/s recovery retain the tower with about 4 mm maximum floor penetration |
| Sixteen-link chain, 100:1 end load | Four substeps with 30 Hz joints still give about 12 cm anchor separation | Sixteen substeps, 32 iterations and 120 Hz joint springs meet the 5 cm budget |
| Four-metre blade, 50 rad/s | One substep with passive contacts penetrates about 31 cm | Four substeps, swept detection and 120 Hz contacts keep penetration below 1 cm in this fixture |
| Platform and pushing | Need actual dynamic coupling rather than agreement between separate query worlds | Kinematic platform carries a dynamic box; authored capsule pushes a crate about 1.8 m. Neither constitutes a gameplay character controller |
| Lifecycle/sleep/replay | Handle reuse, stale constraints, idle cost and repeatability | 12,800 body removals clean their joints; main pool reservation stabilizes; 64 boxes sleep and wake after impulse; 300-tick fresh-world command replay agrees at one and four workers |

These profiles change several coupled parameters. They establish usable response
and the limits of the original profile; they do not attribute the entire improvement
to substeps, spring frequency or CCD independently. The heavy-load profile is
deliberately expensive. It is a qualification fixture, not a universal preset.
Contact springs have finite compliance: increasing iterations cannot eliminate
the equilibrium error of an authored soft spring.

## Cloth specimen

The sheet is a 17 by 17 grid (289 sphere particles) with 1,566 center-distance
limits: horizontal, vertical, diagonal and two-hop links. Two kinematic corners
pin it; their authored motion sweeps 35 cm sideways during the run. It falls over
a sphere, using the backend's real particle contacts. The Vulkan mesh derives
positions and smooth normals from solver output and retains vertex correspondence
for temporal rendering. The support sphere reuses the graphics proof's geometry.

Basic 60 Hz profiles stretch beyond the 10% budget. The qualified experiment uses
120 Hz, eight substeps, 16 iterations, 100 Hz distance springs and 90 Hz contacts.
It reaches roughly 4.3% maximum stretch and 5 mm maximum node penetration. Local
cost is roughly 1.0–1.6 ms per step (about 2.0–3.2 ms per 60 Hz frame when taking
two steps), depending on worker count and self-collision. Use final JSON for exact
timings and awake counts. This is one small sheet, not a garment performance claim.
The cheaper 60 Hz/four-substep profile with the same stiff springs keeps stretch
near 6%, but has about 9 cm maximum particle penetration during motion. It remains
an explicitly failed diagnostic profile; reducing cost without satisfying contact
quality does not qualify a production cloth path.

The optional self-collision profile excludes nearby grid neighbours and enables
distant particle contacts. It reports actual self-contact observations. The
single drape does not certify fold stacks or clothing self-collision.
The drape observes only four self-contact pairs across the run, so this is weak
coverage of self-collision rather than a demanding folding test. Triangle
centroid penetration is sampled as a diagnostic, but the rendered faces and edges
do not collide. Zero sampled penetration is not proof of continuous surface collision.
Two-hop constraints approximate resistance to folding; they are not a material
model with measured bending stiffness. There is no area/volume preservation,
tearing, thickness authoring, anisotropy or skinned-character collision workflow.

Five native captures show the initial sheet, draping, displaced pins and later
response. Capture JSON records device, validation layers and pixel hashes;
`cloth-snapshot.json` retains body states, authored constraints and triangle indices.
These are offscreen Vulkan captures, not a qualified interactive desktop tool.

## What deserves implementation next

1. Keep BEPU for rigid props, constraints and dynamic coupling. The evidence gives
   no reason to replace a cheap CPU rigid-body path with a Vulkan solver now.
2. First improve engine ergonomics: compose joint assemblies and connect the
   existing character/query movement to one authoritative dynamic-world adapter,
   with explicit platform/pushing policy and recorded commands. Keep physical
   results as typed observations published to scene agents after the physics cadence.
3. If a game needs deformable clothing, build one bounded XPBD cloth experiment
   behind an owned contract. Compare it against this CPU specimen with identical
   attachments and measured stretch, bend and full-surface collision budgets. It
   is a better GPU research target than replacing rigid bodies, given this sheet's
   cost and the missing fabric semantics. CPU and Vulkan experiments must use
   explicit plant scheduling and state/readback ownership before coupling to gameplay.
4. Defer hair and volumetric soft bodies until a concrete character or game needs
   them. Hair requires guide constraints, head/scalp coupling and presentation;
   soft bodies require volume/material behavior. Neither is proved by a cloth drape.

Upstream references: BEPU's [CCD](https://github.com/bepu/bepuphysics2/blob/v2.4.0/Documentation/ContinuousCollisionDetection.md),
[cloth example](https://github.com/bepu/bepuphysics2/blob/v2.4.0/Demos/Demos/ClothDemo.cs),
[hinge](https://github.com/bepu/bepuphysics2/blob/v2.4.0/BepuPhysics/Constraints/Hinge.cs)
and [angular motor](https://github.com/bepu/bepuphysics2/blob/v2.4.0/BepuPhysics/Constraints/AngularAxisMotor.cs);
[XPBD paper](https://mmacklin.com/xpbd.pdf) for the deferred deformable comparison.
The fixture composes public APIs; no BEPU source fork or new tool download is required.
