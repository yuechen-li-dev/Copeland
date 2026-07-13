# CTS-M4b first-class Result implementation

**Status:** implemented for the Copeland TS frontend, Cope MIR, and C# proof backend. JavaScript Result emission, postfix unwrap `!`, and `try`/`except` remain unimplemented.

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

The JavaScript backend remains MIR-only and rejects Result-returning types, Result parameters/locals/payloads, constructors, matches, propagation, and nested Result types with `COPE-JS-0001`; it emits no partial artifact. Existing non-Result JavaScript output remains on the established M1–M3 path.

## Deferred work

CTS-M4b does not parse postfix unwrap `expression!`, add `MirUnwrapExpression`, add lexical handlers, add `try`/`except`, or emit JavaScript Results. CTS-M4c is the appropriate JavaScript Result emission milestone over this settled MIR.
