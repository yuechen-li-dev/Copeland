# Oblivion semantic presentation authoring — M19f

## Outcome

M19f establishes `Oblivion.Presentation` as the small Oblivion-facing authoring assembly. It depends only on `Oblivion.Model`. It does not depend on Presenter, Machina, Avalonia, Aurelian, persistence, or renderer infrastructure.

The source is ordinary C# and runtime composition for this milestone. It is not a new serialized language and format-1 JSON/TOML is unchanged.

## Root and content union

`Presentation` owns a typed `PresentationId`, title, ordered `PresentationContent` list, and optional `PresentationLayoutGroup` list. The initial closed semantic union is:

- `SummaryContent`
- `MarkdownContent`
- `CodeContent`
- `DiagramContent`, with Mermaid as its first `DiagramSource`
- `ArtifactContent`
- `DecisionContent`
- `NextActionsContent`

Every item owns a typed `PresentationContentId`, optional title, and optional provenance. Markdown, code, and Mermaid use `PresentationSource`, which retains inline content and an optional source reference. Code can select one validated inclusive line range. Artifact content retains its reference, kind, generated state, and label.

Content IDs must be unique within one presentation. They determine Card identity and do not change when layout changes.

## Default stream and layout

With no layout argument, authored order is the vertical reading stream. Authors never spell `VStack`, `Grid`, tracks, coordinates, widths, or frames.

The three bounded layout relationships are:

- `Compare(id, left, right)`: exactly two items share a horizontal comparison band in the wide host.
- `Columns(id, items)`: two or three items share a horizontal reading band.
- `Focus(id, item)`: exactly one item receives a full-width band.

Groups reference content IDs. Unknown IDs, repeated membership, duplicate members, duplicate group IDs, empty groups, and oversized columns produce explicit diagnostics and prevent materialization. Unreferenced content remains in source order. Every content item appears once. Compact mode deliberately falls back to the semantic stream.

## Ordinary C#

```csharp
using static Oblivion.Presentation.Content;
using static Oblivion.Presentation.Layout;

return Presentation.Create(
    id: "m19-architecture",
    title: "M19 architecture",
    content:
    [
        Summary("summary", summary),
        Markdown("notes", markdown),
        Diagram("flow", new DiagramSource.Mermaid(mermaid)),
        Code("source", code, language: "csharp"),
        Artifact("proof", briefingPng, kind: "png"),
        Decision("direction", decision),
        NextActions("next", actions)
    ],
    layout:
    [
        Compare("source-and-proof", "source", "proof"),
        Focus("architecture-focus", "flow")
    ]);
```

## Non-goals

M19f adds no builder lifecycle, generic slide/page engine, persisted Presentation schema, arbitrary grid DSL, coordinates, renderer handles, controls, editor, browser, chart, media framework, network service, execution model, or Machina-wide presentation language.
