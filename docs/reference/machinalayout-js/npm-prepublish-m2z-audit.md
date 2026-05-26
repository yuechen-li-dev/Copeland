# MachinaLayout M2z npm prepublish readiness audit

Date: 2026-05-02 (UTC)
Scope: prepublish readiness for a future `0.1.0` npm publish (audit only, no publish action).

## 1) Executive summary

Current repo state is **not publish-ready** for npm consumers. The biggest blockers are:

1. Root package is marked `"private": true` and has no runtime entrypoint metadata (`main`/`exports`/`types`), while README demonstrates package consumption by name.
2. Build script is typecheck-only (`tsc --noEmit`), so there is no emitted JS or declaration output for consumers.
3. Tarball currently includes raw source/tests/sample/docs and excludes any built dist folder (none exists).
4. Consumer smoke test via packed tarball fails to resolve `machinalayout` module/types.

No feature/API behavior changes were made in this pass.

## 2) Current package metadata status

Observed in root `package.json`:

- `name`: `machinalayout`
- `version`: `0.1.0`
- `private`: `true`
- `type`: `module`
- Missing: `description`, `license` field, `author`, `repository`, `homepage`, `bugs`, `keywords`, `main`, `module`, `types`, `exports`, `files`, `sideEffects`, `dependencies`
- Present scripts: `build`, `test`
- `peerDependencies`: `react`, `react-dom`
- `devDependencies`: test/build tooling only (TypeScript, Vitest, jsdom, testing-library, React type packages)

Assessment:

- Package name appears npm-compatible syntactically (unscoped), but final naming strategy is unresolved from metadata alone.
- Package is currently **unscoped**.
- Version `0.1.0` is consistent with first publish intent, but `private: true` currently blocks publish.
- React/ReactDOM are correctly peer dependencies, not runtime dependencies.
- Tooling is correctly in devDependencies.
- Export surface metadata is not declared at package boundary (no `exports`/`main`/`types`).
- README/docs are currently included by default due no restrictive `files` list.
- Sample is also included by default and should likely be excluded for library publish unless intentionally bundled.

## 3) Build output status

Command run: `npm run build`

Result: pass, but script is `tsc --noEmit` (typecheck only).

Implications:

- No compiled JS output emitted.
- No `.d.ts` output emitted.
- No source maps emitted.
- No ESM/CJS build artifacts produced.
- Therefore output cannot match package runtime entrypoints (none configured).
- React/text modules exist in source, but not in emitted build artifacts.

## 4) Public exports/API surface status

`src/index.ts` re-exports core modules (`types`, `errors`, validation/padding/length/offset helpers, compile/resolve functions, resolved tree helpers), plus `./react` and `./text`.

Findings:

- Major APIs appear reachable from root source barrel in-repo.
- React exports are intentionally included via `export * from "./react"`.
- Text parser/types and React text view are intentionally included via `export * from "./text"` and nested text/react barrel.
- No obvious accidental internal export from top-level barrel based on current file list.
- Before publish, explicit package `exports` map (root + optional subpaths) is recommended for stability.

## 5) Type declaration status

Post-build status: **no declarations emitted** because build uses `--noEmit`.

- `types` field is absent in `package.json`.
- `.d.ts` distribution is absent.
- This is a **publish blocker**.

## 6) React peer dependency status

Root package uses:

- `peerDependencies.react: ^19.2.5`
- `peerDependencies.react-dom: ^19.2.5`

Assessment:

- Correct placement as peer deps for adapter components.
- No runtime `dependencies` are declared.
- Consider whether peer range should be broadened for ecosystem compatibility in M3.

## 7) Package contents / `npm pack --dry-run` status

Command run: `npm pack --dry-run`

Result:

- Tarball name: `machinalayout-0.1.0.tgz`
- Package size: 57.1 kB
- Unpacked size: 247.2 kB
- Total files: 67

Included content highlights:

- `src/**` TypeScript sources
- `test/**` test sources
- `samples/control-room/**` including its `package-lock.json`
- `docs/**`
- `README.md`, `LICENSE`, `tsconfig.json`

Not included: no `dist/**` output (none exists).

Assessment:

- Current pack contents look like a source repository bundle, not a focused library distribution.
- Inclusion of tests and sample app is likely unintentional for npm consumers unless explicitly desired.
- README and LICENSE are present (good).

## 8) README/docs publish-readiness status

README currently:

- Uses install/import examples with package name `machinalayout`.
- Demonstrates `resolveLayoutRows`, `LayoutRow`, `MachinaReactView` usage.
- Mentions sample run instructions (`cd samples/control-room`, `npm install`, `npm run dev`).
- Explicitly states boundaries like no intrinsic sizing / no CSS layout authority.

Assessment:

- API narrative is mostly aligned with current source exports.
- Install instructions will remain provisional until package metadata/export strategy is finalized.
- No obvious critical stale claims found during this pass.

## 9) Sample package status

Command run in sample: `cd samples/control-room && npm run build`

Result: pass.

Sample dependency model:

- `machinalayout` is consumed via local file dependency: `"file:../.."`.

Assessment:

- Sample is wired to root workspace package path, useful for local integration.
- Sample should likely be excluded from root publish tarball unless intentional.

## 10) Consumer smoke-test status

Method used:

1. Created real tarball via `npm pack`.
2. Created temporary external consumer at `/tmp/machina-consumer-smoke/consumer`.
3. Installed tarball + minimal deps.
4. Created `smoke.ts` importing:
   - `resolveLayoutRows`
   - `RootFrame` type
   - `MachinaReactView`
   - `parseMachinaText`
   - `MachinaTextView`
5. Ran TypeScript compile check.

Result: **fail**.

Error:

- `TS2307: Cannot find module 'machinalayout' or its corresponding type declarations.`

Interpretation:

- Packed package is not consumable as a normal npm dependency due missing entrypoint/type declarations.

## 11) Risks/blockers before 0.1.0

Blockers:

1. `private: true` in root package metadata.
2. Missing entrypoint metadata (`main`/`exports`/`types`).
3. No emitted build artifacts (JS/d.ts).
4. Consumer smoke test fails module/type resolution.

Risks (non-blocker but important):

1. Over-inclusive tarball (tests/samples/docs/source-only) may confuse consumers and bloat installs.
2. Missing discoverability metadata (`description`, repository/homepage/bugs/keywords).
3. No explicit `sideEffects` declaration; tree-shaking semantics are implicit.

## 12) Recommended M3 cleanup checklist

### Package metadata

- [ ] Decide final npm name strategy (keep unscoped `machinalayout` or scope it).
- [ ] Remove/adjust `private` for publish pipeline.
- [ ] Add `description`, `license` field, `author`, `repository`, `homepage`, `bugs`, `keywords`.
- [ ] Add explicit runtime/type entrypoints: `main`/`module` (if dual), `types`, and `exports` map.
- [ ] Add `sideEffects` policy.

### Build/distribution

- [ ] Add actual emit build (e.g., dist JS + d.ts, optional sourcemaps).
- [ ] Ensure build output paths align with package entrypoints.
- [ ] Confirm React/text modules are included in emitted artifacts.

### Packaging contents

- [ ] Add `files` allowlist (or `.npmignore`) to publish intended artifacts only.
- [ ] Exclude tests/sample sources and lockfiles from library tarball unless intentionally shipped.
- [ ] Re-run `npm pack --dry-run` and validate final file list.

### Consumer validation

- [ ] Re-run tarball-based smoke test after metadata/build fixes.
- [ ] Add a lightweight CI smoke-check that installs packed tarball and typechecks imports.

### Docs/readiness

- [ ] Update README install command once final package name/scope is confirmed.
- [ ] Ensure docs state publish support level and adapter peer dependency expectations.

## 13) Exact commands run and results

1. `npm test` → **pass** (17 files, 170 tests). Observed benign environment stderr in jsdom test: "Not implemented: navigation to another Document".
2. `npm run build` → **pass** as typecheck-only (`tsc --noEmit`), no distributable output.
3. `cd samples/control-room && npm run build` → **pass** (`tsc -b && vite build`).
4. `npm pack --dry-run` → **pass**, tarball preview generated with 67 files, includes src/test/sample/docs.
5. Consumer smoke sequence using `npm pack` + external temp project + `npx tsc smoke.ts ...` → **fail** with TS2307 unable to resolve `machinalayout` module/types.

---

Non-goal compliance check:

- No `npm publish` run.
- No release tags created.
- No version bumps performed.
- No runtime/layout/text feature changes performed.
