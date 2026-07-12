# Machina Oblivion Phase Closeout M11g

## Purpose

M11g closes out the M11 Oblivion substrate phase.

The milestone is intentionally narrow:

- document the current static persisted-card architecture
- record the current golden path
- make Markdown the next Oblivion phase
- defer Roslyn and xUnit execution beyond M12
- add readiness audits without implementing execution, a Markdown renderer, a Markdown editor, or Visionary

## Current Oblivion substrate

M11 now leaves Oblivion in a coherent static state inside the canonical presenter shell introduced by M10:

- persisted workspace root manifest
- persisted pages
- persisted cards
- persisted metadata
- source paths
- card selection
- inspector detail view
- bounded card rendering
- cached scroll composition

No execution layer was added in M11g.

## Golden path

The current golden path is:

```text
workspace.oblivion.json
  -> page TOML assets
  -> card TOML assets
  -> static Oblivion card model
  -> presenter shell page render
  -> compact card list + inspector detail
  -> deterministic PNG export + deterministic manifests
```

This path is good enough for persisted static cards, metadata inspection, roadmap authoring, and document-first planning.

## Workspace persistence

M11d established the persistence split and M11g keeps it unchanged:

- `workspace.oblivion.json` is the JSON root graph/tree
- `*.page.toml` hold page metadata
- `*.card.toml` hold card metadata plus body text
- `*.artifact.toml` hold artifact metadata

Asset resolution remains workspace-relative and path-traversal guarded.

## Card model

The current card model remains static and metadata-first.

Supported kinds remain:

- `note`
- `status`
- `ui-preview`
- `artifact`
- `code-fact`
- `code-theory`

Actions and artifacts remain metadata only. `CodeFact` and `CodeTheory` are still placeholders.

## Inspector model

M11f added the current inspector split:

- compact cards stay in the left column
- selected-card details stay in the right column
- source path, tags, actions, artifacts, and execution-deferred notes remain visible

M11g keeps that model and updates the messaging so Markdown-first planning is visible before any execution work begins.

## Test topology

M11b already cleaned the test topology so the regular and slow loops are explicit:

- `dotnet test Machina.UI.slnx`
- `dotnet test Machina.UI.Slow.slnx`

M11g adds closeout, roadmap, export, and boundary tests on top of that topology without changing runtime scope.

## What is ready

- M10 presenter shell remains the canonical host
- M11 static persisted-card substrate is complete enough for roadmap and document-first dogfooding
- workspace persistence is ready
- card inspector is ready
- deterministic exports and manifests are ready
- placeholder code cards are visibly deferred instead of pretending execution exists

## What is intentionally deferred

- Markdown renderer implementation
- Markdown editor implementation
- live editing
- file watching
- Roslyn compilation and execution
- `[Fact]` / `[Theory]` runtime execution
- artifact-generation runtime
- notebook runtime
- Visionary editor/source workspace layer

`[Fact]` / `[Theory]` execution is deferred to M13+ or later unless explicitly re-prioritized.

## Why Markdown comes before code execution

Markdown comes first because:

- Markdown is already the format Codex produces heavily.
- Markdown is the lowest-friction way to dogfood Oblivion as a notebook/workbench.
- Obsidian's core value is Markdown note organization.
- Markdown cards let us validate reading, inspecting, organizing, persistence, and card UI before code execution.
- Execution has a much larger security, runtime, sandbox-claim, diagnostics, and result-model surface and should wait.

## Markdown readiness audit

The first Markdown milestone still needs explicit decisions for:

- Markdown card kind or body format
- plain text vs markdown body format
- heading, paragraph, list, code fence, and inline-code rendering
- links
- relative asset references
- source file path
- front matter relationship to TOML metadata
- whether `.md` files become cards or card bodies
- Markdown import/export
- preview vs edit mode
- no live editor in the first Markdown milestone

Preferred initial direction:

- TOML remains card metadata
- Markdown can be body content
- a card may point to a `.md` body file, or include inline markdown in TOML
- the first Markdown milestone should render only a small safe subset:
  headings, paragraphs, bullet lists, code fences, and inline code
- no editor yet

## Execution readiness audit

Execution remains future work and M11g adds no implementation.

Future work must define:

- trusted local code only
- not a sandbox
- execution result model
- stdout/log capture
- diagnostics
- exceptions
- artifact capture
- workspace-relative temp/output dirs
- permission model later
- determinism policy
- test isolation
- where results appear in the inspector
- how results persist

M11g makes no fake sandbox claims. `CodeFact` and `CodeTheory` cards remain placeholders.

## Visionary future layer

`Visionary` remains the future code editor/source workspace layer.

It is still future-only:

- no Visionary editor
- no source-workspace runtime
- no live editing layer
- no overlap with the current Markdown-first M12 recommendation

## Canonical commands

```powershell
dotnet test tests/Machina.UI/Machina.Presenter.Sample.Tests/Machina.Presenter.Sample.Tests.csproj
dotnet test Machina.UI.slnx
dotnet test Machina.UI.Slow.slnx
dotnet build Machina.UI.slnx --no-restore
git diff --check
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11g\presenter-oblivion-closeout-status.png -SelectedSection oblivion -SelectedTab cards -SelectedCard oblivion-substrate-status
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11g\presenter-oblivion-markdown-roadmap.png -SelectedSection oblivion -SelectedTab execution-roadmap -SelectedCard markdown-first-roadmap
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11g\presenter-oblivion-execution-deferred.png -SelectedSection oblivion -SelectedTab execution-roadmap -SelectedCard execution-deferred
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11g\presenter-oblivion-visionary-future.png -SelectedSection oblivion -SelectedTab execution-roadmap -SelectedCard visionary-future
```

## Next phase recommendation

Recommended phase plan:

```text
M11:
  Oblivion substrate complete enough for static persisted cards.

M12:
  Markdown document/card support.

M13+:
  Trusted local C# execution proof.

Visionary:
  Future code editor/source workspace layer.
```

M11g therefore ends the substrate phase by shifting the next real Oblivion work to Markdown cards and Markdown document dogfooding, not Roslyn execution.

## M12a follow-through note

M12a lands the first Markdown follow-through as a Copeland frontend milestone, not as a renderer/editor milestone.

- `src/Copeland/Copeland.Markdown` now parses a bounded Copeland Markdown subset into backend-neutral document MIR.
- M12b then integrates that frontend into Oblivion as text-card body loading while keeping pages as stacks of typed cards.
- M12c follows with the first visible Markdown rendering dogfood pass for compact cards and the inspector.
- single-file Markdown remains future export/import work, not canonical Oblivion storage.
- existing repo `.md` files now serve as the first real dogfood corpus.
- no Markdown editor was added.
- no Markdown editor was added.
- no Roslyn/xUnit execution was added.
- Roslyn and xUnit execution remain deferred.
