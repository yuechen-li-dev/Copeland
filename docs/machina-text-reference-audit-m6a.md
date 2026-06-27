# Machina.Text Reference Audit M6a

## Purpose

M6a establishes a reference-backed contract for a future C# `Machina.Text` subsystem without changing runtime behavior. This milestone imports upstream text reference material, audits model/parser/view policy, defines the C# target architecture, and sets an incremental plan for M6b+.

Core doctrine for this milestone:

> Frames place text boxes.  
> Machina.Text lays out text inside text boxes.
>
> General layout does not wrap text.  
> Text layout does not place components.
>
> Text is the one domain where wrap/overflow/leading/line layout are correct primitives.  
> Those primitives must not leak into general component layout.

## Imported reference material

Reference files were copied from `MachinaLayout.JS/src/text` into `docs/reference/machinalayout-js/text/` with provenance in that folder README.

Key imported files for this audit:

- `docs/reference/machinalayout-js/text/types.ts`
- `docs/reference/machinalayout-js/text/parseMachinaText.ts`
- `docs/reference/machinalayout-js/text/react/MachinaTextView.tsx`

## JS reference model

### Sources and specs

The JS model separates source kind from text policy:

- `MachinaTextSource`: `plain` or `machina-text`
- `MachinaTextSpec`: contains source plus policy (`variant`, `wrap`, `overflow`, `align`, `leading`, `blockGap`, `listGap`, `valign`)

This explicitly treats policy as text-domain settings, not global layout settings.

### Document blocks

`MachinaTextDocument` is block-based:

- `paragraph` block with inline runs
- `bulletList` block with `MachinaBulletItem[]`

Bullet items support nested children, but parser currently caps practical nesting to depth 2 and emits diagnostics when exceeded.

### Inline runs

Inline grammar supports:

- plain `text`
- `strong`
- `emphasis`
- `code`
- `link`

Inline parser behavior is intentionally restricted/defensive with explicit diagnostics for malformed link or unclosed markers.

### Diagnostics

Parser diagnostics include structured codes and source coordinates:

- `unsupported_syntax`
- `heading_forbidden`
- `max_list_depth_exceeded`
- `malformed_link`
- `unclosed_inline`
- `invalid_escape`

Key behavior: forbidden/unsupported syntax degrades to text-like paragraph output rather than silently disappearing.

### Text policy

Reference policy dimensions and role:

- **variant**: semantic typography preset (`body`, `label`, `caption`, `title`, `mono`)
- **wrap**: line breaking (`word` or `none`)
- **overflow**: box overflow behavior (`clip`, `ellipsis`, `scroll`)
- **align**: horizontal text alignment (`start`, `center`, `end`)
- **leading**: line-height policy (`tight`, `normal`, `loose`, or numeric)
- **blockGap**: vertical spacing between document blocks
- **listGap**: spacing between list items
- **vertical align** (`valign`): block pack position inside text box (`top`, `center`, `bottom`)

### React view behavior

`MachinaTextView` normalizes input into `{ document, diagnostics, policy }`, then renders:

- container style from policy (variant + wrap + overflow + align + valign + leading)
- each block as `<p>` or `<ul>`
- each inline run as fragment/`<strong>`/`<em>`/`<code>`/`<a>`
- optional diagnostics `<pre>` panel when `showDiagnostics` is true

The view demonstrates that text policy is applied *inside* a provided box (`width/height: 100%`) and does not perform component placement.

## C# target model

C# should mirror JS concepts with explicit records and enums while staying idiomatic and renderer-independent.

Proposed shape (contract-level, not implemented in M6a):

- `MachinaTextSource` union equivalent (`PlainTextSource`, `MachinaMarkupSource`)
- `MachinaTextSpec` record with text policy fields
- `MachinaTextDocument` block tree
- `MachinaTextBlock` (`ParagraphBlock`, `BulletListBlock`)
- `MachinaInline` (`TextRun`, `StrongRun`, `EmphasisRun`, `CodeRun`, `LinkRun`)
- `MachinaTextDiagnostic` with code/level/range/line/column
- parser entry points for source and inline-only probes

Recommended M6 baseline policy surface:

- `Variant`: body/label/caption/title/mono
- `Wrap`: none/word
- `Overflow`: clip (M6 baseline); ellipsis/scroll deferred
- `Align`: start/center/end
- `VerticalAlign`: top/center/bottom
- `Leading`: preset or numeric
- `BlockGap`, `ListGap`

## Boundary with general layout

Machina layout boundary to preserve:

- General layout (frame/stack/table rows) determines component rectangles.
- Text layout operates only after rectangle assignment and only within that rectangle.
- No text-driven mutation of component layout algorithm.
- No leakage of wrap/overflow/leading into general component or frame placement primitives.

Heading policy:

- Headings remain forbidden in restricted Machina text content.
- Title hierarchy belongs to component/layout composition and variant choice, not inline markdown structure.

## Integration points

Planned integration contract (future milestones):

- `UI.Text(...)` becomes sugar over `MachinaTextSpec` defaults (plain source + policy defaults).
- Standard controls choose explicit text policies:
  - Button labels: label variant, no-wrap, clip, center/center.
  - Checkbox/Switch labels: label variant, start align, center vertical alignment.
  - Card title/body: explicit variant selection by component authoring.
- Renderer consumes text-layout output (line/run boxes) rather than raw string-only placement.

Current simple text path should be treated as transitional until Machina.Text layout is integrated.

## Deferred features

Deferred beyond M6a (and mostly beyond M6b):

- click-interactive link behavior in runtime if scope is large
- scroll overflow and rich ellipsis behavior
- diagnostics UI surfaces beyond probe/testing utilities
- full markdown compatibility
- heading support in source
- shaping/kerning/font-engine upgrades
- MSDF/atlas/cache backend work

## M6b+ roadmap

### M6b — model + parser

- Add `Machina.Text` and `Machina.Text.Tests` projects.
- Port type model and diagnostic codes.
- Implement restricted parser for plain/machina text.
- Add tests for paragraph/list/inline and heading-forbidden diagnostics.

### M6c — measurement + text layout

- Add bitmap-font-aware measurement seam integration.
- Build line boxes and block flow inside assigned rectangle.
- Implement wrap `none|word`, overflow `clip`, align/valign, leading, block/list gaps.
- Add headless layout tests for deterministic geometry.

### M6d — raster integration

- Bridge text-layout output to render commands.
- Keep compatibility path while migrating from raw-string draw text.
- Validate presenter-sample clipping regressions with headless tests.

### M6e — StandardUI adoption

- Migrate button/checkbox/switch/input/card text usage to Machina.Text policies.
- Add component-level headless fit/overflow assertions.
- Keep dynamic text fitting deferred unless explicitly scoped.

### M6f — optional richer features

- ellipsis and/or scroll overflow upgrades
- link hit-target plumbing
- diagnostics rendering surfaces
- font cache/atlas work if needed later

## Conclusion

M6a concludes that text must be a dedicated subsystem contract, not a retrofit of frame/layout semantics. Upstream JS reference confirms a viable restricted text model (source/spec/document/block/inline/diagnostics + policy dimensions) that can be ported to C# incrementally while preserving Machina’s headless-first, deterministic architecture.

## M6b update — Standard-owned implementation

M6b intentionally changed the integration target from a standalone production `Machina.Text` package to `Machina.Standard.Text`. The M6a doctrine still holds: frames place text boxes, and text layout belongs inside those boxes. The production model/parser now lives in Standard because typography variants, labels, titles, captions, theme policy, and rich text authoring are Standard concerns.

The standalone package remains deferred. `Machina.Core.Authoring.UI.Text(string)` remains the primitive transitional text path, and M6b does not integrate rich text with the renderer or layout resolver. See `docs/machina-standard-text-m6b.md` for the landed Standard-owned model/parser surface.

## M6c update — layout result layer

M6c confirms the next planned boundary from this audit:

- assigned rectangles still come from general layout
- `Machina.Standard.Text` now performs deterministic line/run layout inside those rectangles
- layout output is a renderer-independent result model
- renderer consumption and StandardUI migration remain deferred

See `docs/machina-standard-text-layout-m6c.md` for the concrete M6c layout contract.
