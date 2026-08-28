# CTS compiler ownership boundary (M71)

## Authority

The compiler owns the language. TSPack owns the project around the compiler.

Standalone compiler operation may observe the local environment, but does not become a package manager. TSPack-managed compilation consumes resolved project truth rather than rediscovering it. Compiler configuration is compiler-owned; compiler orchestration is TSPack-owned.

For Copeland, that means parsing, typing, lowering, emission, `tsconfig.tsx`, project-type selection, and the internal `tsc`/`tscl` source partition remain here. TSPack owns complete managed `manifest.tsx` semantics, Requirement Tape resolution, exact dependency choice, aliases, peers, registries, source policy, mirrors, lock/store/materialization, compiler acquisition, lifecycle, security, and orchestration.

Copeland's bounded `manifest.tsx` binder remains useful in standalone mode. It observes compiler-relevant intent; it does not query registries, select versions, resolve peers, write locks, apply mirror policy, run install hooks, or materialize packages.

## Two explicit producers, one context

`CopelandProjectContext.LoadStandalone(projectRoot)` is the standalone producer. It loads the bounded manifest profile, resolves compiler-owned source ownership from `tsconfig.tsx`, and verifies declared npm/JSR packages only in the already-present local `node_modules`. A missing package reports `COPE-PROJECT-0015` with instructions to install it using the user's package manager or use TSPack. The loader performs no network or filesystem realization.

`CopelandProjectContext.LoadResolvedContext(descriptorPath)` is the managed producer. It reads the M71 compiler-target descriptor, validates protocol and Copeland payload versions, uses only its resolved package bindings, and applies `tsconfig.tsx` solely to the candidate sources TSPack supplied. A config-owned source absent from the descriptor is an error; ambient filesystem discovery cannot enlarge the managed compiler world.

Both producers call the same `CopelandProjectContext.Create` semantic constructor and therefore feed build, snapshots, overlays, and LSP through the same compiler-visible model. For equivalent source/package truth they produce the same semantic fingerprint.

The language server uses a resolved context whenever `.tspack/build-manifests` exists. With no managed context directory it uses the explicit standalone loader. It never invokes TSPack or installs packages.

## Protocol

`CompilerTargetDescriptor` is a stable DTO at schema version 1, independent of TSPack's Go structs. It separates target, language, compiler, tool, compiler config, sources, resolved package bindings, runtime, outputs, and capabilities. Copeland accepts `language=copeland-ts`, `compiler=tscl`, and `compilerPayload=copeland-v1@1`. Unknown future generic schema versions and incompatible payloads fail with dedicated diagnostics; additive unknown JSON fields remain forward-compatible.

The generic package binding is authoritative for version and materialization. The payload may supply Copeland-specific static export/component contracts but cannot override or introduce a package absent from the generic bindings. This prevents a local package version from tempting Copeland away from TSPack's selected world.

Pre-M71 unversioned `.request.json` remains read-only compatible for existing materialized contexts and tests. New TSPack writes only the versioned protocol. This compatibility path is not used to acquire or rediscover dependencies.

## Source and fingerprint ownership

Project target ownership and compiler internal source ownership are separate:

```text
TSPack target candidates
        ↓
tsconfig.tsx tsc/tscl partition
        ↓
CopelandProjectContext selected sources
```

The TSPack descriptor identifies and fingerprints candidate inputs and config. Copeland's semantic fingerprint covers the selected source contents, runtime/project-type semantics, and exact package bindings. This supports one compiler-visible world per selected compiler target without duplicating unrelated TSPack policy hashes.

## Independence and diagnostics

Copeland does not reference TSPack source or libraries. The integration is a JSON protocol plus the published `tscl build --project ... --result ...` CLI boundary. TSPack transports Copeland diagnostics without interpreting language meaning.

Copeland owns syntax, typing, lowering, config, and source-ownership diagnostics. TSPack owns tool/config availability, source policy, package realization, adapter capability, target ambiguity, and invocation diagnostics.

This boundary is not a general compiler plugin framework. Future TypeScript-family toolchains may share syntax ancestry without sharing Copeland semantics.
