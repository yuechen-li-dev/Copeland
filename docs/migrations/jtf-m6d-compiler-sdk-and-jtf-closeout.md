# JTF-M6d Compiler SDK and Joint Task Force closeout

## Status

Completed. JTF monorepo organization is closed. This record consolidates the M0–M6 ownership ladder; it does not reopen compiler semantics or introduce a new migration ladder.

## Final ownership topology

- Copeland owns general compiler infrastructure and Copeland TS.
- Machina.UI owns UI authoring, presentation vocabulary, screen composition, and foundational frontend input.
- Aurelian owns engine policy, rendering contracts and backends, shaders, and game/world integration.
- Cross-system translation and hosts live in explicit integration projects.
- Dominatus dependencies are isolated to approved runtime/integration ownership.
- Cope MIR, `DocumentMir`, and VD-MIR remain independent subsystem IRs.

Focused solutions preserve separate reviewer lanes. Fast lanes protect ordinary subsystem contracts; the slow Machina lane owns visual, artifact, font-diagnostic, gallery, presenter, and playback proofs; the integration lane owns explicit Aurelian integration and visible-sample proofs. The authoritative topology and test policy remain [JTF-M0 topology and ownership](../architecture/jtf-m0-topology-and-ownership.md) and the [test-lane doctrine](../architecture/jtf-test-lane-doctrine.md).

## Copeland TS graph and durable checks

```text
Copeland.TS -> Copeland.TS.Mir
Copeland.TS.Backend.CSharp -> Copeland.TS.Mir
Copeland.Cli -> Copeland.TS
Copeland.Cli -> Copeland.TS.Mir
Copeland.Cli -> Copeland.TS.Backend.CSharp
```

`Copeland.TS.Mir` is BCL-only. The frontend and C# backend each consume only MIR; the frontend does not select a backend and the backend does not reference frontend parser/binder internals. CLI owns composition. `tools/Validate-CopelandTsTopology.ps1`, alongside the existing dependency-boundary validator, verifies this graph, solution paths, graph cycles, retired `Copeland.Script` identity, forbidden universal compiler abstractions, `.cope` fixture ownership, and IR project independence.

## Compiler-SDK graduation decision

No audited candidate graduated: no universal MIR, parser framework, source/span abstraction, backend/pass interface, or shared package was created. M6b’s incompatible source-location semantics remain decisive. Cope MIR, `DocumentMir`, and VD-MIR remain locally owned. See the [compiler SDK graduation doctrine](../architecture/jtf-compiler-sdk-graduation-doctrine.md).

## Current facts and intended direction

Current implementation: a small TypeScript-shaped frontend lowers to Cope MIR and uses a C# proof backend. `.ts` is Copeland TS source; `.cope` is deterministic textual Cope MIR output/expectation only, not a parsed production interchange format. `*.xtest.tsx` belongs to TSPack. No Cope Test dialect remains, and this milestone adds no TSX parser, TSPack implementation, `.g.ts`, `.g.js`, JavaScript backend fixtures, or speculative extension.

Intended direction only:

```text
TypeScript 7-shaped source language
    -> Copeland TS frontend
    -> Cope MIR
        -> JavaScript backend
        -> C#/.NET backend
```

TypeScript 7 is a syntax/ecosystem reference point. Copeland TS intends stricter closed-world semantics, not full JavaScript or TypeScript compatibility. JavaScript semantics cleanup, payload enums, and the JavaScript backend remain future work. The C# emitter remains a proof backend. Future .NET targets are RyuJIT for managed execution, NativeAOT for supported native deployment, and .NET WebAssembly AOT for browser/Wasm; browser Wasm is not ordinary NativeAOT.

## Validation baseline and fast loop

The table below records warm runs from this closeout after the corresponding build command. Test counts are the `Total tests` reported by `dotnet test`.

| Lane | Build | Test | Tests | Qualification |
| --- | ---: | ---: | ---: | --- |
| Copeland TS | 3.34 s | 2.79 s | 133 | Warm |
| Copeland | 3.18 s | 2.78 s | 225 | Warm |
| Machina.UI | 3.49 s | 8.22 s | 670 | Warm |
| Aurelian | 3.07 s | 13.90 s | 583 | Warm |
| JointTaskForce | 3.40 s | 15.32 s | 1,478 | Warm |
| JointTaskForce.Integration | 2.74 s | 2.24 s | 41 | Warm |

The exact developer fast loop is:

```powershell
dotnet build Copeland.TS.slnx
dotnet test Copeland.TS.slnx --no-build
pwsh ./tools/Validate-DependencyBoundaries.ps1
pwsh ./tools/Validate-CopelandTsTopology.ps1
```

`Machina.UI.Slow.slnx` was not run: M6d does not change its scope, and the existing doctrine classifies it as an explicit slow/diagnostic lane rather than closeout-fast validation.

## Next work

Do not create `JTF-M7`. Begin a separately named Copeland TS product ladder with a bounded JavaScript-backend semantic audit and executable vertical slice from existing Cope MIR, before adding language features or a universal backend abstraction.

No production compiler semantics, generated C# output, or runtime behavior changed in this closeout.
