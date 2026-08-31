# Oblivion agent presentation trial — M19c

> Historical milestone note: M19j removed the experimental App executable and
> handwritten CLI. The presentation inspection/realization contracts remain
> internal App APIs; commands below record the M19c proof and are not current
> `oblivion` syntax. Current workspace commands are documented in
> `oblivion-cli-baseline-m19j.md`.

## Trial task

Codex authored a human-facing briefing on the M18/M19 Oblivion architecture and artifact evolution. The trial used a standalone repository-owned workspace at `artifacts/m19c/trial-workspace/workspace.oblivion.json`, validated it through `Oblivion.App`, exported it through the real Machina Presenter path, visually inspected the result, and re-inspected the generated PNG through the M19b artifact surface.

The trial did not add a new presentation API. It intentionally used current persistence and runtime contracts to discover what the missing authoring surface should be.

## Content selected

Six durable cards represent eight presentation content kinds:

1. summary/status;
2. Markdown document;
3. Mermaid diagram source inside the document;
4. C# code excerpt;
5. artifact metadata;
6. generated PNG image artifact;
7. decision;
8. next actions.

The two real artifact payloads are the trial workspace JSON and the generated 1440×900 briefing PNG. The code excerpt also carries a semantic-only source artifact declaration.

## Authoring source

The current source required one JSON manifest, one page TOML, six card TOML files, one Markdown file, and two artifact TOML files. This is durable, inspectable, and easy to patch, but too much ceremony for an agent composing one briefing. Content order is duplicated in the workspace manifest while content meaning is split among card kind, status, title, tags, body format, artifact declarations, and handler-derived behavior.

The code card contains the C# shape Codex naturally wanted instead:

```csharp
return Presentation.Create(
    title: "M18/M19 architecture",
    content:
    [
        Content.Summary("Persistent technical state now has two projections."),
        Content.Diagram(DiagramSource.Mermaid(architecture)),
        Content.Code(workspaceResolverExcerpt, language: "csharp"),
        Content.Artifact(presenterPng),
        Content.Decision("Use a semantic stream with optional layout groups."),
        Content.NextActions("Prototype projection and Mermaid rendering.")
    ]);
```

## Layout chosen

The presentation uses the current default vertically ordered Oblivion card stack, with the selected-card inspector beside it on a 1440×900 wide shell. No custom coordinates or grid were necessary. The screenshot shows the first three cards—summary, architecture, and code—in source order, while the inspector exposes selected-card metadata and body details.

This validates sequence as the default. The only relationships that wanted optional spatial composition were code beside rendered output and implementation before/after. Neither justified rebuilding the whole content tree as a grid.

## Reproduction

From the repository root:

```powershell
$workspace = Resolve-Path "artifacts/m19c/trial-workspace/workspace.oblivion.json"

dotnet run --project src/Oblivion/Oblivion.App/Oblivion.App.csproj -- `
  validate --workspace $workspace --json

dotnet run --project src/Oblivion/Oblivion.App/Oblivion.App.csproj -- `
  inspect --workspace $workspace --json

./tools/Export-MachinaPresenter.ps1 `
  -OblivionWorkspace $workspace `
  -SelectedSection oblivion `
  -SelectedTab cards `
  -NavigationPage cards `
  -SelectedCard m19c-summary `
  -OutputPath artifacts/m19c/trial-workspace/artifacts/m19c-human-briefing.png `
  -Width 1440 `
  -Height 900

dotnet run --project src/Oblivion/Oblivion.App/Oblivion.App.csproj -- `
  artifact show m19c-artifact briefing-png --workspace $workspace --json
```

Observed result:

```text
valid=true
pages=1
cards=6
errors=0
warnings=0
PNG=1440x900
PNG exists=true
PNG mediaType=image/png
PNG generated=true
```

## What worked

- Stable workspace/page/card/artifact IDs made agent inspection exact.
- Source order produced a readable vertical briefing without layout authorship.
- Markdown, plain text, code-like text, tags, status, artifacts, and inspector metadata all traveled through the real path.
- The existing Presenter export made a reproducible human proof artifact.
- M19b artifact resolution exposed the PNG's address, safe absolute path, existence, extension, byte count, media type, generated state, and provenance.
- A selected-card inspector complements rather than duplicates the narrative stream.
- The same durable state served the JSON agent view and visual human view.

## What sucked

1. A six-card briefing required eleven hand-authored source assets plus the generated PNG.
2. `OblivionCardKind` forced code display through `code-fact`, which also implied execution-oriented actions irrelevant to presentation.
3. Summary, decision, and next actions are represented indirectly through `Status`/`Note`, title, and tags rather than typed narrative roles.
4. Mermaid source remained readable Markdown fallback but did not render as a diagram.
5. The Presenter navigation shell recognizes the `oblivion` section identity. An initial valid workspace using section ID `briefing` silently exported the Overview page instead of the requested briefing; changing only the section ID fixed the real path. The exporter should eventually diagnose an unknown selected section/page rather than fall back.
6. The generated flag must be repeated on the card artifact declaration even when the referenced artifact asset says `generated = true`; the asset currently supplies label/kind/path but not generated state.
7. The PNG is resolvable and externally openable but is not rendered inline in its card.
8. The selected human result shows body excerpts in a fixed-height dark region; code and Markdown need content-aware presentation policy rather than pretending all bodies are equivalent.
9. Layout and content cannot currently be swapped independently: the card list order is durable, but a wide comparison would require workbench-specific geometry code.

## What Codex wished existed

- one regular C# `Presentation` constructor with a content array;
- typed narrative roles rather than card-handler selection;
- `DiagramSource.Mermaid` with source retained and derived SVG/PNG provenance;
- code source with language and optional file/span metadata;
- artifact content that can request an inline-safe read-only projection while preserving external open;
- filters for human, agent, compact, and handoff projections;
- optional `Compare`, `Columns`, `Grid`, and `Focus` groups referencing content IDs;
- a projection to existing Oblivion cards so current persistence/runtime/UI remain usable;
- strict navigation target diagnostics in Presenter export.

## Human-facing result

The human result is `artifacts/m19c/trial-workspace/artifacts/m19c-human-briefing.png`. It is a genuine Machina Presenter output, not a mockup. It proves that a vertical card stream plus inspector is already a credible reading surface. It also proves the missing pieces: Mermaid and PNG are not inline, code has weak formatting, and six cards do not fit one viewport without scrolling.

The first viewport successfully communicates title hierarchy, content order, card roles, tags, source references, selected identity, and the start of the proposed C# authoring model. It is useful but not yet a polished technical presentation.

## Agent-facing result

The agent view is stronger for exact IDs, source locations, counts, artifact identity, existence, media type, byte length, generated state, and provenance. It does not communicate reading rhythm, density, clipping, or hierarchy as quickly as the screenshot.

The correct product keeps both projections over the same semantic state. An agent should not infer artifact metadata from pixels, and a human should not read a JSON envelope to understand the architecture narrative.

## Recommended primitives

- narrative content with role: summary, decision, question, result, next actions;
- document content with Markdown source/reference;
- code content with language and source/span metadata;
- diagram content with Mermaid as the first source format;
- data content, initially table only;
- artifact content, with existing PNG eligible for inline read-only projection;
- diagnostic content;
- default semantic stream;
- optional comparison/columns/grid/focus layout groups by content ID;
- semantic filters for agent, human, compact, and handoff projections.

No chart, video, rich editor, execution, or generic media framework is justified by this trial.
