# File profiles M0

This mixed workspace keeps the same `.ts` extension in all three source folders:

- `src/legacy/Legacy.ts`: `Legacy`, compiled by the installed TypeScript package.
- `src/copeland/Main.ts`: `Dotnet`, compiled by Copeland through MSBuild.
- `src/shared/Math.ts`: `Copeland`, owned by its header outside the configured
  include folders and imported by the .NET module.

From the repository root:

```powershell
dotnet build src/Copeland/Copeland.Cli/Copeland.Cli.csproj -m:1
npm ci --prefix samples/copeland-ts/file-profiles-m0
pwsh -NoProfile -File samples/copeland-ts/file-profiles-m0/qualify.ps1
```

The qualification executes real TypeScript and MSBuild-generated C#, checks
header-selected JavaScript emission and a production JavaScript project, and
compares a real TypeScript error and exit status with direct `tsc`. Each runtime
returns 42. An incompatible emitter is rejected. Evidence goes to
`artifacts/file-profiles/`; generated workspace/build output stays under `obj/`.
The qualification restores its temporary invalid edit to the Legacy module.

Open this directory in VSCode with the Copeland extension to see the language
mode and profile status. Change an otherwise unowned file's header and run
Workspace Sync to change its compiler lane. A header contradicting an explicit
folder rule produces a diagnostic.

See the [profile contract](../../../docs/Copeland/language/copeland-ts-file-profiles.md).
