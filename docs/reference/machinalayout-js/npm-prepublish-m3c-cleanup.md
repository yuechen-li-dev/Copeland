# MachinaLayout M3c npm packaging cleanup

Date: 2026-05-02 (UTC)
Scope: packaging/distribution readiness for future npm `0.1.0` publish (no publish action performed).

## 1) What changed

- Replaced typecheck-only build with a real package build that emits ESM JavaScript and declarations under `dist/`.
- Added `tsup` build configuration for `src/index.ts` with externalized React dependencies.
- Added root package entrypoint metadata (`main`, `module`, `types`, `exports`) and a `files` allowlist.
- Removed `private: true` from root package to unblock publish readiness.
- Broadened React peer dependency ranges to `>=18 <20`.
- Added npm install guidance to README.
- Verified package contents with `npm pack --dry-run`.
- Verified packed tarball consumer typecheck in an external temporary consumer project.

## 2) Package metadata decisions

- Package name kept as `machinalayout`.
- Version kept as `0.1.0`.
- Kept ESM-only distribution for this pass (`type: module`, ESM `dist/index.js`).
- Added:
  - `description`, `license`, `author`, `repository`, `homepage`, `bugs`, `keywords`
  - `main`, `module`, `types`, `exports`
  - `files` allowlist
  - `sideEffects: false`
- React and ReactDOM remain peer dependencies and are not bundled.

## 3) Build output shape

`npm run build` now does:

1. `npm run typecheck` (`tsc --noEmit`)
2. `tsup --config tsup.config.ts --tsconfig tsconfig.build.json`

Expected emitted artifacts:

- `dist/index.js`
- `dist/index.d.ts`

## 4) Package exports shape

Root package exports are configured as:

- `exports["."].import -> ./dist/index.js`
- `exports["."].types -> ./dist/index.d.ts`

No subpath exports were introduced in this pass.

## 5) Files/tarball contents

`npm pack --dry-run` now shows a focused tarball containing:

- `dist/**`
- `README.md`
- `LICENSE`
- `docs/**`
- `package.json`

Observed exclusions (desired):

- `src/**` excluded
- `test/**` excluded
- `samples/**` excluded

Dry-run snapshot:

- package size: `28.2 kB`
- unpacked size: `93.5 kB`
- total files: `15`

No unexpected runtime payloads were observed.

## 6) Consumer smoke result

Smoke validation used a real packed tarball installed into `/tmp/machina-m3c-smoke` and compiled a TypeScript file importing:

- `resolveLayoutRows`
- `RootFrame` (type)
- `MachinaReactView`
- `parseMachinaText`
- `MachinaTextView`

TypeScript compile succeeded with no module/type resolution errors.

## 7) Remaining manual publish steps (not executed here)

1. Confirm npm auth:
   - `npm whoami`
2. Login if needed:
   - `npm login`
3. Optional name availability check:
   - `npm view machinalayout`
4. Final packaging check:
   - `npm pack --dry-run`
5. Publish:
   - `npm publish --access public`
   - Note: for unscoped packages, npm may not require/accept `--access public`; retry without flag if npm rejects it.
6. Validate org policy for npm 2FA/token/trusted publishing.

## 8) Remaining risks before npm publish

- Repository/homepage/bugs URLs should be confirmed against the canonical upstream repository if different from current metadata.
- No CJS build is included (intentional for this ESM-first pass); consumers requiring CJS will need transpilation/interoperability handling.
- Moderate security advisories remain in current dev dependency graph (non-blocking for packaging outcome, but worth auditing).
