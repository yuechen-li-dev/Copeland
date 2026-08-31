# Oblivion first-class Diagram Cards — M19o

## Outcome and semantic shape

M19o establishes `OblivionCardKind.Diagram` as a first-class Card. The durable Model owns only `OblivionDiagramSource(kind, reference, symbol, projection)`. The bounded M19o vocabulary is `CopelandFlow` plus `State`; no Mermaid source, compiler bound nodes, renderer instance, AST, or manual state/transition copy enters `Oblivion.Model`.

The structured-vault shape is:

```toml
card_kind = "diagram"

[diagram]
kind = "copeland-flow"
reference = "source/VehicleFlow.ts"
symbol = "VehicleFlow"
projection = "state"

[body]
format = "plain"
text = ""
```

`body` remains present for format-1 compatibility but carries no fake diagram text. The semantic truth is `[diagram]`. Diagram references must be workspace-relative and traversal-free. A Diagram declaration on another Card kind, a missing declaration on a Diagram Card, or an unsupported source/projection is a deterministic vault error.

## Compiler-derived realization

`Oblivion.App` resolves the workspace-relative source and invokes the existing Copeland path:

```text
Copeland source file + flow symbol
  -> CopelandCompiler
  -> BoundProgram
  -> StateMachineDiagramProjection
  -> Diagram IR
  -> MermaidEmitter
  -> OblivionExternalMermaidRenderer
  -> qualified PNG/cache sidecar
  -> AvaloniaOblivionContentHost
```

The Card never persists generated Mermaid. Guards, initial/final states, state identities, and transitions come from compiler semantics. The semantic fingerprint hashes the projected semantic identity plus emitted backend source. Existing M19e cache keys retain source hash, renderer identity/version, output format/options, producer, source reference, and workspace/page/Card/content ownership.

## Physical Card behavior

Collapsed Diagram Cards use the ordinary Card shell, title, subtitle, `Diagram · State` badge, status, source hint, and square affordance. The collapsed presentation plan has zero body items, so no miniature PNG, SVG, clipped diagram, or local scroller can appear.

Expanded Cards use the existing mature Avalonia content host. The Diagram handler requests a 920-pixel expanded Card, while the host uses `Stretch.Uniform`, most of the available Card width, and the existing bounded body viewport. Aspect ratio is preserved. The Page owns ordinary scrolling; the mature body has one clear bounded vertical scroller only when its content requires it. M19o adds no pan/zoom system.

Dark and light shells use the existing appearance tokens. The qualified renderer still emits its established white-background PNG. It is highly readable in both modes; in dark mode the white canvas is intentionally recorded as a backend-theme integration friction rather than redesigned here.

## CLI semantics

`oblivion card list` reports kind `diagram`. `oblivion card show vehicle-flow-state` reports the source kind/reference, symbol, projection, semantic fingerprint, derived-artifact status, renderer identity/version, provenance, and correlated diagnostics. It never returns PNG bytes.

M19o chooses content Option A: `oblivion card content <diagram-card>` returns `OBLIVION-CARD-CONTENT-NOT-TEXT`. Generated Mermaid is a backend product, not authored Card content. The existing Markdown-only `card push` remains unchanged; the dogfood fixture is directly authored in the structured vault, so no speculative `push-diagram` operation was justified.

## Failure behavior

Missing source, unknown flow, compiler diagnostics, unsupported projection, renderer failure, invalid cache, and missing/corrupt rendered output remain inspectable. Projection results retain the semantic Diagram source. Renderer results retain source hash, renderer contract, ownership, provenance, and diagnostics. A rendering failure does not mutate or erase the Card declaration.

## Architecture boundary

- `Oblivion.Model`: Card kind and durable semantic reference only.
- `Oblivion.Persistence`: format-1 TOML codec, validation, and safe path boundary.
- `Oblivion.App`: Copeland compilation/projection and derived realization orchestration.
- `Oblivion.UI`: Diagram handler and collapsed/expanded presenter selection.
- `Oblivion.Avalonia`: existing qualified image realization and aspect-preserving host.

Copeland does not depend on Oblivion. M19o adds no reflection query, runtime reflection, browser/WebView, networking, native SVG, diagram editor, pan/zoom, chart, or diagram taxonomy.

