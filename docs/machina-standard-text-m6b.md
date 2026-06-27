# Machina.Standard.Text M6b

## Outcome

M6b lands the rich text model, restricted parser, authoring helpers, and diagnostics under `Machina.Standard.Text`.

This is intentionally **model/parser only**. It does not change raster text rendering, draw commands, layout resolution, StandardUI labels, or presenter behavior.

## Ownership decision

The M6a audit described a future `Machina.Text` subsystem. After design review, M6b keeps the production C# implementation inside `Machina.Standard.Text` instead of creating a standalone production package.

The reason is architectural rather than incidental:

- Standard components own typography variants and text policies for labels, titles, captions, and control text.
- Theme, typography, control labels, and text authoring are intrinsically linked at the Standard layer.
- `Machina.Core` must remain free of `Machina.Standard` dependencies.
- The existing primitive `UI.Text(string)` path remains intact as the transitional core primitive.

A standalone text package is therefore deferred. Future milestones may revisit packaging after Standard-owned text policy and rendering integration have stabilized.

## Model surface

`Machina.Standard.Text` defines:

- `MachinaTextSource`
  - `PlainTextSource`
  - `MachinaMarkupSource`
- `MachinaTextSpec`
  - source plus text policy
- `MachinaTextDocument`
  - block list
- blocks
  - `ParagraphBlock`
  - `BulletListBlock`
  - `MachinaBulletItem`
- inline runs
  - `TextRun`
  - `StrongRun`
  - `EmphasisRun`
  - `CodeRun`
  - `LinkRun`
- diagnostics
  - `MachinaTextDiagnostic`
  - `MachinaTextDiagnosticCode`
  - `MachinaTextDiagnosticLevel`
- parse result
  - `ParseMachinaTextResult`

Policy enums include variant, wrap, overflow, horizontal alignment, vertical alignment, and leading.

Default policy is stable:

- variant: `Body`
- wrap: `Word`
- overflow: `Clip`
- align: `Start`
- leading: `Normal`
- block gap: `8`
- list gap: `2`
- vertical align: `Top`

## Parser behavior

`MachinaTextParser` exposes:

- `Parse(MachinaTextSource source)`
- `ParsePlain(string text)`
- `ParseMarkup(string text)`

Plain text parsing creates one paragraph containing one text run. Plain text does not interpret inline markdown markers.

Machina markup parsing supports a deliberately restricted markdown-like subset:

- paragraphs separated by blank lines
- bullet lists using `- `
- nested bullet lists up to depth 2 using two-space indentation
- inline strong: `**strong**`
- inline emphasis: `*emphasis*`
- inline code: `` `code` ``
- links: `[text](href)`
- escapes for the supported special characters

The parser is defensive and deterministic. Unsupported or malformed syntax emits diagnostics while preserving source content as text-like output whenever practical.

## Diagnostics

M6b implements stable diagnostic codes:

- `UnsupportedSyntax`
- `HeadingForbidden`
- `MaxListDepthExceeded`
- `MalformedLink`
- `UnclosedInline`
- `InvalidEscape`

Diagnostics include 0-based absolute source index plus deterministic 1-based line and column values.

`ParseMachinaTextResult.Ok` is true only when no error-level diagnostics are present.

## Heading policy

Markdown headings remain forbidden. A source line such as `# Heading` emits `HeadingForbidden` and is preserved as paragraph text.

Title hierarchy belongs to component composition and typography variant selection, not source-level markdown headings.

## Authoring helpers

The `Text` helper class provides typed construction helpers for specs, blocks, bullet items, and inline runs, including:

- `Text.Plain(...)`
- `Text.Markup(...)`
- `Text.Paragraph(...)`
- `Text.BulletList(...)`
- `Text.Item(...)`
- `Text.Run(...)`
- `Text.Strong(...)`
- `Text.Emphasis(...)`
- `Text.Code(...)`
- `Text.Link(...)`

These helpers are a Standard-owned authoring surface and do not replace `Machina.Core.Authoring.UI.Text(string)` in M6b.

## Non-goals in M6b

M6b does not include:

- renderer integration
- raster text behavior changes
- `DrawTextCommand` changes
- layout resolver changes
- StandardUI label/button migration
- dynamic font sizing
- MSDF, glyph atlas, or font backend work
- full Markdown compatibility
- heading support
- browser or HTML assumptions

## M6c+ roadmap

Future milestones remain focused on integration after the model is stable:

1. measurement and line layout inside assigned text boxes
2. text layout policy execution for wrap, overflow, leading, block gap, list gap, align, and vertical align
3. renderer bridge from text layout output to draw commands
4. StandardUI adoption for labels, titles, captions, and control text policies

The layout boundary remains unchanged: frames place text boxes; Standard text lays out text inside those boxes.

## M6c update

M6c now lands the next layer: deterministic measurement and layout under `Machina.Standard.Text`.

That work adds:

- `MachinaTextPolicy`
- `MachinaTextBox` / `MachinaTextSize`
- `MachinaTextLayoutResult`
- line/run box records
- renderer-independent layout diagnostics
- deterministic paragraph/list/inline layout inside an assigned rectangle

M6c still does not render and still does not migrate `UI.Text`, `StandardUI.Button`, `StandardUI.Card`, `StandardUI.Checkbox`, or `StandardUI.Switch`. Those integrations remain future work for M6d+.

## M6d update

M6d adds a proof bridge from `MachinaTextLayoutResult` to existing `DrawTextCommand` output, but keeps ownership split:

- Standard text owns model/parser/layout only.
- Dominatus/render bridge code owns draw-command emission.
- Primitive `UI.Text` and existing StandardUI controls remain on their current paths.

The full measurement audit and bridge details live in `docs/machina-standard-text-render-bridge-m6d.md`.
