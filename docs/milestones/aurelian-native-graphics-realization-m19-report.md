# Aurelian native graphics realization M19 report

## 1. Outcome

**Outcome A — Aurelian owns the canonical native graphical realization path.**

The normal Mossward command now opens a Silk/Vulkan window and composes native Profile assets, a Visual-TypeScript semantic fog shader, and the existing `StrategyHudProfile` through native Machina adapters. The former Avalonia/Skia window remains available only through `--compatibility-avalonia`.

## 2. M18 baseline

M18 supplied the application truth: deterministic simulation, selection, `VisibilityGrid`, `IsometricProjection`, nine Profile-authored assets, and `StrategyHudProfile`. Its normal display flattened Profile contours into sample-local `SKPath` objects and copied a whole Skia frame into Avalonia. See [m19-native-graphics-baseline.md](m19-native-graphics-baseline.md).

## 3. Primary graphics ownership law

`Copeland source -> Profile composition IR -> Aurelian native realization -> Vulkan`.

Application state owns meaning. Profile owns ordered renderer-neutral geometry/paint. Aurelian owns cache/resource/draw realization. Vulkan is the platform boundary. Skia and Avalonia remain reference, export, test, and compatibility mechanisms.

## 4. Skia and Avalonia classification

The default program selects `StrategyNativeRuntime`. `--compatibility-avalonia` selects the old window. Skia remains the parity oracle and PNG evidence encoder. The complete classification is in [avalonia-skia-role-m19.md](../research/avalonia-skia-role-m19.md).

## 5. Canonical Profile realization contract

`ProfileNativeCompiler` consumes the composition hash, ordered layers/items, closed canonical contours, fill metadata, bounds, and source path. It returns an immutable `ProfileNativeCompositionResource` with geometry identity, packed MSDF atlas, and deterministic draw plan. Application code receives no Vulkan handle.

`ProfileNativeInstance` carries only origin and scale. `ProfileNativeRealizationCache` privately owns uploaded textures.

## 6. Painter order

The compiler walks layer order and item order without sorting. `PainterIndex` records the resulting sequence. Every submitted paint remains an individual ordered draw; no batching crosses a painter barrier.

## 7. Contour validation

Native compilation rejects empty geometry, open contours, non-finite/invalid geometry, unsupported winding, and unsupported paint. Diagnostics retain the source path and stable M19 codes. Malformed geometry is not approximated.

## 8. Paint support profile

M19 qualifies non-zero closed contour flat fill with optional alpha. Gradients and strokes are explicitly rejected. The nine existing assets required neither, so implementing them would not close an observed gap.

## 9. Flat-fill implementation

Canonical contours use the already-qualified vector-icon MSDF compiler and persistent atlas texture. Per-frame work is transformed ordered quads; no Skia call or CPU rasterization exists in the native runtime path.

Visual review exposed undersized component fields and a fixed reconstruction ramp that made small vectors and 12 px text look swollen. Native Profile compilation now uses 192 px quality, a 40 px short-axis floor, and a proportional 12 px distance range. Mossward glyphs use at least 64 px fields with an 8 px range. Atlas dimensions are chosen from field capacity, rather than geometry count alone. Both glyph and Profile adapters use the same engine-owned small-screen threshold compensation, and the shared shader uses a quarter-pixel coverage ramp. Increasing an atlas page without increasing its component fields is explicitly not treated as a quality fix.

## 10. Gradients

Not qualified. Existing roof facets, shadow ellipses, and layered color recipes provide the required depth without a new paint model.

## 11. Strokes

Not qualified. Current Profile assets contain no required stroke paint. Machina panel borders remain a separate existing analytic primitive.

## 12. Cache identity

The exact `ProfileComposition.SemanticHash` is the cache key. Geometry and atlas hashes guard accidental identity reuse. A changed fill source changes the composition hash. Nine cold assets produce nine uploads; repeated transformed instances produce cache hits rather than uploads.

## 13. Resource lifetime

The cache is bounded (1–1024, 32 in the acceptance tool), requires explicit warm-up before frame submission, supports explicit invalidation, and disposes every owned texture. Resize retargets renderers without rebuilding semantic Profile resources.

## 14. Skia/native parity

The representative worker comparison measured silhouette IoU `0.991319` and mean interior channel error `5.348999` on an 8-bit channel after the high-resolution field and small-screen reconstruction change. Skia is the reference side only.

## 15. M18 asset-pack migration

HQ, worker, ranger, heavy, production/lodge, watchtower, crystal, tree, and marker compile from their unchanged Profile sources into the reusable native resource. The default strategy renderer discovers exactly the nine-asset pack and has no `StrategyAssets`/Skia fallback.

## 16. TinyFarm second consumer

TinyFarm owns a standalone Profile-authored tree in its own asset directory. `TinyFarm.Native` compiles it through `ProfileTsxCompiler` and `ProfileNativeCompiler`, warms the same cache type, and submits it in an existing native MSDF pass. No strategy renderer dependency or TinyFarm semantic change was introduced. The full TinyFarm proof exits zero.

## 17. Profile preview and tooling

Native preview command:

```powershell
dotnet run --project tools/Aurelian.NativeGraphicsM19 -c Release -- --preview hq
```

It records source concept path, composition hash, paint mode, cache status, and a Vulkan readback PNG. This is deliberately a preview card/command, not a vector editor.

The default Mossward app also exposes a bounded development inspector with `F10`. It spawns/despawns a Machina overlay showing Profile cache and fog facts. The overlay is presentation-only and cannot mutate simulation truth.

## 18. Wilderland polish-gap audit

The final scene adds an isometric Profile-tile field, path/water treatment, sharper canonical art, and soft exploration transitions while retaining the M18 HUD. See [wilderland-polish-gap-m19.md](../research/wilderland-polish-gap-m19.md).

## 19. Visibility semantic ownership

`VisibilityGrid` remains authoritative for unknown, explored, and currently visible cells. The shader cannot write, retain, or infer gameplay visibility.

## 20. Visibility field projection

`VisibilityFieldProjector` provides a renderer-neutral immutable RGBA8 grid projection and semantic hash. `VisibilityFieldUploadTracker` proves unchanged facts do not request another upload. The strategy presenter additionally samples that grid through `StrategyView.World` into a low-resolution screen field, reusing the authoritative inverse isometric projection.

## 21. Fog visual model

Unknown terrain receives strong tint/opacity, explored terrain remains readable but dim, and visible terrain clears smoothly through cubic interpolation. Noise and temporal phase are subtle and bounded by `FogPresentationStyle`.

After visual review, composition was deliberately refined to `terrain -> fog -> crisp Profile world objects -> Machina HUD`. Fog is therefore a terrain visibility post-process and cannot soften or recolor vector asset resources. Unknown-object inclusion is still decided by application visibility before submission.

## 22. Open-source shader research

Godot and Bevy fog implementations were inspected as conceptual algorithmic ore. The shipped shader does not copy either implementation.

## 23. Shader provenance and licenses

Godot source at commit `34d06658a85845111a50db9e485ec4a0701d4298` is MIT. Bevy source at commit `7a21c21ecbce9ba28c970ffdf73063321b6bb636` is MIT OR Apache-2.0. Exact paths, links, and use classification are in [m19-shader-provenance.md](../research/m19-shader-provenance.md).

## 24. Visual TypeScript fog

`SemanticFog.v.ts` samples the application field, smooths the visibility value, chooses explored/unexplored opacity, applies tint, and adds bounded triangle-wave noise. It contains no game state or visibility update logic.

## 25. VD-MIR, HLSL, SPIR-V, Vulkan

The evidence tool and shader test compile the real path: Visual TypeScript -> VD-MIR -> generated HLSL -> DXC -> validated SPIR-V -> `VulkanOrderedQuadRenderer`. Both shader stages validate before native execution.

## 26. Visual TS language pressure

The only bounded language gap was scalar `Floor(f32)`, added to the GPU binder and HLSL emitter with the fog test covering it. The canonical semantic-fog material shape was added without a new compiler architecture.

## 27. Fog update and upload behavior

The reusable tracker records one `32 x 20 x 4 = 2,560` byte upload and suppresses an unchanged second projection. The real isometric presenter scans 576 byte-sized states without allocating and rebuilds its `320 x 133` projected texture only when visibility or camera transform changes.

## 28. Isometric coordinate parity

Terrain diamonds use `StrategyView.Screen`. Fog texels use `StrategyView.World`. Selection continues to use both methods, and the minimap consumes the same `VisibilityGrid`. No second isometric formula was introduced.

## 29. Fog and minimap

Both consume the same semantic grid. The minimap intentionally uses direct native cells rather than the atmospheric shader. Its native realization also preserves M18's unit dots, visible-resource dots, and clipped camera viewport outline.

## 30. Fog color and blend correctness

The field is linear UNorm semantic data; tint is explicit RGBA; straight-alpha blending is declared; and no sRGB double encoding is applied. The objective fixture records the convention in `fog-color-proof.json`.

## 31. Objective fog edge/readback proof

The deterministic disk fixture measured luma `1155` visible, `808` explored, and `453` unexplored, satisfying strict monotonic ordering. This is readback evidence, not screenshot-only qualification.

## 32. Final world composition order

The corrected runtime order is:

1. Profile diamond terrain/path/water.
2. Semantic fog field over terrain.
3. Visibility-filtered Profile trees/resources/buildings/units.
4. Native analytic/MSDF Machina HUD.
5. Optional development inspector overlay.

This differs intentionally from placing fog after every world vector: user review showed that such ordering blurred the characters and building details.

## 33. Strategy before and after

`strategy-native-before.png` is the M18 Skia/Avalonia frame. `strategy-native-after.png` is captured only by the real Mossward Vulkan launch smoke, with the same isometric camera, semantic session, `StrategyHudProfile`, Crimson Text and Space Mono sources, and canonical Profile assets. The synthetic fog fixture no longer writes the acceptance image. The real launch frame is also captured as `native-vulkan-launch-frame.png` in the M18 artifact directory, and `strategy-native-acceptance.json` records the capture contract.

## 34. Second-consumer proof

`tinyfarm-second-consumer.png` isolates the shared realization, while TinyFarm's full native proof validates the genuine product path and all existing checkpoints.

## 35. Cached rendering performance

On the measured NVIDIA GeForce RTX 3070 run, first realization took `20.357 ms` for nine texture uploads. The warm acceptance fixture took `23.580 ms`, allocated `4,105,096` managed bytes in the evidence process, emitted 862 quads and 676 draws, and wrote zero new descriptors. Cold per-asset compiler measurements record the intentional startup/memory cost of higher-resolution fields without marketing extrapolation.

## 36. Stress case

The 250-extra-worker scene took `33.368 ms`, allocated `6,263,712` bytes, emitted 3,362 quads and 2,926 draws, and performed no upload per instance. This establishes real M20 pressure for ordered repeated-draw instancing.

## 37. Fresh art extension

The extension path is source-only: add a closed flat-fill Profile composition, compile it, warm it, and use `ProfileNativeInstance`. The TinyFarm tree exercised exactly that path without changing the native compiler or renderer. A future windmill needs asset source, not Skia or renderer work.

## 38. Fresh fog-style extension

Explored warmth and transition softness are bounded `FogPresentationStyle`/shader parameters. They require no `VisibilityGrid` or simulation change.

## 39. Fresh second-consumer extension

The TinyFarm prop demonstrates the public integration dependency directly. Additional props reuse the same cache and presenter pass without importing strategy code.

## 40. Fresh shader extension

Noise modulation lives in Visual TS and material parameters. The existing compiler test rejects bypasses and validates generated SPIR-V; no handwritten shader binary or WGSL runtime authority is needed.

## 41. Owner-lane fixes

- Copeland GPU binder: bounded `Floor(f32)` and semantic-fog material shape.
- Aurelian shader emitter: `floor` emission.
- Aurelian Graphics: semantic-fog submission, validation, material binding, and pipeline option.
- Aurelian Strategy: immutable visibility projection and upload tracker.
- Strategy sample: native window/presenter and development inspector.
- TinyFarm: one real Profile consumer in the existing native pass.

## 42. Deferred systems

Gradients, strokes, arbitrary masks, general shadows, animation graphs, general terrain, PBR, lighting, volumetrics, post-processing graphs, and a traditional editor remain out of scope. Inspector mode is only an explicit app-owned overlay seam.

## 43. Exact M20 recommendation

Implement **painter-barrier-preserving instancing for repeated identical Profile draw plans**. The stress fixture shows 2,093 draws for 250 additional workers while cache uploads remain stable. M20 should reduce repeated geometry/material submission without reordering authored paints. Do not combine it with new paint modes.

## 44. Diff and validation summary

Validation completed locally:

- `dotnet build Aurelian.slnx -c Release -m:1`: passed; 14 pre-existing upstream OpenFont field warnings, zero errors.
- `dotnet test Aurelian.slnx -c Release -m:1 --no-build`: 793 passed, zero failed.
- Profile integration: 3 passed.
- Shader suite: 138 passed.
- Strategy suite: 9 passed, including deterministic M18 proof hash `8CCA7005F7D9222432D4A5FC838A28C0FFA85A70EDC8BC2657FCD16769E09542`.
- Graphics suite: 267 passed.
- Native Mossward launch: four frames, Vulkan swapchain/readback, inspector toggle, exit zero.
- TinyFarm full native proof: exit zero; all measured non-title changed frames remain under 16.67 ms.
- Evidence generator and Vulkan readbacks: exit zero.
- Scoped formatting for all M19 strategy, Profile integration, and evidence-tool files: passed. Project-wide strategy formatting still reports pre-existing compact formatting in M18 files that this milestone did not rewrite.

The tracked diff summary before adding new/untracked files was 39 files, 458 insertions, and 261 deletions; binary proof artifacts account for most modified-file noise. The milestone also adds the Profile integration/test projects, native strategy presenter/font, shader, evidence tool, TinyFarm asset, and four required documents.
