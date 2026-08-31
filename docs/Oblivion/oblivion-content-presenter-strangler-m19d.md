# Oblivion content presenter strangler — M19d

## Outcome

M19d established the content boundary, mature Markdown/code host, and inline PNG path. Its final Outcome B blocker is now closed by M19e: the repo owns `@mermaid-js/mermaid-cli@11.16.0`, actual version qualification, deterministic derived PNG caching, provenance, headless inspection, and the real M19 briefing render. The M19d ownership boundary did not change.

## Before

Machina composed the shell, card chrome, content, and inspector into one raster image. The Avalonia window hosted only that image. `OblivionMarkdownRenderer` therefore owned text measurement, wrapping, line spacing, code layout, clipping, and local scroll rendering. Its deterministic output remains useful for playback and headless export, but its pixel font, selection behavior, and prose metrics are a weak human reading surface.

The audit dispositions were:

| Surface | M19c implementation | Disposition |
| --- | --- | --- |
| Collapsed Markdown | native first-line preview | NATIVE_GOOD_ENOUGH after deliberate summary/type labels |
| Expanded Markdown | native `DocumentMir` raster lowering | MATURE_REPLACEMENT_CANDIDATE |
| Plain text | native wrapped lines | NATIVE_GOOD_ENOUGH |
| Code | native text-like body | MATURE_REPLACEMENT_CANDIDATE |
| Mermaid | native code-fence fallback | EXTERNAL_RENDERER_CANDIDATE |
| PNG | resolvable/external-open only | MATURE_REPLACEMENT_CANDIDATE |
| Artifact metadata | native inspector lines | SEMANTIC_ONLY / NATIVE_GOOD_ENOUGH |
| Inspector prose | native raster lines | NATIVE_WEAK; metadata remains product-owned |

## Strangler mapping

`OblivionContentPresenterSelector` creates an explicit, inspectable plan from a card, its session-only reading state, and optional resolved artifact facts. The mapping is:

| Semantic content | Presenter | Fallback |
| --- | --- | --- |
| Markdown `DocumentMir` projection | `AvaloniaReadOnlyDocument` | existing native raster Markdown |
| Code fact/theory | `AvaloniaReadOnlyCode` | native plain/code preview |
| Mermaid code block | `ExternalMermaidRenderer` | retained source plus diagnostic |
| existing `image/png` artifact | `AvaloniaImage` | artifact metadata and external-open action |
| plain text | `NativeText` | same native presenter |
| artifact facts | `NativeMetadata` | same semantic facts |
| unmatched/unavailable | `DiagnosticFallback` | source/provenance retained |

The selector is a small switch, not registration, reflection, DI, or a widget framework. `Oblivion.Model` does not know about `DocumentMir`, Avalonia, controls, or renderer processes.

## Ownership

- `Oblivion.Model` owns durable card body source, artifact declarations, IDs, and provenance.
- `Oblivion.UI` owns reading state projection, presenter selection, scroll/focus contracts, collapsed summaries, and the typography reference.
- `Oblivion.App` safely resolves artifacts and owns the bounded external Mermaid process adapter.
- `Machina.Presenter.Sample` owns Avalonia controls, selection/copy, platform shaping, image decode, and overlay placement inside the existing Machina card body geometry.
- Machina continues to own the shell, card chrome, selection, expansion, ordering, and layout.

## Markdown result

The real window now overlays expanded Markdown bodies with Avalonia controls built from the existing `DocumentMir`. Headings, paragraphs, inline emphasis/strong/code/link text, lists, quotes, thematic breaks, and code blocks use platform text shaping and selectable text. Raw Markdown remains in product state and is shown through the same selectable code surface inside the inspector's existing body rectangle. Headless export retains the deterministic native renderer.

## Mermaid result

Mermaid source is discovered from `DocumentMir` fenced code blocks. `IOblivionDiagramRenderer` receives content identity, source, source reference, and an output directory. `OblivionExternalMermaidRenderer` invokes an explicit executable without a shell, writes a source-addressed `.mmd`, derives a PNG, records renderer/version/source hash, applies a 30-second bound, and returns typed diagnostics.

Tests use a fake renderer and never use the network. M19e qualifies the real M19 briefing through the repo-owned renderer. Absence remains an explicit source-preserving fallback, but is no longer the supported environment's production state. See `oblivion-mermaid-renderer-qualification-m19e.md` and `oblivion-mermaid-derived-artifacts-m19e.md`.

## PNG result

Resolved PNG facts cross into `Oblivion.UI` as framework-free data. Existing, correctly typed PNGs select `AvaloniaImage`; missing payloads select a diagnostic fallback while the product-owned external-open action remains.

Dogfood exposed that `PresenterPngWriter` emitted raw DEFLATE bytes in an IDAT chunk instead of the PNG-required zlib stream. GDI+ tolerated the file, while Avalonia and FFmpeg rejected it. The writer now uses `ZLibStream`, a test decompresses IDAT through the standard zlib decoder, FFmpeg accepts the regenerated artifact, and the real host displays it inline with `Stretch.Uniform`.

## Headless and fallback behavior

Workspace load, CLI inspection, artifact resolution, presenter selection, and Mermaid diagnostics require no GUI initialization. Export/playback still use the deterministic native raster fallback. A missing Avalonia window or Mermaid executable cannot prevent semantic inspection.

## Dogfood findings

The M19c six-card briefing was reused unchanged: summary, Markdown, Mermaid, code, PNG, artifact metadata, decision, and next actions all remain one semantic workspace.

Before M19d, expanded prose used oversized pixel typography, Mermaid was indistinguishable from code, and the PNG was external-open only. After M19d, the live expanded Markdown body has normal 16/24 selectable prose and clear headings; the content badge says `Markdown + Mermaid`; Mermaid failure is explicit and preserves source; and the existing generated briefing PNG appears inline with its aspect ratio preserved.

What still sucks:

1. Inspector metadata sections remain native raster text; the raw Markdown section now uses the mature selectable overlay.
2. The headless raster fallback intentionally retains weak typography.
3. Links render readably with their target but do not yet emit a product-owned activation event.
4. The overlay currently hosts the single exclusively expanded card, matching current session semantics; multiple simultaneous expanded hosts are not supported.

## Native replacement criteria

See `docs/Machina/machina-native-content-parity-baseline-m19d.md`. No foreign presenter is scheduled for mandatory replacement.
