# CTS-DISTRIBUTION-M0 review

## Result

M0 ships one local-only `0.1.0` Copeland train and one separately packaged
TSPack browser-proof bundle:

| Artifact | ID | Distribution role |
| --- | --- | --- |
| .NET tool | `Copeland.TS.Tool` | `tscl`, compiler, workspace commands, and language-server launcher. |
| NuGet SDK | `Copeland.TS.Sdk` | MSBuild tasks/targets restored through normal `PackageReference`. |
| Template package | `Copeland.TS.Templates` | `copeland-console`, `copeland-library`, `copeland-react`, and `copeland-workspace`. |
| VSIX | `copeland-ts-0.1.0.vsix` | VS Code client, installed locally with `code --install-extension`. |
| TSPack archive | `TSPack.Tool.0.1.7-win-x64.zip` | Separate browser lifecycle and Playwright proof payload for React. |

Nothing is published to NuGet.org or the VS Code Marketplace.

The language-server distribution law is singular: the VSIX discovers `tscl`
on PATH (or `copeland.tsclPath`), verifies the compatible tool/server train,
and launches `tscl language-server`. The VSIX contains no duplicate compiler or
server payload, and generated projects contain neither.

## Compatibility and diagnostics

The M0 law is strict: Copeland project SDK, `tscl`, language server, and VSIX
must be on the same `0.1` train. Ownership, NuGet-contract, npm-contract, and
bridge schemas are independently validated at schema version 1. A project
requesting another train fails with `COPE-DIST-0001` and the action
`Update the Copeland toolchain.`

`tscl install-info --format json` exposes machine-readable component versions.
`tscl doctor --format json` is read-only and reports the .NET SDK, tool,
language server, SDK/package availability, workspace metadata, PATH, and
TSPack when a project declares `manifest.tsx`.

## Installed-artifact proof

`tools/Invoke-CopelandDistributionProof.ps1` builds the local feed and creates
isolated `DOTNET_CLI_HOME`, `NUGET_PACKAGES`, tool, template, VS Code user-data,
and extension directories under `artifacts/cts-distribution-m0/isolated-proof`.
It installs the tool and templates from that feed, installs the built VSIX into
the isolated extensions directory, and removes all three afterwards.

The generated `copeland-react` project restores, builds, synchronizes its
`tsconfig.tsx` ownership map, then runs through the **packaged embedded TSPack
binary**. TSPack starts `dotnet run --no-build`, waits for the declared HTTP
target, supplies `http://127.0.0.1:5137`, and owns process cleanup. The packaged
TSPack Playwright helper loaded the page, clicked **Call CLR operation**, and
observed `Hello, React. This message was compiled from Copeland.` with no page,
console, or request failures. No listener remained on port 5137.

The same proof launches a real VS Code extension host against a generated mixed
workspace using the VSIX-installed extension payload and packaged `tscl`. It
records extension activation, ownership load, Copeland language identity and
diagnostics for `Greeter.ts`, conventional TypeScript diagnostics for
`Legacy.ts`, an unsaved Copeland error/repair cycle, ownership transfer, and
clean host exit. It does not launch the source extension as the payload.

The final measured clean flow was: install 1.601 s, template creation 0.445 s,
console restore/build 2.092 s, console run 0.570 s, and **4.708 s time to first
working application**. React template/build/browser proof took 9.526 s and the
installed VSIX extension-host proof took 9.952 s. Manual interventions: none.

Pure CLR console and library templates have no TSPack or Node dependency.
`copeland-react` needs the separately installed TSPack archive only for its
declared browser-host lifecycle; TSPack remains separate from Copeland and is
not absorbed into the compiler tool package.

## Reproducibility

The proof builds every NuGet package twice. NuGet varies only its random
core-properties part name, relationship IDs, and ZIP timestamps; the proof
canonicalizes those metadata fields into a valid stable local-feed archive.
The VSIX ZIP is likewise sorted and timestamp-normalized after `vsce package`.
The TSPack archive has fixed ZIP timestamps and is built twice. All compared
hashes matched in the final run; the exact values are written to
`isolated-proof/metrics.json`.

## Artifact audit

| Classification | Findings |
| --- | --- |
| Canonical source fixtures | Source under `samples/copeland-ts`, including workspace and React fixtures. |
| Required checked-in golden | Existing browser-WASM publish payload, where tests/reviews treat it as a fixture. |
| Reproducible output / tracked cleanup debt | `publish-modules-m1/`, `samples/copeland-ts/authoring-food/publish*`, and `artifacts/authoring-food-r1-closure` executable/DLL/PDB payloads. |
| Accidental binary | `samples/copeland-ts/react-components-m1/tspack.exe`; distribution no longer relies on it. |
| Package artifacts | M0 packages under ignored `artifacts/cts-distribution-m0/`. |

No tracked binary was removed. A later reviewed removal commit must handle the
classified reproducible/accidental payloads. The generic `*.dll` ignore was
replaced with precise publish-output rules, so binary drift remains visible.

## Validation

- Full isolated local-feed/install/template/React Playwright/installed-VSIX
  extension-host/uninstall proof passed.
- NuGet, VSIX, and TSPack package determinism checks passed.
- `dotnet build Copeland.slnx --no-restore` passed.
- `dotnet test Copeland.slnx --no-build` passed: 1,467 tests.
- VS Code unit tests passed: 2 tests; installed-VSIX extension-host proof passed:
  4 tests.
- Relevant TSPack Playwright tests passed: 10 tests, 2 installed-host skips.
  The broad TSPack frontend and Go command suites have unrelated failures in
  existing dirty work: native-test discovery cleanup; doctor timeout panic;
  manifest declaration drift; and a build-command expectation.
- `git diff --check` passed.

## Additional work performed

- The React template lacked the required Copeland workspace manifest. Added
  `tsconfig.tsx`; its TSPack `manifest.tsx` is intentionally partial/unowned.
- A bare TSPack executable expected a source-adjacent manifest frontend. The
  existing embedded-bridge release mode is now used by a small TSPack browser
  proof package builder, so runtime has no repository-relative frontend path.
- VSIX ZIP timestamps were nondeterministic. The distribution proof now emits
  a valid normalized VSIX and verifies two identical local builds.

## Deferred

No Visual Studio extension, marketplace/public-feed publication, workload,
installer, signing, auto-update, or npm package-manager default path is
included. The recommended follow-up is Visual Studio/onboarding M1: consume the
same installed `tscl` discovery law and retain TSPack as the separate browser
lifecycle owner.
