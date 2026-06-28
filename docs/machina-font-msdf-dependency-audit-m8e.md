# Machina Font MSDF Dependency Audit M8e

## Purpose

M8e is a docs-only dependency audit for the next real Machina font-atlas step: outline extraction plus CPU MSDF generation.

It does not add package references, does not implement real glyph generation, and does not change renderer behavior. Its job is to choose a technically credible path with current license, packaging, and API evidence.

Audit date: 2026-06-27.

## Candidate summary

Primary candidates audited:

- `MSDFGen-Sharp`, especially `MSDF-Sharp.Core`, for distance-field generation.
- `LayoutFarm/Typography`, especially `Typography.OpenFont`, for font loading, glyph lookup, metrics, and outline extraction.

Comparison candidates:

- `SixLabors.Fonts` for managed font loading and shaping, mainly as a policy/licensing comparison.
- `FreeType` bindings such as `SharpFont` as a fallback native path.

Short conclusion:

- `MSDF-Sharp.Core` is a credible managed MSDF generator package for Machina's worker pipeline.
- `Typography.OpenFont` is a credible managed outline source API for Machina's worker pipeline, but its packaging story is weak compared with its technical capability.
- `SixLabors.Fonts` is technically strong but license-policy-heavy for this layer.
- Native FreeType remains the fallback if managed outline extraction proves too brittle on real fixture fonts.

## MSDFGen-Sharp / Msdfgen.Core audit

### Repository and license

Repository inspected:

- [ExtraBinoss/MSDFGen-Sharp](https://github.com/ExtraBinoss/MSDFGen-Sharp)

Current public signals:

- Repository README describes the project as a C# port of `msdfgen` and `msdf-atlas-gen`.
- The repository contains separate `Msdfgen.Core`, `MsdfAtlasGen`, `Msdfgen.Extensions`, and CLI projects.
- The repository README states MIT licensing for the project and cites MIT licensing for upstream `msdfgen` and `msdf-atlas-gen`.
- The inspected `Msdfgen.Core.csproj` declares `PackageLicenseExpression` `MIT`.
- The latest visible commit in the cloned repo was `1a3ab3d` on 2026-01-08.

Recommendation-level reading:

- `MSDF-Sharp.Core` appears to have a real package boundary separate from extensions.
- Maintenance is visible but still modest in ecosystem terms; the GitHub repo showed low star/fork counts during this audit, so Machina should treat it as promising but not yet ecosystem-proven.

### Package availability

Public NuGet packages found:

- `MSDF-Sharp`
- `MSDF-Sharp.Core`
- `MSDF-Sharp.Extensions`

Current versions visible from NuGet:

- `MSDF-Sharp.Core`: `1.0.2`
- `MSDF-Sharp.Extensions`: `1.0.2`
- `MSDF-Sharp`: `1.0.2`

Packaging details inspected from the `.nupkg` metadata:

- `MSDF-Sharp.Core` ships `lib/net9.0/Msdfgen.Core.dll`.
- `MSDF-Sharp.Core` has no package dependencies.
- `MSDF-Sharp.Extensions` ships `lib/net9.0/Msdfgen.Extensions.dll`.
- `MSDF-Sharp.Extensions` depends on `MSDF-Sharp.Core`, `FreeTypeSharp`, and `SixLabors.ImageSharp`.

.NET 10 fit:

- The visible TFM is `net9.0`.
- M8e did not install the package into a `net10.0` project, but by normal forward-compatibility rules a `net9.0` library should be consumable from a `net10.0` app unless package-specific runtime assumptions break.
- This is an inference from the published TFM, not a package-install proof.

### API surface

The core API is low-level and shape-first, which is a good fit for Machina's planned adapter seam.

Key primitives inspected in `Msdfgen.Core`:

- `Shape`
- `Contour`
- `EdgeSegment`
- `LinearSegment`
- `QuadraticSegment`
- `CubicSegment`
- `Projection`
- `Range`
- `Bitmap<T>`
- `GeneratorConfig`
- `MSDFGeneratorConfig`
- `ErrorCorrectionConfig`
- `EdgeColoring`
- `MsdfGenerator`

Shape construction model:

- A caller creates a `Shape`.
- The caller adds one or more `Contour` instances.
- Each contour holds ordered `EdgeSegment` instances.
- Segments can be created as line, quadratic, or cubic segments.
- `Shape.Validate()`, `Shape.OrientContours()`, and `Shape.Normalize()` exist as preprocessing helpers.
- `EdgeColoring.EdgeColoringSimple(...)` and `EdgeColoring.EdgeColoringInkTrap(...)` exist for channel coloring before MSDF generation.

Generation entry points inspected:

- `MsdfGenerator.GenerateSDF(...)`
- `MsdfGenerator.GeneratePSDF(...)`
- `MsdfGenerator.GenerateMSDF(...)`
- `MsdfGenerator.GenerateMTSDF(...)`

Projection and range:

- Generation can be driven by a `Projection` plus `Range`, or by an `SDFTransformation`.
- This maps cleanly onto Machina-owned generation settings such as pixel range, output size, padding, and scale.

Output bitmap format:

- Output is written into `Bitmap<float>`.
- The bitmap stores a flat `T[] Pixels` array with caller-chosen channel count.
- Channel conventions are:
  - SDF: effectively 1 channel, optionally mirrored into RGB.
  - MSDF: RGB float channels.
  - MTSDF: RGBA float channels.

This is favorable for Machina because the output can remain renderer-agnostic until a later packing/export step converts it into bytes or page images.

### Suitability for Machina

Good fit points:

- Pure managed C# core package.
- No renderer dependency in `MSDF-Sharp.Core`.
- No Vulkan or GPU dependency.
- Caller-owned `Shape` input fits an async worker model.
- Caller-owned `Bitmap<float>` output fits a generated-atlas page pipeline.
- Shape construction is explicit, which makes a Machina adapter boundary straightforward.

Worker suitability:

- The API is synchronous, but nothing in the core requires UI thread affinity.
- It should run naturally inside the planned channel/worker model from M8a/M8b.
- Output is plain memory, so page packing and TOML export remain separate concerns.

Determinism expectations:

- Determinism should be reasonable if Machina pins:
  - package version,
  - edge-coloring algorithm,
  - edge-coloring seed,
  - overlap support mode,
  - error correction mode,
  - projection/range math,
  - output dimensions and padding policy.
- Exact cross-runtime or cross-CPU float-bit determinism is not proven by M8e.

### Risks

- Packaging risk: the clean package split exists, but the package is young and not widely adopted yet.
- API stability risk: the package is still early enough that public API churn is plausible.
- Performance risk: pure C# generation may be slower than native `msdfgen` on very large batch generation.
- Determinism risk: floating-point output can vary subtly across runtimes or architectures if Machina later depends on exact byte identity before quantization.
- Feature-gap risk: `MSDF-Sharp.Core` does not itself solve font loading or outline extraction.
- Policy risk: `MSDF-Sharp.Extensions` brings in `SixLabors.ImageSharp`, which Machina should avoid for now if the goal is to keep the font layer free of split-license image dependencies.

## LayoutFarm/Typography / Typography.OpenFont audit

### Repository and license

Repository inspected:

- [LayoutFarm/Typography](https://github.com/LayoutFarm/Typography)

Current public signals:

- The repo README describes `Typography.OpenFont` as the core module and explicitly says it has no visual/graphics rendering dependency.
- The repo license file says the whole project is MIT, with a caution to check per-file headers if copying source directly.
- The project license file also lists multiple permissive upstream sources used in the repo.
- The latest visible commit in the cloned repo was `5877180` on 2023-09-17.

License caveat:

- The project-level statement is permissive and MIT-oriented.
- Some inspected source files in `Typography.OpenFont` use Apache-2.0 or MIT headers, and some reference FreeType-license-derived work.
- For Machina, this still reads as permissive and acceptable in spirit, but it is less tidy than a single-package SPDX-only story.

Recommendation-level reading:

- `Typography.OpenFont` appears permissive enough for consideration.
- If Machina later vendors source or forks packaging, the per-file header mix should be preserved and re-audited at that time.

### Package availability

Important finding:

- The upstream repo is not an SDK-style packaged library in the audited tree.
- `Typography.OpenFont` appears as a shared-project/source-project layout (`.shproj` / `.projitems`), not a first-party SDK-style `.csproj`.
- M8e did not find an official first-party `Typography.OpenFont` NuGet package from `LayoutFarm`.

Public NuGet packages found instead:

- `WycliffeAssociates.Typography.OpenFont` `1.0.0`
- `Typography.OpenFont.NetCore` `1.0.0`
- `Syntellect.Typography.OpenFont.Net6` `1.0.0`

Packaging details inspected:

- `WycliffeAssociates.Typography.OpenFont` ships `lib/netstandard2.0`.
- `Typography.OpenFont.NetCore` ships `lib/netcoreapp2.1`.
- These packages are downstream repackages or forks, not clearly maintained by the upstream repo owner.

.NET 10 fit:

- `netstandard2.0` is broadly usable from `net10.0`.
- `netcoreapp2.1` is a much weaker packaging story for a modern Machina dependency choice.
- The technical source model looks viable; the packaging story is the biggest weakness in this candidate.

### Font loading

Supported formats advertised and source-backed:

- `.ttf`
- `.otf`
- `.ttc`
- `.otc`
- `.woff`
- `.woff2`

Evidence:

- `Typography.OpenFont/README.MD` states support for Open Font Format (`.ttf`, `.otf`, `.ttc`, `.otc`) and Web Open Font Format (`.woff`, `.woff2`).
- `OpenFontReader` contains explicit detection paths for TTC, WOFF, and WOFF2.
- The source tree contains `Tables.CFF`, `Tables.TrueType`, and `WebFont` folders.

How a caller loads a font:

- Create `var reader = new OpenFontReader();`
- Call `reader.Read(stream, streamStartOffset, readFlags)` to get a `Typeface`.
- For collection files, `ReadPreview(stream)` can inspect members and offsets first.

This is a low-level, file/stream-first API that fits Machina well.

### Glyph lookup and metrics

Glyph lookup:

- `Typeface.GetGlyphIndex(int codepoint)`
- `Typeface.GetGlyphIndex(int codepoint, int nextCodepoint, out bool skipNextCodepoint)`
- `Typeface.GetGlyph(ushort glyphIndex)`

Metrics and face-level values:

- `Typeface.UnitsPerEm`
- `Typeface.Ascender`
- `Typeface.Descender`
- `Typeface.LineGap`
- `Typeface.Bounds`
- `Typeface.GetAdvanceWidthFromGlyphIndex(ushort glyphIndex)`
- `Typeface.GetLeftSideBearing(ushort glyphIndex)`

Glyph-level values:

- `Glyph.Bounds`
- `Glyph.MinX`, `Glyph.MaxX`, `Glyph.MinY`, `Glyph.MaxY`
- `Glyph.OriginalAdvanceWidth`

Mapping note:

- Machina can map codepoint to glyph index with `Typeface.GetGlyphIndex(...)`.
- Machina can fetch glyph bounds and advances directly.
- Right/top bearings are not exposed as a single record; Machina would compute those from bounds plus advance and font metrics.

### Outline extraction

This is the strongest technical part of the Typography candidate.

TrueType outlines:

- `Glyph` exposes `GlyphPointF[] GlyphPoints` and `ushort[] EndPoints`.
- `IGlyphTranslator` defines a normalized callback shape:
  - `MoveTo`
  - `LineTo`
  - `Curve3`
  - `Curve4`
  - `CloseContour`
- `IGlyphReaderExtensions.Read(...)` converts TrueType point data into contour callbacks.

CFF/PostScript outlines:

- `Glyph.IsCffGlyph`
- `Glyph.GetCff1GlyphData()`
- `IGlyphReaderExtensions.Read(this IGlyphTranslator tx, CFF.Cff1Font cff1Font, CFF.Cff1GlyphData glyphData, float scale = 1)`
- `Tables.CFF/CffEvaluationEngine.cs` emits cubic `Curve4(...)` calls from CFF Type 2 instructions.

Curve model:

- TrueType outlines emerge as line segments plus quadratic curves.
- CFF outlines emerge as line segments plus cubic curves.
- The translator abstraction already matches the Machina-owned segment model proposed in the mission.

Examples and proof points:

- The repo demos include shape-reading flows based on `OpenFontReader`, `Typeface.GetGlyphIndex(...)`, `Typeface.GetGlyph(...)`, and `IGlyphTranslator`.
- `Demo/Windows/PixelFarmSample.WinForms/FormMsdfTest2.cs` shows an internal sample path from Typography glyph outlines into an MSDF-oriented contour builder.

This is strong evidence that a `Typography.OpenFont -> Machina GlyphOutline -> MSDF-Sharp.Core Shape` bridge is realistic.

### Suitability for Machina

Good fit points:

- Pure managed code.
- No renderer dependency in `Typography.OpenFont`.
- Stream/file-first loading.
- Explicit glyph lookup and metrics APIs.
- Existing contour callback seam is already close to the Machina adapter boundary.
- Supports both quadratic TrueType and cubic CFF paths.

Worker suitability:

- The API is synchronous and CPU-side, so it fits the planned async worker just like `MSDF-Sharp.Core`.
- No GPU or UI thread coupling was visible.

Adapter fit:

- Machina can translate `IGlyphTranslator` calls directly into `GlyphContour` and `GlyphOutlineSegment` records.
- Machina can normalize metrics and bounds into its own records without exposing Typography types to consumers.

### Risks

- Packaging risk is the biggest issue. Upstream does not present a clean official NuGet package in the audited tree.
- Docs clarity risk: the repo is powerful but source-heavy and not especially discoverable from concise API docs.
- Maintenance risk: the latest visible upstream commit was in 2023, so the API may be stable, but visible recent activity is lower than ideal.
- Complexity risk: the library spans far more than simple outline reading, so it is easy to over-adopt.
- Shaping risk: complex shaping lives in `Typography.GlyphLayout`, not in the low-level outline reader; Machina should not confuse outline extraction with complete text shaping.
- Hinting policy risk: Typography has hinting-related machinery, but Machina should define explicitly whether atlas generation uses hinted or unhinted outlines.
- Font-format edge risk: source support for CFF, TTC, WOFF, and WOFF2 is visible, but M8e did not prove those paths against Machina fixture fonts yet.

## SixLabors.Fonts comparison

### License

Current public license:

- [Six Labors Split License, Version 1.0](https://github.com/SixLabors/Fonts/blob/main/LICENSE)

Current public rule summary:

- Apache 2.0 applies for open-source use, transitive dependency use, some small-revenue direct dependency use, and nonprofit use.
- Other direct-dependency scenarios require a commercial license.

Why that is undesirable here:

- Machina.Fonts should be a low-level infrastructure package with a simple downstream license story.
- A split/commercial license adds policy review burden for adopters, forks, CI consumers, and future packaging decisions.
- Even if Machina remains open source, the dependency policy story is noisier than necessary for this layer.

### Technical fit

Technical strengths:

- Broad format support.
- Modern shaping and layout engine.
- Managed implementation.
- Good documentation surface.

Why it is not the best fit here:

- It is higher-level than Machina currently needs for outline extraction plus MSDF generation.
- It pulls in text-layout and shaping policy concerns that M8e is intentionally separating from atlas generation.
- Its license model is the wrong kind of complication for a low-level font-atlas worker.

### Recommendation

- Avoid for now.
- Revisit only if the managed permissive alternatives fail on real fixture fonts or if Machina later wants a broader shaping stack and is comfortable with the license policy.

## FreeType / SharpFont fallback comparison

### Pros

- Very mature font parser and outline source.
- Strong format coverage in practice.
- Well-known metrics and outline behavior.
- Good fallback when obscure fonts break a managed parser.

### Cons

- Native dependency management.
- Cross-platform packaging complexity.
- CI and local developer friction.
- More work for deterministic distribution and runtime probing.
- License/redistribution review must include both the binding and the native FreeType distribution plan.

Specific current evidence:

- The `SharpFont` repo describes itself as MIT-licensed bindings for FreeType.
- The repo quick-start docs require shipping native FreeType binaries.
- The NuGet ecosystem also exposes `SharpFont.Dependencies`, which is a sign that native deployment is a real concern, not just a theoretical one.

### Recommendation

- Keep as fallback.
- Do not make it the first Machina implementation path while a viable managed path still exists.

## Recommended dependency path

Recommended direction for Machina:

1. Use a Machina-owned adapter seam.
2. Target `Typography.OpenFont` semantics for outline extraction.
3. Target `MSDF-Sharp.Core` for distance-field generation.
4. Avoid `MSDF-Sharp.Extensions` in the first real path.
5. Avoid `SixLabors.Fonts` for now.
6. Keep native FreeType as the fallback if fixture-font testing disproves the managed path.

Why this is the best current fit:

- It keeps the pipeline fully CPU-side and managed.
- It avoids renderer coupling.
- It keeps outline loading and MSDF generation independently swappable.
- It avoids split-license image/font layers in the first real path.
- It matches the user's prior Stride/MSDF experience closely enough to reduce conceptual risk.

Important caveat:

- The technical recommendation is stronger than the packaging recommendation on the Typography side.
- M8f should therefore commit to Machina-owned adapters first, not to a direct public-package dependency shape.
- If Machina later consumes Typography via a maintained fork, vendored source, or internal repackaging, that should be an explicit follow-up decision after the proof milestone.

## Adapter boundary

Machina should own the boundary and keep both dependencies behind it.

Suggested outline-source interface:

```csharp
public interface IGlyphOutlineSource
{
    ValueTask<GlyphOutlineLoadResult> LoadGlyphOutlineAsync(
        FontFaceId face,
        int codepoint,
        GlyphOutlineLoadOptions options,
        CancellationToken cancellationToken = default);
}

public sealed record GlyphOutlineLoadOptions(
    float EmSize,
    int FaceIndex,
    GlyphHintingMode HintingMode,
    bool NormalizeToEm,
    bool IncludeColorGlyphLayers = false);

public sealed record GlyphOutlineLoadResult(
    bool Success,
    GlyphOutline? Outline,
    GlyphMetrics? Metrics,
    IReadOnlyList<FontGenerationDiagnostic> Diagnostics);
```

Suggested Machina-owned outline records:

```csharp
public sealed record GlyphOutline(
    GlyphKey Key,
    GlyphMetrics Metrics,
    GlyphBounds Bounds,
    IReadOnlyList<GlyphContour> Contours);

public sealed record GlyphContour(
    IReadOnlyList<GlyphOutlineSegment> Segments);

public abstract record GlyphOutlineSegment;

public sealed record GlyphLineSegment(
    GlyphPoint P0,
    GlyphPoint P1) : GlyphOutlineSegment;

public sealed record GlyphQuadraticSegment(
    GlyphPoint P0,
    GlyphPoint P1,
    GlyphPoint P2) : GlyphOutlineSegment;

public sealed record GlyphCubicSegment(
    GlyphPoint P0,
    GlyphPoint P1,
    GlyphPoint P2,
    GlyphPoint P3) : GlyphOutlineSegment;
```

Suggested generator interface:

```csharp
public interface IGlyphDistanceFieldGenerator
{
    GeneratedGlyphDistanceField Generate(
        GlyphOutline outline,
        MsdfGenerationSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed record GeneratedGlyphDistanceField(
    GlyphKey Key,
    GlyphMetrics Metrics,
    int Width,
    int Height,
    DistanceFieldKind Kind,
    int ChannelCount,
    ReadOnlyMemory<float> Data,
    IReadOnlyList<FontGenerationDiagnostic> Diagnostics);
```

Suggested separation rules:

- `IGlyphOutlineSource` owns font bytes, face selection, glyph lookup, outline extraction, and raw metrics.
- `IGlyphDistanceFieldGenerator` owns only contour-to-distance-field generation.
- Atlas packing stays outside both interfaces.
- Page encoding stays outside both interfaces.
- Renderer consumption stays outside both interfaces.

This boundary preserves swappability:

- `Typography.OpenFont` can later be replaced by FreeType/SharpFont without changing packing, TOML, or renderer code.
- `MSDF-Sharp.Core` can later be replaced by native `msdfgen` without changing font loading or atlas metadata code.

## Determinism and async-worker considerations

Determinism points Machina should pin explicitly:

- outline-source version,
- generator version,
- edge-coloring algorithm,
- edge-coloring seed,
- overlap-support mode,
- error-correction mode,
- projection math,
- pixel range,
- output dimensions,
- padding,
- glyph ordering before packing,
- atlas page ordering,
- float-to-byte quantization policy.

Recommended worker policy:

- Load font bytes and face metadata once per worker cache entry.
- Extract outlines and metrics synchronously inside the worker thread.
- Generate MSDF synchronously inside the worker thread.
- Publish only immutable Machina-owned records.
- Never expose Typography or MSDF-Sharp objects outside the generation layer.

Determinism warning:

- Exact float-byte identity across all runtimes is not proven by M8e.
- Machina should treat deterministic canonical output as a follow-up requirement to be verified with fixed fixture fonts before promising content-hash stability for real page images.

## Licensing considerations

Licensing summary:

- `MSDF-Sharp.Core`: MIT package metadata, with MIT-stated upstream lineage.
- `Typography.OpenFont`: permissive overall project story, but with mixed per-file provenance that should be preserved and re-checked if Machina later vendors or republishes it.
- `SixLabors.Fonts`: split/commercial license model; technically capable but policy-heavy.
- `SharpFont`: MIT bindings, but native FreeType distribution still needs its own review and packaging plan.

Practical policy recommendation:

- Prefer the permissive managed path first.
- Keep the Machina adapter boundary strict so a future license-driven swap is cheap.

## Risks and open questions

- The biggest unresolved issue is not algorithm fit but Typography packaging/consumption strategy.
- M8e did not prove real output quality on Machina fixture fonts yet.
- M8e did not prove exact deterministic byte equality for quantized MSDF outputs.
- M8e did not prove WOFF2, TTC, or CFF fixture coverage in Machina tests yet.
- M8e did not choose a final hinting policy.
- M8e did not choose the final page image encoding library.

Questions to answer in the next implementation milestone:

- Should Machina normalize all outlines to em-space before MSDF generation?
- Should the first proof use unhinted outlines only?
- Should the first proof limit scope to a small fixture set such as one TTF Latin font before attempting CFF and collections?
- Should Machina consume Typography through vendored source, an internal package, or a maintained fork once the adapter proof succeeds?

## M8f+ implementation plan

- `M8f`: add Machina-owned generation adapter records and interfaces, plus fake outline-source and fake distance-field implementations for compile-checked seams.
- `M8g`: add a `Typography.OpenFont` proof adapter against one small fixture font, proving codepoint lookup, metrics extraction, and contour translation into Machina-owned outline records.
- `M8h`: add an `MSDF-Sharp.Core` proof adapter that converts Machina-owned outline records into `Msdfgen.Shape` and emits a generated distance field for a small fixture glyph set.
- `M8i`: integrate real generated fields into the existing atlas page pipeline and real artifact export, initially favoring debug-oriented page outputs and deterministic metadata over renderer integration.
- `M8j`: add a CPU reference MSDF page renderer or inspection path for export/debug validation.
- `M8k`: defer renderer shader consumption and Aurelian/Vulkan integration until the CPU-side contracts are stable.

Recommended success gate for `M8g` and `M8h`:

- one fixture font,
- one codepoint batch,
- stable metrics,
- stable contour counts,
- stable page metadata,
- no renderer integration yet.

## M8f landed follow-up

M8f is now the concrete proof step between this audit and any real dependency adoption.

What landed in M8f:

- Machina-owned outline records and segment types
- generation-local diagnostic records
- `IGlyphOutlineSource`
- `IGlyphDistanceFieldGenerator`
- `FakeGlyphOutlineSource`
- `FakeGlyphDistanceFieldGenerator`
- `GlyphGenerationPipeline`
- focused seam tests

What still did not land:

- no `Typography.OpenFont` package reference
- no `MSDF-Sharp.Core` package reference
- no `SixLabors` dependency
- no native dependency
- no renderer integration

That keeps this audit's swappable-boundary recommendation intact. See `docs/machina-font-generation-adapters-m8f.md`.

## M8g landed follow-up

M8g now proves the first half of this audit's recommendation with a real managed outline adapter.

What landed:

- `Machina.Fonts` consumes `WycliffeAssociates.Typography.OpenFont` `1.0.0`
- `TypographyGlyphOutlineSource` loads an explicit checked-in fixture font from disk
- codepoint lookup, metrics extraction, and contour translation are now tested against a real font
- the proof remains fully managed and keeps the Machina-owned seam intact

What still did not land:

- no `MSDF-Sharp.Core`
- no real distance-field generation
- no renderer integration
- no OS font lookup
- no native fallback dependency

Packaging conclusion update:

- the packaging story is still weaker than ideal
- the chosen package is acceptable for a proof because it ships `netstandard2.0`, includes a package license file, and stays isolated behind the Machina-owned adapter seam
- if M8h+ expands scope beyond the current proof, package freshness and long-term maintenance should be re-evaluated again

See `docs/machina-typography-outline-adapter-m8g.md`.

## Sources

Primary sources inspected:

- [ExtraBinoss/MSDFGen-Sharp](https://github.com/ExtraBinoss/MSDFGen-Sharp)
- [MSDF-Sharp on NuGet](https://www.nuget.org/packages/MSDF-Sharp)
- [MSDF-Sharp.Core on NuGet](https://www.nuget.org/packages/MSDF-Sharp.Core)
- [MSDF-Sharp.Extensions on NuGet](https://www.nuget.org/packages/MSDF-Sharp.Extensions)
- [LayoutFarm/Typography](https://github.com/LayoutFarm/Typography)
- [Typography project license](https://github.com/LayoutFarm/Typography/blob/master/LICENSE.md)
- [SixLabors/Fonts](https://github.com/SixLabors/Fonts)
- [SixLabors.Fonts on NuGet](https://www.nuget.org/packages/SixLabors.Fonts)
- [Six Labors Fonts license](https://github.com/SixLabors/Fonts/blob/main/LICENSE)
- [Robmaister/SharpFont](https://github.com/Robmaister/SharpFont)
- [SharpFont on NuGet](https://www.nuget.org/packages/SharpFont)

Local source and package metadata inspected during M8e:

- `artifacts/m8e-audit/MSDFGen-Sharp`
- `artifacts/m8e-audit/Typography`
- extracted `.nupkg` metadata for `MSDF-Sharp.Core`, `MSDF-Sharp.Extensions`, `WycliffeAssociates.Typography.OpenFont`, and `Typography.OpenFont.NetCore`

Key local files inspected:

- `artifacts/m8e-audit/MSDFGen-Sharp/Msdfgen.Core/Msdfgen.Core.csproj`
- `artifacts/m8e-audit/MSDFGen-Sharp/Msdfgen.Core/Shape.cs`
- `artifacts/m8e-audit/MSDFGen-Sharp/Msdfgen.Core/EdgeSegment.cs`
- `artifacts/m8e-audit/MSDFGen-Sharp/Msdfgen.Core/GeneratorConfig.cs`
- `artifacts/m8e-audit/MSDFGen-Sharp/Msdfgen.Core/MsdfGenerator.cs`
- `artifacts/m8e-audit/MSDFGen-Sharp/Msdfgen.Extensions/FontLoader.cs`
- `artifacts/m8e-audit/Typography/Typography.OpenFont/OpenFontReader.cs`
- `artifacts/m8e-audit/Typography/Typography.OpenFont/Typeface.cs`
- `artifacts/m8e-audit/Typography/Typography.OpenFont/Glyph.cs`
- `artifacts/m8e-audit/Typography/Typography.OpenFont/IGlyphTranslator.cs`
- `artifacts/m8e-audit/Typography/Demo/Windows/PixelFarmSample.WinForms/FormMsdfTest2.cs`

Inference notes:

- The .NET 10 compatibility discussion is an inference from visible TFMs and standard NuGet framework compatibility behavior.
- M8e did not install any audited package into the repository and did not add any package reference.
