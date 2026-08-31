# Machina native content parity baseline — M19d

This document records what the mature host provides. It is a comparison baseline, not a promise to replace it.

## Typography baseline

The reference values are stored in `OblivionReadingTypographyBaseline.MatureReadOnly`:

| Metric | Baseline |
| --- | ---: |
| Body font | 16 px |
| Body line height | 24 px |
| Heading 1 / 2 / 3 | 28 / 24 / 20 px |
| Heading 4 / 5 / 6 | 18 / 16 / 16 px |
| Paragraph spacing | 12 px |
| List indent | 24 px |
| Code font | 14 px monospace |
| Code line height | 20 px |
| Content padding | 16 px |
| Card-body padding | 18 px shell inset |
| Inspector-body padding | 16 px |
| Maximum readable width | 760 px |

These values follow normal Avalonia read-only control behavior and were verified in the live 1440×900 briefing. They replace screenshot guessing with an explicit behavioral reference.

## Presenter parity criteria

### Avalonia read-only Markdown — KEEP_FOREIGN

Provides platform shaping, wrapping, selectable/copyable text, heading scale, paragraph rhythm, lists, quotes, inline emphasis/strong/code/link display, code blocks, bounded scrolling, and deterministic source projection.

A native Machina replacement would need:

- the typography metrics above across headings/prose/lists/code;
- correct measurement at multiple widths without clipping or overlap;
- selection and clipboard behavior or a documented superior alternative;
- local wheel/scroll behavior without stealing header/product input;
- source/provenance and diagnostic equivalence;
- semantic tests plus human dogfood at wide/compact widths.

Replacement is not currently desirable; Machina gains more by improving shell/content hosting than rebuilding platform text interaction.

### Avalonia read-only code — KEEP_FOREIGN

Provides selectable monospace text, 14/20 sizing, no-wrap lines, horizontal overflow, bounded vertical scroll, and a language/source label.

Native replacement is possible only if code measurement, selection/copy, long-line horizontal scroll, Unicode shaping, and source metadata match in focused tests and mixed-content dogfood. Syntax highlighting is not a parity requirement.

### Avalonia PNG image — NO_REASON_TO_REPLACE

Provides platform PNG decoding, aspect-preserving `Uniform` fit, bounded size, and local scrolling when needed. Product actions/provenance remain outside the control.

Parity evidence would require valid/wrong/missing media cases, aspect-ratio checks at multiple body sizes, no uncontrolled stretch, and retained external-open fallback. Native image decoding has no demonstrated product advantage.

### External Mermaid CLI — KEEP_FOREIGN

Provides mature Mermaid parsing/layout and a derived raster artifact while retaining authored source. M19e qualifies the repo-owned `@mermaid-js/mermaid-cli@11.16.0` path, exact version verification, SHA-256 source/key identity, validated PNG/sidecar caching, renderer/owner provenance, bounded execution, headless inspection, and explicit diagnostics. The real M19 briefing diagram now renders inline through this path.

Native replacement would require broad Mermaid grammar compatibility, readable layout, deterministic failure diagnostics, fitted collapsed/expanded presentation, and source/provenance parity. Native replacement is not desirable. The immediate task is local renderer qualification, not graph-layout implementation.

## Native presenters retained

Collapsed summaries, plain text, badges/metadata, inspector structure, and deterministic export/playback remain native. They are retained because they are semantic/chrome surfaces or bounded fallbacks. The native expanded raster Markdown path remains useful for headless proof but does not meet the human reading baseline.

## Evidence required before changing a disposition

Any replacement proposal must include focused semantic/measurement/input tests, real host dogfood, headless fallback proof, canonical 14/14 playback, and before/after evidence against this baseline. Pixel goldens are neither necessary nor sufficient.
