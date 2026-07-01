# Adapter Packaging A0 Plan

## 1) Executive summary

MachinaLayout should stay as a **single npm package during `0.x`**, but move toward a **subpath-exported adapter model** so new adapters do not impose framework installs on every user.

Recommended direction:

- Keep `machinalayout` root import stable for compatibility during `0.x`.
- Add framework-specific adapter entrypoints as subpaths (`machinalayout/react`, `machinalayout/text/react`, future `machinalayout/react-native`, `machinalayout/vue`, etc.).
- Treat new framework peers (`react-native`, `vue`) as **optional peers** when their adapters are added.
- Keep runtime/package behavior unchanged in A0; implement packaging/build updates as A1 prerequisites.

## 2) Current package/export state (observed)

### Package/build

- Package name is `machinalayout`, version `0.1.0`, ESM package type.
- Build command uses `tsup` with a **single entry**: `src/index.ts`.
- Current output is a **single dist entrypoint**:
  - `dist/index.js`
  - `dist/index.d.ts`
- `exports` currently expose only `"."`.

### Peer dependencies

- Current peers are:
  - `react` (`>=18 <20`)
  - `react-dom` (`>=18 <20`)
- No optional peer metadata yet.

### Public exports and source layout

- `src/index.ts` re-exports core, React adapter (`./react`), and text module (`./text`).
- React adapter exists under `src/react`.
- Text parser/types and React text renderer exist under `src/text` and `src/text/react`.
- README examples/documentation currently import React adapter from root (`machinalayout`).

### Packaging snapshot

- `npm pack --dry-run` currently includes `dist` (single bundle), README, LICENSE, and docs.
- No subpath build artifacts are currently emitted.

## 3) Recommended package strategy

### Evaluated options

#### A. Single package + subpath exports (recommended for now)

Pros:

- Minimal project/process overhead while API is still evolving.
- Keeps install/discovery simple for early adopters.
- Preserves existing import compatibility.

Cons:

- `exports`/build matrix becomes more complex.
- Peer policy must clearly separate required vs optional framework peers.

#### B. Separate packages (`@machina/*`) later

Pros:

- Clean dependency isolation per framework.
- Natural long-term scaling for adapters.

Cons:

- Multi-package release/publishing overhead now.
- More migration friction before APIs settle.

### Decision

For `0.x`, use **single package + subpath exports**. Reassess split packages after adapter APIs stabilize and usage justifies monorepo/package split effort.

## 4) Proposed subpath export map

Target import shapes:

```ts
import { resolveLayoutRows } from "machinalayout";
import { MachinaReactView } from "machinalayout/react";
import { parseMachinaText } from "machinalayout/text";
import { MachinaTextView } from "machinalayout/text/react";
import { MachinaReactNativeView } from "machinalayout/react-native";
import { MachinaVueView } from "machinalayout/vue";
```

Export policy:

- **Keep root exports unchanged during `0.x`** for compatibility.
- Introduce and document subpath exports as preferred for adapters.
- New adapters (`react-native`, `vue`, future) should be **subpath-only**.
- Existing root React exports remain temporarily for non-breaking transition.

## 5) Peer dependency policy

### Near term (`0.x`, current compatibility)

- Keep `react` and `react-dom` as non-optional peers while root still exports React adapter surfaces.
- When adding adapter subpaths:
  - Add `react-native` peer + `peerDependenciesMeta.react-native.optional = true`.
  - Add `vue` peer + `peerDependenciesMeta.vue.optional = true`.

### Later (future major or de-rooting)

- If React DOM adapter is no longer root-exported, convert renderer peers to optional and strictly subpath-scoped usage.

Rationale: users should only need the framework peer for the adapter they import.

## 6) Build output proposal

A1 prerequisite: move from single-entry build to multi-entry/subpath build.

Proposed dist shape:

```txt
dist/index.js
dist/index.d.ts
dist/react/index.js
dist/react/index.d.ts
dist/text/index.js
dist/text/index.d.ts
dist/text/react/index.js
dist/text/react/index.d.ts
dist/react-native/index.js
dist/react-native/index.d.ts
dist/vue/index.js
dist/vue/index.d.ts
```

Build notes:

- Use tsup multi-entry configuration (or equivalent) with explicit entry files.
- Externalize framework runtimes in adapter bundles:
  - `react`, `react-dom`, `react/jsx-runtime`
  - `react-native`
  - `vue`
- Keep core runtime free of adapter-specific runtime imports.

## 7) Source tree proposal

Recommended organization:

```txt
src/
  index.ts
  react/
    index.ts
    MachinaReactView.tsx
  react-native/
    index.ts
    MachinaReactNativeView.tsx
  vue/
    index.ts
    MachinaVueView.ts
  text/
    index.ts
    types.ts
    parseMachinaText.ts
    react/
      index.ts
      MachinaTextView.tsx
    react-native/
      index.ts
      MachinaNativeTextView.tsx
    vue/
      index.ts
      MachinaVueTextView.ts
```

Guideline:

- Layout adapters: `src/<adapter>`.
- Text renderers: `src/text/<adapter>`.

This keeps parser/core text independent from renderer frameworks.

## 8) Adapter API previews (non-binding)

### React Native layout adapter

```ts
export type { MachinaReactNativeViewProps, MachinaNativeSlotProps };
export { MachinaReactNativeView };
```

Expected behavior:

- Uses RN `View` + style objects.
- No `className`, no DOM data-attributes.
- No DOM containment/content-visibility policy.
- Preserve layout semantics, `viewData`/`nodeData`, layers/z ordering, and parent-local coordinates.

### Vue layout adapter

```ts
export type { MachinaVueViewProps, MachinaVueSlotProps };
export { MachinaVueView };
```

Expected behavior:

- Vue component/composable renderer using `h()`.
- Supports `layout`, `views`, `viewData`, `nodeData`, `layers`, `defaultLayer`, `debug`.
- Dynamic view mapping via component references.

### Text

- `parseMachinaText` remains framework-agnostic in `machinalayout/text`.
- Framework renderers exposed by subpath (`text/react`, future `text/react-native`, `text/vue`).

## 9) Test strategy

Principles:

- Core tests always run and remain framework-independent.
- Adapter tests are isolated by adapter folders and entrypoints.
- Avoid forcing heavy framework stacks for users who do not use them.

Proposed test layout:

```txt
test/
  core/... 
  react/...
  react-native/...
  vue/...
  text/
    core/...
    react/...
    react-native/...
    vue/...
```

Execution approach (A1+):

- Default CI lane: core + existing React lanes.
- Adapter lanes:
  - RN adapter tests in dedicated job/environment.
  - Vue adapter tests in dedicated job/environment.
- Early RN adapter tests can start with type-level/shape tests and minimal render checks before deeper platform-specific behavior.

## 10) Documentation plan

Planned docs additions:

- `docs/react-native-adapter.md`
- `docs/vue-adapter.md`
- `docs/adapter-packaging-a0-plan.md` (this file)
- README update to clarify preferred import paths and adapter dependency expectations.

Docs guidance:

- Show subpath imports for framework adapters.
- Keep compatibility notes for root React imports during `0.x`.
- Explicitly state optional peer policy for non-default adapters.
- Keep adapter docs aligned to one conceptual model: Machina records author geometry, adapters render resolved rectangles in host primitives.
- Reinforce stable component registries (`views`) plus dynamic `viewData`/`nodeData` channels across React, React Native, and Vue.

## 11) Migration / compatibility notes

- Do **not** break current root import patterns in immediate work.
- Maintain:

```ts
import { MachinaReactView } from "machinalayout";
```

through `0.x`.

- Mark subpath imports as preferred forward path.
- Consider root de-exports only in future major/split-package decision.

## 12) Milestone decomposition

### A1 — React Native layout adapter

Scope:

- Add `src/react-native` adapter entry.
- Add `machinalayout/react-native` subpath export.
- Add optional `react-native` peer metadata.
- Add initial RN adapter tests in isolated lane.

Prereqs from A0:

- Multi-entry build/export plumbing.

Status note (A1a, 2026-05-11): multi-entry dist output and subpath exports for existing modules (`./react`, `./text`, `./text/react`) are now being implemented while preserving root-import compatibility during `0.x`.

### A2 — React Native text renderer

Scope:

- Add `src/text/react-native` renderer.
- Add `machinalayout/text/react-native` subpath export.
- Extend RN adapter test lane for text renderer behavior.

### A3 — Vue layout adapter

Scope:

- Add `src/vue` adapter entry.
- Add `machinalayout/vue` subpath export.
- Add optional `vue` peer metadata.
- Add Vue adapter tests in isolated lane.

### A4 — Vue text renderer

Scope:

- Add `src/text/vue` renderer.
- Add `machinalayout/text/vue` subpath export.
- Extend Vue test lane for text rendering behavior.

### Later adapters

- SVG/debug renderer under dedicated subpaths.
- Svelte/Solid exploration after adapter API stabilization.

## 13) Risks and mitigations

1. **Root export bloat and accidental framework coupling**
   - Mitigation: keep new adapters subpath-only; avoid root re-exports for new frameworks.

2. **Peer dependency confusion**
   - Mitigation: explicit matrix in README (adapter → required peer).

3. **Build complexity regression**
   - Mitigation: explicit multi-entry manifest + pack smoke checks per milestone.

4. **CI/test instability from heavy adapter stacks**
   - Mitigation: separate adapter test lanes and scoped test commands.

5. **Future package split cost**
   - Mitigation: enforce subpath discipline now so split is mostly import-path and publish-surface work later.

## 14) Exact verification commands run

From repository root:

```bash
npm test
npm run build
cd samples/control-room && npm run build
cd samples/music-player && npm run build
npm pack --dry-run
```

Observed result in A0: all commands completed successfully in this environment.
