# Agent-driven humanoid animation

`Aurelian.Humanoid` connects Aetheris's typed gameplay body and anatomical pose
solver to Aurelian scenes, the existing Dominatus runtime and Vulkan rendering.
The integration owns animation and presentation. Aetheris continues to own
body bindings, legal joint coordinates, dual-quaternion palettes and the CPU
deformation reference. This does not promote the Antonia gameplay candidate
to the canonical humanoid artifact format.

## Compose a character

Reference `src/Integrations/Aurelian.Humanoid/Aurelian.Humanoid.csproj` from the
game project. The repository currently expects sibling Aetheris and Dominatus
source checkouts, as the other integrations do.

```csharp
var body = HumanoidGameplayBody.Load(bodyPath);
var policies = new AurelianAgentRuntime(512);
var definition = new HumanoidCharacterDefinition(body, policies, HumanoidAnimationSet.Basic);
using var scene = SceneCompiler.Compile(Scene.World("arena", [
    Scene.Agent("player", definition, at: new Vector3(0, 0, 0)),
    Scene.Agent("guard", definition with { }, at: new Vector3(2, 0, 0)),
])).Mount();
var character = scene.Agents.OfType<SceneAgent<HumanoidCharacterState>>().First();

// Shared simulation cadence: observe, tick all policies once, then advance animation.
definition.Observe(character, new CharacterPresentationObservation(speed, aiming, grounded));
policies.Tick(TimeSpan.FromSeconds(deltaSeconds));
definition.Advance(character, deltaSeconds);

// Presentation samples state; it never advances it.
SolvedHumanoidPose pose = definition.Pose(character);
gpuBody.Present(pose, character.WorldTransform);
renderer.Render([], camera, clearColor, gpuGeometry: gpuBody.Geometry);
```

Create one retained `HumanoidGpuBody(plant, skinningSpirv, body)` per presented
character and dispose it with the graphics resources. Compile
`HumanoidSkinning.v.ts` through `GpuComputeBinder` and `VdMirComputeBackend`; the
lab shows the complete startup path. Scene mounting acquires the Dominatus
policy lease, and despawning/disposal releases it. Definitions can be reused
with `with`; each instance has independent animation state and a fresh brain.

The shared utility policy selects Aim when aiming, Walk when grounded and
moving, and Idle otherwise. Selection and action publication follow Dominatus's
existing cadence; an observation is not an immediate renderer-side branch.
`policies.Inspector.Observe()` exposes the decision trace, observations, selected
motion and clip time through the shared inspection kit.

`HumanoidCharacterState` explicitly stores world position, yaw, motion, clip
time and the current transition source. Position and heading can be customized
with ordinary `with` assignment. Scene placement initializes both position and
yaw. This profile admits upright rigid placements; scaling and tilted placements
fail rather than being silently discarded. Animation state is a typed application
record. The game starter captures that record and the presentation policy through
Deliverance. The inspection trace alone is not a complete animation-state snapshot.

## Playable character fragment

Compose humanoid presentation with the starter's movement, cameras, guns, menus
and saves. Reference `Aurelian.Games` and load the explicit body before opening
the window:

```csharp
var humanoid = HumanoidPlayerOptions.Load(bodyPath) with
{
    Attachments = [HumanoidPlayerOptions.PlaceholderWeapon()],
    // Animations = myAnimationSet,
};
GameStarter.Run("my-game",
    [.. GamePresets.FirstPersonShooter,
        GameConcept.ThirdPersonCamera,
        GameConcept.ThirdPersonControl,
        GameConcept.HumanoidPresentation],
    args, humanoid: humanoid);
```

The same human-controlled scene agent owns position, heading and animation.
Collision-resolved horizontal speed and grounded state feed its Dominatus policy;
aiming takes priority while moving. Camera changes preserve the actor's state.
First-person view hides the body; third-person view draws the retained GPU body.
Paused menus stop movement and animation. Airborne movement currently uses Idle;
dedicated jump and landing clips can follow.

Attachments name an anatomical joint and carry immutable scene geometry plus an
offset in bone-local metres. They follow the solved joint and actor placement.
Attachment content cannot contain agents or collision. The default profile is
unarmed; these applications explicitly add the placeholder wrist-mounted weapon.
This is a rigid visual mount, without grip IK.

Assign `Animations` to replace clips. Every authored key is checked by Aetheris
before startup. Body revision, clip contents and attachments participate in save
compatibility, including changed keys with unchanged clip names.

Starter snapshots include clip phase, interrupted blend source and the Dominatus
policy checkpoint. This particular policy uses steady actions with no timers or
commitments, so its cold checkpoint resumes the same presentation. This does not
make arbitrary coroutine checkpoints exact; use shared input replay for general
rewind.

Run from the repository root:

```powershell
.\humanoid-starter.cmd
.\humanoid-starter.cmd --playtest-script Games/Starter/Playtests/humanoid-proof.json --output artifacts/local/playable-humanoid-starter --save-root artifacts/local/playable-humanoid-starter/saves
.\beacon3d.cmd --humanoid ../Aetheris/artifacts/local/humanoid-production/antonia.gameplay-body.json --playtest-script Games/Beacon3D/humanoid-proof.json --output artifacts/local/playable-humanoid-beacon
```

The starter proof exercises collision, jumping, camera switching, pause,
Deliverance restoration and input replay with native Vulkan captures. Beacon
uses its existing player agent and the same policy, clips, attachments and GPU
presenter. Its proof covers view changes, pause and input replay; Beacon does
not acquire save slots from this integration.

## Author clips

`HumanoidAnimationClip` uses immutable `HumanoidKeyframe` records containing
absolute `AnatomicalJointRequest` channels. Times must increase, endpoints must
match the declared duration, and every key uses the same ordered semantic joints.
Looping endpoints must agree. Replace idle, walk or aim independently by building
a `HumanoidAnimationSet`; no bone-name discovery or animation graph reflection
is involved. Poses are validated by the existing Aetheris solver when sampled.

The basic set has a one-second alternating stride, an idle hold and an aim hold.
Walk playback scales with observed speed. Motion changes blend over 0.2 seconds
using smoothstep; interrupted blends start from the current blended pose. Zero
elapsed time preserves state. Rendering may sample it repeatedly without mutation.
The built-in clips use shoulder/elbow/hip/knee flexion and shoulder abduction.

## GPU and qualification boundary

`HumanoidSkinning.v.ts` applies the two authored pre-skin hip correctives, aligns
quaternion signs, blends all six possible influences and normalizes the dual
quaternion. It converts the source's millimetres and Z-up basis to Aurelian's
metres/Y-up basis and applies a rigid world transform. Smooth rest normals rotate
with the same blended quaternion. They are not recomputed from corrective
surface derivatives. The material is plain gray and untextured.

Mesh data uploads once. Each update uploads only the joint palette, corrective
amounts and world transform. The compute output is consumed directly as the
shared 40-byte solid vertex buffer. CPU skinning/readback exists only in the
explicit qualification scenario. Compute and draw use the existing allocator,
command pool, submission and fence seams; submissions currently wait for
completion. This proves correctness, not asynchronous throughput optimization.

The current retained buffer expands the 54,224 triangles into 162,672 corners.
The source body has 27,193 vertices and 55 semantic joints. This profile accepts
the explicit bilateral hip-corrective bank and rejects unsupported shape banks.
It is not a generic glTF skin/animation importer. No per-frame mesh reconstruction
or four-weight truncation is used.

`characterlab.cmd --proof --record` verifies 240 animated frames and four
active-corrective/world-placement cases on the actual Vulkan device. Every
corner is compared to Aetheris's CPU reference with a 0.02 mm acceptance ceiling;
the evidence records the measured maximum, device, source hashes, distinct
capture hashes and Dominatus trace. Every animated pose must pass Aetheris's
existing deformation screen. That screen is not an exact self-intersection
test, and these cases do not qualify arbitrary new clips.

The lab also offers `--launch-smoke` to verify native swapchain presentation.
It does not prove foot planting, root motion, movement collision, facial/finger
animation, retargeting, clothing, textures or arbitrary twist combinations.
The basic lab walking is intentionally in place; a game supplies its existing
movement state. The optional game locomotion bank adds root travel and foot IK;
see [locomotion status and reproduction](humanoid-locomotion.md). Its current
Mixamo bank has separate, dense surface qualification for the corrected body;
acceptance of new clips still requires running that proof.

Run the integration unit tests with:

```powershell
dotnet test tests/Integrations/Aurelian.Humanoid.Tests -c Release -m:1
```

They cover closed loop interpolation, pause behavior, interrupted blends,
deterministic replay, placement, InputMan focus handling and independent
scene-owned Dominatus lifetimes. See [Character Lab](../../Games/CharacterLab/Aurelian.CharacterLab/README.md)
for the complete runnable example.
