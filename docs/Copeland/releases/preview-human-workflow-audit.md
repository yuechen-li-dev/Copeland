# Preview human-workflow audit

Audit date: 2026-07-30. Host: Windows x64, VS Code 1.131, .NET 10.

Before this milestone, the normal VS Code profile had no Copeland extension and
no Copeland global/local tool. The repository had a VSIX-capable prototype, but
the authoritative mixed sample referenced repository build outputs and required
`tscl workspace sync` to generate an imported props file before an SDK build.

## Findings and disposition

| Finding | Classification | Disposition |
|---|---|---|
| VSIX was packageable but not installed/published | missing packaging | Versioned release-candidate VSIX |
| Activation depended on `tsconfig.tsx` | required product behavior | Retained and made recursive |
| Server was launched automatically | required product behavior | Retained |
| Discovery was configured path or PATH only | bug/missing guidance | Explicit path, local .NET manifest, global PATH; logged |
| Missing ownership required a manual sync | prototype behavior | Automatic initial sync and edit refresh |
| SDK sample imported repository-relative targets/assembly | development-only setup | Replaced by packaged SDK reference |
| SDK compile consumed generated props rather than the manifest | bug | SDK now resolves the shared canonical ownership model directly |
| tscl files used an alternate VS Code language identity | required product behavior | Retained; this prevents built-in TS diagnostics without global disablement |
| tsc-owned files retain `typescript` identity | required product behavior | Verified by extension tests |
| Status bar exposed current owner but not inactive files | usability gap | Owner/state remains visible for the two intended source classes |
| Output omitted discovery source | bug | Selected command, source, and version are logged |
| Project package version regex missed `Copeland.TS.Sdk` | bug | Corrected |
| C# files were not selected by the extension | required product behavior | Retained |
| Marketplace publication was absent | deferred | VSIX is the Preview 1 artifact |

The canonical public steps are now only: install the matching tool and VSIX,
run npm/.NET restore, open the solution/folder, build, and run. Repository-local
build paths and the extension-development host are not part of the guide.
