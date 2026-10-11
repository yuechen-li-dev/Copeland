# Visual TypeScript: statically composed FLOW

## Authoring

GPU compute FLOW uses `run` to compose a statically named fragment with typed
success and error continuations. The compiler expands the composition into a
single flat state graph and typed frame. Inputs and state decisions remain
dynamic; only the composition and storage shape are resolved early.

```ts
flow Countdown(count: u32) -> u32 {
    board { remaining: u32 = count; }
    state Work initial {
        if (board.remaining == 0) {
            finish count;
        }
        board.remaining = board.remaining - 1;
        yield;
    }
}

flow Twice(count: u32) -> u32 {
    state Begin initial { run Countdown(count) -> First; }
    state First(value: u32) { run Countdown(value) -> Last; }
    state Last(value: u32) { finish value; }
}

var frame = Twice.start(U32(Input[0]));
static for (let tick of [0, 1, 2, 3, 4, 5, 6, 7]) {
    frame = Twice.step(frame);
}
```

`run Child(args) -> Accept ! Reject;` supplies the fallible form. An infallible
child has no error destination. Success/error entry types must exactly match
the child's declared types through the existing GPU type binder. Void success
uses a payload-free continuation. Contracts are checked even when the entry
point does not call `step`.

`run` is contextual inside execution states. Copeland's host execution profile
retains explicit bounded `push` and recursive activation semantics. Host `run`
is diagnosed (`COPE-FLOW-EXEC-0017`). GPU `push` is diagnosed with the repair to
use `run` (`COPE-GPU-FLOW-0002`); it cannot silently construct a runtime stack.

## Step and storage laws

- `start` evaluates root arguments and initializes its board without executing
  any state. Inactive storage uses existing certified zero construction.
- `run` saves the parent's board, evaluates child arguments, initializes that
  composition site's child board, selects its initial state, and ends the step.
- Each subsequent `step` runs exactly one active state body. A bare `yield`
  saves the board and restarts the same state body on the next explicit step.
  Locals are transient; progress belongs in the board.
- Child `finish`/`fail` saves its board and delivers the typed payload into the
  statically known parent continuation. The parent body runs on the next step.
- `transition` stays within a fragment and targets a payload-free state. Typed
  continuations require child completion; direct entry is diagnosed.
- Re-entering the same composition site resets its child board and parameters.
  Different sites using the same fragment have separate typed storage.
- Root completion/failure/fault is retained; further steps are idempotent.
  Frames contain ordinary finite GPU values and are independent local snapshots.

The carrier has one payload-free `State` discriminator, a typed board and
parameters per composition site, fixed continuation payload fields, `Outcome`,
and `StepKind`. `entryState` validates delivery into typed continuations; it is
not another active program counter. There are no runtime depth, activation-union
slots, dynamic return addresses, or child scheduler. The source-level hierarchy
is compiled away. Repetition uses `transition` cycles and explicit stepping.

The projection generates ordinary typed syntax, then uses the existing
GPU binder, value/enum layouts, VD-MIR, HLSL emitter, DXC and SPIR-V validation.
It does not add a shader text interpreter or a second layout authority.

## Inspection and limits

`Flow.position(frame)` returns the flat `u32` state position. VD-MIR `FlowTapes`
metadata maps positions to authored state names/spans, fragment paths, parent
paths and composition sites. These maps support a future Dominatus adapter;
this milestone does not add automatic trace readback or a new inspection UI.

Composition is acyclic and closed over statically named flows in the same source
module. Each root admits at most 16 active composition levels, 32 fragment sites
(including the root), and 64 flattened states. Recursion, dynamic/imported child
selection, stack clauses, malformed continuations, fallthrough, resource-backed
frame fields and recursive value layouts are diagnosed. Resources retain their
existing invocation and synchronization owners. Graphics FLOW remains outside
this compute profile. Neither `run` nor a state transition submits Vulkan work
or supplies barriers. No GPU `while`, `goto`, or runtime compute loop is added.

The scalar buffer ABI remains `StorageBuffer<f32>`: local typed snapshot replay
does not qualify typed frame persistence or shape/definition-checked restoration
across dispatches. That remains a separate buffer/adapter milestone.

## Native evidence

```powershell
dotnet test Copeland.TS.slnx -m:1
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet test Aurelian.slnx -m:1
dotnet run --project tools/Aurelian.GraphicsProof -- --flow-proof --flow-nested-experiment --output artifacts/local/flow-tape
```

The real Vulkan proof checks seven shaders twice on an RTX 3070: yielding child
progress, two-level success/error delivery with runtime inputs, re-entry resetting
child storage, reused fragments returning records, checkpoint completion replay,
closed static replay, and dynamic local replay. Evidence and generated VD-MIR,
HLSL and validated SPIR-V live in `artifacts/local/flow-tape/`.

The final full regression runs passed 1,947 Copeland TS tests across three test
projects and 1,168 Aurelian tests across 30 projects, including 241 shader tests.
Logs: `artifacts/local/flow-tape-copeland-tests-final.log` and
`artifacts/local/flow-tape-aurelian-tests-final.log`. The 14 new shader tests cover
composition contracts, source maps, reused storage, finite aggregate layouts,
recursion and budget diagnostics. The host profile also tests the explicit
GPU-only `run` diagnostic while retaining its bounded `push` regression cases.

The earlier dynamic replay exceeded DXC's unchanged 15-second limit with a
three-slot nested activation carrier (`artifacts/local/vts-flow-nested/`). Its
flat replacement uses a 160-byte Root frame and completed total backend
compilation in approximately 0.26 seconds in the recorded run. The ten-step
runtime-input nested shader compiled in approximately 0.94 seconds and the
larger checkpoint-completion replay in 2.18 seconds. These are measured backend
compilation times, not GPU execution times or a universal speedup guarantee.

The current positional replay returns 66, encoding two visits to Child.Work at
position 6. The previous stack-depth specimen returned 34; that was a different
inspection representation. The separate runtime-input completion replay checks
actual delivered results and retained board snapshots, not just positions.
