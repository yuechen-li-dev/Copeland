# Copeland programmable object assets

## Authority and discovery

`*.obj.ts` is the preferred authored source for the bounded SpriteForge panel domain. The suffix is a tooling convention selected explicitly by `tscl asset build <manifest.tsx>`; ordinary compilation and the existing TSON loader do not infer a new language profile from the filename.

The build finds registered objects through root `manifest.tsx`, resolves object dependencies, compiles each source with the ordinary Copeland binder and static evaluator, then decodes exactly one `const $asset = static ...` result into immutable semantic asset IR.

## Bounded model

The IR contains one texture, a columnar region catalog, and programmable panels. A panel has four fixed corner regions, four ordered edge programs, one of `AnalyticFill`, `StretchRegion`, or `TileRegion`, content padding, and border scale. An edge segment has stable ID, region ID, `Fixed` or minimum-weighted `Flex` allocation, and independent `Stretch`, `Tile`, or `Crop` sampling.

Tables are columnar in Copeland source. SUNKILL declares `record table AssetRegions` with `id`, `x`, `y`, `width`, and `height` columns. This avoids row-oriented TS/JS object arrays for relational asset data. Edge segments remain arrays because their order is executable allocation structure rather than a table.

Functions and ordinary expressions may construct the value, and `static` evaluation erases that computation before runtime. Aurelian does not host a Copeland evaluator.

## Outputs

For `foo.obj.ts`, the asset command can generate:

- `foo.obj.toml`: deterministic inspection/interchange projection, classified `generated-obj-ts`.
- `foo.obj.json`: deterministic semantic JSON projection.
- `foo.runtime.toml`: runtime projection, classified `runtime-toml`.
- `foo.audit.json`: source hash, ownership, and lowering evidence.

Generated outputs carry a do-not-edit notice. Authority is one-way: source to semantic IR to projections. Reconstructing source from TOML is neither promised nor desirable.

## Diagnostics

Compilation reports stable source-located diagnostics for the required root and schema, missing or invalid texture data, duplicate IDs, out-of-bounds regions, unknown region references, empty edges, invalid allocation/sampling/center policies, impossible minimum panel sizes, and malformed values. Manifest diagnostics cover missing files, duplicate registrations, unknown dependencies, and dependency cycles.

## Compatibility and runtime path

Legacy SpriteForge TOML remains supported and defaults to `LegacyAuthoredToml`. Generated inspection TOML, runtime TOML, and legacy authored TOML are explicitly classified. The generated file also contains a nine-slice compatibility projection for M14 consumers.

The production path is `*.obj.ts` → Copeland semantic IR → runtime TOML → SpriteForge validation/model → Machina programmable panel → generic span allocation → sampling quads → Aurelian native ordered quads. SUNKILL selects content and style only.

Nine-slice is a high-level prebuilt over explicit edge allocation. Each side is the 3-slice case—fixed corners around one flexible segment—and the compatibility lowerer delegates to the programmable-panel lowerer. There is no second movement/layout engine hiding behind the old API.
