# Oblivion presentation materialization — M19f

## Projection

`PresentationMaterializer.Materialize` validates one semantic source and returns `MaterializedPresentation`: the original source, one existing `OblivionWorkspacePage`, one in-memory `OblivionWorkspace`, ordered content-to-Card mappings, resolved layout bands, and diagnostics.

The Card ID function is deterministic:

```text
presentation.<presentation-id>.<content-id>
```

The Page ID is `cards`, allowing the current Oblivion navigation Page to host the runtime materialization. Workspace identity is `presentation.<presentation-id>`. Repeated projection produces byte-equivalent serialized Cards and ordering. Content edits retain Card identity; layout edits retain both content and Card identity.

## Content mapping

| Presentation content | Existing Card semantics | Mature downstream presenter |
| --- | --- | --- |
| Summary | Status + plain body | native readable text |
| Markdown | Note + Markdown body/reference | `AvaloniaReadOnlyDocument` / native fallback |
| Code | CodeFact + plain source/reference | `AvaloniaReadOnlyCode` / native fallback |
| Mermaid diagram | Note + Markdown Mermaid fence | `ExternalMermaidRenderer` plus retained Markdown |
| Artifact/PNG | Artifact declaration + plain fallback | `AvaloniaImage` when resolved / metadata fallback |
| Decision | Note + plain body | native readable text |
| NextActions | Note + numbered plain body | native readable text |

No Markdown, code, Mermaid, or PNG rendering moved into `Oblivion.Presentation`.

## Layout application

Materialization turns the default stream and optional relationships into ordered bands. `OblivionWorkbench` consumes those bands only for a code-authored presentation: Stream and Focus are full width; Compare and Columns divide the existing wide Cards pane into two or three bounded columns. The ordinary workspace path is unchanged. Compact mode keeps source order because narrow horizontal composition is not readable.

## Provenance and inspection

The materialized mapping directly stores presentation ID, content ID, content kind, Card ID, source reference, layout group, and Card provenance. Generated Card provenance uses producer `oblivion.presentation.materializer.v1` and includes presentation/content identity. Source-bearing items retain their authored reference; artifact declarations retain artifact source truth.

`oblivion presentation inspect [--json]` reports all mapping facts plus selected mature presenter and diagnostics. It performs no GUI initialization and reads no persistence manifest. `presentation realize-diagram` explicitly invokes the already-qualified M19e renderer and cache.

## Diagnostics

Validation is deterministic and rejects duplicate content IDs, invalid code ranges, duplicate layout IDs, unknown content references, duplicate members, multiple layout membership, empty groups, and unsupported group sizes. Materialization never silently drops content.

## Persistence and Page ownership

Presentation is code-authored runtime input. It materializes to one Page and existing Cards. It is neither a second durable product model nor a deck/pagination system. Format-1 persistence readers, writers, and schema remain unchanged.
