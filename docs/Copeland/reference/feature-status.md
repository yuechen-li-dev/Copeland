# Copeland TS feature status

This is the authoritative current-status summary. **Stable** means a tested
real path exists. **Bounded** means the listed subset is implemented and the
omissions are intentional. **Foundation only** means semantic/runtime pieces
exist without a complete user-facing path. **Experimental** is dogfood proof,
not a general compatibility promise. “Backend” means a backend consumes the
canonical bound/MIR fact; it does not imply every runtime ecosystem is present.

| Feature | Status | Parser/binder | C# | JavaScript/browser | LSP/CLI |
| --- | --- | --- | --- | --- | --- |
| Functions, lexical bindings, structured control flow | Stable | Yes | Yes | Yes | diagnostics/compile |
| Local/imported modules and project contexts | Bounded | Yes | Yes | Yes | project-aware |
| Records and `with` | Stable | Yes | Yes | Yes | diagnostics/compile |
| Pure classes and associated functions | Bounded | Yes | Yes | Yes | diagnostics/compile |
| Enums, payload enums, `match` / accepted `switch` forms | Bounded | Yes | Yes | Yes | diagnostics/compile |
| Transparent aliases, erased interfaces, bounded generics | Bounded | Yes | erased | erased | diagnostics/compile |
| `Result`, `?`, `!`, `try` / `except` | Bounded | Yes | Yes | Yes | diagnostics/compile |
| Arrays, TSON values/assets, record tables | Bounded | Yes | Yes | Yes | TSON/table tools |
| Templates and static evaluation | Bounded | Yes | C# generation | generated artifacts | template tooling |
| TSX / XML-shaped frontend | Bounded | React M0 profile | projection | projection | syntax/LSP |
| Components, captures, private streams/layouts | Bounded | Yes | semantic runtime tests | React/Custom Element proof | tables/hover |
| Component state, events, presentation branches | Bounded | Yes | in-process semantic runtime | browser frame proof | inspection |
| Effects | Foundation only | descriptors/runtime model | semantic runtime | browser execution deferred | inspection partial |
| `flow` | Bounded | Yes | Yes | Yes | compile/runtime tests |
| Machina layouts, streams, bindings, derivations, z-order | Bounded | Yes | layout projections | browser CSS/materialization proof | layout/table inspect |
| CSV-shaped layout authoring | Bounded | Yes | layout projections | browser proof | layout/table inspect |
| Text documents, Markdown-style inline syntax, fitting | Bounded | Yes | model/projections | browser text-fit proof | text tables |
| Attachment plans and adapter contracts | Bounded | Yes | semantic registry | Custom Element browser proof | renderer tables |
| Browser host/runtime | Experimental | V1 frame envelope; legacy bridge deprecated | n/a | TSPack `runtime/browser-v1/index.js`; generated output is projection | browser dogfood |
| React integration | Experimental | typed contracts / TSX M0 | n/a | website proof | build/inspection |
| Custom Element integration | Experimental | typed bridge | n/a | attachment proof | renderer inspection |
| npm contracts/materialization | Bounded | resolved contract | sidecar paths | TSPack materialization | project-aware |
| CLR interop / C# blocks / ASP.NET bridge | Bounded | Yes | Yes | boundary only | diagnostics/CLI |
| CLI, projected tables, layout inspection | Stable | consumes compilation | n/a | n/a | Yes |
| Language server and VS Code | Bounded | shared compilation snapshot | n/a | n/a | hover/diagnostics |

## Deferred or unsupported

- Vue, Svelte, Lit, Blazor, native renderer integration, SSR, hydration,
  portals, and public deployment.
- General TypeScript/JavaScript compatibility, dynamic objects, `any`, host
  globals, decorators, structural unions, optional properties, tuple types,
  optional chaining, `null`, and normal `undefined` values.
- Browser effect execution and broader flow push/pop/goto semantics.
- General table mutation/query/iteration, JSON as a language model, database
  or dataframe semantics, and renderer-owned component subtrees.

The [feature inventory](../copeland-feature-inventory.md) gives the precise
subset and evidence for each status.
