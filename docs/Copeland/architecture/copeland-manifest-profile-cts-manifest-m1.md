# CTS-MANIFEST-M1: TSPack manifest semantic profile

**Status:** implemented bounded compile-time project-description profile. CTS-SIDECAR-M1a remains deferred.

## Profile boundary

`manifest.tsx` is parsed as ordinary `.tsx` TS-XML. Parsing alone does not select semantics: arbitrary `.tsx` continues to bind as neutral TS-XML and reports `COPE-TSXML-0101`. The explicit seam is `CopelandProject.LoadRootManifest(projectRoot)`, which resolves exactly `projectRoot/manifest.tsx`, parses it, and invokes `ManifestBinder` with `RootProject` authority. This exposes one immutable `CopelandManifest` result to later project/build consumers; they must not inspect TS-XML directly.

The parser has only generic TypeScript declaration wrappers for imports and `export default`; neither node knows TSPack names or project meaning. The manifest binder alone recognizes TSPack vocabulary.

## Accepted TSPack-compatible subset

The profile preserves TSPack's non-React document shape:

```tsx
import { ... } from "tspack/manifest";
const deps = defineDeps({ ... });
export default define(<Workspace ...>...</Workspace>);
```

Imports are restricted to `tspack/manifest`. Top-level source may contain those imports, restricted `const` values, and exactly one `export default define(...)`/`defineWorkspace(...)`. A root is `<Workspace name="..." runtime="nodejs" | "bun" | "deno">`.

The binder admits the established root/package structures: `Package`, split-workspace `Packages` references, `Security`, `UpdatePolicy`, `CompatFiles`/`JsonFile`, and package-owned `Targets`, `RunTargets`, `Tools`, `Boundaries`, `Publish`, and `Policies`. It validates their legal nesting, singleton/duplicate rules, required strings and row shapes, package and target identity, safe relative paths, runtime/cwd enumerations, and the incompatibility of inline packages with `Packages` references.

The compile-time expression evaluator admits literals, arrays, objects, parenthesized values, previously declared constants/property access, and TSPack's established data helpers: `defineDeps`, `npm`, `git`, `path`, `workspace`, `dep`, `peer`, `tool`, `Env`, `Service`, `json`, `TsConfig.manifestEditor`, `VSCode.settings`, and `VSCode.extensions`. It evaluates data directly into immutable manifest values; it never executes JavaScript/TypeScript.

## Authority and sidecar preparation

TSPack currently has no `<Sidecar>` declaration or deployment artifact. Its established deployment-adjacent data is a root-owned `Package(kind="service")` and its `RunTargets` rows. M1 preserves each run target as `ManifestRunTarget` with distinct runtime and argv fields, then derives an immutable root `ManifestDeploymentBinding` with a deterministic workspace/package/target logical identity. It never creates a shell command string, launches a process, or grants target execution authority.

Dependency manifests use the separate `DependencyManifest` context and `definePackage(<Package ...>)` shape. That context rejects `RunTargets`, so a dependency cannot acquire root deployment/process authority. Split package references are represented and path-validated, but recursive package loading/merging remains deferred.

## Diagnostics and exclusions

Manifest diagnostics retain TS-XML token positions and carry the manifest source path. The deterministic `COPE-MANIFEST-*` family covers missing root/default export, forbidden imports/top-level forms, invalid root, schema nesting/unknown/duplicate fields, invalid path/row/attribute types, forbidden expressions, and dependency deployment authority.

Functions, classes, async/await, loops, conditionals, arbitrary calls, environment/host access, dynamic imports, spread, executable effects, and TS-XML braced children are excluded. No React runtime, general TS-XML extension, sidecar process/stdio transport, build execution, or backend lowering is added.

## Evidence and deferrals

`ManifestProfileTests` uses positive and negative `manifest.tsx` fixtures plus an actual temporary root directory to prove discovery, parsing, profile binding, validation, and immutable IR availability. It also proves that ordinary `.tsx` remains neutral and that dependency authority is rejected.

Deferred: recursive split-workspace package loading, package annotations, all TSPack runtime/lockfile/compat-file materialization semantics, full helper default expansion, build execution, external process launch, sidecar declaration/stdio binding, React compatibility, and CTS-SIDECAR-M1a.
