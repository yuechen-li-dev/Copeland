# Humanoid locomotion

The starter and Beacon can consume an explicit body-bound Idle/Walk/Run bank.
The existing player agent owns gait phase, interrupted blend, requested and
accepted root displacement, aim weight and both world-space foot locks.
Dominatus observes this state; the renderer only presents the solved pose.

**Status: accepted for the current body, three clips and tested game transitions.**
The 38 previously failing samples now pass. Dense qualification covers every
imported key, every key interval midpoint, the original uniform samples and all
600 gameplay ticks: 1,737 poses total. Both CPU and Vulkan ground-query runs pass
the unchanged surface screen. This is bounded animation qualification, not
production certification for arbitrary clips or exact self-intersection proof.

## Bake the user's local references

Use the installed Blender FBX importer; no new tools or downloads are needed.
Run from the Copeland repository root:

```powershell
& 'C:/Users/yuech/AppData/Local/Programs/Python/Python310/python.exe' ../Aetheris/scripts/repair-antonia-locomotion-weights.py --body ../Aetheris/artifacts/local/humanoid-production/antonia.gameplay-body.json --out ../Aetheris/artifacts/local/humanoid-locomotion/antonia.gameplay-body.json
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --python tools/bake-humanoid-locomotion.py -- --body ../Aetheris/artifacts/local/humanoid-locomotion/antonia.gameplay-body.json --idle C:/Users/yuech/Downloads/Idle.fbx --walk C:/Users/yuech/Downloads/Walking.fbx --run C:/Users/yuech/Downloads/Running.fbx --out artifacts/local/playable-humanoid-locomotion/mixamo.locomotion.json
```

The adapter uses an explicit semantic bone map and checks parent correspondence.
It derives scale from thigh length and accounts for distinct rest/animation
facing. Clavicle direction changes are transported through the parent rest frame;
axial bone roll does not move the clavicle. Knuckle landmarks define anatomical
hand/forearm frames, so source forearm pronation does not become a wrist bend.
Other limb directions retain the body's rest roll. The adapter extracts forward
travel and closes the pose loop independently of cumulative distance. It records
source hashes and the original seam discrepancy. It does not reuse Mixamo's
mesh or weights. Raw FBXs and derived character data remain local and ignored.
This is a specific adapter with qualified local assets, not a general FBX importer.

The Aetheris preparation script writes a new body variant. It retains source
vertices, topology, skeleton and hip correctives. It smoothly transfers cervical
support to the skull over the neck/jaw, independent of semantic region labels,
and uses a 200 mm knee transition half-width for deep running flexion. It checks
retained geometry and normalized six-influence weights before writing. No clip
amplitude is reduced to pass the screen; new bodies/clips require their own proof.

## Compose and play

```csharp
var humanoid = HumanoidPlayerOptions.Load(bodyPath);
humanoid = humanoid with
{
    Locomotion = HumanoidLocomotionBank.Load(bankPath, humanoid.Body),
};

GameStarter.Run("my-game",
    [.. GamePresets.FirstPersonShooter, GameConcept.ThirdPersonCamera,
        GameConcept.ThirdPersonControl, GameConcept.HumanoidPresentation],
    args, humanoid: humanoid);
```

`humanoid-starter.cmd` selects the corrected local body and bank. WASD moves; Left Shift
sprints through InputMan; V switches camera; Space jumps; left mouse aims.
Without a locomotion bank, the existing basic in-place animation remains
available. Beacon accepts the same explicit `--humanoid` and `--locomotion` paths.

```powershell
.\humanoid-starter.cmd --gpu-rays --playtest-script Games/Starter/Playtests/locomotion-proof.json --output artifacts/local/playable-humanoid-locomotion/starter --save-root artifacts/local/playable-humanoid-locomotion/starter/saves
```

The bank's cumulative travel requests movement through the existing capsule
motor. Collision remains authoritative. Accepted horizontal displacement advances
gait phase; a blocked actor blends to idle. Clips are paced to the desired walk/run
speed. Lateral sway and vertical bob stay in the visual root offset. These forward
clips also drive strafing/backward movement; directional clips and root yaw are
not implemented.

Stance contacts raycast the shared spatial world. On supported hardware,
`--gpu-rays` uses the existing Vulkan ray-query path. Foot targets lock in world
space, fade over 0.1 seconds, align soles to ground and release on jumps or
unreachable targets. Aetheris performs analytic two-bone IK with the animated
knee pole and rigid link lengths. Pelvis lowering is bounded to 100 mm.
Small joint-sized solves stay on CPU; all surface skinning stays on GPU.

Deliverance snapshots include the bank identity, phase, current rotations,
interrupted blend, pelvis adjustment and foot targets. Restore validates matching
channels and admitted poses before replacing live state. Input replay rebuilds
the same state. Rendering does not advance animation. The new body/bank hashes
change save compatibility; older profiles require their original explicit assets.

## Qualification

```powershell
dotnet run --project tools/Aurelian.LocomotionProof -c Release -- --body ../Aetheris/artifacts/local/humanoid-locomotion/antonia.gameplay-body.json --locomotion artifacts/local/playable-humanoid-locomotion/mixamo.locomotion.json --gpu-rays --output artifacts/local/playable-humanoid-locomotion/qualification-fixed
```

The proof checks 36 uniform clip samples, 1,101 keys/midpoints and 600 actual
starter ticks, including aim transitions and collision. Each case compares all 162,672 GPU
corners against Aetheris CPU deformation and runs the unchanged surface screen.
It requires unit normals, at most 0.02 mm GPU error, reachable fully weighted
foot residual at most 1 mm, no collapsed triangles, no orientation proxies and
bidirectional edge ratios at most four. It writes `evidence.json` even when
surface qualification fails.

Use `--diagnose` for CPU joint-neutralization comparisons, flagged face vertices,
weight support and worst edge witnesses. This isolates rig mapping from surface
support without changing acceptance policy. It is a diagnostic mode, not an
acceptance run. The screen remains a deformation proxy.

### Local evidence, 2026-10-09

Corrected body SHA-256: `206D2485CDDEC9EE7ABACCE4EA35CC97B9C93DD6312695DE2C4EBAA2D6BCBC41`.
Bank SHA-256: `67EAC2031159D82BE9CBF937CDAAECF4C7FD54EAA0CEA93142A7CD2DE4F1C879`.
Device: NVIDIA GeForce RTX 3070.

- Aurelian Release build: zero warnings/errors; 956 tests passed across 27 assemblies.
- Aetheris Release build: zero errors, two existing SQLite/WASM warnings; 4,412 tests passed across 20 assemblies.
- Starter native proof: passed, 357 Vulkan ray-query dispatches and 385 GPU skinning dispatches. Native/headless final observations matched byte-for-byte.
- Run transition, Deliverance restoration and rewind captures shared SHA-256 `393BBBDF3CD20A3EAEAFAD6895287FF4A42B70C85CD910B3594C9C35512603AD`.
- Beacon native proof with the same bank and GPU ray queries: passed.
- Surface/GPU proof: **accepted**, zero failures among 1,737 poses, 1,737 skinning dispatches and 916 Vulkan ray queries. Maximum GPU/CPU error was 0.000977844 mm; maximum fully weighted reachable foot residual was 0.110564 mm across 519 samples. Nineteen unreachable stance targets were released by the existing policy.
- CPU ground-query proof: the same 1,737 poses passed; maximum skinning error 0.001040424 mm and reachable foot residual 0.110521 mm.
- Existing basic animation: 240 frames and four hip-corrective/world-placement cases passed on the repaired body, maximum GPU error 0.001017 mm.
- Re-baking the current sources reproduced the bank byte-for-byte. Three Python regressions cover neck/jaw region continuity, bilateral knee support and unaffected distant support.

Previously failing cases now accepted:

| Case | Corrected cause |
| --- | --- |
| `Idle.6` | Smooth skull/cervical support removes the facial orientation proxy |
| `Walk.2` | Anatomical forearm/palm frames remove wrist-driven reversals |
| `Run.0` | Continuous skull support and wider knee field accommodate deep flexion |
| `Run.9` | Parent-frame clavicle swing removes shoulder compression |
| `game.130.Run` | The corrected clavicle transport also passes the aim transition |

Logs, captures, `walk-run-fixed.gif` and `qualification-fixed/evidence.json` are
retained under `artifacts/local/playable-humanoid-locomotion`. Historical rejected
evidence remains under `repair/baseline-evidence.json`; it is not current acceptance.

The deterministic integration tests use synthetic explicit channels rather than
private assets. They cover cumulative loop travel, wall collision, sloped ground,
jump release, rendering purity, fresh Deliverance restoration, interrupted gait
blends and rejection of incompatible saved channels. Native capture/replay proves
the game integration separately from the surface gate.
