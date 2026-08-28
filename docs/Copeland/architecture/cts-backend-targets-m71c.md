# Copeland backend targets (M71c)

Copeland supports multiple backend targets: JavaScript and .NET-family
outputs. The language is always Copeland TS and semantic compilation happens
once. Backend selection changes how the post-static MIR program is realized;
it does not select a different language or re-run static evaluation.

```text
Copeland TS source
  -> parser/binder/type checker
  -> static evaluation and backend-neutral MIR project graph
  -> JavaScript backend -> Node/V8 or browser
  -> C# backend -> RyuJIT, NativeAOT, or .NET WebAssembly
```

## Identity and ownership

| Axis | Values in M71c |
|---|---|
| Language | `copeland-ts` |
| Compiler | `tscl` / Copeland |
| Backend | `javascript`, `csharp` |
| Runtime | `node`, `browser`, `ryujit`, `nativeaot`, `wasm` |
| Artifact | JavaScript, managed executable, native executable, Wasm module/web bundle, compiler metadata |

Copeland owns backend pairing, lowering, target restrictions, and compiler
diagnostics. TSPack owns target selection, the build graph, tool invocation,
artifact orchestration, runtime lifecycle, and deployment. TSPack's generic
`CompilerTarget` did not gain Copeland fields. The versioned `copeland-v1`
payload carries backend-specific build inputs.

## Compiler-owned configuration

`tsconfig.tsx` can name several realizations of the same owned sources:

```tsx
export default defineTypeScriptWorkspace({
  ownership: "partial",
  tscl: {
    project: "./App.csproj",
    include: ["src/**"],
    targets: {
      "app-js": { backend: "javascript", runtime: "node" },
      "app-clr": { backend: "csharp", runtime: "ryujit", targetFramework: "net10.0" },
      "app-native": {
        backend: "csharp",
        runtime: "nativeaot",
        targetFramework: "net10.0",
        runtimeIdentifier: "win-x64",
      },
      "app-wasm": { backend: "csharp", runtime: "wasm", targetFramework: "net10.0" },
    },
  },
});
```

The parser rejects invalid combinations. JavaScript accepts only Node or
browser; C# accepts only RyuJIT, NativeAOT, or .NET WASM. NativeAOT requires an
explicit RID. The existing default remains JavaScript/Node when no named
backend target exists.

In TSPack, `manifest.tsx` selects the matching compiler-target name and carries
the generic artifact contract (`javaScript`, `managedExecutable`,
`nativeExecutable`, or `wasmModule`). It does not own Copeland lowering.

## Output contracts

| Target | Emission | Runtime | Generic artifact | Launch |
|---|---|---|---|---|
| JS/Node | JavaScript project | V8 | JavaScript | `node <entry>` |
| JS/browser | JavaScript project | browser JS engine | JavaScript/web assets | browser host |
| CLR | generated C# plus published assembly | CLR/RyuJIT | managed executable | `dotnet <entry.dll>` |
| NativeAOT | generated C# plus supported .NET AOT publish | NativeAOT runtime | native executable | direct |
| .NET WASM | generated C# plus `browser-wasm` publish | .NET WebAssembly runtime | Wasm module and web/runtime bundle | browser/static host |

The managed result includes the entry assembly, `.runtimeconfig.json`,
`.deps.json`, target framework, `dotnet` launch identity, compiler/context
fingerprints, output hashes, and capabilities. NativeAOT adds the RID and
direct executable identity. WASM records its target framework, module, .NET
runtime bundle, and browser assumption.

NativeAOT and WASM use normal `dotnet publish`; Copeland does not implement a
runtime, GC, or native code generator. Reflection, dynamic loading, runtime
code generation, and library support remain subject to normal .NET target
constraints. Publish failures remain Copeland diagnostics. The .NET SDK is an
explicit system prerequisite in M71c; neither repository silently changes SDK
authority or manages SDK versions.

## Cache, proof, and policy

TSPack's target fingerprint includes compiler version, config hash, sources,
package contracts, artifact kind, runtime, target framework, RID, and output.
Copeland adds its compiler-owned graph/context fingerprint. JS-to-CLR and RID
changes therefore invalidate independently.

The same-source fixture under TSPack's `fixtures/copeland-targets-m71c` builds
and runs with Node, RyuJIT, and NativeAOT and produces identical text. The .NET
WASM workload on the M71c host also constructs a real `dotnet.native.wasm`
bundle; browser automation is outside this bounded proof. `CtsJitM0` already
keeps the CLR process alive across warmups and repeated kernel measurements.

Backend choice is workload-dependent. RyuJIT and NativeAOT are not “fast
mode,” and no performance superiority is claimed. JavaScript/Node remains the
default because it is the established production path and prior measurements
show V8 can outperform ahead-of-time alternatives on important workloads.
