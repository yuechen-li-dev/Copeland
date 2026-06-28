# Machina.Fonts M8b

## Purpose

M8b lands the first compile-checked `Machina.Fonts` architecture slice. It proves the record model, immutable atlas snapshots, async queue/worker semantics, fake deterministic glyph generation, fake atlas packing, and export-style preflight waiting without changing runtime rendering.

## Project boundaries

`src/Machina.Fonts` is standalone. It does not reference `reference/dominatus`, renderer projects, Aurelian, Vulkan, Avalonia, TOML packages, native libraries, or external font packages. `tests/Machina.Fonts.Tests` validates this fake architecture slice.

## Core records

The core model contains `FontFaceId`, `GlyphKey`, `GlyphMetrics`, `GlyphAtlasEntry`, `FontAtlasPage`, and `FontAtlasSnapshot`. These records validate caller input and copy collection inputs where needed so published snapshots are not mutated by later caller or worker state.

## Service contract

`IFontAtlasService` exposes the current `Snapshot`, synchronous `Resolve`, and async `QueueAsync`. `IFontAtlasVersionSource` is an optional notification interface used by preflight to wait for coherent version publication instead of sleeping blindly.

## Fake generator

`FakeGlyphGenerator` computes deterministic metrics and bitmap rectangle sizes from codepoint category, em size, weight, and slant. It creates no images, reads no font files, performs no outline extraction, and produces no MSDF data. Tests can configure missing codepoints.

## Fake atlas packer

`FakeAtlasPacker` uses deterministic shelf packing into configurable synthetic pages such as `fake.page0.png`. It creates additional pages when rows/pages fill and rejects generated glyphs that are too large for a page.

## Async worker lifecycle

`FakeFontAtlasService` accepts requests through `System.Threading.Channels`. `QueueAsync` deduplicates and returns after work is accepted, not after glyphs are ready. A background worker generates and packs glyphs, then publishes one coherent immutable snapshot version per changed batch. The service implements `IAsyncDisposable` to complete the channel and stop the worker cleanly.

## Snapshot immutability

Consumers only receive `FontAtlasSnapshot` values. Each snapshot copies page and glyph collections, and later worker publications create new snapshots instead of mutating previously observed ones.

## Runtime pending/ready behavior

`Resolve` returns `GlyphReady` for glyphs in the current snapshot, `GlyphPending` for queued/generating glyphs or unknown glyphs, and `GlyphMissing` for known fake-generation or fake-packing failures. Unknown unqueued glyphs are treated as pending because runtime text can ask for glyphs before scheduling has caught up.

## Export preflight behavior

`FontAtlasPreflight.EnsureReadyAsync` queues requested glyphs and waits until each glyph is ready or missing, a timeout expires, or cancellation occurs. Success requires every requested glyph to be ready. Missing glyphs are returned as failures, and timed-out glyphs remain in `PendingGlyphs`.

## Tests

The M8b test project covers record validation, snapshot collection copying, async queue behavior, pending/ready/missing states, worker version increments, request deduplication, deterministic fake output, multi-page packing, clean disposal, and preflight success/failure/timeout behavior.

## Deferred issues

M8b intentionally defers MSDF generation, real font loading, glyph outline extraction, TOML metadata, PNG page writing, renderer integration, `TextBlock` migration, gallery visual changes, native dependencies, and any active build dependency on the Dominatus reference submodule.

## M8c plan

M8c can build on this standalone service by adding stronger diagnostics and generation contracts, then later milestones can add real atlas serialization and renderer consumption behind the immutable snapshot boundary.

## M8c follow-up

M8c adds the standalone `.font-atlas.toml` metadata contract in `Machina.Fonts.Toml`. The M8b runtime records remain standalone; M8c layers deterministic TOML loading, writing, validation diagnostics, and snapshot conversion metadata on top without adding MSDF generation, PNG writing, renderer integration, or native dependencies.
