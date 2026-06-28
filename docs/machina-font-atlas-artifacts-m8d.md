# Machina Font Atlas Artifacts M8d

## Purpose

M8d connects the standalone fake font atlas runtime from M8b with the deterministic `.font-atlas.toml` metadata contract from M8c. It proves the future asset pipeline shape without introducing real font parsing, real MSDF generation, renderer integration, native dependencies, or image libraries.

## Fake artifact scope

M8d writes fake atlas page artifacts with the `.fakepage` extension. These files are deterministic text fixtures, not PNG files and not image files. They exist only to prove export, import, hashing, and page validation behavior until real PNG/MSDF pages are added later.

## Export flow

The intended flow is:

```text
fake glyph keys
  -> FakeFontAtlasService.QueueAsync
  -> FontAtlasPreflight.EnsureReadyAsync
  -> FontAtlasSnapshot
  -> FontAtlasArtifactExporter
  -> .font-atlas.toml + .fakepage artifacts
```

`FontAtlasArtifactExporter.Export` creates the output directory, writes page artifacts first, computes their SHA-256 content hashes, converts the snapshot into TOML metadata, and writes `<name>.font-atlas.toml` deterministically.

## Import flow

`FontAtlasArtifactImporter.Import` loads the TOML file, runs the M8c schema validation, validates referenced page artifacts by default, and returns a `FontAtlasSnapshot` only when no error diagnostics are present.

## Fake page artifact format

Each page artifact is UTF-8 text:

```text
machina-font-atlas-fake-page
format=1
atlas=machina-default
page=0
width=256
height=256
glyphs=U+0041,U+0042
```

Glyph codepoints are sorted deterministically. The extension is `.fakepage` so consumers do not mistake these files for real PNG images.

## Content hash policy

M8d computes SHA-256 over the exact fake page bytes and stores it in TOML as `content_hash = "sha256:<hex>"`. Import validation recomputes the file hash and reports a content hash mismatch if the page artifact is stale or edited.

## Page validation diagnostics

Artifact import reports diagnostics for:

- missing page artifacts,
- content hash mismatches,
- invalid fake page headers or fields,
- fake page width/height values that do not match the TOML page dimensions.

## Snapshot roundtrip equivalence

Roundtrip tests compare page index, width, height, relative image filename, content hash, glyph keys, glyph rectangles, UVs, and metrics. Absolute temp directory paths are not part of snapshot identity.

## Preflight integration

`FontAtlasPreflight.EnsureReadyAsync` remains the readiness gate. `FontAtlasSnapshot` contains ready glyph entries only, so missing glyphs are reported by preflight and are not exported as glyph entries. The exporter exports the snapshot it is given.

## Tests

M8d adds exporter, importer, validator, roundtrip, deterministic output, multipage, and preflight integration tests under `tests/Machina.Fonts.Tests/Artifacts`.

## Deferred real PNG/MSDF work

M8d does not add real MSDF generation, real PNG encoding/decoding, real font file parsing, renderer integration, Aurelian/Vulkan integration, native dependencies, or external image libraries.

## M8e plan

M8e refines the direction above at the dependency/design level, not by changing artifacts yet. The audit recommends:

- `Typography.OpenFont` for outline extraction,
- `MSDF-Sharp.Core` for distance-field generation,
- Machina-owned outline/generator interfaces so artifact export and import remain insulated from dependency churn.

Real PNG pages remain deferred until the outline and generator proofs land in later milestones. See `docs/machina-font-msdf-dependency-audit-m8e.md`.
