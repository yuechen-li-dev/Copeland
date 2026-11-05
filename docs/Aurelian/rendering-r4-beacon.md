# R4: whole-Beacon diffuse GI

The complete authored Beacon arena now has a resident rendering projection and
an explicit GI profile. This includes the checkerboard floor, perimeter walls,
pillars and caps, emissive lamp, animated collectibles, gate, first-person
weapon, creatures and bolts. Both legacy vertices and resident objects come
from the same game-owned authoring function. Gameplay, collision and object
identity remain game-owned.

## Running the game

```powershell
dotnet run --project Games/Beacon3D/Aurelian.Beacon3D -c Release -- --gi --quality high
```

`--gi` selects full 128-direction runtime probe updates for this arena. Hardware
ray queries are requested automatically. `--regional-gi` selects the compiled
baseline plus Dominatus regional policy for comparison. Neither flag changes
ordinary Beacon's existing look. The GI profile uses Lambert, a shared physical
HDR environment, the existing sun, and the same lamp radiance `(12, 7, 3)` used
by Beacon's static diffuse compiler. It does not place a point light against
the lamp's underside as a surrogate.

The regional mode acquires a full stationary GPU field at startup and uses the
existing signed compiled-residual cache. This synchronous startup capture is
an experiment convenience, not an offline asset-compiler performance claim.
The qualification also serializes, reloads and checks the whole-arena field.
Full runtime mode needs no startup bake or field upload.

Both modes use the ordinary `GameApp` / `NativeGameGraphics` path. The host
retains the source environment, GI settings, compiled probe field and ray/
visibility flags when it replaces the renderer on resize. The policy observes
the new renderer owner and invalidates old measurements. Native playtests
exercise spawning, firing, resizing, pausing/resuming and restarting, and assert
actual GI mode, probe count and source-environment presence afterward.

Optional deforming humanoids remain outside this rigid shared-TLAS profile;
they require skinning admission in the shared ray scene. They are rejected
explicitly rather than rendered with stale triangle rays.

## Scene projection and grid admission

`BeaconResidentScene3D` reuses immutable box/diamond meshes and cached mesh
color groups. Changes in transform retain predecessor identity. Spawning,
collecting and other topology/material changes publish a new static revision.
The view weapon uses an orientation-preserving symmetric-box basis, and local
triangle winding agrees with the game-authored outward normals. The legacy
vertex adapter retains its prior positions, normals and colors.

The selected grid is 12 x 3 x 12 with 432 probes, 128 directions and eight by
eight octahedral convolution tiles. The typed contract now admits 2..16 probes
per axis with an explicit **512 total-probe limit**. The GPU indexing already
uses declared dimensions; its fixed storage and dispatch capacity stay 512.
This permits a wide, shallow arena without wasting most probes vertically.
Boundary positions, corners and rejection above 512 probes are tested, and the
noncubic grid runs through the native path.

The compiled numeric payload is 1,327,104 bytes (about 2,742 bytes per square
metre of the 22 x 22 m arena). That excludes JSON overhead, two runtime history
copies, frame-sized query scratch, receiver output and the shared TLAS.

## Qualification and selection

```powershell
./tools/aurelian-rendering-beacon/Qualify.ps1 `
    -Output artifacts/local/rendering-beacon/final
```

The qualifier repeats all earlier rendering/reference/FLIP lanes, builds and
tests the solution and compiler, then runs the whole-Beacon proof and both
native game modes. Source and binary hashes identify the accepted run. Compare
its capture tree against `artifacts/local/rendering-regional/final-coverage`
to verify previous image output remains unchanged.

Three views cover the initial first-person view, an arena overview, and a
second ground-level view. Each compares GI off, compiled-only, fixed residual
32, full runtime 128, and regional control against an independent 2,048-sample,
two-bounce GPU reference. Surface HDR error uses opaque depth coverage; display
FLIP keeps the authored black background outside that coverage. Acceptance
requires HDR and FLIP improvement over GI off in every view, plus compiled/
regional agreement with the stationary full-runtime basis.

The gameplay replay includes camera motion, four shots, moving objects and a
global sun change. Independent path references check selected frames; every
frame also compares with full directional runtime. Stale compiled lighting is
the negative control. Unknown radiance influence dirties every region; the open
arena supplies no fictional room-local hints. Delayed measurements from older
epochs are rejected. Regional updates remain capped at 13,824 probe-direction
updates/frame, versus 55,296 for full runtime. These count cache directions,
not all secondary or receiver-connectivity ray queries.

Full-frame measurements use 720p and 1080p, static and advancing gameplay,
and an explicit queue drain at the end of each throughput window. Reported CPU
cost includes publication, policy, renderer preparation and submission; slot
wait is separate. Wall throughput also includes simulation. Presentation/HUD
cost belongs to the native game playtest and is not included in the offscreen
renderer timing table.

The Beacon profile chooses full runtime 128 because lower directional budgets
measured almost the same frame cost while retaining older directions. Receiver
connectivity dominates. Regional selection remains useful in the earlier
localized-change qualification; it is not the recommended default for this
continuously animated, open arena. No unconditional engine-wide GI default or
full R4 completion is claimed.

## Experiments retained

The initial 8 x 4 x 8 trial improved two views but slightly worsened the initial
first-person view. A denser horizontal grid exposed a much larger overview
error: the provisional point light 0.3 m below the lamp generated an intense
near-field secondary hit. The final profile uses the existing authored
emissive panel instead. These comparative runs remain under
`artifacts/local/rendering-beacon/preflight-*`; an isolated mechanism acceptance
is not a combined image-quality qualification.

## Accepted result

`artifacts/local/rendering-beacon/final/qualification.json` accepted all native,
FLIP and app lanes with clean Vulkan synchronization validation. The final
build had zero warnings/errors. All **3,405 tests passed** (1,666 solution and
1,739 compiler), with no failures or skips. All **804 previous captures are
byte-identical**. Final verification matched 317 source hashes and six binaries;
the corresponding shared native-app binaries also match the proof copies.
Only TRX files named in the final invocation logs contribute to those totals;
files retained from interrupted invocations are excluded explicitly.

| Authored view | GI-off HDR RMSE | GI HDR RMSE | GI-off FLIP | GI FLIP |
| --- | ---: | ---: | ---: | ---: |
| 0 | 0.030055 | 0.021152 | 0.127849 | 0.082930 |
| 1 | 0.048757 | 0.026965 | 0.091290 | 0.056253 |
| 2 | 0.031827 | 0.020630 | 0.162998 | 0.095072 |

HDR improves by 29.6%, 44.7% and 35.2%; FLIP improves by 35.1%, 38.4% and 41.7%.
Stationary compiled and regional images match full runtime with exactly zero
HDR RMSE in all three views. Mean motion error relative to full runtime is
0.023119 for stale compiled lighting, 0.000819 for fixed-32 residual updates,
and 0.001297 for regional updates. Mean selected-frame motion FLIP falls from
0.178506 with stale compiled lighting to 0.089095 with full runtime; fixed-32
and regional are approximately 0.089100 and 0.089095 respectively.

| Resolution and replay | Mode | Median GPU ms | Median CPU ms | Wall renderer frames/s |
| --- | --- | ---: | ---: | ---: |
| 1280 x 720, moving | off | 1.106 | 1.199 | 399.6 |
| 1280 x 720, moving | fixed-32 | 7.341 | 1.373 | 116.9 |
| 1280 x 720, moving | full-128 | 7.452 | 1.409 | 115.6 |
| 1280 x 720, moving | regional | 7.773 | 1.371 | 109.8 |
| 1920 x 1080, moving | off | 1.807 | 0.945 | 353.8 |
| 1920 x 1080, moving | fixed-32 | 15.067 | 1.465 | 60.5 |
| 1920 x 1080, moving | full-128 | 15.108 | 1.462 | 60.4 |
| 1920 x 1080, moving | regional | 15.269 | 1.483 | 60.4 |

These are RTX 3070 offscreen renderer measurements, not live presentation FPS.
The replay uses the game's default CPU spatial backend; the native game also
exercises the existing hardware gameplay-query integration. UI/presentation
and its query submission effects are outside this timing table. The selected
path costs about 7.45 ms GPU at 720p and 15.11 ms at 1080p in the moving replay,
versus 1.11 ms and 1.81 ms with GI off. This is a measured quality gain with a
substantial GI cost. Receiver connectivity remains the main optimization target.

Both actual app modes pass spawning, firing, 960 x 600 to 1280 x 720 resize,
pause/resume and quit-to-title/new-game restart. The final renderer facts retain
432 probes, the source environment, and the expected Runtime or CompiledResidual
mode. Neither mode changes gameplay hashes in the replay.

The failed xUnit-analyzer build and the interrupted run before per-control
work-count correction are preserved separately in the local evidence directory.
The accepted run reports 55,296 full-runtime, 13,824 fixed/regional, and zero
stale-compiled update directions explicitly. No result from a failed or
interrupted invocation is substituted for this final qualification.
