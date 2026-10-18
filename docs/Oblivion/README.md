# Oblivion documentation

Oblivion is a persistent technical-work workspace organized as pages and cards. Its application owns workspace state and operations; Machina projects that state into UI, while CLI and desktop hosts provide separate ways to inspect or use it.

## Learning path

1. [Current architecture and run guide](architecture.md) — project responsibilities, persistence boundaries, current CLI and desktop entry points, source, tests, and a first workflow.
2. [Product contract](oblivion-product-contract-m18b.md) — durable product state versus session/UI state and the core ownership law.
3. [CLI baseline](oblivion-cli-baseline-m19j.md) and [command registry milestone record](oblivion-command-registry-m19m.md) — historical command snapshots; consult the current source-linked guide above for the live command tree and current App registry.
4. [Current authoring examples](#current-authoring-examples) — runnable workspace fixtures already in the repository.

## Current authoring examples

- [Older App sample workspace](../../src/Oblivion/Oblivion.App/OblivionSampleWorkspace/) — the older `workspace.oblivion.json` format used by App-era examples; it is not the first structured-vault CLI example.
- [Structured notebook fixture](../../src/Oblivion/Oblivion.Standalone/M19iNotebook.oblivion/) — workspace, pages, cards, and content.
- [Diagram-card fixture](../../src/Oblivion/Oblivion.Standalone/M19oDiagramCards.oblivion/) — diagram declarations and authored Copeland source.
- [Function-card fixture](../../src/Oblivion/Oblivion.Standalone/M20fFunctionCards.oblivion/) — function card declarations, `.tsxtest` inputs, and content. The project excludes this fixture's authored source from its own compilation; the application materializes and runs it through the product path.
- [Table-card fixture](../../src/Oblivion/Oblivion.Standalone/M20eTsonTables.oblivion/) — TSON-backed table example.

## Milestone evidence

The remaining `oblivion-*-mNN*.md` and similarly named files record milestone decisions, dogfood runs, measurements, and acceptance evidence. Read them for historical context, not as a second current API reference. Older guides can show syntax later replaced by `Oblivion.Cli`; the current executable path is documented in [architecture.md](architecture.md). These reports are retained because they contain provenance and evidence.
