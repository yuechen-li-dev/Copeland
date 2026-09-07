# Author a strategy asset with existing Profile composition

The M18 toolkit is a working **proof pack**, not a new compiler or production GPU asset format. Source lives in `samples/Integrations/Aurelian.StrategyDemo/Assets`. `StrategyArt.ts` provides ordinary typed functions; each `*.profile.tsx` supplies a composition. The sample compiles the toolkit and each asset together through `ProfileTsxCompiler.CompileComposition`.

For a palette variant, the existing ranger source is a small example:

```typescript
const RangerPalette: StrategyPalette = Palette with {
    roof: { fill: "#657b4f" },
    gold: { fill: "#b9bd86" }
};

export default (Layers(Figure(RangerPalette, false)));
```

Use the actual `ranger.profile.tsx` and `watchtower.profile.tsx` as the executable syntax references. The watchtower composes `Shadow`, `Block`, `Roof`, and named window/banner paint without compiler or renderer changes. All Profile item names must remain unique within the resolved composition; template-generated names include their caller-provided prefix.

The existing primitive vocabulary includes closed polygons, ellipses, circles and tubes. Keep overlapping silhouettes in separate named painter layers. Do not use geometric `Add` as a paint-order substitute. Parameters are explicit; palette records use normal Copeland binding and `with`. Coordinates use +Y upward and the foot at `(0,0)`. The realization leaf performs the screen Y flip once.

To add an asset, place its readable Profile source beside the other assets, reuse the toolkit functions, then run:

```powershell
dotnet run --project samples/Integrations/Aurelian.StrategyDemo -c Release -- --proof
```

The content glob copies the file, `StrategyAssets` discovers it without a registry edit, and canonical compilation failures are fatal. Inspect `vector-asset-pack.png` and `vector-asset-hashes.json` under `artifacts/aurelian-rts-pearl-mining-m18`. The asset pack uses the same cached paths as world and portrait rendering. Adding discovery/preview alone does not create a building rule: application definitions still own cost, footprint, selection and behavior.

Qualified paint is flat fill with painter order. Gradients, strokes, animated detail, atlas/MSDF baking and object-card editing are not qualified by this adapter. Do not silently depend on unsupported paint. A future reusable realization layer must validate its supported paint profile and preserve canonical semantics rather than reconstructing shapes from SVG.

The first-party geometry was re-authored from general techniques observed in benchmark sources. No benchmark raster art is shipped. Native typography uses existing repository Crimson Text and Space Mono fixtures with their license files copied alongside them.
