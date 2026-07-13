# CTS-M4b first-class Result implementation

**Status:** historical CTS-M4b implementation record. Result source/MIR and C# proof emission were implemented here; JavaScript Result emission is implemented by CTS-M4c and postfix unwrap `!` by CTS-M5. `try`/`except` remains unimplemented; its accepted direction is [CTS-M6a](../language/copeland-ts-try-except-design-cts-m6a.md).

CTS-M4b implements the accepted M4a Result model. `T ! E` is a structural source type at function, parameter, local, array, nested-type, and enum-payload positions. A fallible call has type `T ! E`; it can be stored, passed, returned, matched, or consumed by `?`.

## Canonical graph

```text
FunctionSymbol.ReturnType = ResultTypeSymbol(T, E)
BoundCallExpression.Type = ResultTypeSymbol(T, E)
BoundPropagateExpression(Operand, Target=FunctionReturn).Type = T
MirFunction.ReturnType = MirResultType(T, E)
MirCallExpression.Type = MirResultType(T, E)
MirOkExpression / MirErrExpression / MirResultMatchExpression / MirPropagateExpression
```

The former `FunctionSymbol.ErrorType`, bound-expression error metadata, `MirFunction.ErrorType`, and `MirCallExpression.IsFallible`, `ErrorType`, and `IsPropagated` are retired. No compatibility facade remains.

## Source and MIR behavior

`ok(value)` and `err(error)` are contextual Result intrinsics. A fallible return wraps a plain success value as `ok`; an already compatible Result is forwarded unchanged. `return;` in `void ! E` constructs successful void. Result match requires exactly `ok(binding)` and `err(binding)` arms. `?` accepts any Result expression and transfers its original error to the enclosing Result-returning function.

Cope MIR has structural named, array, and Result types. Its text projection prints Result types directly and uses visible `ok`, `err`, `result-match`, and `propagate ... to function-return` forms. Result is not entered in the enum catalog and never gains nominal enum identity.

## Backend boundary

The C# proof backend privately emits `CopeResult<TValue,TError>` and `CopeUnit` where needed. It emits explicit constructors, direct forwarding, Result match branches, and function-return propagation without using exceptions as ordinary Result flow. Its representation is not language ABI.

At this M4b checkpoint, the MIR-only JavaScript backend rejected Result-returning types, Result parameters/locals/payloads, constructors, matches, propagation, and nested Result types with `COPE-JS-0001`. CTS-M4c removes that historical boundary while preserving the established M1–M3 path; see [CTS-M4c JavaScript Result backend](copeland-ts-javascript-result-cts-m4c.md).

## Deferred work

CTS-M4b did not parse postfix unwrap `expression!`, add `MirUnwrapExpression`, add lexical handlers, or add `try`/`except`. CTS-M5 later supplies unwrap, CTS-M4c supplies JavaScript Result emission, and CTS-M6a defines the later lexical-handler boundary.
