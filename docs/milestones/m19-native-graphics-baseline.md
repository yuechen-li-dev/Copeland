# M19 native graphics baseline

The ownership law is:

`Copeland source -> Profile composition IR -> Aurelian native realization -> Vulkan`.

Skia is a compatibility/reference rasterizer. Avalonia is a compatibility widget host. Neither defines Profile semantics.

| Concern | Current owner | M18 path | Limitation | M19 action |
| --- | --- | --- | --- | --- |
| Strategy asset source | Copeland Profile TSX | `StrategyArt.ts` plus nine `.profile.tsx` files | None in authoring | Preserve source and composition hash |
| Profile composition | `Copeland.Profile` | Renderer-neutral layers, items, contours and paint | Flattened by the sample immediately before display | Keep the composition intact through native compilation |
| Strategy Profile realization | Sample-local `StrategyAssets` | Profile contours -> `SKPath` -> Skia | Skia was runtime authority | Replace the default path with `Aurelian.Profile.Graphics` -> Vulkan MSDF draws |
| Profile validation | Profile compiler and sample adapter | Mixed validation | No native support declaration or source-linked native failure | Validate closed/non-empty/finite contours, winding, transform and flat fill at the native boundary |
| Profile cache | None reusable | Rebuilt sample-local paths | Filename-oriented ownership; no reusable GPU lifetime | Key a bounded explicit cache by semantic composition hash |
| Strategy frame | Avalonia window and `StrategyRenderer` | Whole-frame Skia bitmap copied into Avalonia | CPU raster/readback was the normal runtime | Make the Silk/Vulkan presenter the default; retain `--compatibility-avalonia` |
| Machina HUD | `StrategyHudProfile` | Machina layout -> CPU panels/direct-outline raster | Correct semantics, non-native realization | Feed the same profile, layout, copy and source order into native analytic/MSDF adapters |
| Visibility semantics | `VisibilityGrid` | Application-owned explored/visible state | None | Preserve authority unchanged |
| Fog projection | Strategy sample | Per-tile hard recolor | Hard edge and coupled presentation | Project immutable grid facts to a GPU field only when facts/camera change |
| Fog shader | None | No soft native fog | Missing Visual TS/Vulkan path | Add bounded `SemanticFog.v.ts` and renderer-neutral `FogPresentationStyle` |
| Isometric alignment | `IsometricProjection` / `StrategyView` | Shared for world, pointer and minimap | Fog had no projected field | Project fog through `StrategyView.World`; do not duplicate the isometric formula |
| TinyFarm graphics | TinyFarm native presenter | Native sprites/Machina | No genuine Profile consumer | Add one Profile-authored tree through the same cache in its existing native UI pass |
| Preview | Sample proof only | Ad hoc | No source-linked native command | Add `Aurelian.NativeGraphicsM19 --preview <asset>` |
| Skia | Sample/runtime/reference | Primary strategy raster and useful parity oracle | Ownership was too central | Retain only compatibility, PNG evidence and parity/reference uses |
| Avalonia | Strategy desktop host | Primary strategy window | Hosted the primary pixels | Retain explicit compatibility host only |

The native path never exposes Vulkan handles to application or Profile code. Transforms stay per-instance; canonical geometry and paint are cached independently.

