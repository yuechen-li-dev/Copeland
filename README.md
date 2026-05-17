# Copeland

Copeland is a Browser TypeScript-to-CLR compiler experiment.

It is not a JavaScript engine.

## Pipeline

`.ts` / future `.tsx` source
-> typed bound tree
-> `.cope` MIR
-> generated `.g.cs`
-> CIL / CLR later

M0 proved generated C# can compile and run on the CLR through tests.

## Current source profile

- explicit type annotations
- no `null`
- no implicit `any`
- no `eval`
- fallible functions with `! ErrorType`
- propagation with `?`

## CLI probe

Current CLI artifact probe:

- `copeland compile input.ts --emit mir --out input.cope`
- `copeland compile input.ts --emit csharp --out input.g.cs`

The CLI emits artifacts only.
It does not execute code yet.
It does not compile generated C# with Roslyn.
It does not provide browser APIs.
