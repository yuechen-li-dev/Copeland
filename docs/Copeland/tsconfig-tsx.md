# `tsconfig.tsx` workspace ownership

`tsconfig.tsx` is a typed, declarative replacement for `tsconfig.json`. It can
be adopted without moving a single source file to Copeland TS:

```tsx
export default defineTypeScriptWorkspace({
    tsc: {
        include: ["src/**"],
        compilerOptions: {
            strict: true,
            target: "ES2024",
            module: "ESNext"
        }
    }
});
```

The source format accepts only literal records, arrays, strings, and booleans
inside `defineTypeScriptWorkspace(...)`. It is configuration, not a build
script: it cannot read files, use the network, start processes, use clocks or
randomness, or execute application logic.

## Ownership law

Each discovered `.ts` or `.tsx` source has exactly one owner: `tsc` or `tscl`.
The default `ownership: "strict"` rejects unowned sources. Set
`ownership: "partial"` to report, rather than reject, sources outside the
declared migration boundary.

```tsx
export default defineTypeScriptWorkspace({
    tsc: {
        include: ["src/legacy/**"],
        compilerOptions: { strict: true, target: "ES2024", module: "ESNext" }
    },
    tscl: {
        project: "./App.csproj",
        include: ["src/copeland/**"]
    }
});
```

Paths are workspace-relative and normalized to `/`. Generated and vendor paths
under `obj`, `bin`, `dist`, `node_modules`, and `.git` are never sources.
Overlaps are errors; declaration order never breaks a tie. A source that
matches an include and that owner's exclude is also an error. Direct relative
imports across owners are rejected: share emitted JavaScript artifacts or an
explicit package/contract instead.

## Synchronizing

Run from the workspace root:

```console
tscl workspace validate
tscl workspace sync
tscl workspace status
tscl workspace owner src/copeland/Domain.ts --format json
```

`sync` first parses, validates, resolves ownership, renders every artifact into
a sibling staging directory, and replaces the owned output directory only after
all rendering succeeds. Identical content is a no-op. It does not overwrite a
user-authored root `tsconfig.json`.

The generated source-of-truth projections live in:

```text
obj/copeland/workspace/
  tsconfig.generated.json
  tscl-files.generated.props
  editor-ownership.generated.json
```

Use `tsconfig.generated.json` wherever an existing tool requires a normal
TypeScript config. It uses an explicit stable `files` list, so `tscl`-owned
folders cannot leak through a broad `tsc` include. The supported M0 compiler
options are `target`, `module`, `moduleResolution`, `strict`, `jsx`,
`jsxImportSource`, `lib`, `types`, `baseUrl`, `paths`, `rootDir`, `outDir`,
`declaration`, `sourceMap`, `esModuleInterop`, `skipLibCheck`, `allowJs`,
`checkJs`, and `resolveJsonModule`. Unknown options are errors.

Import `tscl-files.generated.props` from the declared `.csproj`; it contributes
only explicit `@(CopelandCompile)` source items to the existing Copeland
MSBuild target. The `.csproj` remains the authority for NuGet, CLR references,
and .NET build semantics. `editor-ownership.generated.json` has schema version
1 with stable `files` (`path`, `owner`, `project`, `matchedRule`) and `rules`
entries for future editor consumers.

## Migration and TSPack

Move one folder at a time by changing its include rule from `tsc` to `tscl`,
then run `sync`. Do not enable Copeland syntax in a `tsc`-owned file.

The workspace manifest owns file/compiler partitioning. TSPack remains the
authority for package graphs, materialization, runtime selection, and executing
the declared compiler target. M0 intentionally does not merge their manifests
or add a cross-compiler cycle planner.

## Backend targets

The optional `tscl.targets` map makes output and execution semantics explicit
without changing source ownership. Each target requires `backend` and
`runtime`; `targetFramework` defaults to `net10.0`, and NativeAOT also requires
an explicit `runtimeIdentifier`. See
[Copeland backend targets](architecture/cts-backend-targets-m71c.md) for the
pairing rules, artifact contracts, and multi-backend example.

The mixed proof lives in `samples/copeland-ts/workspace-m0`: `src/legacy` is
compiled by `tsc`; `src/copeland` is compiled through the existing MSBuild
seam. The next natural milestone is an editor consumer for the generated
ownership schema, not a source migration or compatibility dialect.
