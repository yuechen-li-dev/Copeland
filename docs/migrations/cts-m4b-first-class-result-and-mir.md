# CTS-M4b first-class Result and MIR migration

CTS-M4b completes the M4a frontend-to-MIR migration. The authoritative design remains [CTS-M4a](../Copeland/language/copeland-ts-first-class-result-design-cts-m4a.md); this record reports implementation status.

## Changed model

| Before | After |
| --- | --- |
| Function `ReturnType=T`, optional `ErrorType=E` | One `ReturnType=ResultTypeSymbol(T,E)` |
| Fallible call has success `Type=T` plus metadata | Call has complete `Type=ResultTypeSymbol(T,E)` |
| `IsPropagated` is attached to a call | `BoundPropagateExpression` / `MirPropagateExpression` consumes any Result expression |
| String-shaped MIR type | Structural `MirType`, `MirArrayType`, `MirResultType` |
| Backend implicitly wraps fallible returns | Binding/lowering emits explicit `MirOkExpression` or forwards an existing Result |

## Artifacts and evidence

Updated MIR corpus artifacts are limited to the old fallibility cases: `fallible_signature.cope` now contains `ok`, and `propagation.cope` contains dedicated propagation plus explicit return construction. Existing nonfallible MIR/C# and JavaScript `.g.js` artifacts are unchanged. New curated language fixtures cover first-class construction, matching, forwarding, storage, enum payloads, nested Result, void Result, and invalid contextual/error cases.

The C# runtime proof covers existing success propagation and void Result behavior, plus explicit `err`, direct forwarding, and Result match recovery. At this historical checkpoint JavaScript Result MIR was still rejected; CTS-M4c later implements that backend path, and CTS-M6d closes the complete fallibility sequence.

## Validation

Run after this change:

```powershell
dotnet build Copeland.TS.slnx
dotnet test Copeland.TS.slnx --no-build
tools/Validate-CopelandTsTopology.ps1
git diff --check
```

Broader repository solutions remain unchanged by this compiler-lane migration; no Machina, Aurelian, or integration source is modified.
