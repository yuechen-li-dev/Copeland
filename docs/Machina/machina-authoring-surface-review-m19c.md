# Machina agent authoring surface review — M19c

## Outcome

**Outcome A — clear authoring direction established.** The preferred direction is a hybrid:

> Codex authors an ordered semantic content stream in ordinary C#. Spatial layout is an optional, separate declaration over stable content IDs. Machina lowers that declaration to its existing UI and layout pipelines.

`UI.Stack`, `UI.HStack`, `UI.VStack`, `UI.Grid`, frames, tracks, rows, and coordinates remain useful, but they are not the default language for explaining technical work. They describe arrangement. Summary, document, code, diagram, data, artifact, diagnostic, decision, result, question, and next-action meaning belongs above them.

## Evidence reviewed

### Native Machina.UI C#

The current `Machina.Core.Authoring.UI` surface authors a `UiNode` tree. `Text`, `Button`, rich text payloads, actions, and declared semantics contain presentation meaning. `Row`, `Column`, `Stack`, `VStack`, `HStack`, `Grid`, `GridCell`, `StackItem`, `Track`, `At`, `Anchor`, and `Layer` encode layout policy. `UiLowerer` then emits `LayoutRow` records, which compile to `LayoutDocument`, resolve to `ResolvedLayoutDocument`, and feed presentation/render operations.

This surface is readable for localized components. It is much less natural for a technical briefing because the author must choose layout containers, tracks, gaps, padding, sizes, and sometimes coordinates before saying what the briefing means. Content and arrangement are nested together, so swapping a wide comparison for a sequential narrow view reconstructs the tree. Stable IDs are also frequently present for layout, hit testing, or host routing rather than content identity.

The native pipeline nevertheless supplies strong lower layers: deterministic lowering, typed layout declarations, explicit resolved geometry, hit testing, raster output, and reproducible playback. M19c does not replace them.

### Copeland TS Machina

Copeland TS has two related authoring directions.

The functions-first `MachinaView` experiment uses `Root`, `Container`, `VStack`, `HStack`, `Text`, `Button`, `Toggle`, typed frames, tracks, styles, and source spans. This is a tree-shaped authored MIR with compiler-assigned identity. It removes row bookkeeping but still asks the author to express a visual tree.

The `layout` and `stream` syntax goes further. A stream combines named semantic regions, structural containers, geometry, and renderable expressions, then normalizes them through the same bound layout graph, bindings, tables, browser hosts, and resolved geometry. It is concise when the content-to-region relationship and geometry are part of one static composition. It is not a general semantic content stream: the current `stream` construct still declares width, height, row/column/grid/overlay regions, gaps, and fixed/fill dimensions. Reusing the same content under a different topology requires a reusable explicit layout/binding split or a new stream declaration.

The Copeland TS survey already found that functions and trees are better primary source than manual rows, while rows remain excellent compatibility, inspection, fixture, and oracle shapes. M19c agrees and places technical-presentation semantics one level above even the functions-first view tree.

### Copeland TS `stream`

The useful law is not the literal grammar. It is the separation the experiments reveal:

```text
semantic stream = what exists and what deserves human attention
layout declaration = how selected semantic items relate spatially in this projection
```

The current TS `stream` partially combines these concerns. Named regions, singular bindings, and bounded ordered collections are valuable: names express semantic identity; collection order expresses position; the compiler should not invent `item0` identities. Structural nodes and table rows are valuable when the spatial relationship is the point. The default implicit column also confirms that sequence is sufficient for many presentations.

For an agent-authored briefing, content should be constructible before a layout is chosen. The same content can then support:

- an agent debugging projection that includes diagnostics, source, and intermediate artifacts;
- a human briefing projection that selects summary, architecture, decisive evidence, and result;
- a compact summary that retains only summary, decision, and next actions;
- a wide comparison that groups existing content IDs side by side.

Those are a combination of semantic filtering and layout declarations, not four reconstructed content trees.

### MachinaLayout.JS

The imported reference and prior survey show several ideas that aged well:

- deterministic typed frames and explicit resolution errors;
- flat rows as a diffable, serializable, inspectable layout relation;
- stable IDs, parent IDs, sibling order, source/debug labels, layers, and slots below authoring;
- arithmetic stack and grid laws with bounded behavior;
- separation of authored intent from resolved numeric rectangles;
- row/table inspection as a first-class debugging projection.

Several ideas should not be ported literally to the presentation authoring surface:

- author-written `LayoutRow[]` splits hierarchy, order, identity, frame, and renderer slot across records;
- nested `M.*`, `HStack`, and `VStack` helpers are better than rows but remain function-shaped geometry;
- manual coordinates expose renderer bookkeeping and do not adapt;
- renderer slot strings and data registries make a small technical surface require coordinated distant declarations;
- the CSV row/table surface is excellent for genuinely tabular overlays, bulk spatial editing, inspection, and fixtures, but poor for narrative sequence.

MachinaLayout.JS rows belong at `LAYOUT_MIR`. Nested layout functions and native `UI.Stack`/`UI.Grid` belong at `LAYOUT_DECLARATION`. Resolved rows belong at `RESOLVED_LAYOUT`.

### Oblivion Page/Card composition

Oblivion's durable workspace model owns pages, cards, body content, actions, artifacts, diagnostics, and provenance. It is already close to a semantic presentation model, but `OblivionCardKind` mixes human meaning (`Note`, `Status`) with current runtime behaviors (`UiPreview`, `Artifact`, `CodeFact`, `CodeTheory`). A model-authored presentation should not choose a code-fact execution handler merely to display a code excerpt.

Page remains a good durable grouping, navigation, ordering, and projection boundary. Card remains a good runtime unit for localized actions, artifacts, diagnostics, provenance, expansion, inspection, and scrolling. Neither needs to be the smallest authoring primitive.

Recommended relationship:

```text
Presentation + semantic content
    -> projection/filter + optional layout groups
    -> materialized Oblivion Page and Cards
    -> Machina UI tree
    -> Layout MIR
    -> resolved geometry
    -> interactive/raster output
```

Advanced callers may wrap an existing `OblivionCard`, but new briefing source should not start by selecting card handlers.

## Layer classification

| API or type | Classification | Finding |
|---|---|---|
| Proposed `Presentation`, `PresentationContent`, factories | `AUTHORING_API` | Ordinary C# source authored by Codex. |
| Narrative/document/code/diagram/data/artifact/diagnostic content | `SEMANTIC_CONTENT` | Durable meaning independent of geometry. |
| Proposed projection filters and layout groups | `LAYOUT_DECLARATION` | Select and arrange content IDs without rebuilding content. |
| Native `UI.Text`, `UI.Button`, rich text, semantic/action records | `AUTHORING_API` + `PRESENTATION_IR` | Useful localized visual/component construction; lower than presentation semantics. |
| Native `UI.Stack`, `UI.Grid`, `At`, `Anchor`, stack items, tracks | `LAYOUT_DECLARATION` | Explicit arrangement policy, not final technical meaning. |
| Native `UiNode` tree | `PRESENTATION_IR` with embedded `LAYOUT_DECLARATION` | Mixed content/style/layout authoring tree. |
| Native `UiRow` / `LayoutRow`, `LayoutDocument` | `LAYOUT_MIR` | Normalized identities, parentage, frames, arrange specs, and renderer payload links. |
| Native `ResolvedLayoutDocument` / resolved rectangles | `RESOLVED_LAYOUT` | Numeric geometry after layout resolution. |
| Prepared presentation operations / render commands | `PRESENTATION_IR` | Renderer-neutral paint/input preparation. |
| Raster frame, PNG, host controls | `RENDER_OUTPUT` | Human-visible output and host projection. |
| Copeland TS `stream` syntax | `AUTHORING_API` mixing `SEMANTIC_CONTENT` and `LAYOUT_DECLARATION` | Concise static co-authoring when content and geometry are inseparable. |
| Copeland bound layout nodes, bindings, collection rows, projected layout tables | `LAYOUT_MIR` + binding-oriented `PRESENTATION_IR` | Shared normalized relation below multiple syntax projections. |
| MachinaLayout.JS `LayoutRow[]` | `LAYOUT_MIR` | Excellent serialized/debug form; too much bookkeeping as primary source. |
| Oblivion workspace/page/card/body/artifact model | `SEMANTIC_CONTENT` + durable `PRESENTATION_IR` | Strong runtime/durable substrate, but card kinds are too handler-shaped for the smallest presentation vocabulary. |
| Oblivion built-card/runtime/compact/inspector views | `PRESENTATION_IR` | Projection-specific materialization. |

## Smallest useful semantic vocabulary

Avoid a CLR class per visual widget. Use a small closed family with typed sources and roles:

```csharp
public abstract record PresentationContent(ContentId Id);

public sealed record NarrativeContent(
    ContentId Id,
    NarrativeRole Role,
    string Text) : PresentationContent(Id);

public sealed record DocumentContent(
    ContentId Id,
    DocumentSource Source) : PresentationContent(Id);

public sealed record CodeContent(
    ContentId Id,
    CodeSource Source,
    string? Language = null) : PresentationContent(Id);

public sealed record DiagramContent(
    ContentId Id,
    DiagramSource Source) : PresentationContent(Id);

public sealed record DataContent(
    ContentId Id,
    DataPresentation Data) : PresentationContent(Id);

public sealed record ArtifactContent(
    ContentId Id,
    OblivionArtifactAddress Address) : PresentationContent(Id);

public sealed record DiagnosticContent(
    ContentId Id,
    IReadOnlyList<PresentationDiagnostic> Diagnostics) : PresentationContent(Id);
```

`NarrativeRole` covers `Summary`, `Decision`, `Question`, `Result`, and `NextActions` without creating five rendering classes. `DocumentSource` covers Markdown and other durable document sources. `DataPresentation` starts with a table; chart support waits for real pressure. Images are resolved artifacts with a preferred inline projection, not a parallel media framework.

Factories keep common code terse while records remain the visible contract.

## Recommended C# authoring model

This is the concrete source Codex should write:

```csharp
using static Machina.Presentation.Authoring.Content;

return Presentation.Create(
    title: "M18/M19 architecture",
    content:
    [
        Summary(
            id: "summary",
            text: "Persistent technical state now has machine and human projections."),
        Markdown(
            id: "architecture-notes",
            source: DocumentSource.File("docs/Oblivion/oblivion-llm-first-product-baseline-m19a.md")),
        Diagram(
            id: "architecture-flow",
            source: DiagramSource.Mermaid(architectureMermaid)),
        Code(
            id: "workspace-owner",
            source: CodeSource.File("src/Copeland/Copeland.TS.Workspace/WorkspaceCommand.cs"),
            language: "csharp"),
        Artifact(
            id: "human-proof",
            address: briefingPng),
        Decision(
            id: "direction",
            text: "Use a semantic stream with optional layout groups."),
        NextActions(
            id: "next",
            items:
            [
                "Project semantic content to existing Oblivion cards.",
                "Add external Mermaid rendering with provenance.",
                "Add read-only inline PNG presentation."
            ])
    ]);
```

The default is ordered vertical flow. A wide projection can reuse the exact objects:

```csharp
PresentationProjection humanWide = PresentationProjection.Create(
    name: "human-wide",
    include: ContentFilter.Tags("human"),
    layout: PresentationLayout.Stream(
        groups:
        [
            LayoutGroup.Compare(
                id: "implementation-and-proof",
                left: "workspace-owner",
                right: "human-proof"),
            LayoutGroup.Columns(
                id: "decision-and-next",
                content: ["direction", "next"])
        ]));
```

Ungrouped included content remains in semantic order. Groups reference stable IDs and never take ownership of content. A compact projection changes the filter and may use the default stream; an agent projection includes diagnostics and intermediate artifacts. This makes layout replacement and semantic filtering independent operations.

## Mermaid

`Mermaid` should be a first-class `DiagramSource`, not a dedicated top-level card kind and not an opaque image.

```text
DiagramContent
    source: Mermaid text
    derived artifacts: SVG/PNG
    provenance: renderer, source content identity, render parameters
```

Mermaid text is the durable editable truth because models already generate and revise it naturally. An external renderer is acceptable initially. A future structured graph API may sit above Mermaid when graph manipulation requires it; it should lower to `DiagramContent`, not sit below a raster artifact. Machina does not need a complete graph engine.

## Projection model

Use two mechanisms only:

1. a semantic filter chooses content by IDs, roles, tags, severity, or provenance;
2. a layout declaration optionally groups chosen IDs spatially.

The working/agent view includes dense diagnostics, source, evidence, and intermediate artifacts. The human review view includes summary, architecture, key code, result, and decision. The handoff view includes provenance, open questions, and next actions. Content remains shared.

## Artifact presentation pressure

| Kind | Disposition | Reason |
|---|---|---|
| Existing PNG | `INLINE_NATIVE_NOW` | It is already a visual result and the trial immediately wanted it visible without leaving the briefing. |
| Mermaid source | `INLINE_EXTERNAL_RENDERER` | Preserve source; derive SVG/PNG with provenance using an external renderer. |
| Markdown/document | `INLINE_NATIVE_NOW` | Existing renderer and expanded reading mode already provide value. |
| Code | `INLINE_NATIVE_NOW` | Existing code/plain rendering is useful, though language-aware formatting remains weak. |
| JSON/TOML/source artifact | `OPEN_EXTERNALLY` | Semantic metadata and external opening are sufficient until a concrete comparison requires inline structure. |
| Semantic-only artifact placeholder | `SEMANTIC_ONLY` | It has meaning but no payload to render or open. |
| Charts, audio, video | `NO_CURRENT_PRESSURE` | No M19 inventory or trial need justifies a framework. |

## Non-goals

M19c does not add a slide engine, VDOM, hooks, component lifecycle, rich editor, Markdown editor, execution engine, browser, CSS compatibility layer, chart/media framework, source-generator syntax, reflection registration, hidden context, networking, or a broad Machina rewrite. It does not change Oblivion persistence. The recommended types are an M19d prototype target, not an implementation landed in M19c.
