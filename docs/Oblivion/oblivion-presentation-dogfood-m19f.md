# Oblivion semantic presentation dogfood — M19f

## Outcome A

The real M19 briefing is authored in `M19PresentationDogfood.Create` using only Summary, Markdown, Code, Mermaid Diagram, PNG Artifact, Decision, NextActions, Compare, and Focus. It materializes seven existing Cards without manual Card construction or persistence assets.

## Before and after

The M19c source required 11 authored JSON/TOML/Markdown files, 180 lines, six manual Card definitions, repeated artifact plumbing, six card-kind/presenter choices, and duplicated workspace/page/card ordering. M19f uses one C# source. The actual `Create` method occupies 79 lines including proof-path resolution and two useful layout declarations; the semantic call itself is the majority of those lines.

| Measure | M19c | M19f |
| --- | ---: | ---: |
| authored briefing files | 11 | 1 |
| briefing-specific source lines | 180 | 79 |
| manual Card definitions | 6 | 0 |
| manual presenter/card-kind choices | 6 | 0 |
| manual artifact asset files | 2 | 0 |
| layout declarations | 0 | 2 |
| semantic content items | 8 concepts in 6 Cards | 7 typed items in 7 Cards |

This is not code golf: IDs remain explicit because they are the stable join between content, Cards, provenance, inspection, and layout.

## Human result

The real Machina Presenter path produced:

- `artifacts/m19f/m19f-semantic-presentation-human-proof.png`, showing the readable default stream and inspector;
- `artifacts/m19f/m19f-semantic-presentation-layout-proof.png`, showing Code and PNG proof Cards in one Compare band while Focus remains full width.

The first host attempt exposed an Artifact Card fallback with insufficient body space. The adapter now keeps artifact truth in the existing artifact declaration and supplies bounded readable fallback text. No renderer or Card chrome was redesigned.

Markdown and code select their mature read-only presenters. Mermaid cold realization succeeded with pinned `mermaid-cli@11.16.0`; the immediate repeat was a cache hit with the same source hash and cache key. PNG inspection selects `AvaloniaImage` for the existing absolute proof while deterministic export retains native metadata fallback. Decision and NextActions remain readable plain semantic units. Collapsed/expanded state and inspector behavior stay on existing Cards.

## Agent result

`dotnet run --project src/Oblivion/Oblivion.App/Oblivion.App.csproj -- presentation inspect --json` returns seven content items, seven stable Card IDs, kinds, sources, layout membership, presenter choices, producers, and empty diagnostics. It requires neither implementation-code reading nor persisted workspace source.

## What worked

- One ordinary C# array replaced the persistence-oriented authoring bundle.
- Stable typed IDs survived content and layout projection.
- Default order required no layout declaration.
- Compare was materially useful for source beside proof; Focus documented the full-width architecture intent.
- The M19d and M19e paths worked unchanged.
- Human and agent surfaces share the same materialized Cards.

## What sucked

- Read-only code currently maps to `CodeFact`, so its existing handler still advertises a deferred run action. That is honest reuse but semantically noisy.
- Card provenance has no typed presentation/content origin fields. M19f retains a typed runtime mapping and a deterministic producer string rather than changing durable persistence.
- Wide Compare is useful at 1440 px but dense; compact mode correctly returns to source order.
- Headless PNG export shows artifact metadata, while the live Avalonia overlay owns inline image decode. Both are deliberate existing M19d behaviors.

No additional content kind was justified. No trial-only persisted M19c source was deleted because it remains historical before-proof.

## Recommended exact M19g

Add one read-only `CodeExcerpt` Card semantic and handler adapter so presentation code retains source/open behavior and the mature code presenter without inheriting CodeFact execution actions. Keep the Presentation union, layout bands, renderer infrastructure, persistence format, and Machina shell unchanged. Dogfood it by changing only the M19 source item and prove that the deferred run action disappears while Card identity and inspection remain stable.
