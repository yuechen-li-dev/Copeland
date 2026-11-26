# Beacon graphics shader baking and command reuse

Beacon's opening scene exercises the native Vulkan game renderer at
960 × 600. Time to first frame starts at benchmark command entry and ends
after the first swapchain present call succeeds. The opening view includes
the separate first-person model. A second rendering captures the reference
pixels outside the startup measurement.

The shader-baking arm loads the existing `CompiledGraphicsProgram` artifact
for each requested graphics program. Each artifact includes both SPIR-V
stages and the vertex inputs, pixel targets, and descriptor metadata used
to construct its Vulkan pipeline. The loader checks the format and the
stage byte hashes. Bake a fresh pack when changing the shader sources or
enabling a different startup profile.

The command-reuse arm uses the same baked shaders. It records the output
draw into a secondary command buffer per frame slot. The primary retains
the render-pass boundaries and texture transitions. Uniform changes use
the existing mapped buffer. A change to a bound image invalidates the
recording for that slot, once its previous submission has completed.

## Results on RTX 3070

Each arm ran in three fresh processes with 150 measured frames per
process after warmup. Values below are medians. The CPU column covers
recording the output pass. The GPU column uses the output-pass timestamps.

| Mode | Time to first present | Output CPU recording | Output GPU |
|---|---:|---:|---:|
| Runtime shaders and inline draw | 26.82 s | 16.70 µs | 26.11 µs |
| Baked shaders and inline draw | 3.93 s | 19.10 µs | 28.67 µs |
| Baked shaders and replayed draw | 4.02 s | 16.55 µs | 26.62 µs |

The startup pack contains 31 graphics programs (2.59 MiB as JSON). It
also passed the normal game launcher's four-frame smoke. The opening
view and the exposure and bloom transition frames matched bit for bit
across the three arms. The bloom transition recaptured one draw per slot
when the bound texture changed and reused each recording thereafter.

Two additional runs alternated inline and replayed frames in the same
renderer, reversing the order of each pair. Across 600 pairs, the median
paired CPU saving for the output pass was 2.30 µs.

| Paired metric | Inline median | Replay median | Inline p95 | Replay p95 |
|---|---:|---:|---:|---:|
| Output CPU recording | 13.30 µs | 11.30 µs | 17.30 µs | 15.10 µs |
| Whole-frame CPU preparation and recording | 607.70 µs | 601.85 µs | 787.99 µs | 788.49 µs |
| Output GPU | 12.29 µs | 12.29 µs | 15.36 µs | 15.36 µs |

The replay arm also completed the interactive two-slot check with an
exact image match after eight queued frames. Vulkan validation was active
for these runs. The normal game launcher accepts an explicit shader pack.
Command replay is opt-in at the renderer.

## Reproduce

```powershell
dotnet build Games/Beacon3D/Aurelian.Beacon3D -c Release -m:1
$beacon = 'Games/Beacon3D/Aurelian.Beacon3D/bin/Release/net10.0/Aurelian.Beacon3D.dll'
$out = 'artifacts/local/beacon-graphics-reuse'
$pack = "$out/shipped-pack"
dotnet $beacon --graphics-benchmark bake $pack 16
foreach ($mode in 'source', 'packed', 'replay') {
    dotnet $beacon --graphics-benchmark $mode $pack 150
}
dotnet $beacon --launch-smoke --graphics-shader-pack $pack --resolution 960x600 `
    --output "$out/launch-smoke" --save-root "$out/saves"
```

`source` compiles graphics stages at runtime and records the draw inline.
`packed` loads the baked artifacts and records inline. `replay` loads the
same artifacts and reuses the output draw commands. Run each in its own
process and reverse the order in alternating passes. The JSON includes
CPU recording, whole-frame preparation and recording, and GPU timestamps
for the output pass. It also reports pixel hashes across exposure changes
and bloom texture rebinds. Run `replay` twice with 300 samples to reproduce
the paired measurements. Raw results are under
`artifacts/local/beacon-graphics-reuse`.
