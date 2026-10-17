# Source file profiles

Put a profile before imports and other declarations in a `.ts` or `.tsx` module:

```ts
export type CopelandProfile = "JavaScript";
```

The header is ordinary TypeScript syntax and has no runtime value. Exporting it
avoids `noUnusedLocals` errors and global-script alias collisions. It does not
make the rest of a Copeland file compatible with TypeScript. Copeland recognizes
this reserved declaration as file metadata and removes it before ordinary
binding; `CopelandProfile` is not an application type or a module export in
Copeland's semantic graph. No general literal-type or primitive-union feature is
introduced.

| Profile | Compiler and restriction |
| --- | --- |
| `Legacy` | The project's TypeScript compiler, launched by `tscl` through Node |
| `Copeland` | Copeland semantics, without a backend restriction |
| `JavaScript` | Copeland semantics, JavaScript backend; runtime selected by the project |
| `Dotnet` | Copeland semantics, .NET backend family |
| `Dotnet-NativeAOT` | Copeland semantics, .NET backend and NativeAOT runtime |

Names are case-sensitive. Comments and blank lines may precede the header. Both
quote styles work. The bounded initial form requires a literal and semicolon;
computed expressions, aliases, unions, and multiple headers are rejected. The
reader examines the file prologue without interpreting its language body. Put
the header first, before directive statements as well as imports. Files without
headers retain their existing ownership and compilation behavior.

## Compilation

```powershell
tscl compile src/Legacy.ts -- --noEmit --pretty false
tscl compile src/Browser.ts --out browser.js
tscl compile src/Domain.ts --emit mir --out domain.mir
tscl profile src/Browser.ts --format json
```

`Legacy` locates the nearest project configuration and the project's local
`node_modules/typescript/bin/tsc`, then launches it with Node. In a mixed workspace
it uses `obj/copeland/workspace/tsconfig.generated.json`; run `tscl workspace sync`
first. An explicit TypeScript `--project`/`-p` is supported. With no configuration,
the named file is passed to TypeScript. Configuration-based compilation compiles
the TypeScript project, rather than trying to combine `tsc --project` with a
single-file argument. Standard output, standard error, and exit status are
forwarded. Copeland does not install TypeScript, reimplement it, or retry its
errors under Copeland semantics.

`JavaScript` defaults to JavaScript emission; `Dotnet` and `Dotnet-NativeAOT`
default to C# emission. `Copeland` still requires an explicit emitter. MIR remains
available for every Copeland profile. An incompatible concrete emitter is an
error. C# source emission is not NativeAOT publication: project builds additionally
check the actual runtime and retain the existing explicit RID requirement.
Every source in a project is checked, including imported modules. MSBuild checks
the .NET backend, `PublishAot`, and `RuntimeIdentifier`; those properties also
participate in its compiler cache fingerprint.

Existing frontend capabilities and adapters remain project configuration. A
JavaScript profile alone does not select React, Vue, TS-XML bindings, or a browser.
Legacy/Copeland imports retain the existing cross-compiler boundary; profiles do
not turn a source import into an interop bridge.

## Ownership and the editor

Explicit workspace ownership rules must agree with the header. Overlap remains
an error, and exclusions still apply. An otherwise unowned profiled file can
select an already-configured compiler lane; for example, a shared `Copeland`
module can live outside the `tscl.include` folders. The project configuration
still supplies the compiler project and available targets. Header-owned files
appear in generated ownership metadata with `matchedRule: "CopelandProfile"`
and their profile name.

The VSCode extension consumes that metadata for language and LSP routing. For
unsaved headers it asks `tscl profile --stdin` to use the same C# reader, with
debouncing and version checks. Conflicts are diagnosed without silently
overriding configured ownership. Saving a header refreshes workspace ownership
when automatic synchronization is enabled; the explicit Workspace Sync command
also updates routing. The status bar includes the profile.

This first editor pass uses configured mixed workspaces. Header-only standalone
LSP sessions, global-script header conventions, JavaScript directive-prologue
integration, and TypeScript build/watch orchestration are not qualified here.
Existing global scripts can keep project-based ownership without a header.

## Diagnostics and evidence

| Diagnostic | Repair |
| --- | --- |
| `COPE-PROFILE-0001` | Use one supported literal profile header with a semicolon |
| `COPE-PROFILE-0002` | Keep one header |
| `COPE-PROFILE-0003` | Make the header and compiler ownership agree |
| `COPE-PROFILE-0004` | Select a compatible backend/runtime, or use the TypeScript argument lane for Legacy |
| `COPE-PROFILE-0005` | Make the project's TypeScript dependency and Node available; check configuration paths |

The [mixed sample](../../../samples/copeland-ts/file-profiles-m0/README.md) proves
real TypeScript, C#, and JavaScript execution. The
[implementation report](../reviews/file-profiles-m0.md) records compiler,
delegation, NativeAOT, and live editor evidence.
