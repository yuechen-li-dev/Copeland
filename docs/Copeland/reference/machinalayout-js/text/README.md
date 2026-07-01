# MachinaLayout.JS text reference snapshot

This folder is a reference-only snapshot of the upstream MachinaLayout.JS text subsystem.

## Provenance

- Source repository: `https://github.com/yuechen-li-dev/MachinaLayout.JS`
- Source branch: `main`
- Source ref (HEAD at copy time): `deb2660b606a94aaaed8dc70ff095170f89306a8`
- Copy date (UTC): `2026-05-27`
- Upstream subtree: `src/text`

## Copied files

- `index.ts`
- `types.ts`
- `parseMachinaText.ts`
- `react/index.ts`
- `react/MachinaTextView.tsx`
- `react-native/index.ts`
- `react-native/MachinaNativeTextView.tsx`
- `vue/index.ts`
- `vue/MachinaVueTextView.ts`

## Purpose

These files are imported for contract/audit work only (Machina.Text M6a). They document upstream model, parser, diagnostics, and policy/view behavior so C# `Machina.Text` can define a compatible but runtime-appropriate contract.

This directory is not compiled by the C# solution.
