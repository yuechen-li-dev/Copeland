# Machina Oblivion Markdown Body Integration M12b

## Purpose

M12b integrates `Copeland.Markdown` into Oblivion as a text-card body compiler and renderer.

The milestone goal is narrow and explicit:

- keep the canonical Oblivion document model as stacks of typed cards
- allow text/note card bodies to come from `.md` files
- compile those `.md` files through `Copeland.Markdown`
- carry `DocumentMir` plus diagnostics on loaded cards
- render compact previews and fuller inspector content

M12b does not add a Markdown editor, Roslyn execution, xUnit `[Fact]` / `[Theory]` execution, or Visionary.

## Core doctrine

```text
Oblivion page:
  Stack<Card>

Text/Note card:
  body format = Copeland Markdown

Image card:
  future image/media asset card

Table card:
  future structured table card

Video card:
  future media card

Code card:
  future code/execution card

Single-file Markdown:
  future export/import target, not canonical storage
```

## Oblivion page model

The canonical Oblivion model remains:

```text
workspace.oblivion.json
  -> sections/pages/card references

*.card.toml
  -> typed card metadata

body/*.md
  -> text body content for text/note cards
```

M12b does not turn an Oblivion page into one large Markdown document.

## Markdown as text-card body language

Markdown is now a body language for `note` and other text-oriented cards.

It is not the whole Oblivion page model, and it does not implicitly create image, table, video, or execution cards. Those remain future typed-card work.

## JSON/TOML/Markdown file split

Current split:

- `workspace.oblivion.json` keeps workspace/section/page/card topology
- `*.card.toml` keeps typed card metadata, status, tags, actions, and artifacts
- `body/*.md` keeps text-card body content

This preserves the typed-card workspace shell that M10 and M11 established.

## Card TOML body schema

Plain bodies still work:

```toml
[body]
format = "plain"
text = """
Static plain-text body.
"""
```

Markdown bodies now support:

```toml
[body]
format = "copeland-markdown"
path = "body/markdown-first-roadmap.md"
```

Optional inline Markdown is also supported:

```toml
[body]
format = "copeland-markdown"
text = """
# Heading

Inline Markdown body.
"""
```

Path policy in M12b:

- Markdown body `path` is workspace-root-relative
- absolute paths are rejected by default
- traversal outside the workspace root is rejected

## Sample workspace body files

The sample workspace now includes:

- `body/oblivion-substrate-status.md`
- `body/markdown-first-roadmap.md`
- `body/markdown-readiness-audit.md`
- `body/execution-deferred.md`
- `body/visionary-future.md`

These exercise headings, paragraphs, bullet lists, fenced code blocks, inline code, emphasis/strong, links, and a bounded malformed-Markdown diagnostics case.

## Rendering behavior

Compact card rendering:

- keeps cards small
- shows a concise preview lowered from `DocumentMir`
- shows Markdown and diagnostics badges when applicable

Inspector rendering:

- lowers `DocumentMir` into fuller text rows
- keeps headings distinct with `#` markers
- keeps lists as list rows
- renders fenced code blocks as static text only
- renders inline styles as plain-text approximations when rich inline styling is not available in the presenter shell

## Diagnostics

Markdown diagnostics are preserved on the loaded card body.

Behavior:

- malformed Markdown does not crash loading
- best-effort MIR still renders
- inspector shows a Markdown diagnostics panel
- missing Markdown body files report a bounded workspace diagnostic

## Path safety

M12b reuses the M11d path-safety posture:

- no absolute body paths by default
- no traversal outside workspace root
- missing Markdown body files are diagnosed

## Relationship to Copeland.Markdown

`Copeland.Markdown` remains a compiler-style frontend.

M12b consumes it from Oblivion; it does not replace the Oblivion page model and it does not introduce a third-party Markdown dependency.

## Relationship to Document MIR

`DocumentMir` in M12b is body/document MIR only.

It is not the whole Oblivion page model. The page model remains stack-of-typed-cards.

## Single-file Markdown as future export target

Single-file Markdown remains future-only in M12b.

It is a possible lowering/export or import target later, but not canonical storage now.

## What changed

- added `copeland-markdown` card body format support
- added workspace-root-relative external Markdown body loading
- compiled Markdown body files into `DocumentMir`
- attached Markdown body source path and diagnostics to loaded cards
- rendered Markdown previews in compact cards
- rendered fuller Markdown body content and diagnostics in the inspector
- added sample `.md` body files, tests, and M12b manifests/artifacts

## What did not change

- no Markdown editor
- no Roslyn execution
- no xUnit `[Fact]` / `[Theory]` execution
- no Visionary implementation
- no one-big-Markdown-file page model

## Deferred work

- richer inline styling in the presenter
- image/table/video/code typed cards beyond static placeholders
- single-file Markdown export/import flows
- Markdown authoring/editor UI
- trusted local execution work in M13+
