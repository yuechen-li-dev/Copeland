# MachinaLayout.JS Reference Docs (Imported)

These files are copied reference material from the upstream MachinaLayout.JS project to guide C# Machina layout authoring/lowering behavior.

- Source repository: https://github.com/yuechen-li-dev/MachinaLayout.JS
- Source ref: `main` (cloned with `--depth 1`)
- Source commit SHA: `deb2660b606a94aaaed8dc70ff095170f89306a8`
- Copy date: 2026-05-26 (UTC)

## Purpose

This folder is used as evidence for frame/stack semantics during C# audit milestones (M4b and follow-ups), especially for:

- `AbsoluteFrame` / `AnchorFrame` placement semantics
- `FixedFrame` / `FillFrame` stack-child constraints
- ordered arithmetic stack behavior (`gap`, `padding`, `justify`, `align`)
- authoring model guidance that stack is not Flexbox

## Files copied

- `frames-and-stack.md`
- `row-model.md`
- `adapters.md`
- `machina-dispatch.md`
