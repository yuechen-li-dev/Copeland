# File profiles M0

Status: **success for the first configured-workspace and source-command pass**.

`.ts` and `.tsx` modules can now identify their compiler lane explicitly:

```ts
export type CopelandProfile = "Legacy";
```

`tscl compile` delegates that file's project to the installed local TypeScript
compiler through Node. Copeland profiles select its frontend and constrain the
backend/runtime. VSCode uses generated ownership plus the same reader for
unsaved-header checks. The extension does not guess the language from constructs
in the body. No TypeScript compiler was ported or reimplemented.

## Implemented behavior

The shared `SourceFileProfiles` reader lives in `Copeland.TS.Workspace`, which
the compiler, CLI, MSBuild, and language server already reference. It recognizes
the bounded literal header without interpreting either language body. Copeland
masks metadata while preserving source offsets; module export/import analysis
does not treat the header as an application alias. Profiles are `Legacy`,
`Copeland`, `JavaScript`, `Dotnet`, and `Dotnet-NativeAOT`.

`Legacy` resolves the project configuration and local TypeScript dependency.
Mixed workspaces use their generated TypeScript configuration. Arguments after
`--` belong to TypeScript. Diagnostics, output streams, and exit status are
forwarded. Process tests caught Windows dropping inherited redirected output;
both streams are now explicitly forwarded. Missing TypeScript or Node stops
with a diagnostic, without compiler installation or a Copeland retry.

Concrete Copeland profiles select a default source emitter. Project compilation
checks all input profiles against the selected backend/runtime before emission.
MSBuild uses its .NET/AOT settings and includes those settings in cache keys.
The shared `Copeland` profile admits multiple targets. Existing backend checks
still prove feature admission and perform actual NativeAOT publication.

Folder ownership and headers must agree. Otherwise unowned profiled files may
choose a compiler already configured in the workspace. Exclusions, overlap
errors, and explicit interop boundaries remain intact. Generated editor metadata
records the profile and whether ownership came from the header.

The extension asks `tscl profile --stdin` about unsaved headers instead of
implementing a second parser. Requests are debounced and version checked.
Ownership conflicts are diagnosed; synchronized header-owned files switch
between built-in TypeScript and Copeland language/LSP modes. The status bar
shows the profile. Copeland-owned diagnostics remain with its LSP when running,
avoiding duplicate profile errors from the extension's fallback collection.

## Executed evidence

```powershell
dotnet build Copeland.slnx -m:1 --nologo
dotnet test Copeland.slnx -m:1 --no-build --nologo
pwsh -NoProfile -File samples/copeland-ts/file-profiles-m0/qualify.ps1
npm test --prefix src/Copeland/Copeland.TS.VSCode
npm run test:integration --prefix src/Copeland/Copeland.TS.VSCode
```

The solution builds with **zero warnings/errors**. **2,420 tests pass**, with
zero failures/skips. New coverage comprises **13 compiler/header cases** and
**8 CLI cases**, including metadata erasure, graph imports, source offsets,
nominal profile names, invalid headers, missing compiler stops, exact process
transport, emitter conflicts, and workspace ownership. The existing NativeAOT
integration test now carries the explicit profile and publishes and executes
its real native artifact for the current machine's RID.

The mixed sample passes **16 checks** using **TypeScript 5.9.3**. Traditional
TypeScript with `any` and optional chaining compiles through delegated `tsc` and
executes with result 42. Copeland modules with .NET/shared profiles compile
through MSBuild and execute with result 42. A JavaScript-profiled Copeland record
program compiles through the production project emitter and executes with
result 42. A deliberately invalid TypeScript assignment produces identical
direct/delegated diagnostics and exit status **2**. The exported header is erased
under `noUnusedLocals`; accepting that standard boolean option in workspace
configuration was a small required fix found by this sample.

VSCode passes **2 unit tests** and **7 live extension-host tests**. The latter
exercise ordinary and Copeland routing, hover/navigation, invalid unsaved
buffers, unsaved profile conflicts, and a real file transferring between
compiler lanes after a header edit and workspace synchronization. The local
runner uses the basic workspace fixture; its hover test previously assumed the
installed distribution fixture's CLR/npm symbols. It now checks the appropriate
fixture while retaining those richer assertions for installed-package runs.
Installed-VSIX qualification was not performed in this pass.

Logs are under `artifacts/file-profiles-*.log`; runtime outputs, generated source,
build result, TypeScript diagnostics, and `summary.json` are under
`artifacts/file-profiles/`. The reusable sample and its pinned dependency lock
are under `samples/copeland-ts/file-profiles-m0/`.

## Observed scope and next work

The [language contract](../language/copeland-ts-file-profiles.md) records the
current admission rules. The first header must precede imports, ordinary
declarations, and directive statements. Module headers use an exported alias;
unmarked global scripts retain project ownership. Standalone header-only editor
sessions, directive-prologue integration, and TypeScript build/watch orchestration
are separate work. The `build` artifact protocol still describes Copeland
targets; Legacy delegation is the source `compile` lane and generated TypeScript
workspace configuration, rather than a new artifact protocol.

Live tests qualify editor language routing and Copeland diagnostics, without a
new integration for Microsoft's discovery of generated TypeScript configuration
filenames. Unsaved profile checks currently use short CLI requests; a persistent
request channel could reduce their latency while retaining the same reader.

This completes the initial profile experiment. TableScript's next unresolved
items remain application-owned domain validation and frontend derived-table
execution; they were not changed here.
