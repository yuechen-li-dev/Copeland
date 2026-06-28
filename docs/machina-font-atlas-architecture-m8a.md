# Machina Font Atlas Architecture M8a

## Purpose

M8a is a docs-only architecture audit for Machina's long-term text rendering backend. The current deterministic bitmap text renderer remains a bootstrap/debug renderer; M8a defines the future MSDF font atlas contract without replacing rendering, changing `TextBlock`, adding native dependencies, or requiring Aurelian/Vulkan.

The target outcome is a clear boundary between authored text, async font atlas production, immutable atlas consumption, deterministic export preflight, and later renderer-specific MSDF drawing.

## Reference sources audited

Audited reference-only Dominatus submodule areas:

- `reference/dominatus/src/Dominatus.Assets.Toml`
- `reference/dominatus/src/Dominatus.SpriteForge`

Key files inspected:

- `Dominatus.Assets.Toml/AssetDiagnostic.cs`
- `Dominatus.Assets.Toml/AssetValidation.cs`
- `Dominatus.Assets.Toml/Validation.cs`
- `Dominatus.Assets.Toml/TomlAssetLoader.cs`
- `Dominatus.Assets.Toml/TomlAssetPackLoader.cs`
- `Dominatus.Assets.Toml/TomlAssetSourceMap.cs`
- `Dominatus.Assets.Toml/TomlAssetSourceMapBuilder.cs`
- `Dominatus.SpriteForge/SpriteForgeAtlas.cs`
- `Dominatus.SpriteForge/SpriteForgeTomlLoader.cs`
- `Dominatus.SpriteForge/SpriteForgeResolver.cs`
- `Dominatus.SpriteForge/SpriteForgeImageInspector.cs`
- `Dominatus.SpriteForge/SpriteForgePivots.cs`

## Dominatus.Assets.Toml findings

### Problem solved

`Dominatus.Assets.Toml` provides a reusable TOML-backed asset loading layer for projects whose runtime data is shaped as C# records. It lets humans and Codex edit inspectable TOML files while runtime code consumes typed records and diagnostics rather than raw TOML tables.

### C# record/data patterns

The project uses small immutable or init-only records:

- `AssetId` is a trimmed `readonly record struct` with constructor validation.
- `AssetRef<TAsset>` is a typed reference wrapper around `AssetId`.
- `AssetDiagnostic`, `AssetSourceSpan`, load options, load results, reload options, reload results, `AssetPack<TAsset>`, and `AssetPackEntry<TAsset>` are record-shaped data carriers.
- Runtime asset packs expose `IReadOnlyDictionary<AssetId, AssetPackEntry<TAsset>>` and narrow `TryGet` helpers.

This is a good fit for Machina font records: stable IDs, explicit source paths, content hashes, immutable externally visible data, and load results that preserve diagnostics.

### TOML asset model

The loader parses TOML with Tomlyn, builds a source map from syntax nodes, binds the document to a typed C# model, runs optional per-asset validation, and returns a `TomlAssetLoadResult<T>` containing:

- bound value,
- diagnostics,
- source map,
- success predicate.

The pack loader scans files deterministically by ordinal path order, computes SHA-256 hashes for TOML files, reads each document, extracts an ID through a caller-provided function, detects duplicate IDs, and returns an `AssetPack<TAsset>` plus diagnostics.

### Schema/version

There is no universal schema/version field in `Assets.Toml` itself. Schema is intentionally asset-specific and expressed by the bound document type plus validators. Machina should therefore put `format = 1` and `kind = "machina-font-atlas"` in its own `[atlas]` table rather than expecting the generic loader to enforce global schema policy.

### Validation

Validation is explicit and layered:

- `IAssetValidator<T>` validates a single bound asset with `AssetValidationContext`.
- `IAssetPackValidator<T>` validates cross-asset constraints after loading a pack.
- `AssetValidation` creates diagnostics for errors, warnings, info, and required fields.
- Validators can attach TOML key paths and source spans using `AssetValidationContext.GetSpan`.

### Diagnostics

Diagnostics are first-class records with severity, code, message, source path, line, column, span, and key path. Formatters produce human-readable output. This is exactly the style Machina should use for font atlas TOML: bad glyph coordinates, missing page images, stale source hashes, unsupported schema versions, and duplicate glyph entries should be precise diagnostics, not exceptions on the normal path.

### Loading

Loading is robust and non-throwing for expected asset problems. Parse, bind, validation, file IO, duplicate IDs, and pack validation errors are converted into diagnostics. Options include recursive directory loading, continue-on-error, and `RequireNoDiagnostics` for strict modes.

### Writing

`Dominatus.Assets.Toml` does not provide a generic TOML writer. It is primarily a parse/bind/validate/load/reload layer. Machina should design explicit font atlas writing because export needs stable ordering, deterministic formatting, optional non-deterministic metadata policy, and paired PNG page writes.

### Round-tripping

The source map enables diagnostics back to original TOML locations, but the project does not preserve comments or formatting for round-trip edits. Machina should not promise comment-preserving round-trips in early font-atlas milestones. It should support deterministic export of canonical TOML from runtime snapshot state.

### What Machina should reuse directly later

If package boundaries allow, Machina should reuse or depend on the Dominatus package pattern for:

- typed TOML loading through record-shaped document models,
- `AssetDiagnostic`-style diagnostics,
- source-map-backed key-path diagnostics,
- deterministic pack loading order,
- content hash capture,
- single-asset and pack-level validators.

### What Machina should copy only in spirit

Machina should copy the architecture, not the source:

- font-specific schema/version validation,
- deterministic writer/exporter,
- PNG page validation,
- glyph-key and page cross-validation,
- snapshot import/export policy,
- worker cache invalidation rules.

## Dominatus.SpriteForge findings

### What it models

`Dominatus.SpriteForge` models a sprite atlas described by a PNG image plus editable TOML metadata. Its runtime records include an atlas, grids, absolute frames, sprites, animations, animation frame references, and resolved frames.

### TOML format shape

SpriteForge uses a single TOML document with sections roughly shaped as:

```toml
[atlas]
image = "sprites.png"
width = 512
height = 512

[grids.hero]
origin_x = 0
origin_y = 0
columns = 8
rows = 4
cell_width = 32
cell_height = 32
default_pivot = "bottom-center"

[frames.logo]
x = 128
y = 64
width = 96
height = 32
pivot = "center"

[sprites.hero]
kind = "character"
display_name = "Hero"
grid = "hero"
row = 0
col = 0
pivot = "bottom-center"

[sprites.hero.animations.walk]
row = 1
frames = [0, 1, 2, 3]
fps = 12
loop = true
```

Internally, SpriteForge binds this into private TOML document records (`SpriteForgeAtlasTomlDocument`, `SpriteForgeAtlasSection`, grid/sprite/animation/frame TOML records), validates them, then transforms them into public runtime records (`SpriteForgeAtlas`, `SpriteForgeGrid`, `SpriteForgeSprite`, `SpriteForgeAnimation`, `SpriteForgeFrame`, and resolved frame records).

### Pairing image/PNG data with TOML metadata

`[atlas].image` names the image path. The loader resolves relative image paths against the TOML file directory and validates that the image exists when `ValidateImageExists` is enabled. `SpriteForgeImageInspector` can inspect PNG dimensions, and atlas width/height are validated as positive values.

### Atlas/page, sprite keys, rects, pivots/origins, generation metadata

- Atlas/page: SpriteForge is effectively single-image-atlas oriented, with `[atlas]` carrying image, width, and height.
- Sprite names/keys: dictionaries under `[sprites.<id>]`, `[grids.<id>]`, and `[frames.<id>]` use string IDs validated to contain letters, numbers, `.`, `_`, or `-`.
- Rects: absolute frames use `x`, `y`, `width`, and `height`; grids derive rects from origin, rows, columns, cell size, and gaps.
- Pivots/origins: grids have `origin_x`, `origin_y`, `default_pivot`; frames and sprites can specify pivots, offsets, and scale. Pivots are normalized through a known supported set.
- Generation metadata: there is no broad generation/provenance metadata model. SpriteForge is primarily editable metadata over an existing PNG.

### What maps cleanly to font atlas design

- TOML metadata beside binary image data.
- Public runtime records separate from private TOML binding records.
- Relative image path resolution from the metadata file.
- Precise validation of image existence and atlas/page dimensions.
- Coordinate bounds checks for rects against page size.
- Stable IDs and dictionaries for human-editable entries.
- Optional debug/display fields that do not drive runtime identity.

### What does not map

- Sprite grids do not map to font glyphs because glyphs are irregularly packed and derive from outlines/metrics.
- Sprite pivots/animation frame refs do not map directly; font glyphs need bearings, advances, baseline metrics, codepoints, and face/style/size keys.
- Single-image atlas assumptions must become multi-page support.
- SpriteForge's editable source PNG workflow differs from runtime-generated font atlas pages.
- Sprite metadata uses friendly sprite IDs; font glyph identity must be a typed glyph key, not a user-chosen name.

## Lessons for Machina font atlases

Machina should inherit the SpriteForge and Assets.Toml philosophy:

- binary/art asset plus editable TOML metadata,
- typed C# records at runtime,
- diagnostics with source paths and key paths,
- deterministic canonical export,
- human/Codex-inspectable asset state,
- source metadata that can be validated without a GPU runtime.

Machina should not copy Dominatus source into Machina and should not turn the reference submodule into a build dependency during M8a.

## Architecture doctrine

```text
Standard.Text:
  authoring, parsing, layout, run boxes

Machina.Fonts:
  font faces, glyph keys, glyph metrics, atlas records,
  async generation, TOML/PNG export/import, snapshots

Renderer layer:
  consumes atlas snapshots and text run boxes

Aurelian/Vulkan later:
  GPU MSDF shader consumer

Current raster renderer:
  may eventually get CPU reference MSDF rendering for export/debug,
  but M8a does not implement that.
```

Core rules:

- Font atlas generation is async and internally mutable.
- Font atlas consumption is snapshot-based and immutable externally.
- Runtime can show fallback/pending glyphs.
- Export/preflight can await glyph readiness.
- Font atlas metadata is TOML-serializable.
- PNG pages hold heavy generated texture data.
- Text layout does not generate glyphs.
- Renderer does not own authoring data.
- Atlas worker does not mutate layout mid-frame.

## Proposed project/namespace shape

Future projects, not created in M8a:

```text
src/Machina.Fonts/
tests/Machina.Fonts.Tests/
```

Likely namespaces:

- `Machina.Fonts`: IDs, glyph keys, metrics, atlas entries, pages, snapshots, service interfaces.
- `Machina.Fonts.Authoring`: `FontFaceManifest`, `FontSource`, `FontLicenseInfo`.
- `Machina.Fonts.Generation`: worker requests, MSDF settings, generation settings, packing contracts.
- `Machina.Fonts.Toml`: TOML document models, loader/writer, validators, export/import.
- `Machina.Fonts.Testing`: fake generator, fake atlas service, deterministic fixture data.

## Core record model

Proposed initial records and enums:

```csharp
public readonly record struct FontFaceId(string Value);

public enum MachinaFontWeight
{
    Regular = 400,
    Bold = 700
}

public enum MachinaFontSlant
{
    Upright = 0,
    Italic = 1,
    Oblique = 2
}

public readonly record struct GlyphKey(
    FontFaceId Face,
    int Codepoint,
    double EmSize,
    MachinaFontWeight Weight,
    MachinaFontSlant Slant);

public sealed record GlyphMetrics(
    double Advance,
    double BearingX,
    double BearingY,
    double Width,
    double Height);

public sealed record GlyphAtlasEntry(
    GlyphKey Key,
    int PageIndex,
    int X,
    int Y,
    int Width,
    int Height,
    double U0,
    double V0,
    double U1,
    double V1,
    GlyphMetrics Metrics);

public sealed record FontAtlasPage(
    int Index,
    string ImagePath,
    int Width,
    int Height,
    string? ContentHash);

public sealed record FontAtlasSnapshot(
    long Version,
    IReadOnlyList<FontAtlasPage> Pages,
    IReadOnlyDictionary<GlyphKey, GlyphAtlasEntry> Glyphs);
```

Additional records:

- `FontFaceManifest`: stable face ID, family, style, default weight/slant, source, license, fallback policy.
- `FontSource`: source path, source hash, source kind (`ttf`, `otf`, `collection`, future system face), face index.
- `FontLicenseInfo`: SPDX identifier when known, license text path, attribution, redistribution flags.
- `MsdfGenerationSettings`: pixel range, scale, edge coloring mode, miter limit, generator version.
- `FontAtlasGenerationSettings`: page width/height, padding, max pages, bucket policy, glyph ordering, cache root.
- `FontAtlasDocument`: TOML-serializable document containing atlas/font/metrics/msdf/page/glyph sections.

Glyph keys probably include em size, weight, and slant in early runtime contracts. To avoid infinite size buckets, `FontAtlasGenerationSettings` should define an explicit quantization policy: for example canonical buckets (`12`, `14`, `16`, `20`, `24`, `32`, `48`) or controlled rounding to device-independent text sizes used by layout. Arbitrary per-transform scale should remain a renderer concern, not a new atlas key.

## Async worker contract

Proposed service contract:

```csharp
public interface IFontAtlasService
{
    FontAtlasSnapshot Snapshot { get; }

    GlyphResolution Resolve(GlyphKey key);

    ValueTask QueueAsync(
        IReadOnlyList<GlyphKey> keys,
        CancellationToken cancellationToken = default);
}

public abstract record GlyphResolution;

public sealed record GlyphReady(GlyphAtlasEntry Entry) : GlyphResolution;

public sealed record GlyphPending(GlyphMetrics? EstimatedMetrics) : GlyphResolution;

public sealed record GlyphMissing(string Reason) : GlyphResolution;
```

Worker lifecycle:

```text
Channel<GlyphRequestBatch>
  -> dedupe keys
  -> load outline
  -> generate MSDF bitmap
  -> pack into atlas page
  -> publish immutable snapshot version
  -> notify renderer/app of version increment
```

### Batching

Renderers and exporters queue batches of glyph keys. The service deduplicates keys within each batch and across queued/in-flight work. Large text documents should be chunked internally so one huge request cannot starve interactive glyphs forever.

### Deduplication

The worker tracks at least four states per key:

- ready in current snapshot,
- queued,
- generating,
- permanently missing/failed until invalidated.

Duplicate requests should be cheap and should not enqueue duplicate outline or MSDF work.

### Cancellation

Cancellation of `QueueAsync` cancels the caller's enqueue/wait operation, not necessarily already accepted background generation. Export-specific wait APIs in later milestones should have explicit timeout/fail policy. Service disposal should cancel the worker loop and flush no further snapshots.

### Error handling

Glyph failures become `GlyphMissing` with a reason and diagnostics. Missing source font, unsupported outline, missing codepoint, pack overflow, image write failure, and generator exceptions should be diagnosable. A failed glyph must not poison unrelated glyph generation.

### Versioning and snapshot publication

The worker may mutate internal atlas state, packers, and page buffers, but public consumption sees only immutable `FontAtlasSnapshot` values. A new snapshot version is published after a coherent batch of entries/pages is ready. Renderers read a snapshot once per frame and do not observe mid-frame mutation.

### Cache invalidation

Cache identity should include source font hash, face index, MSDF settings, atlas generation settings, generator implementation/version, and glyph key bucket policy. Any change invalidates affected glyphs and pages. Imported TOML with stale hashes should load with diagnostics and a policy choice: reject, warn, or regenerate.

### Pending fallback behavior

Runtime rendering can draw fallback boxes, a replacement glyph, or skip pending glyphs while using estimated metrics when available. Layout must not be retroactively mutated by the atlas worker. If final metrics differ materially from estimates, a later layout invalidation belongs to the app/render pipeline, not to the worker mutating layout mid-frame.

## Runtime mode

```text
render frame:
  renderer sees text run boxes
  creates glyph keys
  resolves glyphs against snapshot
  queues missing glyphs
  draws ready glyphs
  draws fallback for pending/missing glyphs
  worker publishes later snapshot
```

Runtime mode prioritizes responsiveness. It accepts incomplete glyph coverage on early frames and relies on snapshot version notifications to schedule redraws. The renderer consumes text run boxes plus atlas snapshots; it does not parse authoring text or generate glyphs synchronously.

## Export/preflight mode

```text
export:
  traverse document/render commands/text runs
  collect all glyph keys
  queue missing glyphs
  await readiness or timeout/fail policy
  render final artifact
```

Gallery export should eventually use export mode so deterministic artifacts are complete before rendering. This mode needs no GUI and no Vulkan runtime. It can traverse the Standard.Text layout/render bridge, collect glyph keys, queue the atlas service, await readiness under a deterministic timeout/fail policy, then render with a CPU reference path or later renderer-specific export path.

Suggested preflight result:

```csharp
public sealed record FontAtlasPreflightResult(
    bool Success,
    FontAtlasSnapshot Snapshot,
    IReadOnlyList<GlyphKey> ReadyGlyphs,
    IReadOnlyList<GlyphKey> PendingGlyphs,
    IReadOnlyList<GlyphResolution> Failures);
```

## Font atlas TOML schema proposal

File naming:

```text
<name>.font-atlas.toml
<name>.page0.png
<name>.page1.png
```

Initial schema sketch:

```toml
[atlas]
format = 1
kind = "machina-font-atlas"
name = "machina-default"
distance_field = "msdf"
version = 1

[font]
face = "machina-default-sans"
family = "Inter"
style = "Regular"
source = "assets/fonts/Inter-Regular.ttf"
source_hash = "sha256-..."
license = "OFL-1.1"

[metrics]
em_size = 32
units_per_em = 2048
ascent = 26
descent = -7
line_gap = 5
line_height = 38

[msdf]
range = 4.0
scale = 1.0
edge_coloring = "simple"
miter_limit = 1.0

[[page]]
index = 0
image = "machina-default.page0.png"
width = 1024
height = 1024
content_hash = "sha256-..."

[[glyph]]
codepoint = 65
char = "A"
em_size = 32
weight = 400
slant = "upright"
page = 0
x = 12
y = 16
width = 40
height = 44
advance = 36
bearing_x = 1
bearing_y = 34
u0 = 0.01171875
v0 = 0.015625
u1 = 0.05078125
v1 = 0.05859375
```

### Schema policy notes

- `char` should be optional/debug-only. Runtime identity must use `codepoint` and face/style/size fields because non-printable codepoints and combining marks may not have useful display characters.
- Non-printable codepoints should be represented by integer `codepoint` and optionally a debug `name` such as `U+000A` or `LINE FEED` if a later exporter wants it.
- Glyph keys should include face, codepoint, em size bucket, weight, and slant. Variable font axes are deferred and should not be silently squeezed into weight/slant.
- Infinite size buckets should be avoided by generation settings that define allowed buckets or deterministic rounding.
- Page size defaults should start conservative, for example `1024x1024` with padding. Page dimensions belong in the TOML and must be validated against PNG dimensions.
- Atlas growth should append pages rather than rewriting page indices when possible. Repacking may be a separate explicit optimization because it invalidates many UVs and diffs.
- Source font license and hash should be included to make generated artifacts auditable and cacheable.
- Generated timestamps can harm deterministic diffs. Prefer no timestamp in canonical TOML, or place non-deterministic metadata behind an optional `[export]` section omitted by deterministic export.
- `content_hash` for pages should hash PNG bytes. `source_hash` should hash the source font bytes.

## Runtime-generated atlas export

Export from a live `FontAtlasSnapshot` should write:

1. PNG page files for each `FontAtlasPage` in ascending index order.
2. A canonical TOML document with stable section order: `[atlas]`, `[font]`, `[metrics]`, `[msdf]`, `[[page]]`, then `[[glyph]]` sorted by face, em size, weight, slant, codepoint, page, x, y.
3. Content hashes after writing PNG bytes.
4. Diagnostics instead of partial silent success when a page image cannot be written, dimensions mismatch, or a glyph references a missing page.

Early export does not need comment preservation. Its value is deterministic, inspectable state capture.

## MSDF generation dependency/options audit

This is a preliminary local architecture audit, not a final package selection. Package freshness, license details, and platform support should be verified in M8e before adoption.

| Option | Pros | Cons | Dependency risk | Determinism risk | Platform risk | Complexity |
| --- | --- | --- | --- | --- | --- | --- |
| Bind `msdfgen` | Proven concept for MSDF quality; likely closest to expected output; can align with GPU shader examples later. | Native binding and build/distribution complexity; licensing and exact API must be verified; harder CI setup. | High until binding/package strategy is chosen. | Medium: native versions and floating-point behavior must be pinned. | Medium/high across Windows/Linux/macOS and CPU architectures. | Medium for binding, lower for algorithm design. |
| Minimal C# MSDF generator | No native dependency; easier debugging and deterministic CI; can be tailored to Machina. | Significant algorithm work; risk of lower quality around corners/overlaps; easy to under-scope. | Low external dependency risk. | Medium/low if math and tests are pinned. | Low if pure managed code. | High. |
| FreeType or outline extraction bindings | Mature outline extraction and font coverage. | Native dependency; shaping is still separate; binding health/license must be verified. | High until selected. | Medium: version differences can affect outlines/hinting. | Medium/high. | Medium. |
| .NET font library for outline extraction | Managed integration may avoid native install; easier test setup if capable. | Actual outline API quality, licensing, and maintenance must be verified; may not expose all needed contours/metrics. | Unknown until web/package audit. | Unknown/medium. | Low/medium depending on dependencies. | Medium if outline access is good; high if not. |
| Pre-generation tooling before runtime worker | Can unblock TOML/PNG schema and renderer contracts before real runtime generation. | Does not satisfy final runtime worker goal; can become a dead-end if treated as production. | Low if kept as fake/test tooling. | Low for deterministic fixtures. | Low. | Low/medium. |

M8e should perform a current package/license/platform audit before choosing an implementation path. M8a intentionally does not invent package facts.

## Build/submodule boundaries

Boundary doctrine:

- `reference/dominatus` is a reference-only Git submodule.
- No active project should reference `reference/dominatus` projects.
- `Copeland.slnx` should not include submodule projects.
- Active Machina build should continue using NuGet `Dominatus.Core` and `Dominatus.OptFlow` package references.
- M8a adds docs only and no ProjectReferences into the submodule.

Boundary checks run for M8a confirmed no active ProjectReference into `reference/dominatus`, no submodule projects in `Copeland.slnx`, and NuGet package references remain in active Machina Dominatus projects.

## M8b+ staged plan

- M8b: create `Machina.Fonts` records/interfaces plus fake worker and fake atlas generator. No MSDF yet.
- M8c: implement `.font-atlas.toml` loader/writer using Assets.Toml-inspired typed document, validators, diagnostics, and deterministic canonical writer.
- M8d: implement atlas packing/cache/snapshot versioning with fake generated glyph bitmaps to prove async worker semantics.
- M8e: perform current dependency/license/platform audit and build a CPU MSDF generation proof with selected outline extraction path.
- M8f: add CPU reference MSDF rendering for gallery export/debug, still renderer-independent and deterministic.
- M8g: connect gallery export preflight/await glyph readiness so exported artifacts contain complete glyphs without GUI/Vulkan.
- M8h: add Aurelian/Vulkan MSDF shader consumer after renderer contracts are ready.

## Deferred issues

- Font fallback chains and missing-glyph substitution policy.
- Complex shaping, ligatures, grapheme clusters, bidi text, and variable font axes.
- Exact glyph size bucket policy.
- Hinting policy and whether generated metrics should be hinted, unhinted, or layout-derived.
- MSDF shader parameters and gamma/correction policy.
- Page eviction/compaction and long-running cache management.
- Comment-preserving TOML round-trip editing.
- Current package/license verification for MSDF and outline extraction dependencies.

## M8b implementation note

M8b creates the standalone `Machina.Fonts` and `Machina.Fonts.Tests` projects proposed by this audit. The implementation remains fake-generation only: no MSDF, no real font loading, no TOML, no PNG writing, no renderer integration, no native dependencies, and no active build dependency on `reference/dominatus`. See `docs/machina-fonts-m8b.md`.

## M8c contract update

M8c implements the inspectable atlas metadata layer proposed by the audit: a binary/art asset can be paired with editable `.font-atlas.toml` metadata, typed document records, deterministic export, and precise diagnostics. It deliberately avoids Dominatus submodule ProjectReferences and does not implement real MSDF, font parsing, PNG output, or renderer integration.
