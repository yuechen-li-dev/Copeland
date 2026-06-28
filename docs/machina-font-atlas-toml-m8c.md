# Machina Font Atlas TOML M8c

## Purpose

M8c adds an inspectable `.font-atlas.toml` metadata contract for Machina font atlases. It is metadata only: no MSDF generation, font parsing, PNG writing requirement, renderer integration, or native dependency is introduced.

## Dependency choice

The repo had no existing TOML dependency. M8c uses the Tomlyn NuGet package through central package management because it is a focused TOML parser/serializer package and avoids any ProjectReference into `reference/dominatus`.

## Schema

The document uses `[atlas]`, `[font]`, `[metrics]`, `[msdf]`, repeated `[[page]]`, and repeated `[[glyph]]` sections. Format `1`, kind `machina-font-atlas`, and `distance_field = "msdf"` are the initial supported values.

## Runtime records and TOML documents

TOML records live under `Machina.Fonts.Toml` and are separate from runtime atlas records so editable metadata can carry source, license, page hash, and generator settings that runtime snapshots do not own.

## Loader

`FontAtlasTomlLoader.LoadString` and `LoadFile` parse TOML, bind it to document records, validate the document, and convert valid documents to `FontAtlasSnapshot`. Expected asset problems are returned as diagnostics rather than normal-path crashes.

## Writer

`FontAtlasTomlWriter.Write` emits canonical TOML with stable section order, sorted pages, sorted glyphs, invariant numeric formatting, no timestamps, and no comment preservation.

## Validation diagnostics

Diagnostics include parse/bind failures, missing fields, unsupported format, invalid kind/value, duplicate pages/glyphs, missing pages, out-of-bounds glyph rectangles, invalid glyph keys, char/codepoint mismatch, UV mismatch warnings, and missing hashes.

## Snapshot conversion

`FontAtlasTomlConversion.ToSnapshot` converts validated TOML documents to runtime snapshots. `FromSnapshot` exports runtime snapshots when paired with `FontAtlasTomlExportMetadata` for source/license/metrics/MSDF metadata not present in runtime records.

## Deterministic export policy

Export intentionally avoids timestamps and locale-specific formatting. Comments are not preserved in M8c.

## Tests

Tests cover writer ordering/sorting/invariant formatting, loader success and diagnostic cases, validator cases, and deterministic roundtrips.

## Deferred issues

Real font loading, outline extraction, MSDF generation, PNG page writing/inspection, renderer/TextBlock integration, and native dependencies are deferred.

## M8d plan

M8d can add a real generator pipeline around the metadata contract: source font discovery, glyph extraction, MSDF rasterization, PNG page writing, and optional page-file verification while keeping this TOML contract deterministic.


## M8d fake page artifacts

M8d allows TOML `page.image` values to reference `.fakepage` files. These are deterministic text artifacts, not real PNG images, and their SHA-256 bytes are stored in `content_hash` for import validation.
