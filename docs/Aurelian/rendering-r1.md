# R1: declarative frame authoring and compiled tapes

R1 executes the production Vulkan 3D renderer through a cached, verified frame plan. C# declarations
compile to a flat data tape, which lowers once to native pass IDs and memory dependencies. Steady frames
traverse that tape. They do not rebuild dependencies, search resource names, or invoke serialized callbacks.

## Authoring and the artifact

`Native3DFrameGraph` is the production declaration. `FrameGraphBuilder` is also usable directly:

```csharp
var graph = new FrameGraphBuilder();
var input = graph.Resource("surface", "rgba16f", width, height,
    FrameLifetime.Imported, initialized: true);
var scratch = graph.Resource("scratch", "rgba16f", width, height);
var output = graph.Resource("output", "rgba16f", width, height);
graph.Pass("light", lightImplementation).Read(input).Write(scratch);
graph.Pass("display", displayImplementation).Read(scratch).Write(output);
graph.Export(output);
FramePlan plan = FramePlanCompiler.Compile(graph.Build());
string json = plan.Describe();
```

Pass implementation IDs bind to existing renderer methods. Shaders and native handles remain outside the
artifact. `FramePlan` contains immutable declarations, resolved order, barrier/pass instructions, transient
lifetimes and slots, submission batches and waits, and a SHA-256 content hash. Format version is
`aurelian.frame-plan/1`; hash input is its deterministic JSON representation with an empty hash field.
This hash identifies plan data, not shader binaries or a complete rendered scene.

The cache key contains renderer capabilities and execution-relevant scene/settings features: temporal,
bloom, AO, light tiles, shadow count, subsurface, fog/volume, refraction, transparency, particles, GPU history,
and queue/placement options. Extent belongs to the renderer instance. Uniform values remain per-frame data.
Topology changes select another verified cached plan. Repeated frames reuse the same plan object.

`CurrentFramePlan.Describe()` exposes the artifact. Native playtest command `inspect rendering` prints it
through the game surface. `InstallFramePlan(json)` verifies the artifact and admits only a plan matching a
native profile already compiled by that renderer. Native qualification installs the JSON round-trip and
executes subsequent pipelined frames through that object, then checks capture equality.

Copeland TS/TSON can become another frontend to `FrameGraph` or the versioned JSON schema. R1 uses C#
because the renderer's capabilities and optional stages already live there. The current TSON route does
not provide general JSON lowering; adding that compiler feature is independent work. There is one frame
compiler and verifier for every future frontend.

## Compilation and verification

The compiler infers RAW, WAR and WAW dependencies in declaration order, incorporates explicit dependencies,
sorts deterministically, and retains the backward dependency closure of outputs and side effects. The
current limits are 256 logical resources and 128 declared passes. Invalid plans fail before GPU recording:

| Diagnostic | Rejected condition |
| --- | --- |
| AUR-PLAN-001 | Invalid declarations, operands, schema or tape/order agreement |
| AUR-PLAN-002 | Uninitialized read or uninitialized output |
| AUR-PLAN-003 | Cyclic or unsatisfied pass dependency |
| AUR-PLAN-004 | Missing, insufficient, duplicate or late hazard barrier |
| AUR-PLAN-005 | Access unsupported by the declared queue class |
| AUR-PLAN-006 | Unconsumed live write without an explicit unused/output declaration |
| AUR-PLAN-007 | Content hash mismatch |
| AUR-PLAN-008 | Incorrect batch coverage, order, queue or cross-queue wait |
| AUR-PLAN-009 | Incompatible/live alias, incorrect lifetime or missing alias-ownership barrier |
| AUR-PLAN-010 | Unadmitted native artifact or implementation |

The verifier replays the published tape, verifies the dependency and submission schedule, and checks
placement separately. Aliasing requires compatible formats/extents and a happens-before proof including
queue order. Merely occupying disjoint positions in the topological list is insufficient across queues.
WAR barriers retain all preceding reader access classes. Ownership changes between logical occupants of
one storage slot also require a barrier.

Existing packs keep their internal render passes and image-layout tracking. AO/denoise, bloom, diffusion,
and atmosphere remain compound stages with their existing internal transitions. The plan owns dependencies
between stages. Intentional extra MRT outputs and compound scratch are marked `AllowUnused`; this is
visible in the artifact. This is not a proof of arbitrary shader accesses beyond the declarations.

On supported Vulkan 1.3 devices, dependencies use `vkCmdPipelineBarrier2`; older devices retain the legacy
memory-barrier path. Adjacent resource hazards lower to one native memory dependency per boundary, with
conservative `AllCommands` stage scopes. Existing pack-specific layout transitions are retained. This follows
the [Khronos synchronization2 model](https://docs.vulkan.org/guide/latest/extensions/VK_KHR_synchronization2.html).

## Changes with native effects

With AO strength zero, the unused AO/denoise stage is culled and surface lighting binds an initialized
neutral AO texture. With no local lights, or culling disabled, the unused light-tile stage is culled and
the surface stage binds its initialized fallback. The control option preserves both stages for comparison.

The diffuse-light scratch output and the later fog output reuse one physical RGBA16F image. Placement is
compiled from the production declaration with every optional consumer enabled, including subsurface
diffusion and refraction. Runtime profiles can shorten those lifetimes. Other attachments remain dedicated.
Reuse shares the same image object, layout tracker and allocation; it does not bind multiple distinct
VkImages to overlapping memory. The proof measures actual native allocation requirements, including padding.
Only this admitted pair currently uses native transient reuse.

## Second-queue experiment

When the graphics/compute family exposes a second queue, initialization creates it. R1 uses the existing
particle compute kernel as the scheduling specimen. This addresses the proposal's independent-compute
intent while preserving the existing fragment-based AO algorithm. There is no AO compute-port claim.

`FramePlanOptions.AsyncParticleUpdates` opts into the experiment. The compiled schedule submits particle
compute first, an independent graphics batch second, and the consuming graphics batch last. That final
batch waits for the compute timeline. All command buffers are recorded before submission. There are no
CPU waits or Tasks between stages. The particle buffer's previous-frame draw is protected by an additional
compute wait on the preceding graphics-frame timeline. Separate command retirement timelines and per-slot
query pools protect recordings and measurements. Both queues use the same family, so queue-family ownership
transfers are unnecessary. See the [Khronos timeline-semaphore sample](https://docs.vulkan.org/samples/latest/samples/extensions/timeline_semaphore/README.html).

Timestamp evidence compares the particle interval with the directional-shadow interval, rather than
calling a whole-frame interval containing a wait "overlap." A measured interval overlap is a queue-timestamp
observation, not an instruction-level utilization measurement. The default remains one queue, pending
workload-specific evidence that a second queue improves the result.

## Reproduce

```powershell
pwsh -File tools/aurelian-rendering-r1/Qualify.ps1 `
    -Baseline artifacts/local/rendering-r1/before `
    -Output artifacts/local/rendering-r1/final
```

The baseline directory must contain captures generated by the saved pre-change runtime, not new golden
images. This run saved binaries from `1de71d2f` outside the repository and captured all nine modes before
editing the renderer. The script builds/tests `Aurelian.slnx`, runs `--rendering-r1` with synchronization
validation and tiered compilation disabled, reruns nine existing native proof modes, compares every baseline
PNG, and rejects missing captures, differences, failed commands and Vulkan validation errors.

The R1 specimen covers inactive stages, subsurface followed by fog, small visible particles, and a
131,072-particle bandwidth stress case. Most stress emitters sit offscreen to emphasize compute/buffer
traffic over transparency overdraw. It compares control, compiled and second-queue profiles, captures
before and after pipelined JSON-artifact replay, dumps all plans, and records allocation and timestamp data.
Short timing samples are diagnostic; deterministic pass removal, physical bytes and capture equality are
the improvement gates. They do not establish a general renderer speedup.

The retained-graph starting point is [Frostbite's FrameGraph presentation](https://www.gdcvault.com/play/1024612/FrameGraph-A-Retained-Mode-Rendering).
Aurelian's additional boundary is a versioned, hashed, independently checked data artifact and a cached
native tape, as proposed in the rendering foundation design.

## Local measurements, 2026-11-03

The RTX 3070 reports synchronization2 support and provides both requested queues. The full build passed
with zero warnings/errors, and all 1,545 tests passed with no skips. Native R1 qualification passed with
synchronization validation enabled. Evidence is under `artifacts/local/rendering-r1/final/`.
All 225 existing PNG captures across the nine proof modes are byte-identical to the pre-change baseline.

| Native specimen, 640x360 | Control | Compiled default |
| --- | ---: | ---: |
| Inactive-AO/light-tile executable stages | 9 | 7 |
| Diffuse/fog physical images | 2 | 1 |
| Diffuse/fog actual allocation bytes | 3,932,160 | 1,966,080 |
| Profile capture differences | 0 | 0 |

The 50% saving applies to the admitted diffuse/fog pair, not total renderer memory. The saved 1,966,080
bytes include native allocation padding. Nine R1 profile/workload captures and their pipelined artifact
replays match their respective controls.

The first queue experiment submitted graphics before compute. The second submitted compute first. Both
recorded zero particle/directional-shadow overlap. In the final short diagnostic run, the compiled
single-queue path delivered about 884 frames/s for small particles and 672 frames/s for the bandwidth case;
the second-queue path delivered about 744 and 528 frames/s respectively. Thus the second-queue experiment
does not justify default enablement on this device. These are application-loop rates including validation
and CPU work, not GPU-only throughput claims. GPU clock transitions and fixed profile order also make the
initial control timings unsuitable for claiming a general speedup. The allocation and stage-count gains
are exact and independent of those timings.

`native/evidence.json` retains the first queue experiment; `final/rendering-r1/evidence.json` records the
final schedule, plan hashes, measurements and device facts. `MedianGpuFrameMilliseconds` in that JSON is
the graphics queue's frame timestamp interval; a second queue can begin outside that interval. Whole-loop
rates and the separate compute/shadow intervals supply the corresponding context.
