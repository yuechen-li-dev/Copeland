# Oblivion code-first workflow — M19a

> Historical milestone note: M19j replaced this experimental App executable and
> handwritten command syntax with `src/Oblivion/Oblivion.Cli`. Use
> `oblivion workspace show`, `oblivion page list`, `oblivion card list`, and
> `oblivion card show`; see `oblivion-cli-baseline-m19j.md`. Commands below
> document the M19a proof and are no longer runnable compatibility syntax.

## Canonical useful workflow

This workflow uses the real repository-owned sample workspace and the `selected-doc-dogfood` technical card. Run it from the repository root.

```powershell
$project = "src/Oblivion/Oblivion.App/Oblivion.App.csproj"
$workspace = "src/Oblivion/Oblivion.App/OblivionSampleWorkspace/workspace.oblivion.json"

dotnet run --project $project -- inspect --workspace $workspace --json
dotnet run --project $project -- pages --workspace $workspace
dotnet run --project $project -- cards execution-roadmap --workspace $workspace
dotnet run --project $project -- show selected-doc-dogfood --workspace $workspace --json
dotnet run --project $project -- actions selected-doc-dogfood --workspace $workspace --json
dotnet run --project $project -- artifacts --workspace $workspace --json
```

The inspection identifies both durable sources without opening implementation code:

```text
card source:    cards/selected-doc-dogfood.card.toml
content source: body/selected-doc-dogfood.md
```

Edit `src/Oblivion/Oblivion.App/OblivionSampleWorkspace/body/selected-doc-dogfood.md` with an ordinary code editor. The M19a trial corrected its two repository-document links from unsafe traversal-shaped targets to product root-relative targets:

```text
../../../../docs/...  ->  /docs/...
```

Reload, validate, and inspect through the semantic product path:

```powershell
dotnet run --project $project -- validate --workspace $workspace --json
dotnet run --project $project -- invoke selected-doc-dogfood refresh-markdown --workspace $workspace --json
dotnet run --project $project -- show selected-doc-dogfood --workspace $workspace --json
```

Expected evidence:

```text
workspaceId=machina-sample
pageId=execution-roadmap
cardId=selected-doc-dogfood
actionId=refresh-markdown
effectKind=refreshMarkdown
status=completed
card diagnostics before edit=2 errors
card diagnostics after edit=0 errors
workspace pages after reload=4
workspace cards after reload=34
```

Failure recovery is also semantic:

```powershell
dotnet run --project $project -- invoke selected-doc-dogfood missing-action --workspace $workspace --json
```

This exits nonzero with `OBLIVION-ACTION-NOT-FOUND`, the workspace/page/card/action IDs, and the next command to run for discovery.

## Product state before and after

Before M19a, `inspect --json` was interpreted as a manifest filename. The agent had to read `Program.cs`, raw workspace assets, docs catalog generation, and card handler code to reconstruct state. The selected card also contained two targets rejected by the Markdown binder.

After M19a, the same shell discovers 4 pages, 34 cards, 3 runtime actions for the trial card, and 7 artifact references. The durable Markdown edit is visible after a completed typed reload and the card-specific diagnostics are clean. Workspace-wide validation still reports 8 warnings owned by other real documentation cards; they are retained as useful product evidence rather than hidden.

## UI and machine roles

The machine surface was better for exact IDs, source paths, action availability, diagnostics, artifact ownership, and before/after verification. The visual UI remains better for reading flow, card density, page switching, inspector composition, and expanded reading mode. Neither projection should be used to reconstruct information already authoritative in the other.

## Files touched by the workflow

```text
src/Oblivion/Oblivion.App/OblivionSampleWorkspace/body/selected-doc-dogfood.md
src/Oblivion/Oblivion.App/Application/OblivionProductSurface.cs
src/Oblivion/Oblivion.App/Application/OblivionCommandLine.cs
src/Oblivion/Oblivion.App/Program.cs
tests/Oblivion/Oblivion.App.Tests/AppTests.cs
```
